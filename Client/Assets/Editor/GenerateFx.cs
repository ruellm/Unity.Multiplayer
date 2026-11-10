using System;
using System.IO;
using Multiplayer.Game;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Multiplayer.EditorTools
{
    public static class GenerateFx
    {
        const string ArtFolder = "Assets/Art";
        const string FxFolder = ArtFolder + "/FX";
        const string TextureFolder = FxFolder + "/Textures";
        const string MaterialFolder = FxFolder + "/Materials";
        const string PrefabFolder = FxFolder + "/Prefabs";
        const string ResourcesFolder = "Assets/Resources";
        const string SetPath = ResourcesFolder + "/" + FxSet.ResourceName + ".asset";
        const string ShaderName = "Universal Render Pipeline/Particles/Unlit";

        // URP BaseShaderGUI.BlendMode values.
        const float BlendAlpha = 0f;
        const float BlendAdditive = 2f;

        const float TracerLength = 1.5f;
        const float TracerWidth = 0.12f;

        static readonly Color FlashColor = new Color(1f, 0.9f, 0.6f);
        static readonly Color SparkHot = new Color(1f, 0.85f, 0.45f);
        static readonly Color SparkCool = new Color(1f, 0.55f, 0.2f);
        static readonly Color SmokeColor = new Color(0.6f, 0.6f, 0.6f);
        static readonly Color DustColor = new Color(0.55f, 0.48f, 0.4f);
        static readonly Color ExplosionSmokeColor = new Color(0.25f, 0.22f, 0.2f);

        delegate float AlphaFunc(float u, float v);

        [MenuItem("Tools/RTS/Generate FX")]
        public static void Run()
        {
            EnsureFolders();

            Texture2D dot = WriteTexture("T_FX_Dot", 64, DotAlpha);
            Texture2D streak = WriteTexture("T_FX_Streak", 128, StreakAlpha);
            Texture2D smoke = WriteTexture("T_FX_Smoke", 128, SmokeAlpha);

            Material flash = WriteMaterial("M_FX_FlashAdd", dot, BlendAdditive);
            Material spark = WriteMaterial("M_FX_SparkAdd", streak, BlendAdditive);
            Material fire = WriteMaterial("M_FX_FireAdd", smoke, BlendAdditive);
            Material smokeMat = WriteMaterial("M_FX_SmokeAlpha", smoke, BlendAlpha);
            Material dust = WriteMaterial("M_FX_DustAlpha", smoke, BlendAlpha);

            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                FxSet set = AssetDatabase.LoadAssetAtPath<FxSet>(SetPath);
                if (set == null)
                {
                    set = ScriptableObject.CreateInstance<FxSet>();
                    AssetDatabase.CreateAsset(set, SetPath);
                }

                set.muzzleSmall = WritePrefab(scene, "FX_MuzzleSmall", root => BuildMuzzleSmall(root, flash));
                set.muzzleTank = WritePrefab(scene, "FX_MuzzleTank", root => BuildMuzzleTank(root, flash, smokeMat));
                set.tracer = WritePrefab(scene, "FX_Tracer", root => BuildTracer(root, spark));
                set.shell = WritePrefab(scene, "FX_Shell", root => BuildShell(root, flash, spark));
                set.impactSmall = WritePrefab(scene, "FX_ImpactSmall", root => BuildImpactSmall(root, spark));
                set.impactTank = WritePrefab(scene, "FX_ImpactTank", root => BuildImpactTank(root, spark, dust));
                set.explosion = WritePrefab(scene, "FX_Explosion", root => BuildExplosion(root, fire, smokeMat, spark));
                set.dustPuff = WritePrefab(scene, "FX_DustPuff", root => BuildDustPuff(root, dust));
                set.tracerSpeed = 120f;
                set.shellTravelTime = 0.2f;

                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssets();
                Debug.Log("FxSet -> " + SetPath);
                Selection.activeObject = set;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void EnsureFolders()
        {
            EnsureFolder("Assets", "Art");
            EnsureFolder(ArtFolder, "FX");
            EnsureFolder(FxFolder, "Textures");
            EnsureFolder(FxFolder, "Materials");
            EnsureFolder(FxFolder, "Prefabs");
            EnsureFolder("Assets", "Resources");
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
                AssetDatabase.CreateFolder(parent, name);
        }

        // Textures

        static Texture2D WriteTexture(string name, int size, AlphaFunc alpha)
        {
            string path = TextureFolder + "/" + name + ".png";

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                Color32[] pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    float v = (y + 0.5f) / size * 2f - 1f;
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) / size * 2f - 1f;
                        byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(u, v)) * 255f);
                        pixels[y * size + x] = new Color32(255, 255, 255, a);
                    }
                }
                texture.SetPixels32(pixels);
                File.WriteAllBytes(FullPath(path), texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("No TextureImporter for " + path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = true;
            importer.maxTextureSize = size;
            importer.SaveAndReimport();

            Debug.Log("Texture -> " + path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // 1 inside inner, 0 beyond outer, smooth between.
        static float Soft(float d, float inner, float outer)
        {
            float t = Mathf.Clamp01((d - inner) / (outer - inner));
            return 1f - t * t * (3f - 2f * t);
        }

        static float DotAlpha(float u, float v)
        {
            float d = Mathf.Sqrt(u * u + v * v);
            return Soft(d, 0f, 1f);
        }

        static float StreakAlpha(float u, float v)
        {
            float ev = v / 0.25f;
            float d = Mathf.Sqrt(u * u + ev * ev);
            return Soft(d, 0f, 1f);
        }

        static float SmokeAlpha(float u, float v)
        {
            float d = Mathf.Sqrt(u * u + v * v);

            float noise = 0f;
            float amplitude = 0.5f;
            float frequency = 1.5f;
            for (int octave = 0; octave < 3; octave++)
            {
                noise += (Mathf.PerlinNoise(u * frequency + 17.3f, v * frequency + 41.7f) - 0.5f) * amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }

            float blob = Soft(d + noise * 0.8f, 0.15f, 0.9f) * (0.8f + noise * 0.6f);
            return blob * Soft(d, 0.7f, 1f);
        }

        // Materials

        static Material WriteMaterial(string name, Texture2D texture, float blend)
        {
            string path = MaterialFolder + "/" + name + ".mat";

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new InvalidOperationException("Shader not found: " + ShaderName);

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", blend);
            material.SetFloat("_ColorMode", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            BaseShaderGUI.SetMaterialKeywords(material, null, ParticleGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);

            Debug.Log("Material -> " + path);
            return material;
        }

        // Prefabs

        static GameObject WritePrefab(Scene scene, string name, Action<GameObject> build)
        {
            string path = PrefabFolder + "/" + name + ".prefab";

            GameObject root = new GameObject(name);
            try
            {
                SceneManager.MoveGameObjectToScene(root, scene);
                build(root);

                bool success;
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out success);
                if (!success || prefab == null)
                    throw new InvalidOperationException("Could not save " + path);

                Debug.Log("Prefab -> " + path);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static ParticleSystem AddSystem(GameObject target, Material material, ParticleSystemSimulationSpace space, float duration, int maxParticles)
        {
            ParticleSystem ps = target.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.None;
            main.simulationSpace = space;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.duration = duration;
            main.maxParticles = maxParticles;
            main.startSpeed = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            return ps;
        }

        static ParticleSystem AddChildSystem(GameObject parent, string name, Material material, float duration, int maxParticles)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return AddSystem(child, material, ParticleSystemSimulationSpace.World, duration, maxParticles);
        }

        static void Burst(ParticleSystem ps, int min, int max)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)min, (short)max) });
        }

        static void Cone(ParticleSystem ps, float angle, float radius, Vector3 rotation)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
            shape.rotation = rotation;
        }

        // Hemisphere pointing up.
        static void HemisphereUp(ParticleSystem ps, float radius)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = radius;
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }

        static void Sphere(ParticleSystem ps, float radius)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
        }

        // Flat circle in the XZ plane, emitting outward.
        static void CircleFlat(ParticleSystem ps, float radius)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.rotation = new Vector3(90f, 0f, 0f);
        }

        static void Stretch(ParticleSystem ps, float lengthScale, float velocityScale)
        {
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = lengthScale;
            renderer.velocityScale = velocityScale;
        }

        static void RandomRotation(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }

        static void Drag(ParticleSystem ps, float drag)
        {
            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = drag;
        }

        static void Rise(ParticleSystem ps, float speed)
        {
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = speed;
        }

        static void Grow(ParticleSystem ps, float endScale)
        {
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, endScale));
        }

        static void FadeOut(ParticleSystem ps, float startAlpha)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(0f, 1f) });
            SetColorOverLifetime(ps, gradient);
        }

        static void SetColorOverLifetime(ParticleSystem ps, Gradient gradient)
        {
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        static void Sparks(ParticleSystem ps, int min, int max, float speedMin, float speedMax, float lifetimeMin, float lifetimeMax, float gravity)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.15f);
            main.startColor = new ParticleSystem.MinMaxGradient(SparkHot, SparkCool);
            main.gravityModifier = gravity;
            Burst(ps, min, max);
            Stretch(ps, 2f, 0.05f);
            FadeOut(ps, 1f);
        }

        static void BuildMuzzleSmall(GameObject root, Material flash)
        {
            ParticleSystem ps = AddSystem(root, flash, ParticleSystemSimulationSpace.World, 0.2f, 4);
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 0.05f;
            main.startSize = 0.3f;
            main.startColor = FlashColor;
            Burst(ps, 1, 2);
        }

        static void BuildMuzzleTank(GameObject root, Material flash, Material smoke)
        {
            ParticleSystem ps = AddSystem(root, flash, ParticleSystemSimulationSpace.World, 0.2f, 4);
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 0.08f;
            main.startSize = 1f;
            main.startColor = FlashColor;
            Burst(ps, 1, 1);

            ParticleSystem puff = AddChildSystem(root, "Smoke", smoke, 1f, 8);
            ParticleSystem.MainModule puffMain = puff.main;
            puffMain.startLifetime = 0.6f;
            puffMain.startSpeed = new ParticleSystem.MinMaxCurve(2f, 3f);
            puffMain.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            puffMain.startColor = SmokeColor;
            Burst(puff, 4, 6);
            Cone(puff, 20f, 0.1f, Vector3.zero);
            RandomRotation(puff);
            Drag(puff, 3f);
            Rise(puff, 0.8f);
            Grow(puff, 2f);
            FadeOut(puff, 0.7f);
        }

        // Code emits one particle with ParticleSystem.Emit, giving velocity and lifetime.
        static void BuildTracer(GameObject root, Material spark)
        {
            ParticleSystem ps = AddSystem(root, spark, ParticleSystemSimulationSpace.World, 5f, 16);
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 1f;
            main.startSize = TracerWidth;
            main.startColor = SparkHot;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;

            Stretch(ps, TracerLength / TracerWidth, 0f);
        }

        // Local space so the particle follows the root, which code moves.
        static void BuildShell(GameObject root, Material flash, Material spark)
        {
            ParticleSystem ps = AddSystem(root, flash, ParticleSystemSimulationSpace.Local, 5f, 2);
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 5f;
            main.startSize = 0.2f;
            main.startColor = FlashColor;
            Burst(ps, 1, 1);

            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = spark;
            trail.time = 0.08f;
            trail.startWidth = 0.08f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.05f;
            trail.textureMode = LineTextureMode.Stretch;
            trail.alignment = LineAlignment.View;
            trail.autodestruct = false;
            trail.generateLightingData = false;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.startColor = SparkHot;
            trail.endColor = new Color(SparkCool.r, SparkCool.g, SparkCool.b, 0f);
        }

        static void BuildImpactSmall(GameObject root, Material spark)
        {
            ParticleSystem ps = AddSystem(root, spark, ParticleSystemSimulationSpace.World, 0.5f, 16);
            Sparks(ps, 6, 10, 3f, 6f, 0.2f, 0.2f, 1f);
            HemisphereUp(ps, 0.05f);
        }

        static void BuildImpactTank(GameObject root, Material spark, Material dust)
        {
            ParticleSystem ps = AddSystem(root, spark, ParticleSystemSimulationSpace.World, 0.5f, 16);
            Sparks(ps, 6, 10, 3f, 6f, 0.2f, 0.2f, 1f);
            HemisphereUp(ps, 0.05f);

            ParticleSystem puff = AddChildSystem(root, "Dust", dust, 1f, 8);
            ParticleSystem.MainModule puffMain = puff.main;
            puffMain.startLifetime = 0.8f;
            puffMain.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2f);
            puffMain.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            puffMain.startColor = DustColor;
            Burst(puff, 6, 8);
            HemisphereUp(puff, 0.1f);
            RandomRotation(puff);
            Drag(puff, 2f);
            Grow(puff, 2f);
            FadeOut(puff, 0.7f);
        }

        // Authored for a footprint radius of 1.5. Scaling mode is Hierarchy, so scaling the root scales everything.
        static void BuildExplosion(GameObject root, Material fire, Material smoke, Material spark)
        {
            ParticleSystem fireball = AddSystem(root, fire, ParticleSystemSimulationSpace.World, 1f, 16);
            ParticleSystem.MainModule fireMain = fireball.main;
            fireMain.startLifetime = 0.5f;
            fireMain.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
            fireMain.startSize = new ParticleSystem.MinMaxCurve(1f, 2f);
            fireMain.startColor = Color.white;
            Burst(fireball, 10, 15);
            Sphere(fireball, 0.4f);
            RandomRotation(fireball);
            Drag(fireball, 2f);
            Grow(fireball, 1.5f);

            Gradient fireGradient = new Gradient();
            fireGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f),
                    new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.3f),
                    new GradientColorKey(new Color(0.3f, 0.05f, 0f), 0.8f),
                    new GradientColorKey(Color.black, 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            SetColorOverLifetime(fireball, fireGradient);

            ParticleSystem puff = AddChildSystem(root, "Smoke", smoke, 1f, 16);
            ParticleSystem.MainModule puffMain = puff.main;
            puffMain.startLifetime = 1.5f;
            puffMain.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f);
            puffMain.startSize = new ParticleSystem.MinMaxCurve(1f, 1.6f);
            puffMain.startColor = ExplosionSmokeColor;
            Burst(puff, 10, 10);
            HemisphereUp(puff, 0.5f);
            RandomRotation(puff);
            Drag(puff, 1f);
            Rise(puff, 1f);
            Grow(puff, 2f);
            FadeOut(puff, 0.8f);

            ParticleSystem debris = AddChildSystem(root, "Debris", spark, 1f, 32);
            Sparks(debris, 15, 20, 5f, 10f, 0.5f, 0.9f, 2f);
            ParticleSystem.MainModule debrisMain = debris.main;
            debrisMain.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
            HemisphereUp(debris, 0.2f);
        }

        static void BuildDustPuff(GameObject root, Material dust)
        {
            ParticleSystem ps = AddSystem(root, dust, ParticleSystemSimulationSpace.World, 1f, 16);
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 1f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            main.startColor = DustColor;
            Burst(ps, 8, 12);
            CircleFlat(ps, 0.3f);
            RandomRotation(ps);
            Drag(ps, 2f);
            Rise(ps, 0.2f);
            Grow(ps, 1.8f);
            FadeOut(ps, 0.6f);
        }

        static string FullPath(string assetPath)
        {
            string projectDir = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectDir, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}

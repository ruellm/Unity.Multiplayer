using System;
using System.IO;
using Multiplayer.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Multiplayer.EditorTools
{
    public static class RenderUnitIcons
    {
        const string SetPath = "Assets/Resources/UnitVisualSet.asset";
        const string ArtFolder = "Assets/Art";
        const string IconFolder = ArtFolder + "/Icons";
        const string IdleClipName = "Idle";
        const int IconSize = 256;
        const int KeyedSize = 512;
        const float FieldOfView = 25f;
        const float CameraPitch = 35f;
        const float FrameFill = 0.9f;
        const float MinDepth = 0.1f;
        const float KeyTolerance = 0.08f;

        static readonly Color ClearColor = new Color(0f, 0f, 0f, 0f);
        static readonly Color KeyColor = new Color(0f, 1f, 0f, 1f);

        [MenuItem("Tools/RTS/Render Unit Icons")]
        public static void Run()
        {
            UnitVisualSet set = AssetDatabase.LoadAssetAtPath<UnitVisualSet>(SetPath);
            if (set == null)
                throw new InvalidOperationException(SetPath + " not found. Run Tools/RTS/Create Unit Visual Set first.");

            if (!AssetDatabase.IsValidFolder(ArtFolder))
                AssetDatabase.CreateFolder("Assets", "Art");
            if (!AssetDatabase.IsValidFolder(IconFolder))
                AssetDatabase.CreateFolder(ArtFolder, "Icons");

            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (UnitVisualEntry entry in set.Entries)
                {
                    if (entry == null || entry.Prefab == null)
                    {
                        Debug.LogWarning("Render Unit Icons: entry without a prefab skipped.");
                        continue;
                    }

                    string path = IconFolder + "/Icon_" + entry.Name + ".png";
                    Texture2D icon = RenderIcon(scene, entry);
                    try
                    {
                        File.WriteAllBytes(FullPath(path), icon.EncodeToPNG());
                    }
                    finally
                    {
                        Object.DestroyImmediate(icon);
                    }

                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    ConfigureImporter(path);
                    entry.Icon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (entry.Icon == null)
                        Debug.LogWarning("Render Unit Icons: no sprite loaded from " + path);

                    Debug.Log("Icon " + entry.Name + " -> " + path);
                }

                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static Texture2D RenderIcon(Scene scene, UnitVisualEntry entry)
        {
            GameObject instance = null;
            GameObject lightObject = null;
            GameObject cameraObject = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.Prefab, scene);
                Transform root = instance.transform;
                root.position = Vector3.zero;
                root.rotation = Quaternion.identity;
                root.localScale = Vector3.one;
                SampleIdle(instance);

                Bounds bounds = CombinedBounds(instance);

                lightObject = new GameObject("Icon Light");
                SceneManager.MoveGameObjectToScene(lightObject, scene);
                lightObject.transform.rotation = Quaternion.Euler(Bootstrap.SunEuler);
                Light sun = lightObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = Bootstrap.SunIntensity;
                sun.color = Color.white;
                sun.shadows = LightShadows.Soft;
                lightObject.AddComponent<UniversalAdditionalLightData>();

                cameraObject = new GameObject("Icon Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.cameraType = CameraType.Preview;
                camera.fieldOfView = FieldOfView;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = ClearColor;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 200f;

                UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
                cameraData.renderPostProcessing = false;
                cameraData.antialiasing = AntialiasingMode.None;
                cameraData.renderShadows = true;

                float zoom = entry.IconZoom > 0f ? entry.IconZoom : 1f;
                Frame(camera, bounds, entry.IconYaw, zoom);

                Texture2D result = Render(camera, IconSize);
                if (!HasTransparentCorners(result))
                {
                    Debug.LogWarning("Render Unit Icons: URP camera did not write alpha for " + entry.Name + ". Falling back to a " + KeyedSize + " keyed render.");
                    Object.DestroyImmediate(result);
                    result = RenderKeyed(camera);
                }

                return result;
            }
            finally
            {
                if (cameraObject != null)
                    Object.DestroyImmediate(cameraObject);
                if (lightObject != null)
                    Object.DestroyImmediate(lightObject);
                if (instance != null)
                    Object.DestroyImmediate(instance);
            }
        }

        // Poses an animated model on frame 0 of its Idle clip. Models without one keep the prefab pose.
        static void SampleIdle(GameObject instance)
        {
            Animator animator = instance.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
                return;

            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip != null && clip.name == IdleClipName)
                {
                    clip.SampleAnimation(instance, 0f);
                    return;
                }
            }
        }

        static Bounds CombinedBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException("Prefab has no renderers: " + root.name);

            Bounds combined = RendererBounds(renderers[0]);
            for (int i = 1; i < renderers.Length; i++)
                combined.Encapsulate(RendererBounds(renderers[i]));
            return combined;
        }

        static Bounds RendererBounds(Renderer renderer)
        {
            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            return skinned != null ? BakedWorldBounds(skinned) : renderer.bounds;
        }

        // renderer.bounds on a skinned mesh does not follow a sampled pose, the baked vertices do.
        // The bake applies the transform scale, so only position and rotation are applied here.
        // A baked box larger than the renderer's own bounds, or under a quarter of them, means the
        // bake came back in the wrong space, and the renderer bounds are used instead.
        static Bounds BakedWorldBounds(SkinnedMeshRenderer skinned)
        {
            Mesh baked = new Mesh();
            try
            {
                skinned.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;
                if (vertices.Length == 0)
                    return skinned.bounds;

                Transform t = skinned.transform;
                Matrix4x4 toWorld = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                Bounds world = new Bounds(toWorld.MultiplyPoint3x4(vertices[0]), Vector3.zero);
                for (int i = 1; i < vertices.Length; i++)
                    world.Encapsulate(toWorld.MultiplyPoint3x4(vertices[i]));

                float ratio = world.size.magnitude / Mathf.Max(skinned.bounds.size.magnitude, 0.0001f);
                if (ratio > 1.05f || ratio < 0.25f)
                {
                    Debug.LogWarning("Render Unit Icons: baked bounds of " + skinned.name + " are " + ratio.ToString("0.00") + "x the renderer bounds, using renderer bounds.");
                    return skinned.bounds;
                }
                return world;
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }

        static Vector3[] Corners(Bounds bounds)
        {
            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;
            return new[]
            {
                c + new Vector3(-e.x, -e.y, -e.z), c + new Vector3(e.x, -e.y, -e.z),
                c + new Vector3(-e.x, e.y, -e.z), c + new Vector3(e.x, e.y, -e.z),
                c + new Vector3(-e.x, -e.y, e.z), c + new Vector3(e.x, -e.y, e.z),
                c + new Vector3(-e.x, e.y, e.z), c + new Vector3(e.x, e.y, e.z)
            };
        }

        // Distance so the widest projected extent of the box fills FrameFill, divided by zoom, then
        // the camera slides sideways so the projected box center sits in the middle of the frame.
        static void Frame(Camera camera, Bounds bounds, float yaw, float zoom)
        {
            Quaternion rotation = Quaternion.Euler(CameraPitch, yaw, 0f);
            Quaternion toView = Quaternion.Inverse(rotation);
            float limit = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) * FrameFill;

            Vector3[] corners = Corners(bounds);
            for (int i = 0; i < corners.Length; i++)
                corners[i] = toView * (corners[i] - bounds.center);

            float distance = MinDepth;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 v = corners[i];
                distance = Mathf.Max(distance, Mathf.Abs(v.x) / limit - v.z, Mathf.Abs(v.y) / limit - v.z, MinDepth - v.z);
            }
            distance /= Mathf.Max(zoom, 0.01f);

            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 v = corners[i];
                float depth = Mathf.Max(v.z + distance, MinDepth);
                Vector2 projected = new Vector2(v.x / depth, v.y / depth);
                min = Vector2.Min(min, projected);
                max = Vector2.Max(max, projected);
            }

            Vector2 projectedCenter = (min + max) * 0.5f;
            Vector3 shift = new Vector3(projectedCenter.x * distance, projectedCenter.y * distance, 0f);

            camera.transform.rotation = rotation;
            camera.transform.position = bounds.center + rotation * (shift - Vector3.forward * distance);
        }

        static Texture2D Render(Camera camera, int size)
        {
            RenderTexture target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            target.antiAliasing = 1;
            target.Create();
            try
            {
                camera.targetTexture = target;
                Submit(camera, target);
                return ReadBack(target);
            }
            finally
            {
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        static void Submit(Camera camera, RenderTexture target)
        {
            RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                request.destination = target;
                RenderPipeline.SubmitRenderRequest(camera, request);
                return;
            }

            camera.Render();
        }

        static Texture2D ReadBack(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            try
            {
                Texture2D texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                texture.Apply(false);
                return texture;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        // The bounding sphere never reaches the corners, so all four must be clear.
        static bool HasTransparentCorners(Texture2D texture)
        {
            int last = texture.width - 1;
            return texture.GetPixel(0, 0).a < 0.01f
                && texture.GetPixel(last, 0).a < 0.01f
                && texture.GetPixel(0, last).a < 0.01f
                && texture.GetPixel(last, last).a < 0.01f;
        }

        // Renders twice the size against a key colour, keys it out, and box-filters down to IconSize.
        static Texture2D RenderKeyed(Camera camera)
        {
            camera.backgroundColor = KeyColor;
            Texture2D large = Render(camera, KeyedSize);
            try
            {
                Color[] source = large.GetPixels();
                int width = large.width;
                for (int i = 0; i < source.Length; i++)
                {
                    Color c = source[i];
                    bool key = Mathf.Abs(c.r - KeyColor.r) < KeyTolerance
                        && Mathf.Abs(c.g - KeyColor.g) < KeyTolerance
                        && Mathf.Abs(c.b - KeyColor.b) < KeyTolerance;
                    c.a = key ? 0f : 1f;
                    source[i] = c;
                }

                int factor = KeyedSize / IconSize;
                Color[] small = new Color[IconSize * IconSize];
                for (int y = 0; y < IconSize; y++)
                {
                    for (int x = 0; x < IconSize; x++)
                    {
                        Color sum = Color.clear;
                        float alpha = 0f;
                        for (int dy = 0; dy < factor; dy++)
                        {
                            for (int dx = 0; dx < factor; dx++)
                            {
                                Color c = source[(y * factor + dy) * width + x * factor + dx];
                                sum.r += c.r * c.a;
                                sum.g += c.g * c.a;
                                sum.b += c.b * c.a;
                                alpha += c.a;
                            }
                        }

                        Color result = Color.clear;
                        if (alpha > 0f)
                        {
                            result.r = sum.r / alpha;
                            result.g = sum.g / alpha;
                            result.b = sum.b / alpha;
                            result.a = alpha / (factor * factor);
                        }
                        small[y * IconSize + x] = result;
                    }
                }

                Texture2D icon = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
                icon.SetPixels(small);
                icon.Apply(false);
                return icon;
            }
            finally
            {
                Object.DestroyImmediate(large);
                camera.backgroundColor = ClearColor;
            }
        }

        static void ConfigureImporter(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("Render Unit Icons: no TextureImporter for " + path);
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = IconSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
        }

        static string FullPath(string assetPath)
        {
            string projectDir = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectDir, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}

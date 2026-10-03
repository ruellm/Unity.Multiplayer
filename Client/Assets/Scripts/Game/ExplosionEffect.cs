using UnityEngine;
using UnityEngine.Rendering;

namespace Multiplayer.Game
{
    public sealed class ExplosionEffect : MonoBehaviour
    {
        const float Duration = 0.7f;
        const float StartRadius = 0.5f;
        const float EndRadius = 4.5f;
        const float LineWidth = 0.07f;
        const int Segments = 28;
        const int MeridianCount = 4;
        const float LatitudeAngle = 45f;
        const int IgnoreRaycastLayer = 2;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color ExplosionColor = new Color(1f, 0.55f, 0.15f, 1f);

        LineRenderer[] rings;
        Quaternion[] orientations;
        float[] ringRadius;
        float[] ringHeight;
        MaterialPropertyBlock block;
        float age;

        public static void Spawn(Vector3 position, Material material)
        {
            GameObject effectObject = new GameObject("Explosion");
            effectObject.transform.position = position;
            effectObject.AddComponent<ExplosionEffect>().Build(material);
        }

        // Wireframe sphere: an equator, meridians through the poles, and a latitude ring each side.
        void Build(Material material)
        {
            int count = 1 + MeridianCount + 2;
            rings = new LineRenderer[count];
            orientations = new Quaternion[count];
            ringRadius = new float[count];
            ringHeight = new float[count];
            block = new MaterialPropertyBlock();

            float latitude = LatitudeAngle * Mathf.Deg2Rad;
            for (int i = 0; i < count; i++)
            {
                orientations[i] = Quaternion.identity;
                ringRadius[i] = 1f;

                if (i >= 1 && i <= MeridianCount)
                {
                    orientations[i] = Quaternion.Euler(0f, (i - 1) * 180f / MeridianCount, 90f);
                }
                else if (i > MeridianCount)
                {
                    ringRadius[i] = Mathf.Cos(latitude);
                    ringHeight[i] = Mathf.Sin(latitude) * (i == count - 1 ? -1f : 1f);
                }

                rings[i] = CreateRing(material);
            }

            Apply();
        }

        LineRenderer CreateRing(Material material)
        {
            GameObject ringObject = new GameObject("Ring");
            ringObject.layer = IgnoreRaycastLayer;
            ringObject.transform.SetParent(transform, false);

            LineRenderer line = ringObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = Segments;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= Duration)
            {
                Destroy(gameObject);
                return;
            }

            Apply();
        }

        void Apply()
        {
            float progress = age / Duration;
            float eased = 1f - (1f - progress) * (1f - progress);
            float radius = Mathf.Lerp(StartRadius, EndRadius, eased);
            float remaining = 1f - progress;

            // Alpha needs the transparent material variant. The thinning lines fade it regardless.
            Color color = ExplosionColor;
            color.a = remaining;
            block.SetColor(BaseColorId, color);

            Vector3 center = transform.position;
            for (int i = 0; i < rings.Length; i++)
            {
                LineRenderer line = rings[i];
                line.SetPropertyBlock(block);
                line.startWidth = LineWidth * remaining;
                line.endWidth = LineWidth * remaining;

                for (int s = 0; s < Segments; s++)
                {
                    float angle = s * 2f * Mathf.PI / Segments;
                    Vector3 point = new Vector3(Mathf.Cos(angle) * ringRadius[i], ringHeight[i], Mathf.Sin(angle) * ringRadius[i]);
                    line.SetPosition(s, center + orientations[i] * point * radius);
                }
            }
        }
    }
}

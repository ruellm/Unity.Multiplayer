using UnityEngine;
using UnityEngine.Rendering;

namespace Multiplayer.Game
{
    public sealed class HealthBar : MonoBehaviour
    {
        const float BarHeight = 0.18f;
        const int IgnoreRaycastLayer = 2;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Transform anchor;
        Transform cameraTransform;
        Renderer barRenderer;
        MaterialPropertyBlock block;
        float fullWidth;
        float lift;
        float fraction = -1f;

        public static HealthBar Create(Transform anchor, Material material, Camera camera, float width, float lift)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Health Bar";
            quad.layer = IgnoreRaycastLayer;
            quad.transform.SetParent(anchor, false);

            Collider quadCollider = quad.GetComponent<Collider>();
            quadCollider.enabled = false;
            Destroy(quadCollider);

            Renderer quadRenderer = quad.GetComponent<Renderer>();
            quadRenderer.sharedMaterial = material;
            quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;

            HealthBar bar = quad.AddComponent<HealthBar>();
            bar.anchor = anchor;
            bar.cameraTransform = camera.transform;
            bar.barRenderer = quadRenderer;
            bar.block = new MaterialPropertyBlock();
            bar.fullWidth = width;
            bar.lift = lift;
            bar.SetFraction(1f);
            return bar;
        }

        public void SetFraction(float value)
        {
            value = Mathf.Clamp01(value);
            if (value == fraction)
                return;

            fraction = value;
            transform.localScale = new Vector3(fullWidth * fraction, BarHeight, 1f);
            block.SetColor(BaseColorId, Color.Lerp(Color.red, Color.green, fraction));
            barRenderer.SetPropertyBlock(block);
        }

        void LateUpdate()
        {
            if (cameraTransform == null)
                return;

            // Shifted along camera right so the bar drains toward its left edge instead of its centre.
            float shift = (1f - fraction) * fullWidth * 0.5f;
            transform.SetPositionAndRotation(
                anchor.position + Vector3.up * lift - cameraTransform.right * shift,
                cameraTransform.rotation);
        }
    }
}

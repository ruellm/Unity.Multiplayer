using UnityEngine;
using UnityEngine.Rendering;

namespace Multiplayer.Game
{
    public sealed class SelectionController : MonoBehaviour
    {
        const float RingDiameter = 1.9f;
        const float RingLift = 0.03f;
        const int IgnoreRaycastLayer = 2;

        Material ringMaterial;
        Transform ring;
        Unit selected;

        public Unit Selected
        {
            get { return selected; }
        }

        public void Initialize(Material material)
        {
            ringMaterial = material;
        }

        public void HandleClick(WorldHit hit)
        {
            if (hit.Kind == WorldHitKind.Unit)
            {
                Select(hit.Unit);
                return;
            }

            if (hit.Kind == WorldHitKind.Ground && selected != null)
                selected.SetMoveTarget(hit.Point);
        }

        public void ClearSelection()
        {
            selected = null;

            if (ring == null)
                return;

            ring.SetParent(transform, true);
            ring.gameObject.SetActive(false);
        }

        void Select(Unit unit)
        {
            if (unit == selected)
                return;

            selected = unit;

            // The ring lives under the selected unit, so it is gone if that unit was destroyed.
            if (ring == null)
                ring = CreateRing();

            Transform unitTransform = unit.transform;
            float bottom = unit.GetComponentInChildren<Renderer>().bounds.min.y;

            ring.SetParent(unitTransform, true);
            ring.position = new Vector3(unitTransform.position.x, bottom + RingLift, unitTransform.position.z);
            ring.gameObject.SetActive(true);
        }

        Transform CreateRing()
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Selection Ring";
            quad.layer = IgnoreRaycastLayer;

            Collider quadCollider = quad.GetComponent<Collider>();
            quadCollider.enabled = false;
            Destroy(quadCollider);

            Renderer quadRenderer = quad.GetComponent<Renderer>();
            quadRenderer.sharedMaterial = ringMaterial;
            quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;

            Transform quadTransform = quad.transform;
            quadTransform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quadTransform.localScale = new Vector3(RingDiameter, RingDiameter, 1f);
            return quadTransform;
        }
    }
}

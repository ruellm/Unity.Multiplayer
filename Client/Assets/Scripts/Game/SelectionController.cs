using Multiplayer.Protocol;
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
        NetworkClient network;
        Transform ring;
        Unit selected;

        public Unit Selected
        {
            get { return selected; }
        }

        public void Initialize(Material material, NetworkClient client)
        {
            ringMaterial = material;
            network = client;
        }

        public void HandleClick(WorldHit hit)
        {
            if (hit.Kind == WorldHitKind.Unit)
            {
                if (hit.Unit.OwnerId == network.PlayerId)
                    Select(hit.Unit);
                return;
            }

            if (hit.Kind == WorldHitKind.Ground && selected != null)
            {
                MoveRequestMessage request;
                request.EntityId = selected.EntityId;
                request.Target = new Vec2(hit.Point.x, hit.Point.z);
                network.Send(MessageId.MoveRequest, request);
            }
        }

        public void OnUnitRemoved(Unit unit)
        {
            if (unit == selected)
                ClearSelection();
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

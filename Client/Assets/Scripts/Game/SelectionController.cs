using System;
using Multiplayer.Protocol;
using UnityEngine;
using UnityEngine.Rendering;

namespace Multiplayer.Game
{
    public sealed class SelectionController : MonoBehaviour
    {
        const float RingScale = 1.6f;
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

        public event Action<Unit> SelectionChanged;

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

            if (hit.Kind == WorldHitKind.Ground && selected != null && UnitDefs.Get(selected.UnitType).Speed > 0f)
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
            if (selected == null)
                return;

            selected = null;

            if (ring != null)
            {
                ring.SetParent(transform, true);
                ring.gameObject.SetActive(false);
            }

            RaiseSelectionChanged();
        }

        void Select(Unit unit)
        {
            if (unit == selected)
                return;

            selected = unit;

            if (ring == null)
                ring = CreateRing();

            Transform unitTransform = unit.transform;
            Bounds bounds = unit.Body.bounds;
            float diameter = Mathf.Max(bounds.size.x, bounds.size.z) * RingScale;

            ring.SetParent(unitTransform, true);
            ring.position = new Vector3(unitTransform.position.x, bounds.min.y + RingLift, unitTransform.position.z);
            ring.localScale = new Vector3(diameter, diameter, 1f);
            ring.gameObject.SetActive(true);

            RaiseSelectionChanged();
        }

        void RaiseSelectionChanged()
        {
            Action<Unit> handler = SelectionChanged;
            if (handler != null)
                handler(selected);
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
            return quadTransform;
        }
    }
}

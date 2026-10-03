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
        const float AttackLineWidth = 0.06f;
        const int IgnoreRaycastLayer = 2;

        Material ringMaterial;
        Material attackLineMaterial;
        NetworkClient network;
        WorldView worldView;
        Transform ring;
        LineRenderer attackLine;
        Unit selected;

        public Unit Selected
        {
            get { return selected; }
        }

        public event Action<Unit> SelectionChanged;

        public void Initialize(Material ring, Material line, NetworkClient client, WorldView view)
        {
            ringMaterial = ring;
            attackLineMaterial = line;
            network = client;
            worldView = view;
        }

        // Ownership is the only thing checked here. Range and every other rule are the server's call.
        public bool TryAttack(Unit target)
        {
            if (selected == null || selected.OwnerId != network.PlayerId || target.OwnerId == network.PlayerId)
                return false;

            AttackRequestMessage request;
            request.AttackerId = selected.EntityId;
            request.TargetEntityId = target.EntityId;
            network.Send(MessageId.AttackRequest, request);
            return true;
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

            // Sized and placed from the unit root and the body's own scale, not its bounds, which
            // now shift as the body bobs, tilts and turns.
            Transform unitTransform = unit.transform;
            Vector3 size = unit.Body.transform.localScale;
            float diameter = Mathf.Max(size.x, size.z) * RingScale;

            ring.SetParent(unitTransform, true);
            ring.position = unitTransform.position + Vector3.up * RingLift;
            ring.localScale = new Vector3(diameter, diameter, 1f);
            ring.gameObject.SetActive(true);

            RaiseSelectionChanged();
        }

        void LateUpdate()
        {
            Unit target = null;
            bool show = selected != null
                && selected.TargetEntityId != 0
                && worldView.TryGetUnit(selected.TargetEntityId, out target);

            if (!show)
            {
                if (attackLine != null)
                    attackLine.enabled = false;
                return;
            }

            if (attackLine == null)
                attackLine = CreateAttackLine();

            attackLine.SetPosition(0, selected.Body.bounds.center);
            attackLine.SetPosition(1, target.Body.bounds.center);
            attackLine.enabled = true;
        }

        LineRenderer CreateAttackLine()
        {
            GameObject lineObject = new GameObject("Attack Line");
            lineObject.layer = IgnoreRaycastLayer;
            lineObject.transform.SetParent(transform, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = attackLineMaterial;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = AttackLineWidth;
            line.endWidth = AttackLineWidth;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
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

using System;
using Multiplayer.Protocol;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Multiplayer.Game
{
    public enum WorldHitKind
    {
        None,
        Ground,
        Unit,
        Cancel
    }

    public enum WorldButton
    {
        Primary,
        Secondary
    }

    public readonly struct WorldHit
    {
        public readonly WorldHitKind Kind;
        public readonly WorldButton Button;
        public readonly Vector3 Point;
        public readonly Unit Unit;

        WorldHit(WorldHitKind kind, WorldButton button, Vector3 point, Unit unit)
        {
            Kind = kind;
            Button = button;
            Point = point;
            Unit = unit;
        }

        public static WorldHit OnGround(WorldButton button, Vector3 point)
        {
            return new WorldHit(WorldHitKind.Ground, button, point, null);
        }

        public static WorldHit OnUnit(WorldButton button, Unit unit, Vector3 point)
        {
            return new WorldHit(WorldHitKind.Unit, button, point, unit);
        }

        public static WorldHit Cancel()
        {
            return new WorldHit(WorldHitKind.Cancel, WorldButton.Secondary, Vector3.zero, null);
        }
    }

    public sealed class WorldInput : MonoBehaviour
    {
        const float MaxRayDistance = 500f;
        const int MaxHits = 16;

        readonly RaycastHit[] hits = new RaycastHit[MaxHits];
        Camera worldCamera;

        public event Action<WorldHit> Clicked;

        public void Initialize(Camera camera)
        {
            worldCamera = camera;
        }

        void Update()
        {
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Raise(WorldHit.Cancel());
                return;
            }

            if (mouse == null)
                return;

            if (mouse.rightButton.wasPressedThisFrame)
            {
                // A right-click that lands on nothing in the world still cancels, as it always has.
                WorldHit hit;
                if (IsPointerOverUi() || !TryRaycast(mouse, WorldButton.Secondary, out hit))
                    hit = WorldHit.Cancel();

                Raise(hit);
                return;
            }

            if (!mouse.leftButton.wasPressedThisFrame || IsPointerOverUi())
                return;

            WorldHit primary;
            if (TryRaycast(mouse, WorldButton.Primary, out primary))
                Raise(primary);
        }

        static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        // Ground under the cursor, even with a unit in the way.
        public bool TryGroundPoint(out Vector3 point)
        {
            point = Vector3.zero;
            Mouse mouse = Mouse.current;
            if (worldCamera == null || mouse == null)
                return false;

            Ray ray = worldCamera.ScreenPointToRay(mouse.position.ReadValue());
            int count = Physics.RaycastNonAlloc(ray, hits, MaxRayDistance);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].distance < best && hits[i].collider.GetComponentInParent<Ground>() != null)
                {
                    best = hits[i].distance;
                    point = hits[i].point;
                    found = true;
                }
            }
            return found;
        }

        bool TryRaycast(Mouse mouse, WorldButton button, out WorldHit worldHit)
        {
            worldHit = default;
            if (worldCamera == null)
                return false;

            Ray ray = worldCamera.ScreenPointToRay(mouse.position.ReadValue());
            int count = Physics.RaycastNonAlloc(ray, hits, MaxRayDistance);
            if (count == 0)
                return false;

            // Closest unit beats any structure, closest structure beats ground.
            Unit bestUnit = null;
            Vector3 unitPoint = Vector3.zero;
            float unitDistance = float.MaxValue;
            bool unitIsStructure = true;

            bool groundHit = false;
            Vector3 groundPoint = Vector3.zero;
            float groundDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                Unit unit = hit.collider.GetComponentInParent<Unit>();
                if (unit != null)
                {
                    bool isStructure = unit.UnitType.IsStructure();
                    bool better = bestUnit == null
                        || (unitIsStructure && !isStructure)
                        || (unitIsStructure == isStructure && hit.distance < unitDistance);
                    if (better)
                    {
                        bestUnit = unit;
                        unitPoint = hit.point;
                        unitDistance = hit.distance;
                        unitIsStructure = isStructure;
                    }
                    continue;
                }

                if (hit.distance < groundDistance && hit.collider.GetComponentInParent<Ground>() != null)
                {
                    groundHit = true;
                    groundPoint = hit.point;
                    groundDistance = hit.distance;
                }
            }

            if (bestUnit != null)
            {
                worldHit = WorldHit.OnUnit(button, bestUnit, unitPoint);
                return true;
            }

            if (!groundHit)
                return false;

            worldHit = WorldHit.OnGround(button, groundPoint);
            return true;
        }

        void Raise(WorldHit hit)
        {
            Action<WorldHit> handler = Clicked;
            if (handler != null)
                handler(hit);
        }
    }
}

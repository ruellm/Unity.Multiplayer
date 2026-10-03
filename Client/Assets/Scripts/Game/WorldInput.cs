using System;
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

        bool TryRaycast(Mouse mouse, WorldButton button, out WorldHit worldHit)
        {
            worldHit = default;
            if (worldCamera == null)
                return false;

            Ray ray = worldCamera.ScreenPointToRay(mouse.position.ReadValue());
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, MaxRayDistance))
                return false;

            Unit unit = hit.collider.GetComponentInParent<Unit>();
            if (unit != null)
            {
                worldHit = WorldHit.OnUnit(button, unit, hit.point);
                return true;
            }

            if (hit.collider.GetComponentInParent<Ground>() == null)
                return false;

            worldHit = WorldHit.OnGround(button, hit.point);
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

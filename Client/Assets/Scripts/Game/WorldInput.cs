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

    public readonly struct WorldHit
    {
        public readonly WorldHitKind Kind;
        public readonly Vector3 Point;
        public readonly Unit Unit;

        WorldHit(WorldHitKind kind, Vector3 point, Unit unit)
        {
            Kind = kind;
            Point = point;
            Unit = unit;
        }

        public static WorldHit OnGround(Vector3 point)
        {
            return new WorldHit(WorldHitKind.Ground, point, null);
        }

        public static WorldHit OnUnit(Unit unit, Vector3 point)
        {
            return new WorldHit(WorldHitKind.Unit, point, unit);
        }

        public static WorldHit Cancel()
        {
            return new WorldHit(WorldHitKind.Cancel, Vector3.zero, null);
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

            bool cancelPressed = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                || (mouse != null && mouse.rightButton.wasPressedThisFrame);
            if (cancelPressed)
            {
                Raise(WorldHit.Cancel());
                return;
            }

            if (mouse == null || worldCamera == null)
                return;

            if (!mouse.leftButton.wasPressedThisFrame)
                return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            Ray ray = worldCamera.ScreenPointToRay(mouse.position.ReadValue());
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, MaxRayDistance))
                return;

            Unit unit = hit.collider.GetComponentInParent<Unit>();
            if (unit != null)
            {
                Raise(WorldHit.OnUnit(unit, hit.point));
                return;
            }

            if (hit.collider.GetComponentInParent<Ground>() != null)
                Raise(WorldHit.OnGround(hit.point));
        }

        void Raise(WorldHit hit)
        {
            Action<WorldHit> handler = Clicked;
            if (handler != null)
                handler(hit);
        }
    }
}

using UnityEngine;

namespace Multiplayer.Game
{
    [RequireComponent(typeof(Unit))]
    public sealed class UnitMover : MonoBehaviour
    {
        public const float DefaultSpeed = 6f;
        public const float DefaultStopDistance = 0.05f;

        public float speed = DefaultSpeed;
        public float stopDistance = DefaultStopDistance;

        Unit unit;

        void Awake()
        {
            unit = GetComponent<Unit>();
        }

        void Update()
        {
            if (!unit.HasMoveTarget)
                return;

            bool arrived;
            transform.position = Step(transform.position, unit.MoveTarget, speed, stopDistance, Time.deltaTime, out arrived);

            if (arrived)
                unit.ClearMoveTarget();
        }

        public static Vector3 Step(Vector3 position, Vector3 target, float speed, float stopDistance, float deltaTime, out bool arrived)
        {
            float dx = target.x - position.x;
            float dz = target.z - position.z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            if (distance <= stopDistance)
            {
                arrived = true;
                return position;
            }

            float travel = speed * deltaTime;
            if (travel >= distance)
            {
                arrived = true;
                return new Vector3(target.x, position.y, target.z);
            }

            float scale = travel / distance;
            arrived = false;
            return new Vector3(position.x + dx * scale, position.y, position.z + dz * scale);
        }
    }
}

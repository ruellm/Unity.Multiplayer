using System.Collections.Generic;
using Multiplayer.Protocol;
using UnityEngine;

namespace Multiplayer.Game
{
    // Pooled combat effects. Burst effects return to their pool after their longest particle
    // lifetime, tracers and shells after they arrive.
    public sealed class FxPlayer : MonoBehaviour
    {
        const int Prewarm = 4;
        const float ReturnMargin = 0.1f;
        const float ExplosionAuthoredRadius = 1.5f;
        const float MinDistance = 0.001f;

        struct Timed
        {
            public FxPool Pool;
            public GameObject Instance;
            public float EndTime;
        }

        struct Impact
        {
            public FxPool Pool;
            public Unit Target;
            public Vector3 Point;
            public float Time;
        }

        struct Shell
        {
            public GameObject Instance;
            public Unit Target;
            public Vector3 From;
            public Vector3 Point;
            public float StartTime;
        }

        FxSet set;
        Transform container;
        FxPool muzzleSmall;
        FxPool muzzleTank;
        FxPool tracer;
        FxPool shell;
        FxPool impactSmall;
        FxPool impactTank;
        FxPool explosion;
        FxPool dustPuff;
        ParticleSystem tracerSystem;

        readonly List<Timed> timed = new List<Timed>();
        readonly List<Impact> impacts = new List<Impact>();
        readonly List<Shell> shells = new List<Shell>();

        public bool Ready
        {
            get { return set != null; }
        }

        public void Initialize()
        {
            FxSet loaded = Resources.Load<FxSet>(FxSet.ResourceName);
            if (loaded == null)
            {
                Debug.LogError("FxPlayer: Resources/" + FxSet.ResourceName + ".asset not found. Run Tools/RTS/Generate FX in the editor. Combat effects are off.");
                return;
            }

            if (loaded.muzzleSmall == null || loaded.muzzleTank == null || loaded.tracer == null || loaded.shell == null
                || loaded.impactSmall == null || loaded.impactTank == null || loaded.explosion == null || loaded.dustPuff == null)
            {
                Debug.LogError("FxPlayer: " + FxSet.ResourceName + ".asset has an empty prefab field. Run Tools/RTS/Generate FX in the editor. Combat effects are off.");
                return;
            }

            // Scene root, so pooled world space systems emit in world coordinates.
            container = new GameObject("FX").transform;

            set = loaded;
            muzzleSmall = new FxPool(set.muzzleSmall, container, Prewarm);
            muzzleTank = new FxPool(set.muzzleTank, container, Prewarm);
            tracer = new FxPool(set.tracer, container, 1);
            shell = new FxPool(set.shell, container, Prewarm);
            impactSmall = new FxPool(set.impactSmall, container, Prewarm);
            impactTank = new FxPool(set.impactTank, container, Prewarm);
            explosion = new FxPool(set.explosion, container, Prewarm);
            dustPuff = new FxPool(set.dustPuff, container, Prewarm);
        }

        public void PlayMuzzle(UnitType unitType, Vector3 position, Quaternion rotation)
        {
            if (!Ready)
                return;

            PlayTimed(unitType == UnitType.Tank ? muzzleTank : muzzleSmall, position, rotation, 1f);
        }

        // One world space system serves every tracer. Each shot is one emitted particle that
        // flies on its own, and the impact is scheduled for when it gets there.
        public void FireTracer(Vector3 origin, Unit target, Vector3 targetPoint)
        {
            if (!Ready)
                return;

            if (tracerSystem == null)
            {
                tracerSystem = tracer.Take().GetComponent<ParticleSystem>();
                tracerSystem.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                tracerSystem.Play(true);
            }

            Vector3 delta = targetPoint - origin;
            float distance = delta.magnitude;
            float speed = Mathf.Max(set.tracerSpeed, MinDistance);
            float travel = distance / speed;

            if (distance > MinDistance)
            {
                ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
                emit.position = origin;
                emit.velocity = delta * (speed / distance);
                emit.startLifetime = travel;
                tracerSystem.Emit(emit, 1);
            }

            impacts.Add(new Impact { Pool = impactSmall, Target = target, Point = targetPoint, Time = Time.time + travel });
        }

        public void FireShell(Vector3 origin, Unit target, Vector3 targetPoint)
        {
            if (!Ready)
                return;

            GameObject instance = shell.Take();
            Vector3 delta = targetPoint - origin;
            Quaternion rotation = delta.sqrMagnitude > MinDistance * MinDistance ? Quaternion.LookRotation(delta) : Quaternion.identity;
            instance.transform.SetPositionAndRotation(origin, rotation);

            TrailRenderer trail = instance.GetComponent<TrailRenderer>();
            if (trail != null)
                trail.Clear();
            instance.GetComponent<ParticleSystem>().Play(true);

            shells.Add(new Shell { Instance = instance, Target = target, From = origin, Point = targetPoint, StartTime = Time.time });
        }

        public void PlayExplosion(Vector3 position, float footprintRadius)
        {
            if (!Ready)
                return;

            PlayTimed(explosion, position, Quaternion.identity, footprintRadius / ExplosionAuthoredRadius);
        }

        public void PlayDustPuff(Vector3 position)
        {
            if (!Ready)
                return;

            PlayTimed(dustPuff, position, Quaternion.identity, 1f);
        }

        public void Clear()
        {
            for (int i = 0; i < timed.Count; i++)
                timed[i].Pool.Return(timed[i].Instance);
            timed.Clear();

            for (int i = 0; i < shells.Count; i++)
                shell.Return(shells[i].Instance);
            shells.Clear();

            impacts.Clear();

            if (tracerSystem != null)
            {
                tracer.Return(tracerSystem.gameObject);
                tracerSystem = null;
            }
        }

        void Update()
        {
            if (!Ready)
                return;

            float now = Time.time;

            for (int i = timed.Count - 1; i >= 0; i--)
            {
                if (now < timed[i].EndTime)
                    continue;

                timed[i].Pool.Return(timed[i].Instance);
                timed.RemoveAt(i);
            }

            for (int i = impacts.Count - 1; i >= 0; i--)
            {
                Impact impact = impacts[i];
                if (now < impact.Time)
                    continue;

                impacts.RemoveAt(i);
                PlayTimed(impact.Pool, ImpactPoint(impact.Target, impact.Point), Quaternion.identity, 1f);
            }

            float travelTime = set.shellTravelTime;
            for (int i = shells.Count - 1; i >= 0; i--)
            {
                Shell flight = shells[i];
                Vector3 end = ImpactPoint(flight.Target, flight.Point);
                float progress = travelTime > 0f ? (now - flight.StartTime) / travelTime : 1f;
                if (progress < 1f)
                {
                    flight.Instance.transform.position = Vector3.Lerp(flight.From, end, progress);
                    continue;
                }

                shells.RemoveAt(i);
                shell.Return(flight.Instance);
                PlayTimed(impactTank, end, Quaternion.identity, 1f);
            }
        }

        void OnDestroy()
        {
            if (container != null)
                Destroy(container.gameObject);
        }

        void PlayTimed(FxPool pool, Vector3 position, Quaternion rotation, float scale)
        {
            GameObject instance = pool.Take();
            Transform root = instance.transform;
            root.SetPositionAndRotation(position, rotation);
            root.localScale = Vector3.one * scale;
            instance.GetComponent<ParticleSystem>().Play(true);
            timed.Add(new Timed { Pool = pool, Instance = instance, EndTime = Time.time + pool.Lifetime + ReturnMargin });
        }

        // The target's collider center while it still exists, else where it was when the shot left.
        static Vector3 ImpactPoint(Unit target, Vector3 fallback)
        {
            return target != null && target.Body != null ? target.Body.bounds.center : fallback;
        }
    }
}

using Multiplayer.Protocol;
using UnityEngine;
using UnityEngine.Rendering;

namespace Multiplayer.Game
{
    public sealed class UnitVisual : MonoBehaviour
    {
        const float BobHeight = 0.08f;
        const float BobSpeed = 2.5f;
        const float BobBlendSpeed = 4f;
        const float MoveTilt = 10f;
        const float TiltSpeed = 60f;
        const float TurnSpeed = 540f;
        const float RecoilDistance = 0.25f;
        const float RecoilDuration = 0.15f;
        const float TracerDuration = 0.15f;
        const float TracerWidth = 0.07f;
        const float CollapseFraction = 0.9f;
        const float CollapseSpin = 200f;
        const float CollapseRoll = 80f;
        const float FlashDuration = 0.3f;
        const float BurstScale = 0.4f;
        const float MinDirectionSqr = 0.00000001f;
        const int IgnoreRaycastLayer = 2;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color TracerColor = new Color(1f, 0.9f, 0.4f, 1f);
        static readonly Color ShotFlashColor = Color.white;
        static readonly Color ExplosionFlashColor = new Color(1f, 0.5f, 0.1f);

        Unit unit;
        Transform body;
        Transform muzzle;
        Material effectMaterial;
        Vector3 restScale;
        float restHeight;
        bool animated;
        FiringPhase firing;
        int deathTicks;

        Vector3 lastPosition;
        float desiredYaw;
        float yaw;
        float tilt;
        float bobWeight;
        float recoil;

        bool dying;
        uint dyingStartTick;
        Color flashColor;
        float flash;
        float burst;

        LineRenderer tracer;
        MaterialPropertyBlock tracerBlock;
        Unit tracerTarget;
        float tracerAge;

        public bool CanFire
        {
            get { return animated && firing.CooldownTicks > 0; }
        }

        public FiringPhase Firing
        {
            get { return firing; }
        }

        public void Initialize(Unit owner, Transform bodyTransform, Transform muzzleTransform, Material effectLineMaterial, uint stateTick)
        {
            unit = owner;
            body = bodyTransform;
            muzzle = muzzleTransform;
            effectMaterial = effectLineMaterial;
            restScale = bodyTransform.localScale;
            restHeight = bodyTransform.localPosition.y;
            animated = owner.UnitType != UnitType.Structure;
            firing = new FiringPhase(owner.UnitType, owner.EntityId, stateTick);
            deathTicks = Mathf.Max(1, UnitDefs.DeathTicks(owner.UnitType));
            lastPosition = ToWorld(owner.Position);
            transform.position = lastPosition;
        }

        // The one-shot part of a death. The collapse itself comes from the Dying state, so a client
        // that never receives the event still shows the unit going down, only without this.
        public void PlayDeathFlourish(DeathCause cause)
        {
            flash = 1f;
            flashColor = cause == DeathCause.Explosion ? ExplosionFlashColor : ShotFlashColor;
            burst = cause == DeathCause.Explosion ? 1f : 0f;
        }

        public void Refresh(RenderClock clock, WorldView view)
        {
            Vector3 position = ToWorld(unit.Position);
            transform.position = position;

            float dt = Time.deltaTime;
            UpdateFlourish(dt);

            if (unit.ActionState == ActionState.Dying)
            {
                Collapse(clock);
                return;
            }

            if (!animated)
                return;

            Unit target = null;
            if (unit.TargetEntityId != 0)
                view.TryGetUnit(unit.TargetEntityId, out target);

            // Stepped on the render clock's state tick, never on local time, so the loop is in phase
            // with the health it drains and with every other client drawing the same snapshots.
            int targetHealth = target != null ? target.Health : 0;
            if (firing.Step(clock.StateTick, unit.ActionState, unit.Health, unit.TargetEntityId, target != null, targetHealth))
                Fire(target);

            ActionState state = unit.ActionState;
            bool moving = state == ActionState.Moving || state == ActionState.MovingToAttack;

            Vector3 travel = position - lastPosition;
            lastPosition = position;
            if (moving)
                FaceAlong(travel);
            else if (state == ActionState.Attacking && target != null)
                FaceAlong(ToWorld(target.Position) - position);

            yaw = Mathf.MoveTowardsAngle(yaw, desiredYaw, TurnSpeed * dt);
            tilt = Mathf.MoveTowards(tilt, moving ? MoveTilt : 0f, TiltSpeed * dt);
            bobWeight = Mathf.MoveTowards(bobWeight, state == ActionState.Idle ? 1f : 0f, BobBlendSpeed * dt);
            recoil = Mathf.MoveTowards(recoil, 0f, dt / RecoilDuration);

            // Phase offset by id so a group of idle units does not bob in unison.
            float bob = BobHeight * bobWeight * (0.5f + 0.5f * Mathf.Sin(Time.time * BobSpeed + unit.EntityId));
            Vector3 kick = Quaternion.Euler(0f, yaw, 0f) * Vector3.back * (RecoilDistance * recoil);
            body.localRotation = Quaternion.Euler(tilt, yaw, 0f);
            body.localPosition = new Vector3(0f, restHeight + bob, 0f) + kick;

            UpdateTracer(dt, state);
        }

        // Timed on the render clock from the tick Dying first showed, and finished a little before
        // DeathDuration so the body has shrunk to nothing by the time the entity is removed.
        void Collapse(RenderClock clock)
        {
            if (!dying)
            {
                dying = true;
                dyingStartTick = clock.StateTick;
                if (tracer != null)
                    tracer.enabled = false;
            }

            float progress = Mathf.Clamp01((clock.RenderTick - dyingStartTick) / (deathTicks * CollapseFraction));
            float eased = progress * progress * (3f - 2f * progress);
            float remaining = 1f - eased;

            body.localScale = restScale * (remaining * (1f + BurstScale * burst));
            body.localRotation = Quaternion.Euler(tilt, yaw + CollapseSpin * eased, CollapseRoll * eased);
            body.localPosition = new Vector3(0f, restHeight * remaining, 0f);
        }

        void UpdateFlourish(float dt)
        {
            if (flash <= 0f)
                return;

            flash = Mathf.MoveTowards(flash, 0f, dt / FlashDuration);
            burst = Mathf.Min(burst, flash);
            unit.SetFlash(flashColor, flash);
        }

        void FaceAlong(Vector3 direction)
        {
            if (direction.sqrMagnitude > MinDirectionSqr)
                desiredYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        void Fire(Unit target)
        {
            recoil = 1f;

            if (tracer == null)
                tracer = CreateTracer();

            tracerTarget = target;
            tracerAge = 0f;
            tracer.enabled = true;
        }

        void UpdateTracer(float dt, ActionState state)
        {
            if (tracer == null || !tracer.enabled)
                return;

            tracerAge += dt;
            if (state != ActionState.Attacking || tracerTarget == null || tracerAge >= TracerDuration)
            {
                tracer.enabled = false;
                tracerTarget = null;
                return;
            }

            // Alpha needs the transparent material variant. The shrinking width fades it regardless.
            float remaining = 1f - tracerAge / TracerDuration;
            Color color = TracerColor;
            color.a = remaining;
            tracerBlock.SetColor(BaseColorId, color);
            tracer.SetPropertyBlock(tracerBlock);
            tracer.startWidth = TracerWidth * remaining;
            tracer.endWidth = TracerWidth * remaining;
            tracer.SetPosition(0, muzzle.position);
            tracer.SetPosition(1, tracerTarget.Body.bounds.center);
        }

        LineRenderer CreateTracer()
        {
            GameObject lineObject = new GameObject("Tracer");
            lineObject.layer = IgnoreRaycastLayer;
            lineObject.transform.SetParent(transform, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = effectMaterial;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;

            tracerBlock = new MaterialPropertyBlock();
            return line;
        }

        static Vector3 ToWorld(Vec2 position)
        {
            return new Vector3(position.X, 0f, position.Z);
        }
    }
}

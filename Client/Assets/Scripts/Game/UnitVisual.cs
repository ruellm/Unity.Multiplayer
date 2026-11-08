using Multiplayer.Protocol;
using UnityEngine;
using UnityEngine.Rendering;

namespace Multiplayer.Game
{
    public sealed class UnitVisual : MonoBehaviour
    {
        const float TurnSpeed = 540f;
        const float RecoilDuration = 0.15f;
        const float TurretRecoilDistance = 0.03f;
        const float TracerDuration = 0.15f;
        const float TracerWidth = 0.07f;
        const float CollapseFraction = 0.9f;
        const float CollapseSpin = 200f;
        const float CollapseRoll = 80f;
        const float FlashDuration = 0.3f;
        const float BurstScale = 0.4f;
        const float MinDirectionSqr = 0.00000001f;
        const int IgnoreRaycastLayer = 2;
        const string DeadName = "Dead";

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int StateId = Animator.StringToHash("State");
        static readonly int DeadId = Animator.StringToHash(DeadName);
        static readonly Color TracerColor = new Color(1f, 0.9f, 0.4f, 1f);
        static readonly Color ShotFlashColor = Color.white;
        static readonly Color ExplosionFlashColor = new Color(1f, 0.5f, 0.1f);

        Unit unit;
        Transform body;
        Transform model;
        Transform turret;
        Transform muzzle;
        Animator animator;
        Material effectMaterial;
        Vector3 restScale;
        float restHeight;
        Quaternion turretRestRotation;
        Vector3 turretRestPosition;
        float turretTurnRate;
        float deathClipSpeed;
        float corpseHoldSeconds;
        bool isStructure;
        FiringPhase firing;
        int deathTicks;

        Vector3 lastPosition;
        float desiredYaw;
        float yaw;
        float turretYaw;
        float recoil;
        int appliedState = -1;
        bool appliedDead;

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
            get { return firing.CooldownTicks > 0; }
        }

        public FiringPhase Firing
        {
            get { return firing; }
        }

        public void Initialize(Unit owner, Transform bodyTransform, Transform modelTransform, Transform turretPivot, Transform muzzleTransform, UnitVisualEntry entry, Material effectLineMaterial, uint stateTick)
        {
            unit = owner;
            body = bodyTransform;
            model = modelTransform;
            turret = turretPivot;
            muzzle = muzzleTransform;
            effectMaterial = effectLineMaterial;
            restScale = bodyTransform.localScale;
            restHeight = bodyTransform.localPosition.y;
            turretTurnRate = entry.TurretTurnRate;
            deathClipSpeed = entry.DeathClipSpeed;
            corpseHoldSeconds = entry.CorpseHoldSeconds;

            if (turret != null)
            {
                turretRestRotation = turret.localRotation;
                turretRestPosition = turret.localPosition;
            }

            animator = modelTransform.GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController == null)
                animator = null;

            isStructure = owner.UnitType.IsStructure();
            firing = new FiringPhase(owner.UnitType, owner.EntityId, stateTick);
            deathTicks = Mathf.Max(1, UnitDefs.DeathTicks(owner.UnitType));
            lastPosition = ToWorld(owner.Position);
            transform.position = lastPosition;

            // Structures never turn. Their heading is the yaw chosen at placement.
            if (isStructure)
            {
                yaw = YawByte.ToDegrees(owner.Yaw);
                desiredYaw = yaw;
                body.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
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
                if (animator != null)
                    Die(clock);
                else
                    Collapse(clock);
                return;
            }

            Unit target = null;
            if (unit.TargetEntityId != 0)
                view.TryGetUnit(unit.TargetEntityId, out target);

            // Stepped on the render clock's state tick, never on local time, so the loop is in phase
            // with the health it drains and with every other client drawing the same snapshots.
            if (firing.CooldownTicks > 0)
            {
                int targetHealth = target != null ? target.Health : 0;
                if (firing.Step(clock.StateTick, unit.ActionState, unit.Health, unit.TargetEntityId, target != null, targetHealth))
                    Fire(target);
            }

            ActionState state = unit.ActionState;
            bool aiming = (state == ActionState.MovingToAttack || state == ActionState.Attacking) && target != null;

            if (!isStructure)
            {
                bool moving = state == ActionState.Moving || state == ActionState.MovingToAttack;

                Vector3 travel = position - lastPosition;
                lastPosition = position;
                if (moving)
                    FaceAlong(travel);
                else if (state == ActionState.Attacking && target != null && turret == null)
                    FaceAlong(ToWorld(target.Position) - position);

                yaw = Mathf.MoveTowardsAngle(yaw, desiredYaw, TurnSpeed * dt);
                body.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }

            recoil = Mathf.MoveTowards(recoil, 0f, dt / RecoilDuration);

            if (turret != null)
                UpdateTurret(dt, aiming ? target : null);

            if (animator != null)
                UpdateAnimator(state);

            UpdateTracer(dt, state);
        }

        // Yaw around the pivot's rest up axis, toward the target or back to rest. Recoil kicks the
        // pivot along its own back axis.
        void UpdateTurret(float dt, Unit target)
        {
            float desired = 0f;
            if (target != null)
            {
                Vector3 toTarget = turret.parent.InverseTransformDirection(ToWorld(target.Position) - turret.position);
                toTarget = Quaternion.Inverse(turretRestRotation) * toTarget;
                toTarget.y = 0f;
                desired = toTarget.sqrMagnitude > MinDirectionSqr
                    ? Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg
                    : turretYaw;
            }

            turretYaw = Mathf.MoveTowardsAngle(turretYaw, desired, turretTurnRate * dt);
            turret.localRotation = turretRestRotation * Quaternion.Euler(0f, turretYaw, 0f);
            turret.localPosition = turretRestPosition + turret.localRotation * Vector3.back * (TurretRecoilDistance * recoil);
        }

        // Parameters are only pushed once the Animator is initialized. Until then the applied value
        // stays stale, so the next frame pushes it again.
        void UpdateAnimator(ActionState state)
        {
            if (!animator.isInitialized)
                return;

            int value = state == ActionState.Idle ? 0 : (state == ActionState.Attacking ? 2 : 1);
            if (value == appliedState)
                return;

            appliedState = value;
            animator.SetInteger(StateId, value);
        }

        void BeginDying(RenderClock clock)
        {
            if (dying)
                return;

            dying = true;
            dyingStartTick = clock.StateTick;
            if (tracer != null)
                tracer.enabled = false;
        }

        // Timed on the render clock from the tick Dying first showed, and finished a little before
        // DeathDuration so the body has shrunk to nothing by the time the entity is removed.
        void Collapse(RenderClock clock)
        {
            BeginDying(clock);

            float progress = Mathf.Clamp01((clock.RenderTick - dyingStartTick) / (deathTicks * CollapseFraction));
            float eased = progress * progress * (3f - 2f * progress);
            float remaining = 1f - eased;

            body.localScale = restScale * (remaining * (1f + BurstScale * burst));
            body.localRotation = Quaternion.Euler(0f, yaw + CollapseSpin * eased, CollapseRoll * eased);
            body.localPosition = new Vector3(0f, restHeight * remaining, 0f);
        }

        // Animated death: the Dead clip plays and keeps playing on the corpse after removal.
        void Die(RenderClock clock)
        {
            BeginDying(clock);

            if (appliedDead || !animator.isInitialized)
                return;

            appliedDead = true;
            animator.SetBool(DeadId, true);
            animator.speed = deathClipSpeed;
        }

        // Hands the animated model over as a scene-root corpse once Dead has been applied. The caller
        // destroys the entity root afterwards, which takes the collider, health bar and Unit with it.
        public bool TryDetachCorpse(out CorpseTimer corpse)
        {
            corpse = null;
            if (animator == null || !appliedDead || model == null)
                return false;

            model.SetParent(null, true);
            model.name = "Corpse " + unit.EntityId;
            corpse = CorpseTimer.Attach(model.gameObject, animator, DeadName, corpseHoldSeconds);
            return true;
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
            tracer.SetPosition(0, muzzle != null ? muzzle.position : unit.Body.bounds.center);
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

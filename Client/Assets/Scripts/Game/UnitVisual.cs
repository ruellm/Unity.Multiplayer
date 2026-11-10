using Multiplayer.Protocol;
using UnityEngine;

namespace Multiplayer.Game
{
    public sealed class UnitVisual : MonoBehaviour
    {
        const float TurnSpeed = 540f;
        const float RecoilDuration = 0.15f;
        const float TurretRecoilDistance = 0.03f;
        const float CollapseFraction = 0.9f;
        const float CollapseSpin = 200f;
        const float CollapseRoll = 80f;
        const float RunFireHoldSeconds = 0.2f;
        const int RunState = 1;
        const int FireState = 2;
        const float FlashDuration = 0.3f;
        const float BurstScale = 0.4f;
        const float MinDirectionSqr = 0.00000001f;
        const string DeadName = "Dead";

        static readonly int StateId = Animator.StringToHash("State");
        static readonly int DeadId = Animator.StringToHash(DeadName);
        static readonly Color ShotFlashColor = Color.white;
        static readonly Color ExplosionFlashColor = new Color(1f, 0.5f, 0.1f);

        Unit unit;
        Transform body;
        Transform model;
        Transform turret;
        Transform muzzle;
        Animator animator;
        FxPlayer fx;
        Vector3 restScale;
        float restHeight;
        Quaternion turretRestRotation;
        Vector3 turretRestPosition;
        float turretTurnRate;
        float deathClipSpeed;
        float runClipSpeed;
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
        int pendingState = -1;
        float pendingFor;
        bool appliedDead;

        bool dying;
        uint dyingStartTick;
        Color flashColor;
        float flash;
        float burst;

        public bool CanFire
        {
            get { return firing.CooldownTicks > 0; }
        }

        public FiringPhase Firing
        {
            get { return firing; }
        }

        public void Initialize(Unit owner, Transform bodyTransform, Transform modelTransform, Transform turretPivot, Transform muzzleTransform, UnitVisualEntry entry, FxPlayer effects, uint stateTick)
        {
            unit = owner;
            body = bodyTransform;
            model = modelTransform;
            turret = turretPivot;
            muzzle = muzzleTransform;
            fx = effects;
            restScale = bodyTransform.localScale;
            restHeight = bodyTransform.localPosition.y;
            turretTurnRate = entry.TurretTurnRate;
            deathClipSpeed = entry.DeathClipSpeed;
            runClipSpeed = entry.RunClipSpeed > 0f ? entry.RunClipSpeed : 1f;
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

            // Facing and animation read the later sample, so they turn and start running on the
            // tick the position begins to move instead of one tick after.
            ActionState shown = unit.NextActionState;

            if (!isStructure)
            {
                bool moving = shown == ActionState.Moving || shown == ActionState.MovingToAttack;

                Vector3 travel = position - lastPosition;
                lastPosition = position;
                if (moving)
                    FaceAlong(travel);
                else if (shown == ActionState.Attacking && target != null && turret == null)
                    FaceAlong(ToWorld(target.Position) - position);

                yaw = Mathf.MoveTowardsAngle(yaw, desiredYaw, TurnSpeed * dt);
                body.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }

            recoil = Mathf.MoveTowards(recoil, 0f, dt / RecoilDuration);

            if (turret != null)
                UpdateTurret(dt, aiming ? target : null);

            if (animator != null)
                UpdateAnimator(shown, dt);
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
        // stays stale, so the next frame pushes it again. A swap between Run and Fire waits until
        // the new state has been asked for continuously for a short hold, so a chase that flickers
        // between closing and firing does not restart the clips every tick.
        void UpdateAnimator(ActionState state, float dt)
        {
            if (!animator.isInitialized)
                return;

            int value = state == ActionState.Idle ? 0 : (state == ActionState.Attacking ? FireState : RunState);
            if (value != pendingState)
            {
                pendingState = value;
                pendingFor = 0f;
            }
            else
            {
                pendingFor += dt;
            }

            if (value == appliedState)
                return;

            bool runFireSwap = (appliedState == RunState && value == FireState) || (appliedState == FireState && value == RunState);
            if (runFireSwap && pendingFor < RunFireHoldSeconds)
                return;

            appliedState = value;
            animator.SetInteger(StateId, value);
            animator.speed = value == RunState ? runClipSpeed : 1f;
        }

        void BeginDying(RenderClock clock)
        {
            if (dying)
                return;

            dying = true;
            dyingStartTick = clock.StateTick;
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

            if (fx != null)
                fx.PlayDustPuff(transform.position);
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

        // Tanks lob a shell, everything else fires a tracer. Both land on the target's collider center.
        void Fire(Unit target)
        {
            recoil = 1f;
            if (fx == null)
                return;

            Vector3 origin = muzzle != null ? muzzle.position : unit.Body.bounds.center;
            Quaternion rotation = muzzle != null ? muzzle.rotation : body.rotation;
            Vector3 targetPoint = target.Body.bounds.center;

            fx.PlayMuzzle(unit.UnitType, origin, rotation);
            if (unit.UnitType == UnitType.Tank)
                fx.FireShell(origin, target, targetPoint);
            else
                fx.FireTracer(origin, target, targetPoint);
        }

        static Vector3 ToWorld(Vec2 position)
        {
            return new Vector3(position.X, 0f, position.Z);
        }
    }
}

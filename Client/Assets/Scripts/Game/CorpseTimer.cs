using UnityEngine;

namespace Multiplayer.Game
{
    // Lives on a detached death animation. Waits for the Dead state to finish, or for the clip
    // length to elapse if the state is never reached, holds, then destroys the object.
    public sealed class CorpseTimer : MonoBehaviour
    {
        const float FallbackMargin = 1f;

        Animator animator;
        int deadStateHash;
        float holdSeconds;
        float fallbackDeadline;
        float destroyAt;
        bool clipDone;

        public static CorpseTimer Attach(GameObject corpse, Animator animator, string deadName, float holdSeconds)
        {
            CorpseTimer timer = corpse.AddComponent<CorpseTimer>();
            timer.animator = animator;
            timer.deadStateHash = Animator.StringToHash(deadName);
            timer.holdSeconds = Mathf.Max(0f, holdSeconds);

            float speed = animator != null && animator.speed > 0f ? animator.speed : 1f;
            timer.fallbackDeadline = Time.time + ClipLength(animator, deadName) / speed + FallbackMargin;
            return timer;
        }

        static float ClipLength(Animator animator, string clipName)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
                return 0f;

            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip != null && clip.name == clipName)
                    return clip.length;
            }
            return 0f;
        }

        void Update()
        {
            if (!clipDone)
            {
                if (!DeadFinished() && Time.time < fallbackDeadline)
                    return;

                clipDone = true;
                destroyAt = Time.time + holdSeconds;
                return;
            }

            if (Time.time >= destroyAt)
                Destroy(gameObject);
        }

        bool DeadFinished()
        {
            if (animator == null || !animator.isInitialized)
                return true;

            if (animator.IsInTransition(0))
                return false;

            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            return info.shortNameHash == deadStateHash && info.normalizedTime >= 1f;
        }
    }
}

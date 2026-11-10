using UnityEngine;

namespace Multiplayer.Game
{
    public sealed class FxSet : ScriptableObject
    {
        public const string ResourceName = "FxSet";

        public GameObject muzzleSmall;
        public GameObject muzzleTank;
        public GameObject tracer;
        public GameObject shell;
        public GameObject impactSmall;
        public GameObject impactTank;
        public GameObject explosion;
        public GameObject dustPuff;

        // Units per second.
        public float tracerSpeed = 120f;

        // Seconds from muzzle to target.
        public float shellTravelTime = 0.2f;
    }
}

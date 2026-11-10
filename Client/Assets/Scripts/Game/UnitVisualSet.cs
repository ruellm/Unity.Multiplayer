using System;
using UnityEngine;

namespace Multiplayer.Game
{
    [Serializable]
    public sealed class UnitVisualEntry
    {
        public string Name;
        public GameObject Prefab;
        public Sprite Icon;

        // Camera yaw and zoom for the rendered icon. Zoom 0 marks an entry saved before these existed.
        public float IconYaw;
        public float IconZoom;

        public float Scale = 1f;

        // Model space, before Scale is applied.
        public Vector3 ColliderCenter;
        public Vector3 ColliderSize = Vector3.one;

        // Empty attach: a child named Rifle gets a muzzle at its tip, anything else gets none.
        public string MuzzleAttach;
        public Vector3 MuzzleOffset;

        public string TurretPivotName;
        public float TurretTurnRate = 180f;
        public float DeathClipSpeed = 1f;

        // Animator speed while the Run state plays. 0 marks an entry saved before this existed.
        public float RunClipSpeed;

        // How long the detached death animation stays after its clip ends. 0 means no corpse hold.
        public float CorpseHoldSeconds;
    }

    public sealed class UnitVisualSet : ScriptableObject
    {
        public const string ResourceName = "UnitVisualSet";

        public UnitVisualEntry[] Entries = new UnitVisualEntry[0];

        public UnitVisualEntry Find(string entryName)
        {
            if (Entries == null)
                return null;

            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i] != null && Entries[i].Name == entryName)
                    return Entries[i];
            }
            return null;
        }
    }
}

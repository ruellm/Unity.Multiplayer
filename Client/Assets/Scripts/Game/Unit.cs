using Multiplayer.Protocol;
using RTS;
using UnityEngine;

namespace Multiplayer.Game
{
    public sealed class Unit : MonoBehaviour
    {
        BoxCollider body;
        TeamColor teamColor;
        Color ownerColor = Color.white;
        HealthBar healthBar;
        int health;

        public int EntityId;
        public int OwnerId;
        public UnitType UnitType;
        public ActionState ActionState;
        // State of the later bracket sample. Drives animation and facing so they lead the position
        // instead of trailing it by a tick.
        public ActionState NextActionState;
        public int TargetEntityId;
        public Vec2 Position;
        public byte Yaw;
        public UnitVisual Visual;

        public int Health
        {
            get { return health; }
        }

        // The pickable collider on the Body transform. Its bounds stand in for the model's footprint.
        public BoxCollider Body
        {
            get { return body; }
        }

        public Color OwnerColor
        {
            get { return ownerColor; }
            set
            {
                ownerColor = value;
                ApplyColor();
            }
        }

        public void Initialize(BoxCollider bodyCollider, TeamColor color, HealthBar bar)
        {
            body = bodyCollider;
            teamColor = color;
            healthBar = bar;
            ApplyColor();
        }

        public void SetHealth(int value)
        {
            health = value;
            if (healthBar == null)
                return;

            int max = UnitDefs.Get(UnitType).MaxHealth;
            healthBar.SetFraction(max > 0 ? (float)value / max : 0f);
        }

        // Model materials are shared through TeamColorMaterialCache, so there is no per-unit material to tint.
        public void SetFlash(Color color, float amount)
        {
        }

        void ApplyColor()
        {
            if (teamColor != null)
                teamColor.SetTeamColor(ownerColor);
        }
    }
}

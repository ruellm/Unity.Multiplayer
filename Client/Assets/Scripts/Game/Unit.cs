using Multiplayer.Protocol;
using UnityEngine;

namespace Multiplayer.Game
{
    public sealed class Unit : MonoBehaviour
    {
        Renderer bodyRenderer;
        Material bodyMaterial;
        Color ownerColor = Color.white;
        HealthBar healthBar;
        int health;

        public int EntityId;
        public int OwnerId;
        public UnitType UnitType;
        public ActionState ActionState;
        public int TargetEntityId;
        public Vec2 Position;
        public UnitVisual Visual;

        public int Health
        {
            get { return health; }
        }

        public Renderer Body
        {
            get { return bodyRenderer; }
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

        public void Initialize(Renderer body, HealthBar bar)
        {
            bodyRenderer = body;
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

        public void SetFlash(Color color, float amount)
        {
            if (bodyMaterial != null)
                bodyMaterial.color = Color.Lerp(ownerColor, color, amount);
        }

        void OnDestroy()
        {
            if (bodyMaterial != null)
                Destroy(bodyMaterial);
        }

        void ApplyColor()
        {
            if (bodyRenderer == null)
                return;

            if (bodyMaterial == null)
                bodyMaterial = bodyRenderer.material;

            bodyMaterial.color = ownerColor;
        }
    }
}

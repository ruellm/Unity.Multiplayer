using UnityEngine;

namespace Multiplayer.Game
{
    public sealed class Unit : MonoBehaviour
    {
        Renderer bodyRenderer;
        Material bodyMaterial;
        Color ownerColor = Color.white;

        public int EntityId;
        public int OwnerId;

        public Color OwnerColor
        {
            get { return ownerColor; }
            set
            {
                ownerColor = value;
                ApplyColor();
            }
        }

        void Awake()
        {
            bodyRenderer = GetComponentInChildren<Renderer>();
            ApplyColor();
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

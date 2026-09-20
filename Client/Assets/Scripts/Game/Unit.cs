using UnityEngine;

namespace Multiplayer.Game
{
    public sealed class Unit : MonoBehaviour
    {
        Renderer bodyRenderer;
        Material bodyMaterial;
        Color ownerColor = Color.white;

        public Color OwnerColor
        {
            get { return ownerColor; }
            set
            {
                ownerColor = value;
                ApplyColor();
            }
        }

        public bool HasMoveTarget { get; private set; }
        public Vector3 MoveTarget { get; private set; }

        public void SetMoveTarget(Vector3 target)
        {
            MoveTarget = target;
            HasMoveTarget = true;
        }

        public void ClearMoveTarget()
        {
            HasMoveTarget = false;
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

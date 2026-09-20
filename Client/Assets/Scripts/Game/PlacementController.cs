using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Game
{
    public sealed class PlacementController : MonoBehaviour
    {
        const float CubeSize = 1f;

        static readonly Color IdleButtonColor = Color.white;
        static readonly Color ArmedButtonColor = new Color(1f, 0.8f, 0.3f);

        Button addButton;
        Material unitMaterial;
        Color ownerColor;
        int spawnCount;

        public bool IsArmed { get; private set; }

        public void Initialize(Button button, Material material, Color color)
        {
            addButton = button;
            unitMaterial = material;
            ownerColor = color;
            addButton.onClick.AddListener(ToggleArmed);
            SetArmed(false);
        }

        public void HandleClick(WorldHit hit)
        {
            if (!IsArmed || hit.Kind != WorldHitKind.Ground)
                return;

            Spawn(hit.Point);
            SetArmed(false);
        }

        public void Cancel()
        {
            SetArmed(false);
        }

        void OnDestroy()
        {
            if (addButton != null)
                addButton.onClick.RemoveListener(ToggleArmed);
        }

        void ToggleArmed()
        {
            SetArmed(!IsArmed);
        }

        void SetArmed(bool armed)
        {
            IsArmed = armed;
            if (addButton != null && addButton.image != null)
                addButton.image.color = armed ? ArmedButtonColor : IdleButtonColor;
        }

        void Spawn(Vector3 groundPoint)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawnCount++;
            cube.name = "Unit " + spawnCount;
            cube.transform.localScale = Vector3.one * CubeSize;
            cube.transform.position = new Vector3(groundPoint.x, groundPoint.y + CubeSize * 0.5f, groundPoint.z);
            cube.GetComponent<Renderer>().sharedMaterial = unitMaterial;

            Unit unit = cube.AddComponent<Unit>();
            unit.OwnerColor = ownerColor;
            cube.AddComponent<UnitMover>();
        }
    }
}

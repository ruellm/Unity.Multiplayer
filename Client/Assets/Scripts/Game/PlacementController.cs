using Multiplayer.Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Game
{
    public sealed class PlacementController : MonoBehaviour
    {
        static readonly Color IdleButtonColor = Color.white;
        static readonly Color ArmedButtonColor = new Color(1f, 0.8f, 0.3f);

        Button addButton;
        NetworkClient network;

        public bool IsArmed { get; private set; }

        public void Initialize(Button button, NetworkClient client)
        {
            addButton = button;
            network = client;
            addButton.onClick.AddListener(ToggleArmed);
            SetArmed(false);
        }

        public void HandleClick(WorldHit hit)
        {
            if (!IsArmed || hit.Kind != WorldHitKind.Ground)
                return;

            SpawnRequestMessage request;
            request.Position = new Vec2(hit.Point.x, hit.Point.z);
            network.Send(MessageId.SpawnRequest, request);
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
    }
}

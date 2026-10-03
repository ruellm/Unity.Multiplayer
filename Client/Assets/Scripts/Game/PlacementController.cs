using Multiplayer.Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Game
{
    public sealed class PlacementController : MonoBehaviour
    {
        static readonly Color IdleButtonColor = Color.white;
        static readonly Color ArmedButtonColor = new Color(1f, 0.8f, 0.3f);

        Button placeButton;
        NetworkClient network;

        public bool IsArmed { get; private set; }

        public void Initialize(Button button, NetworkClient client)
        {
            placeButton = button;
            network = client;
            placeButton.onClick.AddListener(ToggleArmed);
            SetArmed(false);
        }

        public void SetAvailable(bool available)
        {
            if (!available)
                SetArmed(false);

            placeButton.interactable = available;
        }

        public void HandleClick(WorldHit hit)
        {
            if (!IsArmed || hit.Kind != WorldHitKind.Ground)
                return;

            PlaceStructureRequestMessage request;
            request.Position = new Vec2(hit.Point.x, hit.Point.z);
            network.Send(MessageId.PlaceStructureRequest, request);
            SetArmed(false);
        }

        public void Cancel()
        {
            SetArmed(false);
        }

        void OnDestroy()
        {
            if (placeButton != null)
                placeButton.onClick.RemoveListener(ToggleArmed);
        }

        void ToggleArmed()
        {
            SetArmed(!IsArmed);
        }

        void SetArmed(bool armed)
        {
            IsArmed = armed;
            if (placeButton != null && placeButton.image != null)
                placeButton.image.color = armed ? ArmedButtonColor : IdleButtonColor;
        }
    }
}

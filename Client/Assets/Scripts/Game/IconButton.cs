using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Game
{
    // A square uGUI button with an icon and a short label. Disabling greys the icon and label too,
    // since Button only tints its target graphic.
    public sealed class IconButton
    {
        static readonly Color IdleBackground = Color.white;
        static readonly Color ArmedBackground = new Color(1f, 0.8f, 0.3f);
        const float DisabledAlpha = 0.35f;

        readonly Image background;
        readonly Image icon;
        readonly Text label;
        readonly Color labelColor;

        public readonly Button Button;

        public IconButton(Button button, Image backgroundImage, Image iconImage, Text labelText)
        {
            Button = button;
            background = backgroundImage;
            icon = iconImage;
            label = labelText;
            labelColor = labelText.color;
        }

        public void SetInteractable(bool value)
        {
            Button.interactable = value;
            float alpha = value ? 1f : DisabledAlpha;

            Color iconColor = icon.color;
            iconColor.a = alpha;
            icon.color = iconColor;

            Color textColor = labelColor;
            textColor.a *= alpha;
            label.color = textColor;
        }

        public void SetArmed(bool armed)
        {
            background.color = armed ? ArmedBackground : IdleBackground;
        }
    }
}

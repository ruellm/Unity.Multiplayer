using System.Collections.Generic;
using Multiplayer.Protocol;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Multiplayer.Game
{
    public sealed class DebugOverlay : MonoBehaviour
    {
        sealed class Label
        {
            public RectTransform Rect;
            public Text Text;
            public ActionState ActionState;
            public int TargetEntityId;
            public int Health;
            public int TicksUntilShot;
        }

        const int FontSize = 15;
        const int EventFontSize = 18;
        const float ScreenOffset = 8f;

        static readonly Color BackgroundColor = new Color(0f, 0f, 0f, 0.6f);

        readonly Dictionary<int, Label> labels = new Dictionary<int, Label>();
        readonly Stack<Label> pool = new Stack<Label>();

        RectTransform root;
        Canvas canvas;
        Font font;
        Camera worldCamera;
        WorldView worldView;
        Text eventText;
        int shownQueued = -1;
        int shownReleased = -1;
        int shownDropped = -1;
        bool visible;

        public void Initialize(RectTransform overlayRoot, Font labelFont, Camera camera, WorldView view)
        {
            root = overlayRoot;
            canvas = overlayRoot.GetComponentInParent<Canvas>();
            font = labelFont;
            worldCamera = camera;
            worldView = view;

            worldView.EntityRemoved += OnEntityRemoved;
            eventText = CreateEventText();
            root.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (worldView != null)
                worldView.EntityRemoved -= OnEntityRemoved;
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (root == null || keyboard == null || !keyboard.f3Key.wasPressedThisFrame)
                return;

            visible = !visible;
            root.gameObject.SetActive(visible);
        }

        void LateUpdate()
        {
            if (!visible)
                return;

            WriteEvents(worldView.Events);

            float scale = canvas.scaleFactor;
            foreach (Unit unit in worldView.Units)
            {
                Label label;
                if (!labels.TryGetValue(unit.EntityId, out label))
                {
                    label = pool.Count > 0 ? pool.Pop() : CreateLabel();
                    labels[unit.EntityId] = label;
                    Write(label, unit);
                }
                else if (label.ActionState != unit.ActionState
                    || label.TargetEntityId != unit.TargetEntityId
                    || label.Health != unit.Health
                    || label.TicksUntilShot != ShownTicksUntilShot(unit))
                {
                    Write(label, unit);
                }

                Vector3 screen = worldCamera.WorldToScreenPoint(unit.transform.position);
                bool inFront = screen.z > 0f;
                if (label.Rect.gameObject.activeSelf != inFront)
                    label.Rect.gameObject.SetActive(inFront);

                if (inFront)
                    label.Rect.anchoredPosition = new Vector2(screen.x / scale, screen.y / scale - ScreenOffset);
            }
        }

        void OnEntityRemoved(Unit unit)
        {
            Label label;
            if (!labels.TryGetValue(unit.EntityId, out label))
                return;

            labels.Remove(unit.EntityId);
            label.Rect.gameObject.SetActive(false);
            pool.Push(label);
        }

        void WriteEvents(EventQueue events)
        {
            if (events.Count == shownQueued && events.ReleasedCount == shownReleased && events.DroppedCount == shownDropped)
                return;

            shownQueued = events.Count;
            shownReleased = events.ReleasedCount;
            shownDropped = events.DroppedCount;

            string last = "none";
            if (events.ReleasedCount > 0)
            {
                GameEventMessage gameEvent = events.LastReleased;
                switch ((GameEventType)gameEvent.EventType)
                {
                    case GameEventType.EntityDied:
                        last = "EntityDied #" + gameEvent.EntityDied.EntityId + " " + (DeathCause)gameEvent.EntityDied.Cause;
                        break;
                    case GameEventType.Explosion:
                        Vec2 at = gameEvent.Explosion.Position;
                        last = "Explosion " + (ExplosionType)gameEvent.Explosion.ExplosionType + " at (" + at.X.ToString("0.0") + ", " + at.Z.ToString("0.0") + ")";
                        break;
                }
                last += ", tick " + gameEvent.Tick;
            }

            eventText.text = "Events queued " + shownQueued + ", released " + shownReleased + ", dropped " + shownDropped
                + "\nLast released: " + last;
        }

        Text CreateEventText()
        {
            GameObject textObject = new GameObject("Event Queue");
            textObject.layer = root.gameObject.layer;
            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.SetParent(root, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(20f, 20f);
            rect.sizeDelta = new Vector2(900f, 52f);

            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = EventFontSize;
            text.alignment = TextAnchor.LowerLeft;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static void Write(Label label, Unit unit)
        {
            label.ActionState = unit.ActionState;
            label.TargetEntityId = unit.TargetEntityId;
            label.Health = unit.Health;
            label.TicksUntilShot = ShownTicksUntilShot(unit);

            string target = unit.TargetEntityId != 0 ? "#" + unit.TargetEntityId : "none";
            string text = "#" + unit.EntityId + " " + unit.UnitType + " P" + unit.OwnerId
                + "\n" + unit.ActionState + ", target " + target
                + "\nHP " + unit.Health + "/" + UnitDefs.Get(unit.UnitType).MaxHealth;

            // The schedule is a function of the tick alone, so two clients reading different numbers
            // here are simply drawing different ticks at that moment.
            if (unit.Visual.CanFire)
            {
                text += label.TicksUntilShot >= 0
                    ? "\nfire in " + label.TicksUntilShot + "/" + unit.Visual.Firing.CooldownTicks + " ticks"
                    : "\nfire off";
            }

            label.Text.text = text;
        }

        static int ShownTicksUntilShot(Unit unit)
        {
            FiringPhase firing = unit.Visual.Firing;
            return firing.Active ? firing.TicksUntilShot : -1;
        }

        Label CreateLabel()
        {
            int uiLayer = root.gameObject.layer;

            GameObject labelObject = new GameObject("Debug Label");
            labelObject.layer = uiLayer;
            RectTransform rect = labelObject.AddComponent<RectTransform>();
            rect.SetParent(root, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 1f);

            // Nothing here may be a raycast target, or labels would swallow world clicks.
            Image background = labelObject.AddComponent<Image>();
            background.color = BackgroundColor;
            background.raycastTarget = false;

            VerticalLayoutGroup layout = labelObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 3, 3);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = labelObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject textObject = new GameObject("Text");
            textObject.layer = uiLayer;
            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.SetParent(rect, false);

            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = FontSize;
            text.alignment = TextAnchor.UpperLeft;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            Label label = new Label();
            label.Rect = rect;
            label.Text = text;
            return label;
        }
    }
}

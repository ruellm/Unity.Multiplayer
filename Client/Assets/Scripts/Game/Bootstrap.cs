using Multiplayer.Protocol;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Multiplayer.Game
{
    public sealed class Bootstrap : MonoBehaviour
    {
        const string LitShaderName = "Universal Render Pipeline/Lit";
        const string UnlitShaderName = "Universal Render Pipeline/Unlit";
        const string UiFontName = "LegacyRuntime.ttf";
        const float GroundSize = 100f;
        const float PlaneMeshSize = 10f;
        const int RingTextureSize = 128;
        const float BarMargin = 20f;
        const float ButtonSpacing = 10f;
        const float IconButtonSize = 96f;
        const float IconSize = 60f;
        const float IconLabelHeight = 20f;

        // Shared with the editor icon renderer so icons are lit like the game.
        public const float SunIntensity = 1.2f;
        public static readonly Vector3 SunEuler = new Vector3(50f, -30f, 0f);

        static readonly Color GroundColor = new Color(0.32f, 0.42f, 0.3f);
        static readonly Color RingColor = new Color(0.3f, 1f, 0.4f);
        static readonly Color AttackLineColor = new Color(1f, 0.25f, 0.2f);
        static readonly Color UiTextColor = new Color(0.1f, 0.1f, 0.1f);
        static readonly Vector2 SmallButtonSize = new Vector2(IconButtonSize, (IconButtonSize - ButtonSpacing) * 0.5f);

        sealed class UiHandles
        {
            public IconButton PlaceHQ;
            public IconButton PlaceTower;
            public IconButton PlaceWall;
            public Button Rotate;
            public Button Cancel;
            public GameObject ProductionBar;
            public IconButton BuildSoldier;
            public IconButton BuildTank;
            public Text Status;
            public RectTransform DebugOverlayRoot;
        }

        WorldInput worldInput;
        PlacementController placement;
        SelectionController selection;
        ProductionController production;
        DebugOverlay debugOverlay;
        NetworkClient network;
        WorldView worldView;
        readonly PlayerRoster roster = new PlayerRoster();
        Text statusText;

        void Awake()
        {
            Application.runInBackground = true;

            Shader litShader = FindShader(LitShaderName);
            Shader unlitShader = FindShader(UnlitShaderName);
            if (litShader == null || unlitShader == null)
            {
                enabled = false;
                return;
            }

            UnitVisualSet visuals = Resources.Load<UnitVisualSet>(UnitVisualSet.ResourceName);
            if (visuals == null)
            {
                Debug.LogError("Bootstrap: Resources/" + UnitVisualSet.ResourceName + ".asset not found. Run Tools/RTS/Create Unit Visual Set in the editor. Scene was not built.");
                enabled = false;
                return;
            }

            Material groundMaterial = CreateMaterial(litShader, "Ground", GroundColor);
            Material ringMaterial = CreateRingMaterial(unlitShader);
            Material healthBarMaterial = CreateMaterial(unlitShader, "Health Bar", Color.white);
            Material attackLineMaterial = CreateMaterial(unlitShader, "Attack Line", AttackLineColor);
            Material effectMaterial = CreateEffectMaterial(unlitShader);

            BuildLight();
            BuildGround(groundMaterial);
            Camera worldCamera = BuildCamera();
            BuildEventSystem();
            UiHandles ui = BuildUi(visuals);
            statusText = ui.Status;

            worldInput = gameObject.AddComponent<WorldInput>();
            placement = gameObject.AddComponent<PlacementController>();
            selection = gameObject.AddComponent<SelectionController>();
            production = gameObject.AddComponent<ProductionController>();
            network = gameObject.AddComponent<NetworkClient>();
            worldView = gameObject.AddComponent<WorldView>();
            debugOverlay = gameObject.AddComponent<DebugOverlay>();

            worldInput.Initialize(worldCamera);
            worldView.Initialize(visuals, healthBarMaterial, effectMaterial, roster, worldCamera);
            placement.Initialize(ui.PlaceHQ, ui.PlaceTower, ui.PlaceWall, ui.Rotate, ui.Cancel, visuals, ringMaterial, worldInput, network, worldView);
            selection.Initialize(ringMaterial, attackLineMaterial, network, worldView);
            production.Initialize(ui.ProductionBar, ui.BuildSoldier, ui.BuildTank, selection, network);
            debugOverlay.Initialize(ui.DebugOverlayRoot, statusText.font, worldCamera, worldView);

            worldInput.Clicked += RouteClick;
            placement.Changed += RefreshStatus;
            worldView.EntityRemoved += selection.OnUnitRemoved;
            worldView.EntitiesChanged += RefreshStatus;
            network.StateChanged += OnNetworkStateChanged;
            network.Welcomed += OnWelcomed;
            network.PlayerJoined += roster.Add;
            network.PlayerLeft += roster.Remove;
            network.SnapshotReceived += worldView.Apply;
            network.GameEventReceived += worldView.Enqueue;
            RefreshStatus();
        }

        void OnDestroy()
        {
            if (worldInput != null)
                worldInput.Clicked -= RouteClick;

            if (placement != null)
                placement.Changed -= RefreshStatus;

            if (worldView != null && selection != null)
                worldView.EntityRemoved -= selection.OnUnitRemoved;

            if (worldView != null)
                worldView.EntitiesChanged -= RefreshStatus;

            if (network != null)
            {
                network.StateChanged -= OnNetworkStateChanged;
                network.Welcomed -= OnWelcomed;
                network.PlayerJoined -= roster.Add;
                network.PlayerLeft -= roster.Remove;
                network.SnapshotReceived -= worldView.Apply;
                network.GameEventReceived -= worldView.Enqueue;
            }
        }

        // Only place that decides what a world click means.
        void RouteClick(WorldHit hit)
        {
            if (hit.Kind == WorldHitKind.Cancel || hit.Button == WorldButton.Secondary)
            {
                if (placement.IsArmed)
                    placement.Cancel();
                else if (hit.Kind != WorldHitKind.Unit || !selection.TryAttack(hit.Unit))
                    selection.ClearSelection();
                return;
            }

            if (placement.IsArmed)
                placement.HandleClick(hit);
            else
                selection.HandleClick(hit);
        }

        void OnNetworkStateChanged(NetConnectionState state)
        {
            if (state != NetConnectionState.Connected)
            {
                placement.Cancel();
                worldView.Clear();
                roster.Clear();
            }

            RefreshStatus();
        }

        void OnWelcomed()
        {
            RefreshStatus();
        }

        void RefreshStatus()
        {
            int owned = 0;
            bool hasHQ = false;
            if (network.IsWelcomed)
                owned = worldView.CountOwnedBy(network.PlayerId, out hasHQ);

            placement.SetAvailable(network.IsWelcomed, hasHQ);

            string status = network.State + " " + network.Endpoint;
            if (network.IsWelcomed)
                status += ", Player " + network.PlayerId;
            status += ", Entities " + worldView.EntityCount;
            if (network.IsWelcomed)
                status += ", Mine " + owned + ", HQ " + (hasHQ ? "placed" : "not placed");
            if (placement.IsArmed)
                status += ", Placing " + placement.PendingType + " yaw " + placement.YawDegrees.ToString("0") + " (R rotates)";
            if (placement.Notice != null)
                status += ", " + placement.Notice;
            statusText.text = status;
        }

        static Shader FindShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                Debug.LogError("Bootstrap: shader '" + shaderName + "' not found. Check that URP is installed and the shader is not stripped. Scene was not built.");
            return shader;
        }

        static Material CreateMaterial(Shader shader, string materialName, Color color)
        {
            Material material = new Material(shader);
            material.name = materialName;
            material.color = color;
            return material;
        }

        static Material CreateRingMaterial(Shader unlitShader)
        {
            Material material = CreateMaterial(unlitShader, "Selection Ring", RingColor);
            material.mainTexture = CreateRingTexture();
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            return material;
        }

        // Shared by tracers and explosions. Each tints and fades it through its own property block.
        static Material CreateEffectMaterial(Shader unlitShader)
        {
            Material material = CreateMaterial(unlitShader, "Effect", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        static Texture2D CreateRingTexture()
        {
            const float outerRadius = 0.48f;
            const float innerRadius = 0.41f;

            Texture2D texture = new Texture2D(RingTextureSize, RingTextureSize, TextureFormat.RGBA32, false);
            texture.name = "Selection Ring";
            texture.wrapMode = TextureWrapMode.Clamp;

            Color32[] pixels = new Color32[RingTextureSize * RingTextureSize];
            Color32 solid = new Color32(255, 255, 255, 255);
            Color32 clear = new Color32(255, 255, 255, 0);

            for (int y = 0; y < RingTextureSize; y++)
            {
                for (int x = 0; x < RingTextureSize; x++)
                {
                    float u = (x + 0.5f) / RingTextureSize - 0.5f;
                    float v = (y + 0.5f) / RingTextureSize - 0.5f;
                    float radius = Mathf.Sqrt(u * u + v * v);
                    bool inside = radius >= innerRadius && radius <= outerRadius;
                    pixels[y * RingTextureSize + x] = inside ? solid : clear;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static void BuildLight()
        {
            GameObject lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(SunEuler);

            Light sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = SunIntensity;
            sun.shadows = LightShadows.Soft;

            lightObject.AddComponent<UniversalAdditionalLightData>();
        }

        static void BuildGround(Material material)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            float scale = GroundSize / PlaneMeshSize;
            ground.transform.localScale = new Vector3(scale, 1f, scale);
            ground.GetComponent<Renderer>().sharedMaterial = material;
            ground.AddComponent<Ground>();
        }

        static Camera BuildCamera()
        {
            GameObject cameraObject = new GameObject("RTS Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 18f, -14f), Quaternion.Euler(50f, 0f, 0f));

            Camera worldCamera = cameraObject.AddComponent<Camera>();
            worldCamera.fieldOfView = 50f;
            worldCamera.nearClipPlane = 0.3f;
            worldCamera.farClipPlane = 300f;

            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraObject.AddComponent<AudioListener>();
            return worldCamera;
        }

        static void BuildEventSystem()
        {
            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }

        static UiHandles BuildUi(UnitVisualSet visuals)
        {
            int uiLayer = LayerMask.NameToLayer("UI");

            GameObject canvasObject = new GameObject("Canvas");
            canvasObject.layer = uiLayer;
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            UiHandles ui = new UiHandles();

            // Built first so the debug labels draw underneath the bars and status text.
            ui.DebugOverlayRoot = BuildDebugOverlayRoot(canvasObject.transform, uiLayer);

            float step = IconButtonSize + ButtonSpacing;

            RectTransform buildBar = BuildBar(canvasObject.transform, uiLayer, "Build Bar", new Vector2(BarMargin, BarMargin), 3);
            ui.PlaceHQ = BuildIconButton(buildBar, uiLayer, "HQ", IconFor(visuals, "HQ"), new Vector2(0f, 0f));
            ui.PlaceTower = BuildIconButton(buildBar, uiLayer, "Tower", IconFor(visuals, "Tower"), new Vector2(step, 0f));
            ui.PlaceWall = BuildIconButton(buildBar, uiLayer, "Wall", IconFor(visuals, "Wall"), new Vector2(2f * step, 0f));

            RectTransform productionBar = BuildBar(canvasObject.transform, uiLayer, "Production Bar", new Vector2(BarMargin, BarMargin + step), 2);
            ui.BuildSoldier = BuildIconButton(productionBar, uiLayer, "Soldier", IconFor(visuals, "Soldier"), new Vector2(0f, 0f));
            ui.BuildTank = BuildIconButton(productionBar, uiLayer, "Tank", IconFor(visuals, "Tank"), new Vector2(step, 0f));
            ui.ProductionBar = productionBar.gameObject;

            RectTransform tools = BuildBar(canvasObject.transform, uiLayer, "Placement Tools", new Vector2(BarMargin + 3f * step, BarMargin), 1);
            ui.Cancel = BuildTextButton(tools, uiLayer, "Cancel", new Vector2(0f, 0f), SmallButtonSize);
            ui.Rotate = BuildTextButton(tools, uiLayer, "Rotate", new Vector2(0f, SmallButtonSize.y + ButtonSpacing), SmallButtonSize);

            ui.Status = BuildStatusText(canvasObject.transform, uiLayer);
            return ui;
        }

        static Sprite IconFor(UnitVisualSet visuals, string entryName)
        {
            UnitVisualEntry entry = visuals.Find(entryName);
            return entry != null ? entry.Icon : null;
        }

        static RectTransform BuildDebugOverlayRoot(Transform canvas, int uiLayer)
        {
            GameObject rootObject = new GameObject("Debug Overlay");
            rootObject.layer = uiLayer;
            RectTransform rootRect = rootObject.AddComponent<RectTransform>();
            rootRect.SetParent(canvas, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            return rootRect;
        }

        // Bottom-left anchored container sized for a row of icon buttons.
        static RectTransform BuildBar(Transform canvas, int uiLayer, string name, Vector2 position, int slots)
        {
            GameObject barObject = new GameObject(name);
            barObject.layer = uiLayer;
            RectTransform rect = barObject.AddComponent<RectTransform>();
            rect.SetParent(canvas, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(slots * IconButtonSize + (slots - 1) * ButtonSpacing, IconButtonSize);
            return rect;
        }

        static IconButton BuildIconButton(Transform parent, int uiLayer, string labelText, Sprite icon, Vector2 position)
        {
            GameObject buttonObject = new GameObject(labelText + " Button");
            buttonObject.layer = uiLayer;
            RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.SetParent(parent, false);
            buttonRect.anchorMin = Vector2.zero;
            buttonRect.anchorMax = Vector2.zero;
            buttonRect.pivot = Vector2.zero;
            buttonRect.anchoredPosition = position;
            buttonRect.sizeDelta = new Vector2(IconButtonSize, IconButtonSize);

            Image background = buttonObject.AddComponent<Image>();
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            GameObject iconObject = new GameObject("Icon");
            iconObject.layer = uiLayer;
            RectTransform iconRect = iconObject.AddComponent<RectTransform>();
            iconRect.SetParent(buttonRect, false);
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.anchoredPosition = new Vector2(0f, -(IconButtonSize - IconSize - IconLabelHeight) * 0.5f);
            iconRect.sizeDelta = new Vector2(IconSize, IconSize);
            Image iconImage = iconObject.AddComponent<Image>();
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            iconImage.enabled = icon != null;

            Text label = CreateText(buttonRect, uiLayer, "Label", 16, TextAnchor.MiddleCenter);
            label.text = labelText;
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(0f, IconLabelHeight);

            return new IconButton(button, background, iconImage, label);
        }

        static Button BuildTextButton(Transform parent, int uiLayer, string labelText, Vector2 position, Vector2 size)
        {
            GameObject buttonObject = new GameObject(labelText + " Button");
            buttonObject.layer = uiLayer;
            RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.SetParent(parent, false);
            buttonRect.anchorMin = Vector2.zero;
            buttonRect.anchorMax = Vector2.zero;
            buttonRect.pivot = Vector2.zero;
            buttonRect.anchoredPosition = position;
            buttonRect.sizeDelta = size;

            Image buttonImage = buttonObject.AddComponent<Image>();
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            Text label = CreateText(buttonRect, uiLayer, "Label", 18, TextAnchor.MiddleCenter);
            label.text = labelText;
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            return button;
        }

        static Text BuildStatusText(Transform canvas, int uiLayer)
        {
            Text status = CreateText(canvas, uiLayer, "Status", 22, TextAnchor.UpperRight);
            RectTransform rect = status.rectTransform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-20f, -20f);
            rect.sizeDelta = new Vector2(1000f, 32f);
            status.color = Color.white;
            return status;
        }

        static Text CreateText(Transform parent, int uiLayer, string objectName, int fontSize, TextAnchor alignment)
        {
            GameObject textObject = new GameObject(objectName);
            textObject.layer = uiLayer;
            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.SetParent(parent, false);

            Text text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>(UiFontName);
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = UiTextColor;
            text.raycastTarget = false;
            return text;
        }
    }
}

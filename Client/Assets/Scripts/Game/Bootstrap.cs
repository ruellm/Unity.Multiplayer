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
        const float ButtonSpacing = 10f;

        static readonly Color GroundColor = new Color(0.32f, 0.42f, 0.3f);
        static readonly Color RingColor = new Color(0.3f, 1f, 0.4f);
        static readonly Color UiTextColor = new Color(0.1f, 0.1f, 0.1f);
        static readonly Vector2 ButtonSize = new Vector2(240f, 56f);

        WorldInput worldInput;
        PlacementController placement;
        SelectionController selection;
        ProductionController production;
        NetworkClient network;
        WorldView worldView;
        readonly PlayerRoster roster = new PlayerRoster();
        Text statusText;
        int shownEntityCount = -1;
        int shownOwnedCount = -1;
        bool shownHasStructure;

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

            Material groundMaterial = CreateMaterial(litShader, "Ground", GroundColor);
            Material unitMaterial = CreateMaterial(litShader, "Unit", Color.white);
            Material ringMaterial = CreateRingMaterial(unlitShader);
            Material healthBarMaterial = CreateMaterial(unlitShader, "Health Bar", Color.white);

            BuildLight();
            BuildGround(groundMaterial);
            Camera worldCamera = BuildCamera();
            BuildEventSystem();
            Button placeButton;
            GameObject productionPanel;
            Button soldierButton;
            Button tankButton;
            BuildUi(out placeButton, out productionPanel, out soldierButton, out tankButton, out statusText);

            worldInput = gameObject.AddComponent<WorldInput>();
            placement = gameObject.AddComponent<PlacementController>();
            selection = gameObject.AddComponent<SelectionController>();
            production = gameObject.AddComponent<ProductionController>();
            network = gameObject.AddComponent<NetworkClient>();
            worldView = gameObject.AddComponent<WorldView>();

            worldInput.Initialize(worldCamera);
            placement.Initialize(placeButton, network);
            selection.Initialize(ringMaterial, network);
            production.Initialize(productionPanel, soldierButton, tankButton, selection, network);
            worldView.Initialize(unitMaterial, healthBarMaterial, roster, worldCamera);

            worldInput.Clicked += RouteClick;
            worldView.EntityRemoved += selection.OnUnitRemoved;
            network.StateChanged += OnNetworkStateChanged;
            network.Welcomed += OnWelcomed;
            network.PlayerJoined += roster.Add;
            network.PlayerLeft += roster.Remove;
            network.SnapshotReceived += OnSnapshot;
            RefreshStatus();
        }

        void OnDestroy()
        {
            if (worldInput != null)
                worldInput.Clicked -= RouteClick;

            if (worldView != null && selection != null)
                worldView.EntityRemoved -= selection.OnUnitRemoved;

            if (network != null)
            {
                network.StateChanged -= OnNetworkStateChanged;
                network.Welcomed -= OnWelcomed;
                network.PlayerJoined -= roster.Add;
                network.PlayerLeft -= roster.Remove;
                network.SnapshotReceived -= OnSnapshot;
            }
        }

        // Only place that decides what a world click means.
        void RouteClick(WorldHit hit)
        {
            if (hit.Kind == WorldHitKind.Cancel)
            {
                if (placement.IsArmed)
                    placement.Cancel();
                else
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
                worldView.Clear();
                roster.Clear();
            }

            RefreshStatus();
        }

        void OnWelcomed()
        {
            RefreshStatus();
        }

        void OnSnapshot(WorldSnapshotMessage snapshot)
        {
            worldView.Apply(snapshot);

            bool hasStructure;
            int owned = worldView.CountOwnedBy(network.PlayerId, out hasStructure);
            if (worldView.EntityCount != shownEntityCount || owned != shownOwnedCount || hasStructure != shownHasStructure)
                RefreshStatus();
        }

        void RefreshStatus()
        {
            shownEntityCount = worldView.EntityCount;
            shownOwnedCount = 0;
            shownHasStructure = false;
            if (network.IsWelcomed)
                shownOwnedCount = worldView.CountOwnedBy(network.PlayerId, out shownHasStructure);

            placement.SetAvailable(network.IsWelcomed && !shownHasStructure);

            string status = network.State + " " + network.Endpoint;
            if (network.IsWelcomed)
                status += ", Player " + network.PlayerId;
            status += ", Entities " + shownEntityCount;
            if (network.IsWelcomed)
                status += ", Mine " + shownOwnedCount + ", Structure " + (shownHasStructure ? "placed" : "not placed");
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
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Light sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
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

        static void BuildUi(out Button placeButton, out GameObject productionPanel, out Button soldierButton, out Button tankButton, out Text statusText)
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

            placeButton = BuildButton(canvasObject.transform, uiLayer, "Place Structure", new Vector2(20f, -20f));

            float step = ButtonSize.y + ButtonSpacing;
            RectTransform panel = BuildProductionPanel(canvasObject.transform, uiLayer, new Vector2(20f, -20f - step));
            soldierButton = BuildButton(panel, uiLayer, "Build Soldier", Vector2.zero);
            tankButton = BuildButton(panel, uiLayer, "Build Tank", new Vector2(0f, -step));
            productionPanel = panel.gameObject;

            statusText = BuildStatusText(canvasObject.transform, uiLayer);
        }

        static RectTransform BuildProductionPanel(Transform canvas, int uiLayer, Vector2 position)
        {
            GameObject panelObject = new GameObject("Production Panel");
            panelObject.layer = uiLayer;
            RectTransform panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.SetParent(canvas, false);
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = position;
            panelRect.sizeDelta = new Vector2(ButtonSize.x, ButtonSize.y * 2f + ButtonSpacing);
            return panelRect;
        }

        static Button BuildButton(Transform parent, int uiLayer, string labelText, Vector2 position)
        {
            GameObject buttonObject = new GameObject(labelText + " Button");
            buttonObject.layer = uiLayer;
            RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.SetParent(parent, false);
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(0f, 1f);
            buttonRect.pivot = new Vector2(0f, 1f);
            buttonRect.anchoredPosition = position;
            buttonRect.sizeDelta = ButtonSize;

            Image buttonImage = buttonObject.AddComponent<Image>();
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            Text label = CreateText(buttonRect, uiLayer, "Label", 26, TextAnchor.MiddleCenter);
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

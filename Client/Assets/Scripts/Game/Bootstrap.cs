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
        const float GroundSize = 100f;
        const float PlaneMeshSize = 10f;
        const int RingTextureSize = 128;

        static readonly Color GroundColor = new Color(0.32f, 0.42f, 0.3f);
        static readonly Color LocalPlayerColor = new Color(0.2f, 0.45f, 0.9f);
        static readonly Color RingColor = new Color(0.3f, 1f, 0.4f);

        WorldInput worldInput;
        PlacementController placement;
        SelectionController selection;

        void Awake()
        {
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

            BuildLight();
            BuildGround(groundMaterial);
            Camera worldCamera = BuildCamera();
            BuildEventSystem();
            Button addButton = BuildUi();

            worldInput = gameObject.AddComponent<WorldInput>();
            placement = gameObject.AddComponent<PlacementController>();
            selection = gameObject.AddComponent<SelectionController>();

            worldInput.Initialize(worldCamera);
            placement.Initialize(addButton, unitMaterial, LocalPlayerColor);
            selection.Initialize(ringMaterial);

            worldInput.Clicked += RouteClick;
        }

        void OnDestroy()
        {
            if (worldInput != null)
                worldInput.Clicked -= RouteClick;
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

        static Button BuildUi()
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

            GameObject buttonObject = new GameObject("Add Cube Button");
            buttonObject.layer = uiLayer;
            RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.SetParent(canvasObject.transform, false);
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(0f, 1f);
            buttonRect.pivot = new Vector2(0f, 1f);
            buttonRect.anchoredPosition = new Vector2(20f, -20f);
            buttonRect.sizeDelta = new Vector2(200f, 56f);

            Image buttonImage = buttonObject.AddComponent<Image>();
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            GameObject labelObject = new GameObject("Label");
            labelObject.layer = uiLayer;
            RectTransform labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.SetParent(buttonRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.AddComponent<Text>();
            label.text = "Add Cube";
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 26;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.1f, 0.1f, 0.1f);
            label.raycastTarget = false;

            return button;
        }
    }
}

using System;
using Multiplayer.Protocol;
using RTS;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Multiplayer.Game
{
    public sealed class PlacementController : MonoBehaviour
    {
        const float NoticeSeconds = 2f;
        const float RingLift = 0.03f;
        const int IgnoreRaycastLayer = 2;
        const string BlockedNotice = "Can't place here";

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color AllowedColor = new Color(0.3f, 1f, 0.4f);
        static readonly Color BlockedColor = new Color(1f, 0.25f, 0.2f);

        IconButton hqButton;
        IconButton towerButton;
        IconButton wallButton;
        Button rotateButton;
        Button cancelButton;
        UnitVisualSet visuals;
        Material ringMaterial;
        WorldInput worldInput;
        NetworkClient network;
        WorldView worldView;

        UnitType pendingType;
        float yawDegrees;
        GameObject ghost;
        Transform ring;
        Renderer ringRenderer;
        MaterialPropertyBlock ringBlock;
        bool hasGroundPoint;
        Vector3 groundPoint;
        bool allowed;
        float noticeUntil;

        public bool IsArmed { get; private set; }

        public UnitType PendingType
        {
            get { return pendingType; }
        }

        public float YawDegrees
        {
            get { return yawDegrees; }
        }

        // Text for the status line while a blocked click is still fresh, otherwise null.
        public string Notice
        {
            get { return noticeUntil > 0f ? BlockedNotice : null; }
        }

        // Raised when the pending type, yaw, armed state or notice changes.
        public event Action Changed;

        public void Initialize(IconButton hq, IconButton tower, IconButton wall, Button rotate, Button cancel, UnitVisualSet visualSet, Material ring, WorldInput input, NetworkClient client, WorldView view)
        {
            hqButton = hq;
            towerButton = tower;
            wallButton = wall;
            rotateButton = rotate;
            cancelButton = cancel;
            visuals = visualSet;
            ringMaterial = ring;
            worldInput = input;
            network = client;
            worldView = view;

            hqButton.Button.onClick.AddListener(ToggleHQ);
            towerButton.Button.onClick.AddListener(ToggleTower);
            wallButton.Button.onClick.AddListener(ToggleWall);
            rotateButton.onClick.AddListener(Rotate);
            cancelButton.onClick.AddListener(Cancel);
            Disarm();
        }

        // The HQ comes first. Towers and walls need one to exist.
        public void SetAvailable(bool connected, bool hasHQ)
        {
            hqButton.SetInteractable(connected && !hasHQ);
            towerButton.SetInteractable(connected && hasHQ);
            wallButton.SetInteractable(connected && hasHQ);

            if (IsArmed && !ButtonFor(pendingType).Button.interactable)
                Disarm();
        }

        // A left click while armed places at the ghost, whatever the cursor is over.
        public void HandleClick(WorldHit hit)
        {
            if (!IsArmed || hit.Kind == WorldHitKind.Cancel || hit.Button != WorldButton.Primary || !hasGroundPoint)
                return;

            if (!allowed)
            {
                noticeUntil = Time.time + NoticeSeconds;
                RaiseChanged();
                return;
            }

            PlaceStructureRequestMessage request;
            request.Position = new Vec2(groundPoint.x, groundPoint.z);
            request.UnitType = (byte)pendingType;
            request.Yaw = YawByte.FromDegrees(yawDegrees);
            network.Send(MessageId.PlaceStructureRequest, request);
            Disarm();
        }

        public void Rotate()
        {
            if (!IsArmed)
                return;

            yawDegrees = yawDegrees == 0f ? 90f : 0f;
            RaiseChanged();
        }

        public void Cancel()
        {
            Disarm();
        }

        void Update()
        {
            if (noticeUntil > 0f && Time.time >= noticeUntil)
            {
                noticeUntil = 0f;
                RaiseChanged();
            }

            if (!IsArmed)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                Rotate();

            UpdateGhost();
        }

        void UpdateGhost()
        {
            hasGroundPoint = worldInput.TryGroundPoint(out groundPoint);
            if (ghost == null)
                return;

            ghost.SetActive(hasGroundPoint);
            ring.gameObject.SetActive(hasGroundPoint);
            if (!hasGroundPoint)
                return;

            ghost.transform.SetPositionAndRotation(groundPoint, Quaternion.Euler(0f, yawDegrees, 0f));
            ring.position = groundPoint + Vector3.up * RingLift;

            allowed = IsAllowed(new Vec2(groundPoint.x, groundPoint.z));
            ringBlock.SetColor(BaseColorId, allowed ? AllowedColor : BlockedColor);
            ringRenderer.SetPropertyBlock(ringBlock);
        }

        // Same rules the server applies, against the entities this client currently sees.
        bool IsAllowed(Vec2 position)
        {
            if (!Footprint.InBounds(position, Footprint.WorldHalfExtent))
                return false;

            float radius = UnitDefs.Get(pendingType).FootprintRadius;
            foreach (Unit unit in worldView.Units)
            {
                if (Footprint.Overlaps(position, radius, unit.Position, UnitDefs.Get(unit.UnitType).FootprintRadius))
                    return false;
            }
            return true;
        }

        void OnDestroy()
        {
            if (hqButton != null && hqButton.Button != null)
                hqButton.Button.onClick.RemoveListener(ToggleHQ);
            if (towerButton != null && towerButton.Button != null)
                towerButton.Button.onClick.RemoveListener(ToggleTower);
            if (wallButton != null && wallButton.Button != null)
                wallButton.Button.onClick.RemoveListener(ToggleWall);
            if (rotateButton != null)
                rotateButton.onClick.RemoveListener(Rotate);
            if (cancelButton != null)
                cancelButton.onClick.RemoveListener(Cancel);

            DestroyGhost();
        }

        void ToggleHQ()
        {
            Toggle(UnitType.HQ);
        }

        void ToggleTower()
        {
            Toggle(UnitType.Tower);
        }

        void ToggleWall()
        {
            Toggle(UnitType.Wall);
        }

        void Toggle(UnitType type)
        {
            if (IsArmed && pendingType == type)
            {
                Disarm();
                return;
            }

            IsArmed = true;
            pendingType = type;
            CreateGhost();
            Paint();
            RaiseChanged();
        }

        void CreateGhost()
        {
            DestroyGhost();

            UnitVisualEntry entry = visuals.Find(WorldView.EntryName(pendingType));
            if (entry == null || entry.Prefab == null)
            {
                Debug.LogWarning("PlacementController: no visual entry for " + pendingType + ", placing without a ghost.");
                return;
            }

            ghost = Instantiate(entry.Prefab);
            ghost.name = "Placement Ghost";
            ghost.transform.localScale = Vector3.one * entry.Scale;
            SetLayerRecursively(ghost.transform, IgnoreRaycastLayer);

            foreach (Collider collider in ghost.GetComponentsInChildren<Collider>(true))
                Destroy(collider);

            TeamColor teamColor = ghost.GetComponent<TeamColor>();
            if (teamColor != null)
                teamColor.SetTeamColor(network.OwnerColor);

            ring = SelectionController.CreateRing(ringMaterial, "Placement Ring");
            float diameter = 2f * UnitDefs.Get(pendingType).FootprintRadius;
            ring.localScale = new Vector3(diameter, diameter, 1f);
            ringRenderer = ring.GetComponent<Renderer>();
            ringBlock = new MaterialPropertyBlock();

            ghost.SetActive(false);
            ring.gameObject.SetActive(false);
        }

        void DestroyGhost()
        {
            if (ghost != null)
                Destroy(ghost);
            if (ring != null)
                Destroy(ring.gameObject);

            ghost = null;
            ring = null;
            ringRenderer = null;
            hasGroundPoint = false;
            allowed = false;
        }

        void Disarm()
        {
            bool wasArmed = IsArmed;
            IsArmed = false;
            DestroyGhost();
            Paint();
            if (wasArmed)
                RaiseChanged();
        }

        void Paint()
        {
            hqButton.SetArmed(IsArmed && pendingType == UnitType.HQ);
            towerButton.SetArmed(IsArmed && pendingType == UnitType.Tower);
            wallButton.SetArmed(IsArmed && pendingType == UnitType.Wall);
            rotateButton.gameObject.SetActive(IsArmed);
            cancelButton.gameObject.SetActive(IsArmed);
        }

        IconButton ButtonFor(UnitType type)
        {
            switch (type)
            {
                case UnitType.Tower:
                    return towerButton;
                case UnitType.Wall:
                    return wallButton;
                default:
                    return hqButton;
            }
        }

        static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }

        void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null)
                handler();
        }
    }
}

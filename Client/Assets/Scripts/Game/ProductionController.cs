using Multiplayer.Protocol;
using UnityEngine;

namespace Multiplayer.Game
{
    public sealed class ProductionController : MonoBehaviour
    {
        GameObject bar;
        IconButton soldierButton;
        IconButton tankButton;
        SelectionController selection;
        NetworkClient network;

        public void Initialize(GameObject productionBar, IconButton soldier, IconButton tank, SelectionController selectionController, NetworkClient client)
        {
            bar = productionBar;
            soldierButton = soldier;
            tankButton = tank;
            selection = selectionController;
            network = client;

            soldierButton.Button.onClick.AddListener(BuildSoldier);
            tankButton.Button.onClick.AddListener(BuildTank);
            selection.SelectionChanged += OnSelectionChanged;
            OnSelectionChanged(selection.Selected);
        }

        void OnDestroy()
        {
            if (soldierButton != null && soldierButton.Button != null)
                soldierButton.Button.onClick.RemoveListener(BuildSoldier);

            if (tankButton != null && tankButton.Button != null)
                tankButton.Button.onClick.RemoveListener(BuildTank);

            if (selection != null)
                selection.SelectionChanged -= OnSelectionChanged;
        }

        void OnSelectionChanged(Unit unit)
        {
            if (bar != null)
                bar.SetActive(IsOwnHQ(unit));
        }

        void BuildSoldier()
        {
            Build(UnitType.Soldier);
        }

        void BuildTank()
        {
            Build(UnitType.Tank);
        }

        void Build(UnitType unitType)
        {
            Unit structure = selection.Selected;
            if (!IsOwnHQ(structure))
                return;

            BuildUnitRequestMessage request;
            request.StructureId = structure.EntityId;
            request.UnitType = (byte)unitType;
            network.Send(MessageId.BuildUnitRequest, request);
        }

        bool IsOwnHQ(Unit unit)
        {
            return unit != null && unit.UnitType == UnitType.HQ && unit.OwnerId == network.PlayerId;
        }
    }
}

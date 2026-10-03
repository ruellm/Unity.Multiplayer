using Multiplayer.Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Game
{
    public sealed class ProductionController : MonoBehaviour
    {
        GameObject panel;
        Button soldierButton;
        Button tankButton;
        SelectionController selection;
        NetworkClient network;

        public void Initialize(GameObject productionPanel, Button soldier, Button tank, SelectionController selectionController, NetworkClient client)
        {
            panel = productionPanel;
            soldierButton = soldier;
            tankButton = tank;
            selection = selectionController;
            network = client;

            soldierButton.onClick.AddListener(BuildSoldier);
            tankButton.onClick.AddListener(BuildTank);
            selection.SelectionChanged += OnSelectionChanged;
            OnSelectionChanged(selection.Selected);
        }

        void OnDestroy()
        {
            if (soldierButton != null)
                soldierButton.onClick.RemoveListener(BuildSoldier);

            if (tankButton != null)
                tankButton.onClick.RemoveListener(BuildTank);

            if (selection != null)
                selection.SelectionChanged -= OnSelectionChanged;
        }

        void OnSelectionChanged(Unit unit)
        {
            if (panel != null)
                panel.SetActive(IsOwnStructure(unit));
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
            if (!IsOwnStructure(structure))
                return;

            BuildUnitRequestMessage request;
            request.StructureId = structure.EntityId;
            request.UnitType = (byte)unitType;
            network.Send(MessageId.BuildUnitRequest, request);
        }

        bool IsOwnStructure(Unit unit)
        {
            return unit != null && unit.UnitType == UnitType.Structure && unit.OwnerId == network.PlayerId;
        }
    }
}

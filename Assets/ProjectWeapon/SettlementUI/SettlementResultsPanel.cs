#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.SettlementUI
{
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class SettlementResultsPanel : MonoBehaviour
    {
        private SettlementResultsView? view;
        private IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> definitions = null!;
        private Guid? displayedRun;

        public bool IsOpen => view?.IsOpen == true;
        public event Action<Guid>? ReturnToLobbyRequested;

        public void Initialize(WeaponCatalog catalog, TMP_FontAsset font)
        {
            if (view != null)
                throw new InvalidOperationException("This settlement panel is already initialized.");
            if (!catalog)
                throw new ArgumentNullException(nameof(catalog));
            if (!font)
                throw new ArgumentNullException(nameof(font));
            if (!Resources.Load<TMP_Settings>("TMP Settings"))
                throw new InvalidOperationException("Import TMP Essential Resources before showing settlement results.");

            definitions = catalog.CreateLookup();
            view = new SettlementResultsView((RectTransform)transform, font, RequestReturn);
        }

        /// <summary>Shows the outcome supplied by the host without performing another settlement.</summary>
        public void ShowCommit(RunSettlementCommit commit)
        {
            RequireInitialized();
            if (commit == null)
                throw new ArgumentNullException(nameof(commit));
            Present(SettlementResultsDisplay.FromCommit(commit, WeaponName));
        }

        /// <summary>Reopens an already stored result without a coordinator request.</summary>
        public void ShowReceipt(SettlementSaveRecord receipt)
        {
            RequireInitialized();
            if (receipt == null)
                throw new ArgumentNullException(nameof(receipt));
            Present(SettlementResultsDisplay.FromReceipt(receipt, WeaponName));
        }

        public void Hide()
        {
            view?.SetOpen(false);
            displayedRun = null;
        }

        private void Present(SettlementResultsDisplay display)
        {
            displayedRun = display.RunId;
            view!.SetOpen(true);
            view.Render(display);
        }

        private void RequestReturn()
        {
            if (!IsOpen || displayedRun is not Guid id)
                return;

            Hide();
            ReturnToLobbyRequested?.Invoke(id);
        }

        private void RequireInitialized()
        {
            if (view == null)
                throw new InvalidOperationException("Initialize the settlement panel before showing results.");
        }

        private string WeaponName(WeaponDefinitionId id)
        {
            if (!id.IsValid)
                return "무기 정보 없음";
            return definitions.TryGetValue(id, out WeaponDefinition definition) ? definition.DisplayName : id.Value;
        }
    }
}

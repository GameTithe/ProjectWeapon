#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using ProjectWeapon.CoinWeaponDraw;
using ProjectWeapon.Inventory;
using ProjectWeapon.Save;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.CoinDrawUI
{
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class CoinDrawResultPanel : MonoBehaviour
    {
        private CoinWeaponDrawService? purchases;
        private Func<int, int>? nextExclusive;
        private IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition>? definitions;
        private CoinDrawResultView? view;
        private Guid requestId;
        private int requestCount;
        private bool executing;
        private bool canRetry;
        private bool canAcknowledge;

        public Guid? RequestId => requestId == Guid.Empty ? null : requestId;
        public int? RequestCount => RequestId.HasValue ? requestCount : null;

        public void Initialize(CoinWeaponDrawService service, WeaponCatalog catalog, Func<int, int> drawSample,
            TMP_FontAsset font)
        {
            if (view != null)
                throw new InvalidOperationException("The coin draw panel is already initialized.");
            if (!catalog)
                throw new ArgumentNullException(nameof(catalog));
            if (!font)
                throw new ArgumentNullException(nameof(font));
            if (!Resources.Load<TMP_Settings>("TMP Settings"))
                throw new InvalidOperationException("Import TMP Essential Resources before initializing the coin draw panel.");

            purchases = service ?? throw new ArgumentNullException(nameof(service));
            nextExclusive = drawSample ?? throw new ArgumentNullException(nameof(drawSample));
            definitions = catalog.CreateLookup();
            view = new CoinDrawResultView((RectTransform)transform, font, () => TryDraw(1),
                () => TryDraw(CoinWeaponDrawService.TenDrawCount), () => Retry(), Acknowledge);
            view.ShowReady();
        }

        /// <summary>Starts one purchase and locks further purchases until its result is acknowledged.</summary>
        public bool TryDraw(int count) => TryResume(Guid.NewGuid(), count);

        /// <summary>Reopens a known request. A committed receipt is replayed without another charge.</summary>
        public bool TryResume(Guid id, int count)
        {
            if (view == null || !isActiveAndEnabled || requestId != Guid.Empty || id == Guid.Empty
                || (count != 1 && count != CoinWeaponDrawService.TenDrawCount))
                return false;

            requestId = id;
            requestCount = count;
            Attempt();
            return true;
        }

        public bool Retry()
        {
            if (!isActiveAndEnabled || executing || !canRetry)
                return false;

            Attempt();
            return true;
        }

        public void Acknowledge()
        {
            if (executing || !canAcknowledge)
                return;

            requestId = Guid.Empty;
            canAcknowledge = false;
            view!.ShowReady();
        }

        private void Attempt()
        {
            executing = true;
            canRetry = false;
            canAcknowledge = false;
            view!.ShowBusy();
            try
            {
                CoinWeaponDrawResult result = requestCount == 1
                    ? purchases!.Purchase(requestId, nextExclusive!)
                    : purchases!.PurchaseTen(requestId, nextExclusive!);
                switch (result.Status)
                {
                    case CoinWeaponDrawStatus.Purchased:
                    case CoinWeaponDrawStatus.AlreadyPurchased:
                        ShowReceipt(result.Receipt!, result.ActiveSnapshot!);
                        canAcknowledge = true;
                        break;
                    case CoinWeaponDrawStatus.InsufficientFunds:
                        ShowProblem("코인이 부족합니다.", retry: false);
                        break;
                    case CoinWeaponDrawStatus.RequestConflict:
                        ShowProblem("이 요청은 다른 뽑기에 사용되었습니다.", retry: false);
                        break;
                    case CoinWeaponDrawStatus.RevisionConflict:
                        ShowProblem("저장 상태가 바뀌었습니다. 같은 요청을 다시 확인하세요.", retry: true);
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected coin draw outcome '{result.Status}'.");
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is InvalidDataException)
            {
                ShowProblem("저장 결과를 확인하지 못했습니다. 같은 요청을 다시 확인하세요.", retry: true);
            }
            catch
            {
                ShowProblem("거래 결과를 확인하지 못했습니다. 같은 요청을 다시 확인하세요.", retry: true);
                throw;
            }
            finally
            {
                executing = false;
            }
        }

        private void ShowProblem(string message, bool retry)
        {
            canRetry = retry;
            canAcknowledge = !retry;
            view!.ShowProblem(message, retry);
        }

        private void ShowReceipt(CoinWeaponDrawSaveRecord receipt, SaveSnapshot active)
        {
            var warehouse = new HashSet<WeaponInstanceId>();
            foreach (WeaponInstance instance in active.Weapons)
            {
                if (instance.Location.Kind == WeaponLocationKind.Warehouse)
                    warehouse.Add(instance.Id);
            }

            IReadOnlyList<CoinWeaponDrawAward> awards = receipt.Awards;
            var captions = new string[awards.Count];
            int inWarehouse = 0;
            for (int index = 0; index < awards.Count; index++)
            {
                CoinWeaponDrawAward award = awards[index];
                if (warehouse.Contains(award.InstanceId))
                    inWarehouse++;
                string name = definitions!.TryGetValue(award.DefinitionId, out WeaponDefinition definition) && definition
                    ? definition.DisplayName : award.DefinitionId.Value;
                captions[index] = $"#{index + 1:00}  {name}  ·  {award.Grade}등급";
            }

            view!.ShowReceipt(captions, inWarehouse);
        }
    }
}

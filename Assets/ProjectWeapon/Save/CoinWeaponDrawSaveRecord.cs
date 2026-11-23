#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Inventory;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.Save
{
    [Serializable]
    public struct CoinWeaponDrawAward
    {
        [SerializeField] private WeaponInstanceId instanceId;
        [SerializeField] private string definitionId;
        [SerializeField] private int grade;

        public CoinWeaponDrawAward(WeaponInstanceId instanceId, WeaponDefinitionId definitionId, int grade)
        {
            this.instanceId = instanceId;
            this.definitionId = definitionId.Value;
            this.grade = grade;
        }

        public WeaponInstanceId InstanceId => instanceId;
        public WeaponDefinitionId DefinitionId => new WeaponDefinitionId(definitionId);
        public int Grade => grade;
        internal bool IsValid => InstanceId.IsValid && DefinitionId.IsValid && grade >= 1 && grade <= 5;
    }

    [Serializable]
    public sealed class CoinWeaponDrawSaveRecord
    {
        [SerializeField] private string requestId;
        [SerializeField] private long cost;
        [SerializeField] private long balanceAfterPurchase;
        [SerializeField] private WeaponInstanceId instanceId;
        [SerializeField] private string definitionId;
        [SerializeField] private int grade;
        // Older single-draw receipts store only the first-award fields.
        [SerializeField] private CoinWeaponDrawAward[]? additionalAwards;

        public CoinWeaponDrawSaveRecord(Guid requestId, long cost, long balanceAfterPurchase,
            WeaponInstanceId instanceId, WeaponDefinitionId definitionId, int grade)
        {
            this.requestId = requestId.ToString("D");
            this.cost = cost;
            this.balanceAfterPurchase = balanceAfterPurchase;
            this.instanceId = instanceId;
            this.definitionId = definitionId.Value;
            this.grade = grade;
        }

        public CoinWeaponDrawSaveRecord(Guid requestId, long cost, long balanceAfterPurchase,
            IReadOnlyList<CoinWeaponDrawAward> awards)
        {
            if (awards == null)
                throw new ArgumentNullException(nameof(awards));
            if (awards.Count == 0)
                throw new ArgumentException("A draw receipt requires at least one awarded weapon.", nameof(awards));

            this.requestId = requestId.ToString("D");
            this.cost = cost;
            this.balanceAfterPurchase = balanceAfterPurchase;
            CoinWeaponDrawAward first = awards[0];
            instanceId = first.InstanceId;
            definitionId = first.DefinitionId.Value;
            grade = first.Grade;

            if (awards.Count == 1)
                return;

            additionalAwards = new CoinWeaponDrawAward[awards.Count - 1];
            for (int index = 1; index < awards.Count; index++)
                additionalAwards[index - 1] = awards[index];
        }

        public Guid RequestId => Guid.TryParseExact(requestId, "D", out Guid value) ? value : Guid.Empty;
        public long Cost => cost;
        public long BalanceAfterPurchase => balanceAfterPurchase;
        public WeaponInstanceId InstanceId => instanceId;
        public WeaponDefinitionId DefinitionId => new WeaponDefinitionId(definitionId);
        public int Grade => grade;
        public int AwardCount => 1 + (additionalAwards?.Length ?? 0);

        public IReadOnlyList<CoinWeaponDrawAward> Awards
        {
            get
            {
                var awards = new CoinWeaponDrawAward[AwardCount];
                awards[0] = new CoinWeaponDrawAward(InstanceId, DefinitionId, Grade);
                additionalAwards?.CopyTo(awards, 1);
                return Array.AsReadOnly(awards);
            }
        }

        internal bool IsValid
        {
            get
            {
                if (RequestId == Guid.Empty || cost <= 0 || balanceAfterPurchase < 0
                    || !instanceId.IsValid || !DefinitionId.IsValid || grade < 1 || grade > 5)
                    return false;

                if (additionalAwards == null || additionalAwards.Length == 0)
                    return true;

                foreach (CoinWeaponDrawAward award in additionalAwards)
                {
                    if (!award.IsValid)
                        return false;
                }

                return true;
            }
        }
    }
}

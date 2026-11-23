#nullable enable

using System;
using ProjectWeapon.Inventory;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.Save
{
    [Serializable]
    public sealed class CoinWeaponDrawSaveRecord
    {
        [SerializeField] private string requestId;
        [SerializeField] private long cost;
        [SerializeField] private long balanceAfterPurchase;
        [SerializeField] private WeaponInstanceId instanceId;
        [SerializeField] private string definitionId;
        [SerializeField] private int grade;

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

        public Guid RequestId => Guid.TryParseExact(requestId, "D", out Guid value) ? value : Guid.Empty;
        public long Cost => cost;
        public long BalanceAfterPurchase => balanceAfterPurchase;
        public WeaponInstanceId InstanceId => instanceId;
        public WeaponDefinitionId DefinitionId => new WeaponDefinitionId(definitionId);
        public int Grade => grade;

        internal bool IsValid => RequestId != Guid.Empty && cost > 0 && balanceAfterPurchase >= 0
            && instanceId.IsValid && DefinitionId.IsValid && grade >= 1 && grade <= 5;
    }
}

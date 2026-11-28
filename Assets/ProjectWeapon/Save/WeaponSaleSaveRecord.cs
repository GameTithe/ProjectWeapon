#nullable enable

using System;
using ProjectWeapon.Inventory;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.Save
{
    [Serializable]
    public sealed class WeaponSaleSaveRecord
    {
        [SerializeField] private string requestId;
        [SerializeField] private WeaponInstanceId instanceId;
        [SerializeField] private string definitionId;
        [SerializeField] private int grade;
        [SerializeField] private long price;
        [SerializeField] private long balanceAfterSale;

        public WeaponSaleSaveRecord(Guid requestId, WeaponInstanceId instanceId, WeaponDefinitionId definitionId,
            int grade, long price, long balanceAfterSale)
        {
            this.requestId = requestId.ToString("D");
            this.instanceId = instanceId;
            this.definitionId = definitionId.Value;
            this.grade = grade;
            this.price = price;
            this.balanceAfterSale = balanceAfterSale;
        }

        public Guid RequestId => Guid.TryParseExact(requestId, "D", out Guid value) ? value : Guid.Empty;
        public WeaponInstanceId InstanceId => instanceId;
        public WeaponDefinitionId DefinitionId => new WeaponDefinitionId(definitionId);
        public int Grade => grade;
        public long Price => price;
        public long BalanceAfterSale => balanceAfterSale;
        internal bool IsValid => RequestId != Guid.Empty && InstanceId.IsValid && DefinitionId.IsValid
            && grade >= 1 && grade <= 5 && price > 0;
    }
}

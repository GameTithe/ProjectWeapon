using System;
using UnityEngine;

namespace ProjectWeapon.WeaponSale
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Weapon Sale Prices")]
    public sealed class WeaponSalePrices : ScriptableObject
    {
        [SerializeField, Min(1)] private int grade1 = 30;
        [SerializeField, Min(1)] private int grade2 = 75;
        [SerializeField, Min(1)] private int grade3 = 150;
        [SerializeField, Min(1)] private int grade4 = 300;
        [SerializeField, Min(1)] private int grade5 = 450;

        public long GetPrice(int grade) => grade switch
        {
            1 => grade1,
            2 => grade2,
            3 => grade3,
            4 => grade4,
            5 => grade5,
            _ => throw new ArgumentOutOfRangeException(nameof(grade))
        };
    }
}

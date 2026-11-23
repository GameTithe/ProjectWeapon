#nullable enable

using System;
using ProjectWeapon.Inventory;
using UnityEngine;

namespace ProjectWeapon.Loot
{
    [DisallowMultipleComponent]
    public sealed class WorldLoot : MonoBehaviour
    {
        public WeaponInstance? Item { get; private set; }

        public void Initialize(WeaponInstance item)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
        }
    }
}

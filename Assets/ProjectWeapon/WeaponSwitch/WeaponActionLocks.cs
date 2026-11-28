#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Inventory;

namespace ProjectWeapon.WeaponSwitch
{
    public enum CombatWeaponAction
    {
        BasicAttack,
        Skill
    }

    /// <summary>Retains action locks across equipped-slot changes within a sortie.</summary>
    public sealed class WeaponActionLocks
    {
        private readonly TimeSpan[] shared = new TimeSpan[2];
        private readonly Dictionary<(CombatWeaponAction Action, WeaponInstanceId Weapon), TimeSpan> weapons =
            new Dictionary<(CombatWeaponAction, WeaponInstanceId), TimeSpan>();

        public TimeSpan ReadyAt(CombatWeaponAction action, WeaponInstanceId weapon)
        {
            int actionIndex = Index(action);
            weapons.TryGetValue((action, weapon), out TimeSpan weaponReadyAt);
            return Later(shared[actionIndex], weaponReadyAt);
        }

        /// <summary>Record only a started action, against the captured firing weapon and the same run clock.</summary>
        public void RecordAccepted(CombatWeaponAction action, WeaponInstanceId weapon,
            TimeSpan sharedReadyAt, TimeSpan weaponReadyAt)
        {
            int actionIndex = Index(action);
            if (!weapon.IsValid)
                throw new ArgumentException("An accepted action needs a valid instance ID.", nameof(weapon));
            if (sharedReadyAt < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(sharedReadyAt));
            if (weaponReadyAt < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(weaponReadyAt));

            var key = (action, weapon);
            shared[actionIndex] = Later(shared[actionIndex], sharedReadyAt);
            weapons.TryGetValue(key, out TimeSpan previous);
            weapons[key] = Later(previous, weaponReadyAt);
        }

        private static int Index(CombatWeaponAction action) => action switch
        {
            CombatWeaponAction.BasicAttack => 0,
            CombatWeaponAction.Skill => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        private static TimeSpan Later(TimeSpan first, TimeSpan second) => first >= second ? first : second;
    }
}

#nullable enable

using System;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Movement;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;

namespace ProjectWeapon.CombatHud
{
    /// <summary>The current fight's existing action owners and clocks.</summary>
    public sealed class CombatHudBindings
    {
        public CombatHudBindings(RunState run, PlayerMovement movement, MovementSettings movementSettings,
            InventoryService fieldInventory, EquippedWeaponSelection equipment, ManualSkillExecutor skills,
            Func<TimeSpan> clock, Func<bool> inputAvailable, Func<bool> canExtract, Func<bool> tryExtract,
            Func<bool>? weaponActionBusy = null, Func<ManualSkillPattern, bool>? supportsSkill = null)
        {
            Run = run ?? throw new ArgumentNullException(nameof(run));
            Movement = movement ? movement : throw new ArgumentNullException(nameof(movement));
            MovementSettings = movementSettings ? movementSettings : throw new ArgumentNullException(nameof(movementSettings));
            FieldInventory = fieldInventory ?? throw new ArgumentNullException(nameof(fieldInventory));
            Equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            Skills = skills ?? throw new ArgumentNullException(nameof(skills));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            InputAvailable = inputAvailable ?? throw new ArgumentNullException(nameof(inputAvailable));
            CanExtract = canExtract ?? throw new ArgumentNullException(nameof(canExtract));
            TryExtract = tryExtract ?? throw new ArgumentNullException(nameof(tryExtract));
            WeaponActionBusy = weaponActionBusy ?? (() => false);
            SupportsSkill = supportsSkill ?? (_ => true);
        }

        internal RunState Run { get; }
        internal PlayerMovement Movement { get; }
        internal MovementSettings MovementSettings { get; }
        internal InventoryService FieldInventory { get; }
        internal EquippedWeaponSelection Equipment { get; }
        internal ManualSkillExecutor Skills { get; }
        internal Func<TimeSpan> Clock { get; }
        internal Func<bool> InputAvailable { get; }
        internal Func<bool> CanExtract { get; }
        internal Func<bool> TryExtract { get; }
        internal Func<bool> WeaponActionBusy { get; }
        internal Func<ManualSkillPattern, bool> SupportsSkill { get; }
    }
}

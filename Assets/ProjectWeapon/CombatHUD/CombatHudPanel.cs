#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Inventory;
using ProjectWeapon.Player;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.CombatHUD
{
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class CombatHudPanel : MonoBehaviour
    {
        private CombatHudView? view;
        private PlayerHealth? health;
        private ZoneRunCountdown? countdown;
        private InventoryService? inventory;
        private IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition>? definitions;

        public void Initialize(TMP_FontAsset font)
        {
            if (!font)
                throw new ArgumentNullException(nameof(font));
            if (view != null)
                throw new InvalidOperationException("The combat HUD is already initialized.");
            if (!Resources.Load<TMP_Settings>("TMP Settings"))
                throw new InvalidOperationException("Import TMP Essential Resources before initializing the combat HUD.");

            view = new CombatHudView((RectTransform)transform, font);
        }

        /// <summary>Read a new run's sources immediately and replace any prior health subscription.</summary>
        public void Bind(PlayerHealth playerHealth, ZoneRunCountdown clock, InventoryService playerInventory,
            WeaponCatalog weaponCatalog, TimeSpan observedAt, int? activeSlot = null)
        {
            if (view == null)
                throw new InvalidOperationException("Initialize the combat HUD before binding it.");
            if (playerHealth == null)
                throw new ArgumentNullException(nameof(playerHealth));
            if (clock == null)
                throw new ArgumentNullException(nameof(clock));
            if (playerInventory == null)
                throw new ArgumentNullException(nameof(playerInventory));
            if (!weaponCatalog)
                throw new ArgumentNullException(nameof(weaponCatalog));

            var catalogLookup = weaponCatalog.CreateLookup();
            StopListening();
            definitions = catalogLookup;
            health = playerHealth;
            countdown = clock;
            inventory = playerInventory;
            StartListening();
            RenderHealth();
            RefreshTime(observedAt);
            RefreshEquipment(activeSlot);
        }

        /// <summary>Sample the authoritative clock after that frame's run observations are processed.</summary>
        public void RefreshTime(TimeSpan observedAt)
            => view?.RenderTime(countdown?.Sample(observedAt)?.Remaining);

        /// <summary>Refresh after a committed slot or active weapon change. Slot indices are zero-based.</summary>
        public void RefreshEquipment(int? activeSlot)
        {
            if (view == null || inventory == null)
                return;

            WeaponInstance? first = null;
            WeaponInstance? second = null;
            foreach (WeaponInstance item in inventory.Snapshot())
            {
                if (item.Location.Kind != WeaponLocationKind.Equipped)
                    continue;

                switch (item.Location.SlotIndex)
                {
                    case 0:
                        first = item;
                        break;
                    case 1:
                        second = item;
                        break;
                }
            }

            view.RenderSlot(0, DisplayName(first), first != null && activeSlot == 0);
            view.RenderSlot(1, DisplayName(second), second != null && activeSlot == 1);
        }

        private void OnEnable()
        {
            StartListening();
            RenderHealth();
        }

        private void OnDisable() => StopListening();

        private void StartListening()
        {
            if (health != null && isActiveAndEnabled)
                health.DamageApplied += OnDamage;
        }

        private void StopListening()
        {
            if (health != null)
                health.DamageApplied -= OnDamage;
        }

        private void OnDamage(PlayerDamage _) => RenderHealth();

        private void RenderHealth()
        {
            if (health != null)
                view?.RenderHealth(health.CurrentHealth, health.MaxHealth);
        }

        private string DisplayName(WeaponInstance? instance)
        {
            if (instance == null)
                return "비어 있음";

            return definitions != null && definitions.TryGetValue(instance.DefinitionId, out WeaponDefinition definition)
                && definition ? definition.DisplayName : "미확인 무기";
        }
    }
}

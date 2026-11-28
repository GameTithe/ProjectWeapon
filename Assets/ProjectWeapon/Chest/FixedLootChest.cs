#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectWeapon.Dungeon
{
    [DisallowMultipleComponent]
    public sealed class FixedLootChest : MonoBehaviour
    {
        [SerializeField] private WeaponDefinition weapon = null!;
        [SerializeField] private WorldLoot inactiveDropPrefab = null!;
        [SerializeField] private Transform dropPoint = null!;
        [SerializeField] private SpriteRenderer chestVisual = null!;
        [SerializeField] private Sprite openSprite = null!;

        private RunState? currentRun;
        private RunId boundRun;
        private bool opening;

        public bool IsOpen { get; private set; }

        /// <summary>Binds a fresh chest instance to the current expedition.</summary>
        public void BindRun(RunState run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (currentRun != null || run.Phase != RunPhase.Exploring || run.CurrentRun == null)
                throw new InvalidOperationException("Bind a fresh chest after its expedition starts.");
            currentRun = run;
            boundRun = run.CurrentRun.Id;
        }

        public bool TryOpen([NotNullWhen(true)] out WorldLoot? spawnedDrop)
        {
            spawnedDrop = null;
            if (opening || IsOpen || !isActiveAndEnabled || currentRun?.Phase != RunPhase.Exploring ||
                currentRun.CurrentRun?.Id != boundRun || !weapon || !weapon.Id.IsValid || weapon.Grade != 1 ||
                !inactiveDropPrefab || inactiveDropPrefab.gameObject.activeSelf || !dropPoint || !chestVisual || !openSprite)
                return false;

            opening = true;
            try
            {
                var instance = WeaponInstance.Create(weapon.Id, WeaponProvenance.ExplorationLoot, WeaponLocation.Warehouse());
                WorldLoot drop = Instantiate(inactiveDropPrefab, dropPoint.position, dropPoint.rotation);
                if (drop.gameObject.scene != gameObject.scene)
                    SceneManager.MoveGameObjectToScene(drop.gameObject, gameObject.scene);

                drop.Initialize(instance);
                IsOpen = true;
                chestVisual.sprite = openSprite;
                drop.gameObject.SetActive(true);
                spawnedDrop = drop;
                return true;
            }
            finally
            {
                opening = false;
            }
        }
    }
}

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectWeapon.Loot.PlayModeTests
{
    public sealed class LootPickupTriggerTests
    {
        [UnityTest]
        public IEnumerator LegacyTriggerChecksPivotAndReportsOnlyOnceAcrossTwoCollidersAndReentry()
        {
            Scene scene = SceneManager.CreateScene("Loot Pickup Physics",
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            AsyncOperation? unload = null;
            try
            {
                var inventory = new InventoryService(Array.Empty<WeaponInstance>());
                LootPickupTrigger trigger = CreatePicker(scene, inventory);
                var reports = new List<InventoryPickupResult>();
                trigger.OutcomeReported += (_, _, result) => reports.Add(result);

                WeaponInstance loot = WeaponInstance.Create(new WeaponDefinitionId("old-sword"),
                    WeaponProvenance.ExplorationLoot, WeaponLocation.Warehouse());
                GameObject itemObject = SpawnLoot(scene, loot).gameObject;

                PhysicsScene2D physics = scene.GetPhysicsScene2D();
                itemObject.transform.position = new Vector3(2, 0, 0);
                Step(physics);

                itemObject.transform.position = new Vector3(1.1f, 0, 0);
                Step(physics);
                Assert.That(reports, Is.Empty);
                Assert.That(inventory.Snapshot(), Is.Empty);

                itemObject.transform.position = new Vector3(1, 0, 0);
                Step(physics);
                Assert.That(reports.Count, Is.EqualTo(1));
                Assert.That(reports[0].Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
                Assert.That(inventory.Snapshot()[0].Id, Is.EqualTo(loot.Id));

                itemObject.transform.position = new Vector3(2, 0, 0);
                Step(physics);
                itemObject.transform.position = new Vector3(0.8f, 0, 0);
                Step(physics);
                Assert.That(reports.Count, Is.EqualTo(1));
                Assert.That(itemObject.activeInHierarchy, Is.True);
                Assert.That(inventory.Snapshot().Count, Is.EqualTo(1));
            }
            finally
            {
                unload = SceneManager.UnloadSceneAsync(scene);
            }

            yield return unload;
        }

        [UnityTest]
        public IEnumerator ChoiceEventSuppliesTheLiveSourceForConfirmationOrAbandonment()
        {
            Scene scene = SceneManager.CreateScene("Loot Pickup Choice",
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            AsyncOperation? unload = null;
            try
            {
                WeaponInstance existing = WeaponInstance.Create(new WeaponDefinitionId("old-sword"),
                    WeaponProvenance.Personal, WeaponLocation.Carried(0));
                WeaponInstance loot = WeaponInstance.Create(new WeaponDefinitionId("old-sword"),
                    WeaponProvenance.ExplorationLoot, WeaponLocation.Warehouse());
                var inventory = new InventoryService(new[] { existing }, new InventorySlotCapacity(0, 1));
                LootPickupTrigger trigger = CreatePicker(scene, inventory);
                WorldLoot worldLoot = SpawnLoot(scene, loot);
                var reports = new List<(WorldLoot? Source, WeaponInstanceId Id, InventoryPickupResult Result)>();
                trigger.OutcomeReported += (source, id, result) => reports.Add((source, id, result));
                PhysicsScene2D physics = scene.GetPhysicsScene2D();
                worldLoot.transform.position = new Vector3(0.8f, 0, 0);
                Step(physics);

                Assert.That(reports.Count, Is.EqualTo(1));
                Assert.That(reports[0].Source, Is.SameAs(worldLoot));
                Assert.That(reports[0].Id, Is.EqualTo(loot.Id));
                Assert.That(reports[0].Result.Status, Is.EqualTo(InventoryPickupStatus.ChoiceRequired));

                worldLoot.transform.position = new Vector3(2, 0, 0);
                Step(physics);
                Assert.That(trigger.TryResolveChoice(reports[0].Source, reports[0].Result.Choice, existing.Id), Is.False);
                Assert.That(trigger.TryResolveChoice(null, reports[0].Result.Choice, null), Is.True);
                Assert.That(reports[1].Result.Status, Is.EqualTo(InventoryPickupStatus.Cancelled));
                Assert.That(reports[1].Source, Is.Null);
                Assert.That(inventory.Snapshot()[0], Is.SameAs(existing));

                Assert.That(trigger.AllowRetry(loot.Id), Is.True);
                worldLoot.transform.position = new Vector3(0.8f, 0, 0);
                Step(physics);
                Assert.That(reports[2].Result.Status, Is.EqualTo(InventoryPickupStatus.ChoiceRequired));
                Assert.That(trigger.TryResolveChoice(reports[2].Source, reports[2].Result.Choice, existing.Id), Is.True);
                Assert.That(reports[3].Result.Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
                Assert.That(reports[3].Result.Displaced, Is.SameAs(existing));
                Assert.That(reports[3].Source, Is.SameAs(worldLoot));
                Assert.That(inventory.Snapshot()[0].Id, Is.EqualTo(loot.Id));
            }
            finally
            {
                unload = SceneManager.UnloadSceneAsync(scene);
            }

            yield return unload;
        }

        private static LootPickupTrigger CreatePicker(Scene scene, InventoryService inventory)
        {
            var player = new GameObject("Picker");
            SceneManager.MoveGameObjectToScene(player, scene);
            player.AddComponent<Rigidbody2D>().gravityScale = 0;
            LootPickupTrigger trigger = player.AddComponent<LootPickupTrigger>();
            var circle = player.GetComponent<CircleCollider2D>();
            circle.isTrigger = true;
            circle.radius = 1;
            trigger.Initialize(new LootPickupCoordinator(inventory));
            return trigger;
        }

        private static WorldLoot SpawnLoot(Scene scene, WeaponInstance loot)
        {
            var item = new GameObject("World loot");
            SceneManager.MoveGameObjectToScene(item, scene);
            WorldLoot source = item.AddComponent<WorldLoot>();
            source.Initialize(loot);
            AddCollider(scene, item.transform, -0.04f);
            AddCollider(scene, item.transform, 0.04f);
            return source;
        }

        private static void AddCollider(Scene scene, Transform parent, float offset)
        {
            var child = new GameObject("Loot trigger shape");
            SceneManager.MoveGameObjectToScene(child, scene);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = new Vector3(offset, 0, 0);
            child.AddComponent<CircleCollider2D>().radius = 0.2f;
        }

        private static void Step(PhysicsScene2D physics)
        {
            Physics2D.SyncTransforms();
            Assert.That(physics.Simulate(0.02f), Is.True);
            Assert.That(physics.Simulate(0.02f), Is.True);
        }
    }
}

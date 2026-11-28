#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Enemies.Boss;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectWeapon.ZoneOneGreybox.Tests
{
    public sealed class BossEncounterTests
    {
        private const string ScenePath = "Assets/Scenes/ZoneOneGreybox/ZoneOneGreybox.unity";
        private const string WeaponFolder = "Assets/ProjectWeapon/Weapons/PrototypeStarter/";
        private Scene scene;
        private ZoneOneGreybox room = null!;
        private TimeSpan now;
        private InventoryService inventory = null!;
        private WeaponInstance equipped = null!;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator OldSwordBasicAndManualSlashDefeatPlacedBossOnce()
        {
            yield return Open("OldSword", 12000);
            BossShockwaveActor actor = room.Encounter.Boss;
            BossShockwave boss = actor.Boss!;
            LineRenderer ring = actor.GetComponentInChildren<LineRenderer>(true);
            SpriteRenderer sprite = actor.GetComponentInChildren<SpriteRenderer>();
            Color idleColor = sprite.color;
            Assert.That(actor.transform.position, Is.EqualTo(new Vector3(1.5f, 6.5f, 0)));
            Assert.That(boss.Health, Is.EqualTo(90));

            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(WeaponFolder + "PrototypeStarterWeaponCatalog.asset");
            var equipment = new EquippedWeaponSelection(room.Run, inventory, catalog,
                () => room.Encounter.IsWeaponActionBusy, new WeaponActionLocks());
            ManualSkillExecutor skill = room.Encounter.BindPlayerEquipment(equipment, () => false);
            int defeats = 0;
            boss.Defeated += (id, target, time) =>
            {
                Assert.That(id, Is.EqualTo(room.Run.CurrentRun!.Id));
                Assert.That(target, Is.EqualTo("zone1-boss"));
                Assert.That(time, Is.EqualTo(Ms(6100)));
                defeats++;
            };

            room.Player.position = new Vector2(2.5f, 6.5f);
            yield return Sample(100);
            Assert.That(ring.enabled, Is.True);
            Assert.That(Vector3.Distance(ring.GetPosition(0), actor.transform.position), Is.EqualTo(1.6f).Within(.001f));
            Assert.That(boss.Health, Is.EqualTo(80));
            Assert.That(sprite.color, Is.Not.EqualTo(idleColor));
            Assert.That(skill.TryActivate(Guid.NewGuid(), equipped.Id, now, new WorldPoint2D(2.5f, 6.5f),
                ManualSkillAim.Along(new WorldPoint2D(-1, 0))), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(boss.Health, Is.EqualTo(54));
            yield return Sample(999);
            Assert.That(room.Encounter.PlayerHealth, Is.EqualTo(100));
            yield return Sample(1000);
            Assert.That(room.Encounter.PlayerHealth, Is.EqualTo(76));
            Assert.That(ring.enabled, Is.False);

            for (int at = 1100; at <= 6100; at += 1000)
                yield return Sample(at);
            Assert.That(boss.IsDead, Is.True);
            Assert.That(defeats, Is.EqualTo(1));
            Assert.That(ring.enabled, Is.False);
            Assert.That(sprite.color, Is.EqualTo(Color.gray));
            Assert.That(room.Encounter.PlayerHealth, Is.GreaterThan(0));
            yield return Sample(10000);
            Assert.That(defeats, Is.EqualTo(1));
            Assert.That(ring.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator DaggerFollowsItsBossTargetAndDoesNotFallBackToTheRangedGuard()
        {
            yield return Open("Dagger", 12000);
            BossShockwave boss = room.Encounter.Boss.Boss!;
            room.Player.position = new Vector2(2.5f, 6.5f);
            yield return Sample(100);
            Assert.That(boss.Health, Is.EqualTo(85));
            yield return Sample(219);
            Assert.That(boss.Health, Is.EqualTo(85));
            yield return Sample(270);
            Assert.That(boss.Health, Is.EqualTo(80));
            yield return Sample(270);
            Assert.That(boss.Health, Is.EqualTo(80));
        }

        [UnityTest]
        public IEnumerator BeforeDeadlineBossHitSurvivesLateFrameAndExactDeadlineCastIsRejected()
        {
            yield return Open("OldSword", 1001);
            BossShockwave boss = room.Encounter.Boss.Boss!;
            int resolved = 0;
            TimeSpan effectiveAt = TimeSpan.Zero;
            boss.ShockwaveResolved += (_, _, at) =>
            {
                resolved++;
                effectiveAt = at;
            };
            room.Player.position = new Vector2(2.5f, 6.5f);
            yield return Sample(100);
            Assert.That(boss.ActiveCast!.Value.HitsAt, Is.EqualTo(Ms(1000)));
            yield return Sample(1200);
            Assert.That(room.Encounter.PlayerHealth, Is.EqualTo(76));
            Assert.That(effectiveAt, Is.EqualTo(Ms(1000)));
            Assert.That(room.Run.Result?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            yield return Sample(1400);
            Assert.That(resolved, Is.EqualTo(1));

            yield return SceneManager.UnloadSceneAsync(scene);
            yield return Open("OldSword", 1000);
            boss = room.Encounter.Boss.Boss!;
            room.Player.position = new Vector2(2.5f, 6.5f);
            yield return Sample(100);
            Assert.That(boss.ActiveCast, Is.Null);
            yield return Sample(1000);
            Assert.That(room.Encounter.PlayerHealth, Is.EqualTo(100));
            Assert.That(room.Run.Result?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
        }

        private IEnumerator Open(string weaponName, int durationMilliseconds)
        {
            now = TimeSpan.Zero;
            void Bind(Scene loaded, LoadSceneMode _)
            {
                if (loaded.path != ScenePath)
                    return;

                scene = loaded;
                room = scene.GetRootGameObjects().Select(root => root.GetComponent<ZoneOneGreybox>())
                    .Single(component => component != null)!;
                var zones = AssetDatabase.LoadAssetAtPath<ZoneCatalog>("Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset");
                var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponFolder + weaponName + ".asset");
                Assert.That(weapon, Is.Not.Null);
                equipped = WeaponInstance.Create(weapon.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
                inventory = new InventoryService(new[] { equipped });
                TimeSpan Clock() => now;
                var run = new RunState(Clock);
                Assert.That(run.TryStartRun(RunId.New(), room.Zone.ZoneId, Ms(durationMilliseconds)), Is.True);
                room.BindRun(run, new ZoneRunCountdown(zones, run), Clock, inventory, weapon);
            }

            SceneManager.sceneLoaded += Bind;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                    new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            }
            finally
            {
                SceneManager.sceneLoaded -= Bind;
            }
            Assert.That(room.Encounter.Boss, Is.Not.Null);
            yield return null;
        }

        private IEnumerator Sample(int milliseconds)
        {
            now = Ms(milliseconds);
            yield return null;
        }

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
#endif

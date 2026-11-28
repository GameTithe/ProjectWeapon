#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Extraction;
using ProjectWeapon.ForestBoss;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.Movement;
using ProjectWeapon.Run;
using ProjectWeapon.Zone3Forest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace ProjectWeapon.Zone3Greybox.Tests
{
    public sealed class Zone3GreyboxTests
    {
        private const int ZoneId = 3;
        private const float StepTime = 0.02f;
        private const string ScenePath = "Assets/Scenes/Zone3Greybox/Zone3Greybox.unity";
        private const string CatalogPath = "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset";
        private Scene scene;
        private PhysicsScene2D physics;
        private Keyboard? keyboard;
        private Rigidbody2D player = null!;
        private CircleCollider2D returnArea = null!;
        private ZoneCatalog catalog = null!;
        private Zone3ForestTrial trial = null!;
        private InputSettings.UpdateMode previousUpdateMode;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
        private float previousFixedDeltaTime;
        private TimeSpan now;

        [UnitySetUp]
        public IEnumerator Prepare()
        {
            previousUpdateMode = InputSystem.settings.updateMode;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            previousFixedDeltaTime = Time.fixedDeltaTime;
            Time.fixedDeltaTime = StepTime;
            now = TimeSpan.Zero;
            keyboard = InputSystem.AddDevice<Keyboard>();
            SceneManager.sceneLoaded += ConfigureTrial;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                    new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            }
            finally
            {
                SceneManager.sceneLoaded -= ConfigureTrial;
            }
            scene = SceneManager.GetSceneByPath(ScenePath);
            Assert.That(scene.isLoaded, Is.True);
            physics = scene.GetPhysicsScene2D();
            Assert.That(physics.IsValid(), Is.True);
            player = Find<PlayerMovement>().GetComponent<Rigidbody2D>();
            Assert.That(player.GetComponent<PlayerMovement>(), Is.Not.Null);
            returnArea = scene.GetRootGameObjects().Single(root => root.name == "Return Point")
                .GetComponent<CircleCollider2D>();
            catalog = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(returnArea.isTrigger, Is.True);
            Find<TilemapCollider2D>().ProcessTilemapChanges();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Dispose()
        {
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode = previousUpdateMode;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
            Time.fixedDeltaTime = previousFixedDeltaTime;
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator AuthoredWallsBlockMovementAndAWalkableRouteReachesTheReturnPoint()
        {
            RunState run = trial.Run;
            RunId id = run.CurrentRun!.Id;
            ExtractionAccess access = CreateAccess(run);
            Assert.That(player.position, Is.EqualTo(new Vector2(1.5f, 7.5f)));
            Assert.That(access.CanStart(id, Position), Is.False);

            SetMovement(Key.D);
            for (int step = 0; step < 70; step++)
                yield return SimulateStep();
            Assert.That(player.position.x, Is.InRange(3.50f, 3.67f));
            Assert.That(player.position.y, Is.EqualTo(7.5f).Within(0.02f));

            yield return WalkTo(Key.A, Vector2.left, -3.5f);
            yield return WalkTo(Key.S, Vector2.down, -1.5f);
            yield return WalkTo(Key.D, Vector2.right, 14.5f);
            Assert.That(access.CanStart(id, Position), Is.False);

            yield return WalkTo(Key.W, Vector2.up, 2f);
            SetMovement();
            yield return SimulateStep();
            yield return PressInteract();
            ExtractionAttempt attempt = run.ActiveExtraction!;
            Assert.That(attempt, Is.Not.Null);
            now = attempt.CompletesAt;
            yield return null;
            Assert.That(run.Result?.Reason, Is.EqualTo(RunEndReason.Extracted));
            Assert.That(trial.PlayerHealth, Is.EqualTo(100));
            Assert.That(trial.Chest.IsOpen, Is.False);
            Assert.That(trial.Guard.Brain.CurrentHealth, Is.EqualTo(24));
            Assert.That(trial.Sentry.Brain.CurrentHealth, Is.EqualTo(18));
            Assert.That(trial.Boss.CurrentHealth, Is.EqualTo(205));
            Assert.That(trial.BossDefeat, Is.Null);
        }

        [UnityTest]
        public IEnumerator ActualForestBossDealsDamageAndTheBasicSwordPublishesOneDefeat()
        {
            ForestBossActor boss = trial.Boss;
            RunId runId = trial.Run.CurrentRun!.Id;
            var bossPosition = new Vector2(13.5f, 8.5f);
            var south = new Vector2(13.5f, 7.25f);
            var north = new Vector2(13.5f, 9.5f);
            Assert.That((Vector2)boss.transform.position, Is.EqualTo(bossPosition));
            Assert.That(boss.GetComponent<Collider2D>(), Is.Null);
            Assert.That(boss.CurrentHealth, Is.EqualTo(205));

            var defeats = new List<ForestBossDefeat>();
            trial.BossDefeated += defeats.Add;
            player.position = south;
            now = TimeSpan.FromMilliseconds(100);
            yield return null;
            Assert.That(boss.CurrentHealth, Is.EqualTo(195));
            Assert.That(boss.ActiveAttack, Is.Not.Null);
            ForestRootStrike first = boss.ActiveAttack!.Value;
            Assert.That(first.Center.Y, Is.EqualTo(south.y));
            Assert.That(boss.GetComponentInChildren<LineRenderer>().enabled, Is.True);
            now = first.HitsAt;
            yield return null;
            Assert.That(trial.PlayerHealth, Is.EqualTo(66));

            int avoided = 0;
            for (int step = 0; step < 40 && boss.CurrentHealth > 0; step++)
            {
                if (boss.ActiveAttack is { } attack)
                {
                    player.position = attack.Center.Y < bossPosition.y ? north : south;
                    now = attack.HitsAt;
                    avoided++;
                }
                else
                    now += TimeSpan.FromSeconds(1);
                yield return null;
                Assert.That(trial.PlayerHealth, Is.EqualTo(66));
            }

            Assert.That(avoided, Is.GreaterThan(0));
            Assert.That(boss.CurrentHealth, Is.Zero);
            ForestBossDefeat defeat = defeats.Single();
            Assert.That(defeat.RunId, Is.EqualTo(runId));
            Assert.That(defeat.ZoneId, Is.EqualTo(ZoneId));
            Assert.That(defeat.BossId, Is.EqualTo(ForestBossActor.BossId));
            Assert.That(defeat.DefeatedAt, Is.EqualTo(now));
            Assert.That(trial.BossDefeat, Is.EqualTo(defeat));
            Assert.That(trial.Carried, Is.Empty);

            now += TimeSpan.FromSeconds(3);
            yield return null;
            Assert.That(defeats, Has.Count.EqualTo(1));
            Assert.That(boss.GetComponentInChildren<LineRenderer>().enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator ActualForestBossLateFrameUsesTheScheduledHitBeforeTheRoomTimesOut()
        {
            player.position = new Vector2(12.5f, 7.5f);
            now = TimeSpan.FromMilliseconds(718900);
            yield return null;
            Assert.That(trial.Boss.ActiveAttack?.HitsAt, Is.EqualTo(TimeSpan.FromMilliseconds(719900)));
            Assert.That(trial.PlayerHealth, Is.EqualTo(100));

            now = TimeSpan.FromSeconds(720);
            yield return null;
            Assert.That(trial.PlayerHealth, Is.EqualTo(66));
            Assert.That(trial.Run.Result?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(trial.Boss.ActiveAttack, Is.Null);
        }

        [Test]
        public void ZoneSettingsAndExactRadiusAndTimeoutUseTheExistingRunServices()
        {
            ZoneDefinition definition = catalog.GetZone(ZoneId);
            Assert.That(definition.RecommendedGrade, Is.EqualTo(3));
            Assert.That(definition.MinimumRewardGrade, Is.EqualTo(2));
            Assert.That(definition.MaximumRewardGrade, Is.EqualTo(4));
            Assert.That(definition.TimeLimitSeconds, Is.EqualTo(720));
            Assert.That(returnArea.radius, Is.EqualTo(.75f));
            WorldPoint2D edge = new WorldPoint2D(Center.X - returnArea.radius, Center.Y);
            WorldPoint2D outside = new WorldPoint2D(edge.X - .01f, edge.Y);
            var run = new RunState(() => now);
            var countdown = new ZoneRunCountdown(catalog, run);
            RunId id = RunId.New();
            Assert.That(countdown.TryStartRun(id, ZoneId), Is.True);
            ExtractionAccess access = CreateAccess(run);
            Assert.That(access.CanStart(id, edge), Is.True);
            Assert.That(access.CanStart(id, outside), Is.False);
            Assert.That(access.CanStart(RunId.New(), edge), Is.False);

            now = TimeSpan.FromSeconds(717);
            Assert.That(access.TryStart(id, edge), Is.Not.Null);
            now = TimeSpan.FromSeconds(720);
            Assert.That(countdown.ProcessFrame(Array.Empty<RunSignal>())?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(access.CanStart(id, edge), Is.False);
        }

        [UnityTest]
        public IEnumerator OptionalClearingCombatsBothEnemiesAndReturnsWithOneChestInstance()
        {
            Assert.That(trial.Zone, Is.SameAs(catalog.GetZone(ZoneId)));
            Assert.That(Enumerable.Range(1, 5).Select(trial.Zone.GetRewardProbabilityBasisPoints),
                Is.EqualTo(new[] { 0, 4500, 4500, 1000, 0 }));
            Assert.That((Vector2)trial.Guard.transform.position, Is.EqualTo(new Vector2(6.5f, 6.5f)));
            Assert.That((Vector2)trial.Sentry.transform.position, Is.EqualTo(new Vector2(9.5f, 4.5f)));
            Assert.That((Vector2)trial.Chest.transform.position, Is.EqualTo(new Vector2(7.5f, 4.5f)));
            Assert.That(trial.TryOpenChest(), Is.False);

            int meleeWarnings = 0;
            trial.Guard.Brain.AttackStarted += _ => meleeWarnings++;
            yield return WalkTo(Key.D, Vector2.right, 3.5f);
            yield return WalkTo(Key.S, Vector2.down, -6.5f);
            yield return WalkTo(Key.D, Vector2.right, 4.3f);
            SetMovement();
            yield return WaitUntil(() => meleeWarnings > 0, "The clearing stalker never telegraphed.");
            yield return WaitUntil(() => trial.Guard.Brain.CurrentHealth <= 0, "The clearing stalker survived the validation sword.");
            Assert.That(trial.PlayerHealth, Is.LessThan(100));

            yield return WalkTo(Key.D, Vector2.right, 7.5f);
            yield return WalkTo(Key.S, Vector2.down, -4.5f);
            SetMovement();
            int beforeShot = trial.PlayerHealth;
            yield return WaitUntil(() => trial.Sentry.Brain.ActiveAttack.HasValue, "The sentry never telegraphed.");
            yield return WaitUntil(() => trial.PlayerHealth < beforeShot, "The sentry missed the player on the clear path.");
            Assert.That(trial.PlayerHealth, Is.EqualTo(beforeShot - 6));

            yield return PressInteract();
            Assert.That(trial.Chest.IsOpen, Is.True);
            WorldLoot drop = Find<WorldLoot>();
            Assert.That(drop.Item, Is.Not.Null);
            WeaponInstanceId droppedId = drop.Item!.Id;
            Assert.That(trial.TryOpenChest(), Is.False);
            yield return WalkTo(Key.D, Vector2.right, 8.5f);
            SetMovement();
            yield return WaitUntil(() => trial.Carried.Count == 1, "The player never picked up the actual fixed chest instance.");
            Assert.That(trial.Carried.Single().Id, Is.EqualTo(droppedId));
            Assert.That(trial.Carried.Single().DefinitionId.Value, Is.EqualTo("wooden_spear"));
            Assert.That(trial.Carried.Single().Location.Kind, Is.EqualTo(WeaponLocationKind.Carried));
            Assert.That(drop.gameObject.activeSelf, Is.False);
            yield return WaitUntil(() => trial.Sentry.Brain.CurrentHealth <= 0, "The sentry survived the validation sword.");

            yield return WalkTo(Key.S, Vector2.down, -1.5f);
            yield return WalkTo(Key.D, Vector2.right, 14.5f);
            yield return PressInteract();
            Assert.That(trial.Run.Phase, Is.EqualTo(RunPhase.Exploring));
            yield return WalkTo(Key.W, Vector2.up, 2f);
            yield return PressInteract();
            ExtractionAttempt attempt = trial.Run.ActiveExtraction!;
            Assert.That(attempt, Is.Not.Null);
            Assert.That(attempt.CompletesAt - attempt.StartedAt, Is.EqualTo(TimeSpan.FromSeconds(3)));
            now = attempt.CompletesAt - TimeSpan.FromTicks(1);
            yield return null;
            Assert.That(trial.Run.Phase, Is.EqualTo(RunPhase.Extracting));
            now = attempt.CompletesAt;
            yield return null;
            Assert.That(trial.Run.Result?.Reason, Is.EqualTo(RunEndReason.Extracted));
            Assert.That(trial.Run.Result?.EffectiveAt, Is.EqualTo(attempt.CompletesAt));
        }

        [UnityTest]
        public IEnumerator SceneReturnAtTheDeadlineFailsAndLocksUnopenedChest()
        {
            player.position = new Vector2(Center.X - returnArea.radius, Center.Y);
            now = TimeSpan.FromSeconds(717);
            yield return PressInteract();
            Assert.That(trial.Run.ActiveExtraction?.CompletesAt, Is.EqualTo(trial.Run.CurrentRun!.Deadline));
            now = TimeSpan.FromSeconds(720);
            yield return null;
            Assert.That(trial.Run.Result?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(trial.Carried, Is.Empty);
            player.position = trial.Chest.transform.position;
            yield return PressInteract();
            Assert.That(trial.TryOpenChest(), Is.False);
            Assert.That(trial.Chest.IsOpen, Is.False);
        }

        private void ConfigureTrial(Scene loaded, LoadSceneMode _)
        {
            if (loaded.path != ScenePath)
                return;

            trial = loaded.GetRootGameObjects().Select(root => root.GetComponent<Zone3ForestTrial>())
                .Single(component => component != null)!;
            trial.ConfigureClock(() => now);
        }

        private IEnumerator PressInteract()
        {
            SetMovement(Key.E);
            yield return null;
            SetMovement();
            yield return null;
        }

        private IEnumerator WaitUntil(Func<bool> condition, string failure)
        {
            for (int step = 0; step < 200 && !condition(); step++)
                yield return SimulateStep();

            Assert.That(condition(), Is.True, () => $"{failure} Player {player.position} HP {trial.PlayerHealth}, guard {trial.Guard.transform.position} as {trial.Guard.Brain.Phase} HP {trial.Guard.Brain.CurrentHealth}, sentry HP {trial.Sentry.Brain.CurrentHealth}.");
        }

        private IEnumerator WalkTo(Key key, Vector2 direction, float goal)
        {
            SetMovement(key);
            for (int step = 0; step < 200; step++)
            {
                if (Vector2.Dot(player.position, direction) >= goal)
                    yield break;
                yield return SimulateStep();
            }
            Assert.Fail($"Did not walk to {goal} along {direction}. Player stopped at {player.position}.");
        }

        private IEnumerator SimulateStep()
        {
            now += TimeSpan.FromMilliseconds(20);
            yield return new WaitForFixedUpdate();
            Assert.That(physics.Simulate(StepTime), Is.True);
        }

        private void SetMovement(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        private T Find<T>() where T : Component
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>()).Single();

        private ExtractionAccess CreateAccess(RunState run) => new ExtractionAccess(run, ZoneId, Center, returnArea.radius);
        private WorldPoint2D Center
        {
            get
            {
                Vector3 center = returnArea.transform.TransformPoint(returnArea.offset);
                return new WorldPoint2D(center.x, center.y);
            }
        }
        private WorldPoint2D Position => new WorldPoint2D(player.position.x, player.position.y);
    }
}
#endif

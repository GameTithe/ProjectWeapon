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
using ProjectWeapon.Movement;
using ProjectWeapon.Player;
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

namespace ProjectWeapon.ForestBoss.Tests
{
    public sealed class ForestBossTests
    {
        private const string Layout = "Assets/Scenes/Zone3Greybox/Zone3Greybox.unity";
        private const string Prefab = "Assets/ProjectWeapon/ForestBoss/ForestRootSpirit.prefab";
        private const string Catalog = "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset";
        private const float Step = .02f;
        private static readonly Vector2 Bay = new Vector2(13.5f, 8.5f);
        private static readonly Vector2 ClearTarget = new Vector2(12.5f, 7.5f);
        private Scene scene;
        private PhysicsScene2D physics;
        private Rigidbody2D player = null!;
        private ForestBossActor boss = null!;
        private LineRenderer warning = null!;
        private RunState run = null!;
        private RunId runId;
        private ZoneRunCountdown countdown = null!;
        private PlayerHealth health = null!;
        private Keyboard? keyboard;
        private TimeSpan now;
        private InputSettings.UpdateMode previousInputMode;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
        private float previousFixedStep;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousInputMode = InputSystem.settings.updateMode;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousFixedStep = Time.fixedDeltaTime;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Time.fixedDeltaTime = Step;
            keyboard = InputSystem.AddDevice<Keyboard>();
            now = TimeSpan.Zero;

            SceneManager.sceneLoaded += IsolateMap;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Layout,
                    new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            }
            finally
            {
                SceneManager.sceneLoaded -= IsolateMap;
            }
            scene = SceneManager.GetSceneByPath(Layout);
            Assert.That(scene.isLoaded, Is.True);
            physics = scene.GetPhysicsScene2D();
            player = Find<PlayerMovement>().GetComponent<Rigidbody2D>();
            TilemapCollider2D walls = Find<TilemapCollider2D>();
            walls.ProcessTilemapChanges();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            Assert.That(prefab, Is.Not.Null);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            boss = instance.GetComponent<ForestBossActor>();
            warning = instance.GetComponentInChildren<LineRenderer>();
            boss.transform.position = Bay;
            health = new PlayerHealth(100);
            run = new RunState(() => now);
            runId = RunId.New();
            countdown = new ZoneRunCountdown(AssetDatabase.LoadAssetAtPath<ZoneCatalog>(Catalog), run);
            Assert.That(countdown.TryStartRun(runId, ForestBossActor.ZoneId), Is.True);
            boss.Bind(run, "player", player, health, walls);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode = previousInputMode;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
            Time.fixedDeltaTime = previousFixedStep;
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [Test]
        public void PrefabWarningStaysAtAcceptedPointAndItsVisibleBoundaryDealsDamageOnce()
        {
            Pose(Bay, ClearTarget);
            Sprite sprite = boss.GetComponent<SpriteRenderer>().sprite;
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(32));
            Assert.That(sprite.texture.width, Is.EqualTo(32));
            Assert.That(sprite.texture.height, Is.EqualTo(32));
            Assert.That(sprite.texture.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(boss.CurrentHealth, Is.EqualTo(205));

            StartWarning(100);
            ForestRootStrike strike = boss.ActiveAttack!.Value;
            Assert.That(strike.Center.X, Is.EqualTo(ClearTarget.x));
            Assert.That(strike.Center.Y, Is.EqualTo(ClearTarget.y));
            Assert.That(strike.Radius, Is.EqualTo(1.3f));
            Assert.That(strike.HitsAt, Is.EqualTo(Ms(1100)));
            Assert.That(strike.ReadyAt, Is.EqualTo(Ms(2750)));
            Assert.That(warning.enabled && warning.loop && warning.useWorldSpace, Is.True);
            Assert.That(warning.sharedMaterial, Is.Not.Null);
            Assert.That(warning.positionCount, Is.EqualTo(48));
            Vector3 edge = warning.GetPosition(6);
            Assert.That(Vector2.Distance(edge, ClearTarget), Is.EqualTo(strike.Radius).Within(.000001));

            Pose(new Vector2(13.5f, 8.75f), edge);
            Assert.That(Tick(1099), Is.Null);
            Assert.That(warning.GetPosition(6), Is.EqualTo(edge));
            Assert.That(boss.ActiveAttack!.Value.Center, Is.EqualTo(strike.Center));
            ForestRootImpact impact = Tick(1100)!.Value;
            Assert.That(impact.Attack.DamageId, Is.EqualTo(strike.DamageId));
            DamageRequest hit = impact.Hits.Single();
            Assert.That(hit.TargetId, Is.EqualTo("player"));
            Assert.That(hit.AttackerId, Is.EqualTo(ForestBossActor.BossId));
            Assert.That(hit.Amount, Is.EqualTo(34));
            Assert.That(PlayerRunDamage.ApplyHit(health, run, runId, (int)hit.Amount, impact.Attack.HitsAt,
                new List<RunSignal>()), Is.True);
            Assert.That(health.CurrentHealth, Is.EqualTo(66));
            Assert.That(warning.enabled, Is.False);
            Assert.That(Tick(1100), Is.Null);
            Assert.That(Tick(2749), Is.Null);
            StartWarning(2750);
            Assert.That(boss.ActiveAttack!.Value.DamageId, Is.Not.EqualTo(strike.DamageId));
        }

        [Test]
        public void JustOutsideTheVisibleCircleMissesEvenIfThePlayerRemainsVisible()
        {
            Pose(Bay, ClearTarget);
            StartWarning(100);
            Pose(Bay, ClearTarget + Vector2.right * 1.301f);
            ForestRootImpact result = Tick(1100)!.Value;
            Assert.That(result.Hits, Is.Empty);
            Assert.That(health.CurrentHealth, Is.EqualTo(100));
            Assert.That(warning.enabled, Is.False);
        }

        [Test]
        public void SolidMapWallsBlockDetectionAndImpactWhileTheDetectionEdgeIsInclusive()
        {
            Pose(Bay, new Vector2(8.5f, 8.5f));
            Assert.That(Tick(0), Is.Null);
            Assert.That(boss.ActiveAttack, Is.Null);

            Pose(new Vector2(5.5f, 1.5f), new Vector2(10.751f, 1.5f));
            Assert.That(Tick(50), Is.Null);
            Assert.That(boss.ActiveAttack, Is.Null);
            Pose(new Vector2(5.5f, 1.5f), new Vector2(10.75f, 1.5f));
            StartWarning(100);
            Assert.That(Tick(1100)!.Value.Hits, Has.Count.EqualTo(1));

            Pose(Bay, new Vector2(11.5f, 7.5f));
            StartWarning(2800);
            Pose(Bay, new Vector2(10.5f, 7.95f));
            Assert.That(Tick(3800)!.Value.Hits, Is.Empty);
        }

        [TestCase(718900, true)]
        [TestCase(719000, false)]
        [TestCase(719001, false)]
        public void DeadlineUsesOriginalStrikeTimeBeforeTheCutoffOnly(int attackStart, bool hits)
        {
            Pose(Bay, ClearTarget);
            StartWarning(attackStart);
            ForestRootImpact? result = Tick(720000);
            Assert.That(result.HasValue, Is.EqualTo(hits));
            if (result is { } impact)
            {
                Assert.That(impact.Attack.HitsAt, Is.EqualTo(Ms(719900)));
                Assert.That(PlayerRunDamage.ApplyHit(health, run, runId, (int)impact.Hits.Single().Amount,
                    impact.Attack.HitsAt, new List<RunSignal>()), Is.True);
            }
            Assert.That(health.CurrentHealth, Is.EqualTo(hits ? 66 : 100));
            Assert.That(warning.enabled, Is.False);
            Assert.That(countdown.ProcessFrame(Array.Empty<RunSignal>())!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
        }

        [Test]
        public void KillingTheBossCancelsTheStrikeAndPublishesOneSameRunDefeat()
        {
            Pose(Bay, ClearTarget);
            StartWarning(100);
            var defeats = new List<ForestBossDefeat>();
            boss.Defeated += defeats.Add;
            DamageRequest wound = DamageBoss(25);
            Assert.That(boss.ApplyDamage(wound, Ms(200)), Is.True);
            Assert.That(boss.ApplyDamage(wound, Ms(200)), Is.False);
            Assert.That(boss.CurrentHealth, Is.EqualTo(180));
            Assert.That(boss.ApplyDamage(DamageBoss(180), Ms(250)), Is.True);
            Assert.That(boss.ApplyDamage(DamageBoss(1), Ms(251)), Is.False);
            Assert.That(warning.enabled, Is.False);
            Assert.That(Tick(1100), Is.Null);
            ForestBossDefeat defeat = defeats.Single();
            Assert.That(defeat.RunId, Is.EqualTo(runId));
            Assert.That(defeat.ZoneId, Is.EqualTo(3));
            Assert.That(defeat.BossId, Is.EqualTo(ForestBossActor.BossId));
            Assert.That(defeat.DefeatedAt, Is.EqualTo(Ms(250)));
        }

        [Test]
        public void DeadPlayerAndReplacedRunCannotReceiveThePendingAttack()
        {
            Pose(Bay, ClearTarget);
            StartWarning(100);
            Assert.That(PlayerRunDamage.ApplyHit(health, run, runId, 100, Ms(200), new List<RunSignal>()), Is.True);
            Assert.That(Tick(1100)!.Value.Hits, Is.Empty);
            Assert.That(Tick(2750), Is.Null);
            Assert.That(boss.ActiveAttack, Is.Null);

            now = Ms(720000);
            Assert.That(countdown.ProcessFrame(Array.Empty<RunSignal>()), Is.Not.Null);
            Assert.That(run.TryReturnToTown(runId), Is.True);
            Assert.That(countdown.TryStartRun(RunId.New(), ForestBossActor.ZoneId), Is.True);
            Assert.That(Tick(721000), Is.Null);
            Assert.That(boss.ApplyDamage(DamageBoss(205), now), Is.False);
            Assert.That(boss.Defeat, Is.Null);
        }

        [UnityTest]
        public IEnumerator SouthernBypassReachesTheActualPortalWithAFullyHealthyBoss()
        {
            Assert.That(player.position, Is.EqualTo(new Vector2(1.5f, 7.5f)));
            yield return Walk(Key.D, Vector2.right, 3.55f);
            yield return Walk(Key.A, Vector2.left, -3.5f);
            yield return Walk(Key.S, Vector2.down, -1.5f);
            yield return Walk(Key.D, Vector2.right, 14.5f);
            yield return Walk(Key.W, Vector2.up, 2f);
            SetInput();
            yield return MoveFrame();

            var portal = scene.GetRootGameObjects().Single(root => root.name == "Return Point").GetComponent<CircleCollider2D>();
            Vector2 center = portal.transform.TransformPoint(portal.offset);
            var access = new ExtractionAccess(run, ForestBossActor.ZoneId, Point(center), portal.radius);
            Assert.That(access.TryStart(runId, Point(player.position)), Is.Not.Null);
            now += RunState.ExtractionDuration;
            Assert.That(countdown.ProcessFrame(Array.Empty<RunSignal>())!.Reason, Is.EqualTo(RunEndReason.Extracted));
            Assert.That(boss.CurrentHealth, Is.EqualTo(205));
            Assert.That(boss.Defeat, Is.Null);
        }

        private T Find<T>() where T : Component
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>()).Single();

        private static void IsolateMap(Scene loaded, LoadSceneMode _)
        {
            if (loaded.path != Layout)
                return;

            Zone3ForestTrial trial = loaded.GetRootGameObjects().Select(root => root.GetComponent<Zone3ForestTrial>())
                .Single(component => component != null);
            trial.enabled = false;
            trial.Boss.gameObject.SetActive(false);
        }

        private void Pose(Vector2 actor, Vector2 target)
        {
            boss.transform.position = actor;
            player.position = target;
            Physics2D.SyncTransforms();
        }

        private void StartWarning(int at)
        {
            Assert.That(Tick(at), Is.Null);
            Assert.That(boss.ActiveAttack.HasValue, Is.True, $"No warning at {at} ms.");
        }

        private ForestRootImpact? Tick(int elapsed)
        {
            now = Ms(elapsed);
            return boss.Advance(now);
        }

        private static DamageRequest DamageBoss(int amount)
        {
            var hit = AreaDamageRequest.Circle(Guid.NewGuid(), "player", default, 1, amount);
            return AreaDamageSelector.FindTargets(hit, new[] { new TargetCandidate(ForestBossActor.BossId, default, true, true) }).Single();
        }

        private IEnumerator Walk(Key key, Vector2 direction, float goal)
        {
            SetInput(key);
            for (int i = 0; i < 180; i++)
            {
                if (Vector2.Dot(player.position, direction) >= goal)
                    yield break;
                yield return MoveFrame();
            }
            Assert.Fail($"The authored corridor did not reach {goal}. Player is at {player.position}.");
        }

        private IEnumerator MoveFrame()
        {
            yield return new WaitForFixedUpdate();
            Assert.That(physics.Simulate(Step), Is.True);
            now += Ms(20);
            Assert.That(boss.Advance(now), Is.Null);
            Assert.That(boss.ActiveAttack, Is.Null);
        }

        private void SetInput(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
        private static WorldPoint2D Point(Vector2 value) => new WorldPoint2D(value.x, value.y);
    }
}
#endif

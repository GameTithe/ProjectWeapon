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
using ProjectWeapon.Zone2Prison;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace ProjectWeapon.PrisonGuard.Tests
{
    public sealed class PrisonGuardTests
    {
        private const string ZonePath = "Assets/Scenes/Zone2Greybox/Zone2Greybox.unity";
        private const string GuardPath = "Assets/ProjectWeapon/PrisonGuard/PrisonGuard.prefab";
        private const string CatalogPath = "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset";
        private const float StepTime = .02f;
        private readonly List<RunSignal> signals = new List<RunSignal>();
        private Scene scene;
        private PhysicsScene2D physics;
        private PrisonGuardActor guard = null!;
        private LineRenderer warning = null!;
        private SpriteRenderer sprite = null!;
        private Rigidbody2D player = null!;
        private PlayerMovement movement = null!;
        private PlayerHealth health = null!;
        private RunState run = null!;
        private RunId runId;
        private ZoneRunCountdown countdown = null!;
        private CircleCollider2D returnArea = null!;
        private TimeSpan now;
        private Keyboard? keyboard;
        private InputSettings.UpdateMode previousUpdateMode;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousUpdateMode = InputSystem.settings.updateMode;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            now = TimeSpan.Zero;
            SceneManager.sceneLoaded += IsolateMap;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ZonePath,
                    new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            }
            finally
            {
                SceneManager.sceneLoaded -= IsolateMap;
            }
            scene = SceneManager.GetSceneByPath(ZonePath);
            physics = scene.GetPhysicsScene2D();
            movement = Find<PlayerMovement>();
            player = movement.GetComponent<Rigidbody2D>();
            TilemapCollider2D walls = Find<TilemapCollider2D>();
            walls.ProcessTilemapChanges();
            returnArea = scene.GetRootGameObjects().Single(root => root.name == "Return Point")
                .GetComponent<CircleCollider2D>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GuardPath);
            Assert.That(prefab, Is.Not.Null);
            var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            guard = actor.GetComponent<PrisonGuardActor>();
            warning = actor.GetComponentInChildren<LineRenderer>();
            sprite = actor.GetComponent<SpriteRenderer>();
            health = new PlayerHealth(100);
            runId = RunId.New();
            run = new RunState(() => now);
            countdown = new ZoneRunCountdown(AssetDatabase.LoadAssetAtPath<ZoneCatalog>(CatalogPath), run);
            Assert.That(countdown.TryStartRun(runId, PrisonGuardActor.ZoneId), Is.True);
            guard.Bind(run, "player", player, health, walls);
            signals.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode = previousUpdateMode;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [Test]
        public void PrefabWarnsWithItsRealInclusiveHitRectangleAndAppliesOneTypedDamage()
        {
            SetPositions(new Vector2(3.5f, 1.25f), new Vector2(5.5f, 1.25f));
            Assert.That(guard.CurrentHealth, Is.EqualTo(160f));
            Assert.That(sprite.sprite.pixelsPerUnit, Is.EqualTo(32f));
            Assert.That(sprite.sprite.texture.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(warning.sharedMaterial, Is.Not.Null);

            Assert.That(Tick(100), Is.Null);
            PrisonGuardSlam slam = guard.ActiveAttack!.Value;
            Assert.That(warning.enabled && warning.loop && warning.useWorldSpace, Is.True);
            Assert.That(slam.HitsAt, Is.EqualTo(Ms(1000)));
            Assert.That(slam.Length, Is.EqualTo(3f));
            Assert.That(slam.Width, Is.EqualTo(1f));
            Assert.That(warning.GetPosition(0), Is.EqualTo(new Vector3(3.5f, 1.75f, 0f)));
            Assert.That(warning.GetPosition(1), Is.EqualTo(new Vector3(6.5f, 1.75f, 0f)));
            Assert.That(warning.GetPosition(2), Is.EqualTo(new Vector3(6.5f, .75f, 0f)));
            Assert.That(warning.GetPosition(3), Is.EqualTo(new Vector3(3.5f, .75f, 0f)));
            Assert.That(Tick(999), Is.Null);

            SetPositions(new Vector2(4.5f, 1.5f), new Vector2(6.5f, 1.75f));
            PrisonGuardImpact impact = Tick(1000)!.Value;
            Assert.That(impact.Attack.DamageId, Is.EqualTo(slam.DamageId));
            Assert.That(impact.Attack.Origin.X, Is.EqualTo(3.5f));
            Assert.That(impact.Attack.Direction.X, Is.EqualTo(1f));
            DamageRequest hit = impact.Hits.Single();
            Assert.That(hit.ApplicationId, Is.EqualTo(slam.DamageId));
            Assert.That(hit.AttackerId, Is.EqualTo(PrisonGuardActor.BossId));
            Assert.That(hit.TargetId, Is.EqualTo("player"));
            Assert.That(hit.Amount, Is.EqualTo(30f));
            Assert.That(health.CurrentHealth, Is.EqualTo(70));
            Assert.That(warning.enabled, Is.False);
            Assert.That(Tick(1000), Is.Null);
            Assert.That(health.CurrentHealth, Is.EqualTo(70));

            Assert.That(Tick(2799), Is.Null);
            Assert.That(guard.ActiveAttack, Is.Null);
            Assert.That(Tick(2800), Is.Null);
            Assert.That(guard.ActiveAttack!.Value.StartsAt, Is.EqualTo(Ms(2800)));
            Assert.That(guard.ActiveAttack.Value.DamageId, Is.Not.EqualTo(slam.DamageId));
        }

        [Test]
        public void DetectionRangeMayStartAThreatOutsideDamageAndWallBlocksItAtBothStages()
        {
            SetPositions(new Vector2(3.5f, 1.5f), new Vector2(8.01f, 1.5f));
            Tick(10);
            Assert.That(guard.ActiveAttack, Is.Null);
            player.position = new Vector2(8f, 1.5f);
            Tick(11);
            Assert.That(guard.ActiveAttack, Is.Not.Null);
            Assert.That(Tick(911)!.Value.Hits, Is.Empty);

            SetPositions(new Vector2(4.5f, 9.5f), new Vector2(7.25f, 9.5f));
            Tick(2711);
            Assert.That(guard.ActiveAttack, Is.Null, "The guard detected the player through authored prison walls.");
            player.position = new Vector2(5.4f, 9.5f);
            Tick(2712);
            PrisonGuardSlam slam = guard.ActiveAttack!.Value;
            player.position = new Vector2(7.25f, 9.5f);
            Assert.That(slam.Contains(Point(player.position)), Is.True);
            Assert.That(Tick(3612)!.Value.Hits, Is.Empty);
            Assert.That(health.CurrentHealth, Is.EqualTo(100));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SidestepOrDeadTargetAvoidsTheLockedSlam(bool killPlayer)
        {
            SetPositions(new Vector2(3.5f, 1.25f), new Vector2(5.5f, 1.25f));
            Tick(10);
            Assert.That(guard.ActiveAttack, Is.Not.Null);
            if (killPlayer)
                health.ApplyDamage(100, Ms(400));
            else
                player.position = new Vector2(5.5f, 1.751f);

            Assert.That(Tick(910)!.Value.Hits, Is.Empty);
            Assert.That(warning.enabled, Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(killPlayer ? 0 : 100));
        }

        [Test]
        public void ActualIncomingHitFlashesAndDefeatCancelsThePendingSlamAndReportsOnce()
        {
            SetPositions(new Vector2(3.5f, 1.5f), new Vector2(5f, 1.5f));
            Color original = sprite.color;
            var defeated = new List<PrisonGuardDefeat>();
            guard.Defeated += defeated.Add;
            Tick(10);
            Assert.That(warning.enabled, Is.True);

            DamageRequest graze = PlayerHit(20f);
            Assert.That(guard.ApplyDamage(graze, Ms(100)), Is.True);
            Assert.That(guard.ApplyDamage(graze, Ms(101)), Is.False);
            Assert.That(guard.CurrentHealth, Is.EqualTo(140f));
            Assert.That(sprite.color, Is.Not.EqualTo(original));
            Tick(220);
            Assert.That(sprite.color, Is.EqualTo(original));

            Assert.That(guard.ApplyDamage(PlayerHit(140f), Ms(500)), Is.True);
            Assert.That(guard.ApplyDamage(PlayerHit(140f), Ms(501)), Is.False);
            Assert.That(guard.CurrentHealth, Is.Zero);
            Assert.That(warning.enabled, Is.False);
            Assert.That(Tick(910), Is.Null);
            Assert.That(health.CurrentHealth, Is.EqualTo(100));
            PrisonGuardDefeat evidence = defeated.Single();
            Assert.That(evidence.RunId, Is.EqualTo(runId));
            Assert.That(evidence.ZoneId, Is.EqualTo(2));
            Assert.That(evidence.BossId, Is.EqualTo("prison_guard"));
            Assert.That(evidence.EffectiveAt, Is.EqualTo(Ms(500)));
        }

        [Test]
        public void LateObservationAtDeadlinePreservesAnEarlierPendingHitForFrameArbitration()
        {
            SetPositions(new Vector2(3.5f, 1.5f), new Vector2(5f, 1.5f));
            TimeSpan? damageAt = null;
            health.DamageApplied += damage => damageAt = damage.EffectiveAt;
            Tick(599000);
            Assert.That(guard.ActiveAttack!.Value.HitsAt, Is.EqualTo(Ms(599900)));
            PrisonGuardImpact impact = Tick(600000)!.Value;
            Assert.That(impact.Attack.HitsAt, Is.EqualTo(Ms(599900)));
            Assert.That(damageAt, Is.EqualTo(Ms(599900)));
            Assert.That(impact.Hits.Single().Amount, Is.EqualTo(30f));
            Assert.That(health.CurrentHealth, Is.EqualTo(70));
            Assert.That(warning.enabled, Is.False);
            Assert.That(Tick(600000), Is.Null);
            Assert.That(countdown.ProcessFrame(signals)!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(health.CurrentHealth, Is.EqualTo(70));
        }

        [TestCase(3.5f, 1.25f, false)]
        [TestCase(1.5f, 6.5f, true)]
        public void DrawnDiagonalCornersAreIncludedAndAnOutwardOffsetMisses(float x, float y, bool checkImpact)
        {
            // The lower-coordinate sample is intentionally inside an authored wall; exercise its rectangle only.
            if (!checkImpact)
                Find<TilemapCollider2D>().enabled = false;

            SetPositions(new Vector2(x, y), new Vector2(x + 1f, y + 1f));
            Tick(10);
            PrisonGuardSlam slam = guard.ActiveAttack!.Value;
            for (int corner = 0; corner < warning.positionCount; corner++)
                Assert.That(slam.Contains(Point(warning.GetPosition(corner))), Is.True, $"Corner {corner} was excluded.");

            WorldPoint2D farRight = slam.GetCorner(far: true, left: false);
            var outside = new WorldPoint2D(farRight.X + .001f, farRight.Y - .001f);
            Assert.That(slam.Contains(outside), Is.False);
            if (checkImpact)
            {
                player.position = new Vector2(farRight.X, farRight.Y);
                Assert.That(Tick(910)!.Value.Hits.Single().Amount, Is.EqualTo(30f));

                player.position = new Vector2(x + 1f, y + 1f);
                Tick(2710);
                player.position = new Vector2(outside.X, outside.Y);
                Assert.That(Tick(3610)!.Value.Hits, Is.Empty);
            }
        }

        [TestCase(599100)]
        [TestCase(599101)]
        public void DeadlineAndDifferentRunCannotDealDamageOrCreateDefeat(int startAt)
        {
            SetPositions(new Vector2(3.5f, 1.5f), new Vector2(5f, 1.5f));
            Tick(startAt);
            Assert.That(guard.ActiveAttack!.Value.HitsAt, Is.EqualTo(Ms(startAt + 900)));
            Assert.That(Tick(600000), Is.Null);
            Assert.That(warning.enabled, Is.False);
            Assert.That(guard.ApplyDamage(PlayerHit(200f), now), Is.False);
            Assert.That(countdown.ProcessFrame(Array.Empty<RunSignal>())!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(run.TryReturnToTown(runId), Is.True);
            Assert.That(countdown.TryStartRun(RunId.New(), PrisonGuardActor.ZoneId), Is.True);
            Assert.That(Tick(600100), Is.Null);
            Assert.That(guard.ApplyDamage(PlayerHit(200f), now), Is.False);
            Assert.That(guard.Defeat, Is.Null);
            Assert.That(health.CurrentHealth, Is.EqualTo(100));
        }

        [UnityTest]
        public IEnumerator ActualPrisonRouteReachesAndCompletesReturnWhileTheBossLives()
        {
            Assert.That(player.position, Is.EqualTo(new Vector2(1.5f, 9.5f)));
            SetPositions(new Vector2(5.5f, 9.5f), player.position);
            Tick(0);
            Assert.That(warning.enabled, Is.True);
            yield return WalkTo(Key.D, Vector2.right, 4.5f);
            yield return WalkTo(Key.S, Vector2.down, -4.5f);
            yield return WalkTo(Key.A, Vector2.left, -2.5f);
            yield return WalkTo(Key.S, Vector2.down, -1.5f);
            yield return WalkTo(Key.D, Vector2.right, 14.65f);
            SetKeys();
            yield return SimulateStep();

            Vector3 center = returnArea.transform.TransformPoint(returnArea.offset);
            var access = new ExtractionAccess(run, PrisonGuardActor.ZoneId, Point(center), returnArea.radius);
            ExtractionAttempt? attempt = access.TryStart(runId, Point(player.position), movement.SampleDirection().magnitude);
            Assert.That(attempt, Is.Not.Null);
            now = attempt!.CompletesAt;
            Assert.That(countdown.ProcessFrame(Array.Empty<RunSignal>())!.Reason, Is.EqualTo(RunEndReason.Extracted));
            Assert.That(guard.CurrentHealth, Is.EqualTo(160f));
            Assert.That(guard.Defeat, Is.Null);
        }

        private static void IsolateMap(Scene loaded, LoadSceneMode _)
        {
            if (loaded.path != ZonePath)
                return;

            Zone2PrisonTrial trial = loaded.GetRootGameObjects().Select(root => root.GetComponent<Zone2PrisonTrial>())
                .Single(candidate => candidate != null)!;
            trial.enabled = false;
        }

        private IEnumerator WalkTo(Key key, Vector2 direction, float goal)
        {
            SetKeys(key);
            for (int step = 0; step < 200; step++)
            {
                if (Vector2.Dot(player.position, direction) >= goal)
                    yield break;
                yield return SimulateStep();
            }
            Assert.Fail($"The prison path stopped at {player.position} before {goal}.");
        }

        private IEnumerator SimulateStep()
        {
            yield return new WaitForFixedUpdate();
            Assert.That(physics.Simulate(StepTime), Is.True);
            now += Ms(20);
            Tick((long)now.TotalMilliseconds);
            countdown.ProcessFrame(signals);
        }

        private PrisonGuardImpact? Tick(long milliseconds)
        {
            now = Ms(milliseconds);
            signals.Clear();
            PrisonGuardImpact? result = guard.Advance(now);
            if (result is { } impact)
                foreach (DamageRequest hit in impact.Hits)
                    PlayerRunDamage.ApplyHit(health, run, runId, (int)hit.Amount, impact.Attack.HitsAt, signals,
                        movement.Dodge);
            return result;
        }

        private DamageRequest PlayerHit(float amount)
        {
            WorldPoint2D point = Point(guard.transform.position);
            AreaDamageRequest contact = AreaDamageRequest.Circle(Guid.NewGuid(), "player", point, 0f, amount);
            return AreaDamageSelector.FindTargets(contact, new[] { new TargetCandidate(PrisonGuardActor.BossId, point,
                isAlive: true, isAttackable: true) }).Single();
        }

        private void SetPositions(Vector2 guardPosition, Vector2 playerPosition)
        {
            guard.transform.position = guardPosition;
            player.position = playerPosition;
            Physics2D.SyncTransforms();
        }

        private void SetKeys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard!, new KeyboardState(keys));
            InputSystem.Update();
        }

        private T Find<T>() where T : Component
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>()).Single();

        private static TimeSpan Ms(long milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
        private static WorldPoint2D Point(Vector2 vector) => new WorldPoint2D(vector.x, vector.y);
    }
}
#endif

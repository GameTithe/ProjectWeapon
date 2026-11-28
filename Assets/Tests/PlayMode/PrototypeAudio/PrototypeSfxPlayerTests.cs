#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Run;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ProjectWeapon.PrototypeAudio.Tests
{
    public sealed class PrototypeSfxPlayerTests
    {
        private const string Folder = "Assets/ProjectWeapon/PrototypeAudio/";
        private PrototypeSfxPlayer player = null!;
        private GameObject? listener;
        private RunId run;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (Object.FindAnyObjectByType<AudioListener>() == null)
                listener = new GameObject("Test audio listener", typeof(AudioListener));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "PrototypeSfx.prefab");
            Assert.That(prefab, Is.Not.Null);
            player = Object.Instantiate(prefab).GetComponent<PrototypeSfxPlayer>();
            run = RunId.New();
            player.BeginRun(run);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (player != null)
                Object.Destroy(player.gameObject);
            if (listener != null)
                Object.Destroy(listener);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EachTargetCanTriggerOnceButTheRollingEnemyAndPlayerQuotasApply()
        {
            Guid attack = Guid.NewGuid();
            Assert.That(player.OnEnemyHit(run, attack, "enemy-a"), Is.True);
            Assert.That(player.OnEnemyHit(run, attack, "enemy-a"), Is.False);
            Assert.That(player.OnEnemyHit(run, attack, "enemy-b"), Is.True);
            Assert.That(player.OnEnemyHit(run, attack, "enemy-c"), Is.False);
            Assert.That(player.OnPlayerHit(run, Guid.NewGuid()), Is.True);
            Assert.That(player.OnPlayerHit(run, Guid.NewGuid()), Is.False);

            yield return new WaitForSecondsRealtime(0.12f);
            Assert.That(player.OnEnemyHit(run, attack, "enemy-c"), Is.False);
            Assert.That(player.OnEnemyHit(run, Guid.NewGuid(), "enemy-c"), Is.True);
            Assert.That(player.OnPlayerHit(run, Guid.NewGuid()), Is.True);
        }

        [UnityTest]
        public IEnumerator FourOneShotsAreTheCapAndCriticalCuesReplaceLowerPrioritySounds()
        {
            Guid initial = Guid.NewGuid();
            Assert.That(player.OnWeaponAttack(run, initial), Is.True);
            Assert.That(player.OnWeaponAttack(run, initial), Is.False);
            for (int index = 1; index < 4; index++)
                Assert.That(player.OnWeaponAttack(run, Guid.NewGuid()), Is.True);

            Assert.That(player.ActiveOneShots, Is.EqualTo(4));
            Assert.That(player.OnWeaponAttack(run, Guid.NewGuid()), Is.False);
            Assert.That(player.OnPlayerHit(run, Guid.NewGuid()), Is.True);
            Assert.That(IsPlaying("PlayerHit"), Is.True);
            Assert.That(player.ActiveOneShots, Is.EqualTo(4));

            Assert.That(player.OnExtractionStarted(run, 1), Is.True);
            Assert.That(player.IsExtractionLoopPlaying, Is.True);
            Assert.That(player.ActiveOneShots, Is.EqualTo(4));
            Assert.That(player.OnExtractionResolved(run, 1, succeeded: true), Is.True);
            Assert.That(player.IsExtractionLoopPlaying, Is.False);
            Assert.That(IsPlaying("ExtractionSuccess"), Is.True);
            Assert.That(IsPlaying("PlayerHit"), Is.True);
            Assert.That(player.ActiveOneShots, Is.EqualTo(4));
            Assert.That(player.OnExtractionResolved(run, 1, succeeded: true), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MuteStopsPlaybackAndRestoresOnlyAStillActiveAttempt()
        {
            Assert.That(player.Volume, Is.EqualTo(0.8f));
            Assert.That(player.GetComponents<AudioSource>().Length, Is.EqualTo(5));
            Assert.That(player.GetComponents<AudioSource>().All(source => source.volume == 0.8f), Is.True);
            Assert.That(player.GetComponents<AudioSource>().Count(source => source.loop), Is.EqualTo(1));
            Assert.That(player.OnExtractionStarted(run, 2), Is.True);
            Assert.That(player.OnDodge(run, Guid.NewGuid()), Is.True);

            player.SetMuted(true);
            Assert.That(player.ActiveOneShots, Is.Zero);
            Assert.That(player.IsExtractionLoopPlaying, Is.False);
            Guid whileMuted = Guid.NewGuid();
            Assert.That(player.OnPickup(run, whileMuted), Is.False);
            player.SetMuted(false);
            Assert.That(player.IsExtractionLoopPlaying, Is.True);
            Assert.That(player.ActiveOneShots, Is.Zero);
            Assert.That(player.OnPickup(run, whileMuted), Is.False);
            Assert.That(player.OnPickup(run, Guid.NewGuid()), Is.True);

            player.SetVolume(0.37f);
            Assert.That(player.GetComponents<AudioSource>().All(source => source.volume == 0.37f), Is.True);
            player.SetMuted(true);
            Assert.That(player.OnExtractionResolved(run, 2, succeeded: false), Is.True);
            player.SetMuted(false);
            Assert.That(player.IsExtractionLoopPlaying, Is.False);
            Assert.That(player.ActiveOneShots, Is.Zero);
            Assert.That(player.OnExtractionStarted(run, 2), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ANewAttemptIgnoresStaleSignalsAndRunEndStopsTheLoop()
        {
            Assert.That(player.OnExtractionStarted(run, 1), Is.True);
            Assert.That(player.OnExtractionResolved(run, 1, succeeded: false), Is.True);
            Assert.That(player.IsExtractionLoopPlaying, Is.False);
            Assert.That(player.ActiveOneShots, Is.Zero);

            Assert.That(player.OnExtractionStarted(run, 2), Is.True);
            Assert.That(player.OnExtractionStarted(run, 2), Is.False);
            Assert.That(player.OnExtractionResolved(run, 1, succeeded: false), Is.False);
            Assert.That(player.IsExtractionLoopPlaying, Is.True);

            player.EndRun(run);
            Assert.That(player.IsExtractionLoopPlaying, Is.False);
            Assert.That(player.OnExtractionStarted(run, 3), Is.False);
            Assert.That(player.OnWeaponAttack(run, Guid.NewGuid()), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SourceFilesImportAsSafeMonoPcmAndTheLoopHasAQuietSeam()
        {
            string[] stems = { "WeaponAttack", "EnemyHit", "PlayerHit", "Dodge", "Pickup", "ExtractionLoop", "ExtractionSuccess" };
            foreach (string stem in stems)
            {
                string path = Folder + "Clips/" + stem + ".wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                Assert.That(clip.channels, Is.EqualTo(1), stem);
                Assert.That(clip.frequency, Is.EqualTo(24000), stem);
                Assert.That(clip.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad), stem);
                Assert.That(importer.defaultSampleSettings.compressionFormat, Is.EqualTo(AudioCompressionFormat.PCM), stem);

                var samples = new float[clip.samples];
                Assert.That(clip.GetData(samples, 0), Is.True, stem);
                float peak = samples.Max(sample => Mathf.Abs(sample));
                Assert.That(peak, Is.InRange(0.08f, 0.221f), stem);
                if (stem == "ExtractionLoop")
                    Assert.That(Mathf.Abs(samples[0] - samples[samples.Length - 1]), Is.LessThan(0.0001f));
            }
            yield return null;
        }

        private bool IsPlaying(string clipName)
            => player.GetComponents<AudioSource>().Any(source => source.isPlaying && source.clip.name == clipName);
    }
}
#endif

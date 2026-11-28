#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.PrototypeAudio
{
    [DisallowMultipleComponent]
    public sealed class PrototypeSfxPlayer : MonoBehaviour
    {
        private const int VoiceCount = 4;
        private const double HitWindowSeconds = 0.1;
        private const int EnemyHitLimit = 2;

        [SerializeField] private AudioClip weaponAttack = null!;
        [SerializeField] private AudioClip enemyHit = null!;
        [SerializeField] private AudioClip playerHit = null!;
        [SerializeField] private AudioClip dodge = null!;
        [SerializeField] private AudioClip pickup = null!;
        [SerializeField] private AudioClip extractionLoop = null!;
        [SerializeField] private AudioClip extractionSuccess = null!;
        [SerializeField, UnityEngine.Range(0f, 1f)] private float volume = 0.8f;

        private enum Cue
        {
            WeaponAttack, EnemyHit, PlayerHit, Dodge, Pickup, ExtractionSuccess
        }

        private enum VoicePriority
        {
            AttackOrEnemyHit, DodgeOrPickup, PlayerHit, ExtractionSuccess
        }

        private readonly AudioSource?[] voices = new AudioSource?[VoiceCount];
        private readonly VoicePriority[] priorities = new VoicePriority[VoiceCount];
        private readonly HashSet<(Cue Cue, Guid EventId, string? TargetId)> acceptedEvents = new();
        private readonly Queue<double> enemyHits = new Queue<double>(EnemyHitLimit);
        private double lastPlayerHit = double.NegativeInfinity;
        private AudioSource? loop;
        private RunId currentRun;
        private int lastAttempt;
        private int activeAttempt;

        public float Volume => volume;
        public bool IsMuted { get; private set; }
        public bool IsExtractionLoopPlaying => loop != null && loop.isPlaying;
        public int ActiveOneShots
        {
            get
            {
                int count = 0;
                foreach (AudioSource? voice in voices)
                {
                    if (voice != null && voice.isPlaying)
                        count++;
                }
                return count;
            }
        }

        private void Awake()
        {
            for (int index = 0; index < voices.Length; index++)
                voices[index] = CreateSource(isLoop: false);
            loop = CreateSource(isLoop: true);
            loop.clip = extractionLoop;
        }

        private void OnEnable() => ResumeExtractionLoop();

        private void OnDisable()
        {
            StopOneShots();
            if (loop != null)
                loop.Stop();
        }

        public void BeginRun(RunId runId)
        {
            if (!runId.IsValid)
                throw new ArgumentException("A valid run ID is required.", nameof(runId));
            if (currentRun == runId)
                return;

            StopOneShots();
            StopExtractionLoop();
            currentRun = runId;
            lastAttempt = 0;
            lastPlayerHit = double.NegativeInfinity;
            enemyHits.Clear();
            acceptedEvents.Clear();
        }

        /// <summary>Closes the event session. A result cue already playing can finish.</summary>
        public void EndRun(RunId runId)
        {
            if (!IsCurrentRun(runId))
                return;

            StopExtractionLoop();
            currentRun = default;
            acceptedEvents.Clear();
        }

        public void SetVolume(float value)
        {
            if (!float.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            volume = Mathf.Clamp01(value);
            foreach (AudioSource? voice in voices)
            {
                if (voice != null)
                    voice.volume = volume;
            }
            if (loop != null)
                loop.volume = volume;
        }

        public void SetMuted(bool muted)
        {
            if (IsMuted == muted)
                return;

            IsMuted = muted;
            if (muted)
            {
                StopOneShots();
                if (loop != null)
                    loop.Stop();
            }
            else
            {
                ResumeExtractionLoop();
            }
        }

        public bool OnWeaponAttack(RunId runId, Guid activationId)
            => TryPlayEvent(runId, activationId, Cue.WeaponAttack);

        public bool OnEnemyHit(RunId runId, Guid applicationId, string targetId)
            => !string.IsNullOrWhiteSpace(targetId) && TryPlayEvent(runId, applicationId, Cue.EnemyHit, targetId);

        public bool OnPlayerHit(RunId runId, Guid applicationId)
            => TryPlayEvent(runId, applicationId, Cue.PlayerHit);

        public bool OnDodge(RunId runId, Guid activationId)
            => TryPlayEvent(runId, activationId, Cue.Dodge);

        public bool OnPickup(RunId runId, Guid confirmationId)
            => TryPlayEvent(runId, confirmationId, Cue.Pickup);

        public bool OnExtractionStarted(RunId runId, int attemptId)
        {
            if (!IsCurrentRun(runId) || attemptId <= lastAttempt)
                return false;

            StopExtractionLoop();
            lastAttempt = attemptId;
            activeAttempt = attemptId;
            ResumeExtractionLoop();
            return true;
        }

        public bool OnExtractionResolved(RunId runId, int attemptId, bool succeeded)
        {
            if (!IsCurrentRun(runId) || attemptId <= 0 || activeAttempt != attemptId)
                return false;

            StopExtractionLoop();
            if (succeeded && CanPlay)
                TryPlayCue(Cue.ExtractionSuccess);
            return true;
        }

        private bool CanPlay => !IsMuted && isActiveAndEnabled;

        private bool IsCurrentRun(RunId runId) => runId.IsValid && currentRun == runId;

        private bool TryPlayEvent(RunId runId, Guid eventId, Cue cue, string? targetId = null)
        {
            if (!IsCurrentRun(runId) || eventId == Guid.Empty || !acceptedEvents.Add((cue, eventId, targetId))
                || !CanPlay)
                return false;

            double now = Time.unscaledTimeAsDouble;
            if (cue == Cue.EnemyHit)
            {
                while (enemyHits.Count != 0 && now - enemyHits.Peek() >= HitWindowSeconds)
                    enemyHits.Dequeue();

                if (enemyHits.Count == EnemyHitLimit)
                    return false;
            }
            if (cue == Cue.PlayerHit && now - lastPlayerHit < HitWindowSeconds)
                return false;

            if (!TryPlayCue(cue))
                return false;

            if (cue == Cue.EnemyHit)
                enemyHits.Enqueue(now);
            if (cue == Cue.PlayerHit)
                lastPlayerHit = now;
            return true;
        }

        private bool TryPlayCue(Cue cue)
        {
            (AudioClip clip, VoicePriority priority) = cue switch
            {
                Cue.WeaponAttack => (weaponAttack, VoicePriority.AttackOrEnemyHit),
                Cue.EnemyHit => (enemyHit, VoicePriority.AttackOrEnemyHit),
                Cue.PlayerHit => (playerHit, VoicePriority.PlayerHit),
                Cue.Dodge => (dodge, VoicePriority.DodgeOrPickup),
                Cue.Pickup => (pickup, VoicePriority.DodgeOrPickup),
                Cue.ExtractionSuccess => (extractionSuccess, VoicePriority.ExtractionSuccess),
                _ => throw new ArgumentOutOfRangeException(nameof(cue))
            };
            if (clip == null)
                return false;

            int index = FindVoice(priority);
            if (index < 0)
                return false;

            AudioSource voice = voices[index]!;
            voice.Stop();
            priorities[index] = priority;
            voice.clip = clip;
            voice.Play();
            return true;
        }

        private int FindVoice(VoicePriority priority)
        {
            int replace = -1;
            for (int index = 0; index < voices.Length; index++)
            {
                AudioSource? voice = voices[index];
                if (voice == null)
                    continue;
                if (!voice.isPlaying)
                    return index;
                if (priorities[index] < priority && (replace < 0 || priorities[index] < priorities[replace]))
                    replace = index;
            }
            return replace;
        }

        private void StopOneShots()
        {
            foreach (AudioSource? voice in voices)
            {
                if (voice != null)
                    voice.Stop();
            }
        }

        private void StopExtractionLoop()
        {
            activeAttempt = 0;
            if (loop != null)
                loop.Stop();
        }

        private void ResumeExtractionLoop()
        {
            if (activeAttempt > 0 && CanPlay && loop != null && loop.clip != null && !loop.isPlaying)
                loop.Play();
        }

        private AudioSource CreateSource(bool isLoop)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = isLoop;
            source.spatialBlend = 0f;
            source.volume = volume;
            return source;
        }
    }
}

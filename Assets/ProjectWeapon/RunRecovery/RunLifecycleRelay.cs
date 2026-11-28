#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.RunRecovery
{
    /// <summary>Bridges Unity app callbacks and the host's connection notifications into run recovery.</summary>
    public sealed class RunLifecycleRelay : MonoBehaviour
    {
        private RunRecoveryCoordinator? coordinator;
        private Func<IEnumerable<RunSignal>>? readConfirmedFrame;
        private Func<bool>? hasVerifiedClock;
        private bool paused;
        private bool unfocused;
        private bool disconnected;
        private bool quitting;
        private bool reportedInterruption;

        private bool IsUnavailable => paused || unfocused || disconnected || quitting;
        public bool IsGameplayBlocked => IsUnavailable || coordinator?.IsGameplayBlocked == true;
        public event Action<RunRecoveryResult>? RecoveryChanged;

        public void Initialize(RunRecoveryCoordinator recovery, Func<IEnumerable<RunSignal>> confirmedFrame,
            Func<bool> verifiedClock)
        {
            if (coordinator != null)
                throw new InvalidOperationException("Use one lifecycle relay per recovery owner.");
            if (recovery == null)
                throw new ArgumentNullException(nameof(recovery));
            readConfirmedFrame = confirmedFrame ?? throw new ArgumentNullException(nameof(confirmedFrame));
            hasVerifiedClock = verifiedClock ?? throw new ArgumentNullException(nameof(verifiedClock));
            coordinator = recovery;
            UpdateAvailability();
        }

        public void SetConnectionAvailable(bool connected)
        {
            disconnected = !connected;
            UpdateAvailability();
        }

        public RunRecoveryResult Reconnect(RunId runId, VerifiedRunRecoveryCheckpoint? checkpoint = null)
        {
            if (coordinator == null)
                throw new InvalidOperationException("Initialize the run lifecycle relay before reconnecting.");
            RunRecoveryResult result = IsUnavailable
                ? new RunRecoveryResult(RunRecoveryStatus.RecoveryPending)
                : coordinator.Reconnect(runId, hasVerifiedClock!(), checkpoint);
            RecoveryChanged?.Invoke(result);
            return result;
        }

        private void OnApplicationPause(bool isPaused)
        {
            paused = isPaused;
            UpdateAvailability();
        }

        private void OnApplicationFocus(bool isFocused)
        {
            unfocused = !isFocused;
            UpdateAvailability();
        }

        private void OnApplicationQuit()
        {
            quitting = true;
            UpdateAvailability();
        }

        private void UpdateAvailability()
        {
            if (coordinator == null || reportedInterruption == IsUnavailable)
                return;

            reportedInterruption = IsUnavailable;
            if (reportedInterruption)
            {
                bool verified = hasVerifiedClock!();
                IEnumerable<RunSignal> frame = verified ? readConfirmedFrame!() : Array.Empty<RunSignal>();
                RunRecoveryResult result = coordinator.Suspend(frame, verified);
                RecoveryChanged?.Invoke(result);
            }
            else if (coordinator.ActiveRunId is { } active)
            {
                Reconnect(active);
            }
        }
    }
}

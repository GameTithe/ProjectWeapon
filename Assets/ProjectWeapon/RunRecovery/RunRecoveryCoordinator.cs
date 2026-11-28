#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectWeapon.Extraction;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.RunRecovery
{
    public enum RunRecoveryStatus
    {
        NoActiveRun,
        Suspended,
        Resumed,
        Finalized,
        Settled,
        RecoveryPending
    }

    /// <summary>The host supplies this only after verifying the complete checkpoint and its clock.</summary>
    public readonly struct VerifiedRunRecoveryCheckpoint
    {
        public VerifiedRunRecoveryCheckpoint(RunCheckpoint timeline, long saveRevision, TimeSpan verifiedNow)
        {
            Timeline = timeline;
            SaveRevision = saveRevision;
            VerifiedNow = verifiedNow;
        }

        public RunCheckpoint Timeline { get; }
        public long SaveRevision { get; }
        public TimeSpan VerifiedNow { get; }
    }

    public sealed class RunRecoveryResult
    {
        internal RunRecoveryResult(RunRecoveryStatus status, RunCheckpoint? checkpoint = null,
            RunResult? result = null, SnapshotLoadResult? save = null, SettlementSaveRecord? receipt = null)
        {
            Status = status;
            Checkpoint = checkpoint;
            Result = result;
            Save = save;
            Receipt = receipt;
        }

        public RunRecoveryStatus Status { get; }
        public RunCheckpoint? Checkpoint { get; }
        public RunResult? Result { get; }
        public SnapshotLoadResult? Save { get; }
        public SettlementSaveRecord? Receipt { get; }
    }

    /// <summary>Coordinates lifecycle notifications on the same thread as the owning RunState.</summary>
    public sealed class RunRecoveryCoordinator
    {
        private readonly RunState run;
        private readonly ExtractionChannel extraction;
        private readonly AtomicSnapshotStore store;
        private readonly Func<WeaponDefinitionId, bool> definitionExists;
        private readonly Func<SaveSnapshot, RunId, bool>? hasReservation;
        private bool unverifiedInterruption;
        private RunRecoveryResult? lastInterruption;

        public RunRecoveryCoordinator(RunState run, ExtractionChannel extraction, AtomicSnapshotStore store,
            Func<WeaponDefinitionId, bool> definitionExists, Func<SaveSnapshot, RunId, bool>? hasReservation = null)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            this.extraction = extraction ?? throw new ArgumentNullException(nameof(extraction));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.definitionExists = definitionExists ?? throw new ArgumentNullException(nameof(definitionExists));
            this.hasReservation = hasReservation;
        }

        public RunId? ActiveRunId => run.CurrentRun?.Id;
        public bool IsGameplayBlocked { get; private set; }

        public RunRecoveryResult Suspend(IEnumerable<RunSignal> confirmedFrame, bool hasVerifiedClock)
        {
            if (confirmedFrame == null)
                throw new ArgumentNullException(nameof(confirmedFrame));
            if (run.CurrentRun == null)
                return new RunRecoveryResult(RunRecoveryStatus.NoActiveRun);
            if (lastInterruption is { Checkpoint: { } checkpoint } && checkpoint.RunId == run.CurrentRun.Id)
                return lastInterruption;

            IsGameplayBlocked = true;
            if (run.Result != null)
                return lastInterruption = Capture(RunRecoveryStatus.Finalized);

            unverifiedInterruption = !hasVerifiedClock
                || !extraction.TryProcessRecoveryFrame(confirmedFrame, interrupt: true, out _);
            return lastInterruption = Capture(unverifiedInterruption ? RunRecoveryStatus.RecoveryPending
                : run.Result == null ? RunRecoveryStatus.Suspended : RunRecoveryStatus.Finalized);
        }

        public RunRecoveryResult Reconnect(RunId requestedRun, bool hasVerifiedContinuity,
            VerifiedRunRecoveryCheckpoint? verifiedCheckpoint = null)
        {
            if (run.CurrentRun is { } current && current.Id != requestedRun)
                return new RunRecoveryResult(RunRecoveryStatus.RecoveryPending);

            IsGameplayBlocked = true;
            if (!requestedRun.IsValid)
                return Capture(RunRecoveryStatus.RecoveryPending);

            SnapshotLoadResult save;
            try
            {
                save = store.LoadWithRecovery(definitionExists);
            }
            catch (IOException)
            {
                return Capture(RunRecoveryStatus.RecoveryPending);
            }
            catch (UnauthorizedAccessException)
            {
                return Capture(RunRecoveryStatus.RecoveryPending);
            }
            if (save.Status != SnapshotLoadStatus.Healthy || save.Snapshot is not { } snapshot)
                return Capture(RunRecoveryStatus.RecoveryPending, save);

            SettlementSaveRecord? receipt = snapshot.Settlements.FirstOrDefault(record => record.RunId == requestedRun.Value);
            if (receipt != null)
            {
                return Capture(HasKnownOutcome(receipt) ? RunRecoveryStatus.Settled : RunRecoveryStatus.RecoveryPending,
                    save, receipt);
            }

            if (hasReservation?.Invoke(snapshot, requestedRun) == false)
                return Capture(RunRecoveryStatus.RecoveryPending, save);
            if (run.Result != null)
                return Capture(RunRecoveryStatus.Finalized, save);
            if (!hasVerifiedContinuity)
                return Capture(RunRecoveryStatus.RecoveryPending, save);

            if (run.CurrentRun != null)
            {
                if (unverifiedInterruption || (run.ActiveExtraction != null && lastInterruption == null)
                    || !extraction.TryProcessRecoveryFrame(Array.Empty<RunSignal>(),
                    interrupt: false, out _))
                    return Capture(RunRecoveryStatus.RecoveryPending, save);
            }
            else if (verifiedCheckpoint is not { } cold || cold.Timeline.RunId != requestedRun
                || cold.SaveRevision != snapshot.Revision
                || !run.TryRestoreVerifiedCheckpoint(cold.Timeline, cold.VerifiedNow))
            {
                return Capture(RunRecoveryStatus.RecoveryPending, save);
            }

            lastInterruption = null;
            unverifiedInterruption = false;
            IsGameplayBlocked = run.Result != null;
            return Capture(run.Result == null ? RunRecoveryStatus.Resumed : RunRecoveryStatus.Finalized, save);
        }

        private RunRecoveryResult Capture(RunRecoveryStatus status, SnapshotLoadResult? save = null,
            SettlementSaveRecord? receipt = null)
            => new RunRecoveryResult(status, run.GetRecoveryCheckpoint(), run.Result, save, receipt);

        private static bool HasKnownOutcome(SettlementSaveRecord receipt)
            => RunSettlementDetails.Read(receipt) != null
                || (Enum.TryParse(receipt.Result, out RunEndReason reason) && Enum.IsDefined(typeof(RunEndReason), reason)
                    && StringComparer.Ordinal.Equals(receipt.Result, reason.ToString()));
    }
}

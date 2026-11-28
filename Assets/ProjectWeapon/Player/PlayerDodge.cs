#nullable enable

using System;
using ProjectWeapon.Run;

namespace ProjectWeapon.Player
{
    /// <summary>Owns dodge timing for the active sortie, independent of physics step timing.</summary>
    public sealed class PlayerDodge
    {
        private RunId runId;
        private TimeSpan startedAt;
        private TimeSpan invulnerableUntil;
        private TimeSpan readyAt;

        public bool TryStart(RunState run, TimeSpan effectiveAt, TimeSpan duration,
            TimeSpan invulnerability, TimeSpan cooldown)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));

            RunSession? session = run.CurrentRun;
            if (session == null || run.Result != null || effectiveAt < session.StartedAt || effectiveAt >= session.Deadline
                || duration <= TimeSpan.Zero || invulnerability < TimeSpan.Zero || invulnerability > duration
                || cooldown < duration || effectiveAt > TimeSpan.MaxValue - cooldown
                || (runId == session.Id && effectiveAt < readyAt))
                return false;

            runId = session.Id;
            startedAt = effectiveAt;
            invulnerableUntil = effectiveAt + invulnerability;
            readyAt = effectiveAt + cooldown;
            return true;
        }

        public bool IsInvulnerable(RunId id, TimeSpan effectiveAt)
            => runId == id && effectiveAt >= startedAt && effectiveAt < invulnerableUntil;
    }
}

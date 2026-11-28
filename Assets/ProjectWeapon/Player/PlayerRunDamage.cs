#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Run;

namespace ProjectWeapon.Player
{
    public static class PlayerRunDamage
    {
        /// <summary>Applies a hit and adds its observations to the caller's current frame.</summary>
        /// <remarks>The caller supplies observed, valid hits for the current frame in effective-time order.</remarks>
        public static bool ApplyHit(
            PlayerHealth health,
            RunState run,
            RunId runId,
            int amount,
            TimeSpan effectiveAt,
            List<RunSignal> frameSignals,
            PlayerDodge? dodge = null)
        {
            if (health == null)
                throw new ArgumentNullException(nameof(health));
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (frameSignals == null)
                throw new ArgumentNullException(nameof(frameSignals));

            RunSession? session = run.CurrentRun;
            if (session == null || runId != session.Id || run.Result != null
                || effectiveAt < session.StartedAt || effectiveAt >= session.Deadline
                || (dodge?.IsInvulnerable(runId, effectiveAt) ?? false))
                return false;

            ExtractionAttempt? attempt = run.ActiveExtraction;
            if (!health.TryAcceptDamage(amount, effectiveAt, out PlayerDamage damage))
                return false;

            if (attempt != null && effectiveAt >= attempt.StartedAt)
                frameSignals.Add(RunSignal.CancelExtraction(runId, attempt.Id, effectiveAt));

            if (damage.IsFatal)
                frameSignals.Add(RunSignal.PlayerDied(runId, effectiveAt));

            health.PublishDamage(damage);
            return true;
        }
    }
}

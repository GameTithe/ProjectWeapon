#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.AutoAttack
{
    /// <summary>Schedules basic attacks on the run's owning gameplay thread.</summary>
    public sealed class AutoAttackScheduler
    {
        private readonly RunState run;
        private readonly Func<BasicAttackRequest, bool> tryExecute;
        private RunId observedRun;
        private TimeSpan lastTickAt;
        private TimeSpan? lastAttackAt;
        private TimeSpan cooldown;
        private bool dispatching;

        /// <param name="run">The authoritative run state used by the extraction channel.</param>
        /// <param name="tryExecute">Starts an attack synchronously. Return false only when nothing started.</param>
        /// <param name="initiallyEnabled">The player's initial toggle setting.</param>
        public AutoAttackScheduler(RunState run, Func<BasicAttackRequest, bool> tryExecute, bool initiallyEnabled)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            this.tryExecute = tryExecute ?? throw new ArgumentNullException(nameof(tryExecute));
            IsEnabled = initiallyEnabled;
        }

        public bool IsEnabled { get; private set; }
        public bool IsEffective => IsEnabled && run.Phase == RunPhase.Exploring;

        /// <summary>Applies a toggle immediately and preserves its previous value throughout extraction.</summary>
        public bool TrySetEnabled(bool enabled)
        {
            if (run.Phase == RunPhase.Extracting)
                return false;

            IsEnabled = enabled;
            return true;
        }

        /// <summary>Call after extraction and run signals are processed, using the same sampled run clock.</summary>
        public bool TryAttack(
            TimeSpan now, WorldPoint2D origin, WeaponDefinition? weapon, IReadOnlyList<TargetCandidate> targets)
        {
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));

            RunSession? session = run.CurrentRun;
            if (dispatching || session == null || now < session.StartedAt || now >= session.Deadline)
                return false;

            if (observedRun != session.Id)
            {
                observedRun = session.Id;
                lastTickAt = session.StartedAt;
                lastAttackAt = null;
            }

            if (now < lastTickAt)
                return false;

            lastTickAt = now;
            if (!IsEffective || (lastAttackAt.HasValue && now - lastAttackAt.Value < cooldown))
                return false;

            if (weapon == null || !weapon.Id.IsValid || weapon.BasicAttack is not BasicAttackPattern pattern
                || pattern == null || !pattern.IsValid)
                return false;

            TargetCandidate? target = TargetSelector.FindNearest(origin, pattern.Range, targets);
            if (!target.HasValue)
                return false;

            TimeSpan startedCooldown = pattern.Cooldown;
            var request = new BasicAttackRequest(session.Id, weapon.Id, pattern, origin, target.Value, now);
            dispatching = true;
            try
            {
                if (!tryExecute(request))
                    return false;

                lastAttackAt = now;
                cooldown = startedCooldown;
                return true;
            }
            finally
            {
                dispatching = false;
            }
        }
    }
}

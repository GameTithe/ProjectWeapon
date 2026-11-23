#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace ProjectWeapon.Combat.Tests
{
    public sealed class ProjectileLifecycleTests
    {
        private static readonly Guid FirstApplication = new Guid("57ef833d-a346-409e-b221-f7f43bd542af");
        private static readonly Guid NextApplication = new Guid("0f008c32-dc29-41c0-b971-20f941e7966d");
        private const string Attacker = "shooter";

        [Test]
        public void LaunchInitializesPositionVelocityAndElapsedAndRejectsActiveRelaunch()
        {
            var state = new ProjectileLifecycle();
            Assert.That(state.Phase, Is.EqualTo(ProjectilePhase.Inactive));
            Assert.That(state.Advance(FirstApplication, Ms(50)), Is.False);
            Assert.That(state.TryLaunch(Launch(FirstApplication)), Is.True);
            Assert.That(state.ApplicationId, Is.EqualTo(FirstApplication));
            Assert.That(state.Phase, Is.EqualTo(ProjectilePhase.Active));
            Assert.That(state.Position.X, Is.EqualTo(2));
            Assert.That(state.Position.Y, Is.EqualTo(4));
            Assert.That(state.Velocity.X, Is.EqualTo(8));
            Assert.That(state.Lifetime, Is.EqualTo(Ms(1000)));
            Assert.That(state.Elapsed, Is.EqualTo(TimeSpan.Zero));
            Assert.That(state.TryLaunch(Launch(NextApplication)), Is.False);

            Assert.That(state.Advance(FirstApplication, Ms(250)), Is.True);
            Assert.That(state.Position.X, Is.EqualTo(4));
            Assert.That(state.Position.Y, Is.EqualTo(3));
            Assert.That(state.Advance(FirstApplication, Ms(250)), Is.True);
            Assert.That(state.Position.X, Is.EqualTo(4));
            Assert.That(state.Advance(FirstApplication, Ms(249)), Is.False);
            Assert.That(state.Elapsed, Is.EqualTo(Ms(250)));
        }

        [Test]
        public void PiercingModeEmitsOneRequestPerEligibleTargetAndIgnoresDuplicateColliders()
        {
            var state = new ProjectileLifecycle();
            Assert.That(state.TryLaunch(Launch(FirstApplication)), Is.True);
            Assert.That(state.ContactTarget(FirstApplication, Ms(25), Candidate(Attacker)), Is.Null);
            Assert.That(state.ContactTarget(FirstApplication, Ms(25), Candidate("dead", alive: false)), Is.Null);
            Assert.That(state.ContactTarget(FirstApplication, Ms(25), Candidate("friendly", attackable: false)), Is.Null);
            Assert.That(state.ContactTarget(FirstApplication, Ms(25), default), Is.Null);

            DamageRequest? hit = state.ContactTarget(FirstApplication, Ms(50), Candidate("enemy"));
            Assert.That(hit.HasValue, Is.True);
            Assert.That(hit!.Value.ApplicationId, Is.EqualTo(FirstApplication));
            Assert.That(hit.Value.AttackerId, Is.EqualTo(Attacker));
            Assert.That(hit.Value.TargetId, Is.EqualTo("enemy"));
            Assert.That(hit.Value.Amount, Is.EqualTo(4.5f));
            Assert.That(state.ContactTarget(FirstApplication, Ms(50), Candidate("enemy")), Is.Null);
            Assert.That(state.ContactTarget(FirstApplication, Ms(100), Candidate("enemy")), Is.Null);
            Assert.That(state.ContactTarget(FirstApplication, Ms(100), Candidate("other"))?.TargetId, Is.EqualTo("other"));
            Assert.That(state.Phase, Is.EqualTo(ProjectilePhase.Active));
        }

        [Test]
        public void StopModeTerminatesOnlyForAnAdmittedTarget()
        {
            var state = new ProjectileLifecycle();
            Assert.That(state.TryLaunch(Launch(FirstApplication, ProjectileHitBehavior.StopOnTarget)), Is.True);
            Assert.That(state.ContactTarget(FirstApplication, Ms(25), Candidate(Attacker)), Is.Null);
            Assert.That(state.Phase, Is.EqualTo(ProjectilePhase.Active));
            Assert.That(state.ContactTarget(FirstApplication, Ms(50), Candidate("first"))?.TargetId, Is.EqualTo("first"));
            Assert.That(state.Phase, Is.EqualTo(ProjectilePhase.HitTarget));
            Assert.That(state.ContactTarget(FirstApplication, Ms(50), Candidate("second")), Is.Null);
            Assert.That(state.Advance(FirstApplication, Ms(999)), Is.False);
            Assert.That(state.TryLaunch(Launch(NextApplication)), Is.False);
        }

        [Test]
        public void WorldContactStopsAndRejectsFutureHitsEvenWhenPiercing()
        {
            var state = new ProjectileLifecycle();
            Assert.That(state.TryLaunch(Launch(FirstApplication)), Is.True);

            Assert.That(state.ContactWorld(FirstApplication, Ms(500)), Is.True);
            Assert.That(state.Phase, Is.EqualTo(ProjectilePhase.Blocked));
            Assert.That(state.Position.X, Is.EqualTo(6));
            Assert.That(state.Position.Y, Is.EqualTo(2));
            Assert.That(state.ContactTarget(FirstApplication, Ms(501), Candidate("enemy")), Is.Null);
            Assert.That(state.ContactWorld(FirstApplication, Ms(502)), Is.False);
        }

        [Test]
        public void ExactLifetimeExpiresBeforeCollisionAndClampsThePosition()
        {
            var state = new ProjectileLifecycle();
            Assert.That(state.TryLaunch(Launch(FirstApplication)), Is.True);
            Assert.That(state.ContactTarget(FirstApplication, Ms(999), Candidate("last")), Is.Not.Null);
            Assert.That(state.ContactTarget(FirstApplication, Ms(1000), Candidate("at-deadline")), Is.Null);

            Assert.That(state.Phase, Is.EqualTo(ProjectilePhase.Expired));
            Assert.That(state.Elapsed, Is.EqualTo(Ms(1000)));
            Assert.That(state.Position.X, Is.EqualTo(10));
            Assert.That(state.Position.Y, Is.EqualTo(0));
            Assert.That(state.ContactWorld(FirstApplication, Ms(1000)), Is.False);
        }

        [Test]
        public void ExpirationWithNoContactsIsHandledByAdvance()
        {
            var state = new ProjectileLifecycle();
            Assert.That(state.TryLaunch(Launch(FirstApplication)), Is.True);
            Assert.That(state.Advance(FirstApplication, TimeSpan.MaxValue), Is.True);
            Assert.That(state.Phase, Is.EqualTo(ProjectilePhase.Expired));
            Assert.That(state.Elapsed, Is.EqualTo(Ms(1000)));
            Assert.That(state.Position.X, Is.EqualTo(10));
        }

        [Test]
        public void PoolResetClearsAllRentalStateAndRejectsOldContacts()
        {
            var state = new ProjectileLifecycle();
            Assert.That(state.TryLaunch(Launch(FirstApplication)), Is.True);
            Assert.That(state.ContactTarget(FirstApplication, Ms(400), Candidate("enemy")), Is.Not.Null);
            Assert.That(state.ContactWorld(FirstApplication, Ms(600)), Is.True);
            state.ResetForPool();

            Assert.That(state.Phase, Is.EqualTo(ProjectilePhase.Inactive));
            Assert.That(state.ApplicationId, Is.EqualTo(Guid.Empty));
            Assert.That(state.Lifetime, Is.EqualTo(TimeSpan.Zero));
            Assert.That(state.Elapsed, Is.EqualTo(TimeSpan.Zero));
            Assert.That(state.Position.X, Is.Zero);
            Assert.That(state.Position.Y, Is.Zero);
            Assert.That(state.Velocity.X, Is.Zero);
            Assert.That(state.Velocity.Y, Is.Zero);
            Assert.That(state.ContactTarget(FirstApplication, Ms(600), Candidate("enemy")), Is.Null);

            var next = new ProjectileLaunch(NextApplication, "next-shooter", new WorldPoint2D(-3, 1),
                new WorldPoint2D(1, 2), Ms(200), 9f, ProjectileHitBehavior.StopOnTarget);
            Assert.That(state.TryLaunch(next), Is.True);
            Assert.That(state.Position.X, Is.EqualTo(-3));
            Assert.That(state.Velocity.Y, Is.EqualTo(2));
            Assert.That(state.Lifetime, Is.EqualTo(Ms(200)));
            Assert.That(state.ContactWorld(FirstApplication, Ms(150)), Is.False);
            Assert.That(state.Advance(FirstApplication, Ms(150)), Is.False);
            Assert.That(state.Elapsed, Is.EqualTo(TimeSpan.Zero));
            Assert.That(state.ContactTarget(NextApplication, Ms(-1), Candidate("enemy")), Is.Null);

            DamageRequest? hit = state.ContactTarget(NextApplication, Ms(10), Candidate("enemy"));
            Assert.That(hit?.AttackerId, Is.EqualTo("next-shooter"));
            Assert.That(hit?.ApplicationId, Is.EqualTo(NextApplication));
            Assert.That(hit?.Amount, Is.EqualTo(9));
            Assert.That(state.Phase, Is.EqualTo(ProjectilePhase.HitTarget));
        }

        [TestCaseSource(nameof(InvalidLaunches))]
        public void InvalidLaunchDoesNotPolluteInactiveState(ProjectileLaunch invalid)
        {
            var state = new ProjectileLifecycle();
            Assert.That(state.TryLaunch(invalid), Is.False);
            Assert.That(state.Phase, Is.EqualTo(ProjectilePhase.Inactive));
            Assert.That(state.ApplicationId, Is.EqualTo(Guid.Empty));
            Assert.That(state.TryLaunch(Launch(FirstApplication)), Is.True);
        }

        private static IEnumerable<TestCaseData> InvalidLaunches()
        {
            yield return Invalid(default, "Default");
            yield return Invalid(Launch(Guid.Empty), "MissingApplication");
            yield return Invalid(new ProjectileLaunch(FirstApplication, "", default, default, Ms(500), 1, default), "MissingAttacker");
            yield return Invalid(new ProjectileLaunch(FirstApplication, Attacker, new WorldPoint2D(float.NaN, 0),
                default, Ms(500), 1, default), "InvalidOrigin");
            yield return Invalid(new ProjectileLaunch(FirstApplication, Attacker, default,
                new WorldPoint2D(float.PositiveInfinity, 0), Ms(500), 1, default), "InvalidVelocity");
            yield return Invalid(new ProjectileLaunch(FirstApplication, Attacker, default, default, Ms(0), 1, default), "ZeroLifetime");
            yield return Invalid(new ProjectileLaunch(FirstApplication, Attacker, default, default, Ms(-1), 1, default), "NegativeLifetime");
            yield return Invalid(new ProjectileLaunch(FirstApplication, Attacker, default, default, Ms(500), -1, default), "NegativeDamage");
            yield return Invalid(new ProjectileLaunch(FirstApplication, Attacker, default, default, Ms(500), float.NaN, default), "InvalidDamage");
            yield return Invalid(new ProjectileLaunch(FirstApplication, Attacker, default, default, Ms(500), 1,
                (ProjectileHitBehavior)123), "UnknownHitBehavior");
            yield return Invalid(new ProjectileLaunch(FirstApplication, Attacker, default,
                new WorldPoint2D(float.MaxValue, 0), Ms(2000), 1, default), "PositionOverflow");
        }

        private static TestCaseData Invalid(ProjectileLaunch launch, string reason)
            => new TestCaseData(launch).SetName($"InvalidLaunchDoesNotPolluteInactiveState({reason})");

        private static ProjectileLaunch Launch(Guid id, ProjectileHitBehavior behavior = ProjectileHitBehavior.ContinueThroughTargets)
            => new ProjectileLaunch(id, Attacker, new WorldPoint2D(2, 4), new WorldPoint2D(8, -4), Ms(1000), 4.5f, behavior);

        private static TargetCandidate Candidate(string id, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(0, 0), alive, attackable);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}

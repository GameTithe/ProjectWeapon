#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Leaderboard.Tests
{
    public sealed class GeneralRankingTests
    {
        private static readonly DateTimeOffset AchievedAt = new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero);
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private static readonly WeaponDefinitionId Axe = new WeaponDefinitionId("test_axe");
        private static readonly WeaponDefinitionId Bow = new WeaponDefinitionId("test_bow");

        [Test]
        public void RankFollowsEachCriterionInOrder()
        {
            AssertBefore(Record(zone: 3), Record(zone: 2));
            AssertBefore(Record(bossDefeated: true), Record(bossDefeated: false));
            AssertBefore(Record(milliseconds: 1001), Record(milliseconds: 1000));
            AssertBefore(Record(exportValue: 71), Record(exportValue: 70));
        }

        [Test]
        public void AllLowerPriorityValuesCanBeHigherWithoutReversingTheOrder()
        {
            AssertBefore(Record(zone: 3, bossDefeated: false, milliseconds: 1, exportValue: 0),
                Record(zone: 2, bossDefeated: true, milliseconds: 900000, exportValue: long.MaxValue));
            AssertBefore(Record(bossDefeated: true, milliseconds: 1, exportValue: 0),
                Record(bossDefeated: false, milliseconds: 900000, exportValue: long.MaxValue));
            AssertBefore(Record(milliseconds: 1001, exportValue: 0),
                Record(milliseconds: 1000, exportValue: long.MaxValue));
        }

        [Test]
        public void TiesShareRankAndDisplayUsesUtcThenOrdinalSubmissionId()
        {
            GeneralRankingRecord early = Record(achievedAt: AchievedAt, submissionId: "z");
            GeneralRankingRecord late = Record(achievedAt: AchievedAt.AddTicks(1), submissionId: "A");
            GeneralRankingRecord sameUtcUpper = Record(achievedAt: AchievedAt.ToOffset(TimeSpan.FromHours(9)),
                submissionId: "Z");
            GeneralRankingRecord sameUtcLower = Record(achievedAt: AchievedAt, submissionId: "a");

            Assert.That(GeneralRanking.CompareRank(early, late), Is.Zero);
            Assert.That(GeneralRanking.CompareRank(sameUtcUpper, sameUtcLower), Is.Zero);
            Assert.That(GeneralRanking.CompareDisplayOrder(early, late), Is.LessThan(0));
            Assert.That(sameUtcUpper.AchievedAtUtc.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(GeneralRanking.CompareDisplayOrder(sameUtcUpper, sameUtcLower), Is.LessThan(0));
            Assert.That(GeneralRanking.CompareDisplayOrder(early, Record(submissionId: "z")), Is.Zero);
            Assert.That(GeneralRanking.CompareDisplayOrder(Record(zone: 3, achievedAt: AchievedAt.AddDays(1)), early),
                Is.LessThan(0));
        }

        [Test]
        public void RemainingTimeUsesWholeMillisecondsAndCanBeZeroAfterTruncation()
        {
            RunResult submillisecond = StartRun(2, TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond - 1),
                RunEndReason.Extracted).Run;
            Assert.That(GeneralRankingRecord.TryCreate(submillisecond, true, 0, AchievedAt, "tiny", out var result), Is.True);
            Assert.That(result!.RemainingMilliseconds, Is.Zero);
        }

        [Test]
        public void PreviouslyAcceptedBestCanBeComparedAfterReload()
        {
            var saved = new GeneralRankingRecord(2, true, 1000, 70, AchievedAt, "saved");
            Assert.That(GeneralRanking.CompareRank(Record(bossDefeated: true), saved), Is.Zero);
            Assert.That(GeneralRanking.CompareRank(Record(bossDefeated: true, milliseconds: 1001), saved),
                Is.LessThan(0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GeneralRankingRecord(2, true, -1, 0, AchievedAt, "invalid"));
        }

        [TestCase(RunEndReason.Death)]
        [TestCase(RunEndReason.TimeExpired)]
        public void FailureCreatesNoRankingCandidate(RunEndReason reason)
        {
            RunResult failure = StartRun(5, TimeSpan.FromSeconds(100), reason).Run;

            for (int retry = 0; retry < 3; retry++)
            {
                Assert.That(GeneralRankingRecord.TryCreate(failure, true, long.MaxValue,
                    AchievedAt, "failed", out GeneralRankingRecord? candidate), Is.False);
                Assert.That(candidate, Is.Null);
            }
        }

        [Test]
        public void ExportCountsNewSettledInstanceIdsAndSkipsPackedAndLoanedWeapons()
        {
            var packed = WeaponInstance.Create(Bow, WeaponProvenance.ExplorationLoot, WeaponLocation.Equipped(0));
            var town = new InventoryService(new[] { packed });
            var fixture = StartRun(2, TimeSpan.FromSeconds(5), RunEndReason.Extracted, town);
            var first = NewRecovery(Sword);
            var third = NewRecovery(Axe);
            var sameDefinition = NewRecovery(Sword);
            var packedRecovery = WeaponInstance.Rehydrate(packed.Id, packed.DefinitionId, packed.Provenance, WeaponLocation.Warehouse());
            var loan = WeaponInstance.Create(Bow, WeaponProvenance.Loan, WeaponLocation.Equipped(0));
            var grades = new Dictionary<WeaponDefinitionId, int> { [Sword] = 1, [Axe] = 3, [Bow] = 5 };
            var recoveries = new[] { packedRecovery, loan, first, third, first, sameDefinition };

            Assert.That(WeaponExportValues.Prototype.Calculate(fixture.Run, fixture.Manifest!, recoveries, id => grades[id]),
                Is.EqualTo(80));
            Assert.That(WeaponExportValues.Prototype.Calculate(fixture.Run, fixture.Manifest!,
                new[] { packedRecovery, first, third }, id => grades[id]), Is.EqualTo(70));
            Assert.That(WeaponExportValues.Prototype.Calculate(fixture.Run, fixture.Manifest!,
                Array.Empty<WeaponInstance>(), id => grades[id]), Is.Zero);
            Assert.That(recoveries[2], Is.SameAs(first));
        }

        [TestCase(1, 10)]
        [TestCase(2, 25)]
        [TestCase(3, 60)]
        [TestCase(4, 140)]
        [TestCase(5, 300)]
        public void PrototypeValuesCanBeReplacedWithInjectedBalance(int grade, int expected)
        {
            var town = new InventoryService(new[] {
                WeaponInstance.Create(Bow, WeaponProvenance.Personal, WeaponLocation.Equipped(0))
            });
            var fixture = StartRun(1, TimeSpan.FromSeconds(5), RunEndReason.Extracted, town);
            var recoveries = new[] { NewRecovery(Sword) };

            Assert.That(WeaponExportValues.Prototype.Calculate(fixture.Run, fixture.Manifest!, recoveries, _ => grade),
                Is.EqualTo(expected));
            Assert.That(new WeaponExportValues(1, 2, 3, 4, 5).Calculate(fixture.Run, fixture.Manifest!, recoveries, _ => grade),
                Is.EqualTo(grade));
        }

        [Test]
        public void ExportRejectsWrongRunUnsettledItemsAndConflictingIdentities()
        {
            var brought = WeaponInstance.Create(Bow, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            var fixture = StartRun(1, TimeSpan.FromSeconds(5), RunEndReason.Extracted, new InventoryService(new[] { brought }));
            var unrelated = StartRun(1, TimeSpan.FromSeconds(5), RunEndReason.Extracted);
            var failure = StartRun(1, TimeSpan.FromSeconds(5), RunEndReason.Death);
            var first = NewRecovery(Sword);
            var conflicting = WeaponInstance.Rehydrate(first.Id, Axe, first.Provenance, first.Location);
            var unsettled = WeaponInstance.Create(Sword, WeaponProvenance.ExplorationLoot, WeaponLocation.Carried(0));
            WeaponExportValues values = WeaponExportValues.Prototype;

            Assert.Throws<ArgumentException>(() => values.Calculate(unrelated.Run, fixture.Manifest!, new[] { first }, _ => 1));
            Assert.Throws<ArgumentException>(() => values.Calculate(failure.Run, fixture.Manifest!, new[] { first }, _ => 1));
            Assert.Throws<ArgumentException>(() => values.Calculate(fixture.Run, fixture.Manifest!, new[] { unsettled }, _ => 1));
            Assert.Throws<ArgumentException>(() => values.Calculate(fixture.Run, fixture.Manifest!, new[] { first, conflicting }, _ => 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => values.Calculate(fixture.Run, fixture.Manifest!, new[] { first }, _ => 6));
        }

        private static WeaponInstance NewRecovery(WeaponDefinitionId definition) =>
            WeaponInstance.Create(definition, WeaponProvenance.ExplorationLoot, WeaponLocation.Warehouse());

        private static void AssertBefore(GeneralRankingRecord first, GeneralRankingRecord second)
        {
            Assert.That(GeneralRanking.CompareRank(first, second), Is.LessThan(0));
            Assert.That(GeneralRanking.CompareRank(second, first), Is.GreaterThan(0));
        }

        private static GeneralRankingRecord Record(int zone = 2, bool bossDefeated = false, int milliseconds = 1000,
            long exportValue = 70, DateTimeOffset? achievedAt = null, string submissionId = "verified")
        {
            RunResult run = StartRun(zone, TimeSpan.FromMilliseconds(milliseconds), RunEndReason.Extracted).Run;
            Assert.That(GeneralRankingRecord.TryCreate(run, bossDefeated, exportValue, achievedAt ?? AchievedAt,
                submissionId, out var record), Is.True);
            return record!;
        }

        private static (RunResult Run, SortieManifest? Manifest) StartRun(int zone, TimeSpan remaining,
            RunEndReason reason, InventoryService? town = null)
        {
            TimeSpan now = TimeSpan.Zero;
            var state = new RunState(() => now);
            RunId id = RunId.New();
            TimeSpan limit = RunState.ExtractionDuration + remaining;
            SortieManifest? manifest = null;
            if (town == null)
            {
                Assert.That(state.TryStartRun(id, zone, limit), Is.True);
            }
            else
            {
                SortieStartResult started = town.TryStartSortie(id.Value, () => state.TryStartRun(id, zone, limit));
                Assert.That(started.Status, Is.EqualTo(SortieStartStatus.Started));
                manifest = started.Manifest;
            }

            if (reason == RunEndReason.Extracted)
            {
                ExtractionAttempt attempt = state.TryBeginExtraction(id, isInRange: true)!;
                now = attempt.CompletesAt;
                state.ProcessSignals(new[] { RunSignal.CompleteExtraction(id, attempt.Id, now) });
            }
            else if (reason == RunEndReason.Death)
            {
                now = TimeSpan.FromSeconds(1);
                state.ProcessSignals(new[] { RunSignal.PlayerDied(id, now) });
            }
            else
            {
                now = limit;
                state.ProcessSignals(Array.Empty<RunSignal>());
            }

            Assert.That(state.Result!.Reason, Is.EqualTo(reason));
            return (state.Result, manifest);
        }
    }
}

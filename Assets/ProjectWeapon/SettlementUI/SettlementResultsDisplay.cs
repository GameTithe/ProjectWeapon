#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.Weapons;

[assembly: InternalsVisibleTo("ProjectWeapon.SettlementUI.Tests")]

namespace ProjectWeapon.SettlementUI
{
    internal readonly struct SettlementSection
    {
        public SettlementSection(string title, params string[] lines)
        {
            Title = title;
            Lines = lines.Length == 0 ? new[] { "없음" } : lines;
        }

        public string Title { get; }
        public IReadOnlyList<string> Lines { get; }
    }

    internal sealed class SettlementResultsDisplay
    {
        private SettlementResultsDisplay(string title, string notice, Guid? runId, RunEndReason? reason,
            SettlementSection[]? items = null, SettlementSection[]? records = null)
        {
            Title = title;
            Notice = notice;
            RunId = runId;
            Reason = reason;
            Items = items ?? Array.Empty<SettlementSection>();
            Records = records ?? Array.Empty<SettlementSection>();
        }

        public string Title { get; }
        public string Notice { get; }
        public Guid? RunId { get; }
        public RunEndReason? Reason { get; }
        public IReadOnlyList<SettlementSection> Items { get; }
        public IReadOnlyList<SettlementSection> Records { get; }

        public static SettlementResultsDisplay FromCommit(RunSettlementCommit commit, Func<WeaponDefinitionId, string> name)
            => FromStatus(commit.Status, commit.Receipt, name);

        internal static SettlementResultsDisplay FromStatus(RunSettlementStatus status, SettlementSaveRecord? receipt,
            Func<WeaponDefinitionId, string> name)
        {
            switch (status)
            {
                case RunSettlementStatus.RetryRequired:
                    return new SettlementResultsDisplay("정산 저장 대기", "정산 저장을 완료하지 못했습니다.", null, null);
                case RunSettlementStatus.MissingSave:
                    return new SettlementResultsDisplay("정산 정보 확인 불가", "저장 데이터를 찾을 수 없습니다.", null, null);
                case RunSettlementStatus.Committed:
                case RunSettlementStatus.AlreadyCommitted:
                case RunSettlementStatus.ConflictingOutcome:
                    if (receipt != null)
                        return FromReceipt(receipt, name, status == RunSettlementStatus.ConflictingOutcome);
                    break;
            }

            return new SettlementResultsDisplay("정산 정보 확인 불가", "확정된 정산 내역을 찾을 수 없습니다.", null, null);
        }

        public static SettlementResultsDisplay FromReceipt(SettlementSaveRecord receipt,
            Func<WeaponDefinitionId, string> name, bool conflicting = false)
        {
            if (receipt.RunId == Guid.Empty)
                return new SettlementResultsDisplay("정산 정보 확인 불가", "확정된 정산 내역을 찾을 수 없습니다.", null, null);

            string notice = conflicting ? "이미 확정된 정산을 표시합니다." : string.Empty;
            RunSettlementDetails? details = RunSettlementDetails.Read(receipt);
            if (details == null)
            {
                RunEndReason? reason = receipt.Result switch
                {
                    nameof(RunEndReason.Extracted) => RunEndReason.Extracted,
                    nameof(RunEndReason.Death) => RunEndReason.Death,
                    nameof(RunEndReason.TimeExpired) => RunEndReason.TimeExpired,
                    _ => null
                };
                return new SettlementResultsDisplay(TitleFor(reason),
                    Append(notice, "저장된 영수증에 상세 내역이 없습니다."), receipt.RunId, reason);
            }

            var coins = new SettlementSection("정산 코인",
                $"확정 코인 +{details.AwardedCoins:N0}", $"소멸 코인 {details.ForfeitedCoins:N0}");
            if (!details.HasItemizedDetails)
            {
                return new SettlementResultsDisplay(TitleFor(details.Reason),
                    Append(notice, "이전 영수증에 저장된 항목만 표시합니다."), receipt.RunId, details.Reason,
                    new[]
                    {
                        new SettlementSection("회수·획득 합계", $"{details.RecoveredInstanceIds.Count:N0}개"),
                        new SettlementSection("상실", $"{details.LostInstanceIds.Count:N0}개"),
                        new SettlementSection("대여 종료", $"{details.ClosedLoanIds.Count:N0}개")
                    },
                    new[]
                    {
                        coins,
                        new SettlementSection("도감", "상세 내역 없음"),
                        new SettlementSection("기존 자산 보존", "미반입 창고 무기", "기존 확정 코인")
                    });
            }

            var items = new[]
            {
                new SettlementSection("반입품 회수", WeaponLines(details.ReturnedWeapons, name)),
                new SettlementSection("신규 획득", WeaponLines(details.NewWeapons, name)),
                new SettlementSection("상실", WeaponLines(details.LostWeapons, name)),
                new SettlementSection(details.Reason == RunEndReason.Extracted ? "대여 반납" : "대여 종료",
                    WeaponLines(details.ClosedLoans, name))
            };
            var records = new[]
            {
                coins,
                new SettlementSection("도감 신규 회수", details.NewCatalogRecoveries
                    .Select(definition => name(new WeaponDefinitionId(definition))).ToArray()),
                new SettlementSection("기존 자산 보존",
                    $"미반입 창고 무기 {details.PreservedWarehouseCount:N0}개",
                    $"기존 확정 코인 {details.PriorConfirmedCoins:N0}")
            };
            return new SettlementResultsDisplay(TitleFor(details.Reason), notice, receipt.RunId, details.Reason, items, records);
        }

        private static string[] WeaponLines(IReadOnlyList<SettledWeapon> weapons, Func<WeaponDefinitionId, string> name)
            => weapons.GroupBy(item => item?.DefinitionId ?? default)
                .Select(group => $"{name(group.Key)} ×{group.Count():N0}").ToArray();

        private static string Append(string existing, string message)
            => existing.Length == 0 ? message : $"{existing}\n{message}";

        private static string TitleFor(RunEndReason? reason) => reason switch
        {
            RunEndReason.Extracted => "귀환 성공",
            RunEndReason.Death => "귀환 실패 · 사망",
            RunEndReason.TimeExpired => "귀환 실패 · 시간 초과",
            _ => "저장된 정산"
        };
    }
}

#nullable enable

using System;
using System.Runtime.CompilerServices;
using ProjectWeapon.Extraction;
using ProjectWeapon.Run;

[assembly: InternalsVisibleTo("ProjectWeapon.ExtractionUI.Tests")]

namespace ProjectWeapon.ExtractionUI
{
    internal enum ExtractionStatusKind
    {
        Hidden,
        Channeling,
        Cancelled,
        Succeeded
    }

    /// <summary>Reads only the outcomes accepted by the run and the progress published by its channel.</summary>
    internal readonly struct ExtractionStatusDisplay
    {
        private ExtractionStatusDisplay(ExtractionStatusKind kind, string title, float fraction, int percentage)
        {
            Kind = kind;
            Title = title;
            Fraction = fraction;
            Percentage = percentage;
        }

        public ExtractionStatusKind Kind { get; }
        public string Title { get; }
        public float Fraction { get; }
        public int Percentage { get; }

        public static ExtractionStatusDisplay Read(RunState run, ExtractionChannel channel)
        {
            if (run.Result is RunResult result)
                return result.Succeeded
                    ? new ExtractionStatusDisplay(ExtractionStatusKind.Succeeded, "귀환 완료", 1, 100)
                    : default;

            if (run.CurrentRun is not RunSession session)
                return default;

            if (run.ActiveExtraction is ExtractionAttempt attempt)
            {
                float fraction = channel.Progress is { } progress && progress.RunId == session.Id
                    && progress.AttemptId == attempt.Id ? progress.Fraction : 0;
                int percent = Math.Min(99, (int)(fraction * 100));
                return new ExtractionStatusDisplay(ExtractionStatusKind.Channeling, "귀환 중", fraction, percent);
            }

            if (run.LastExtractionCancellation is { } cancellation && cancellation.RunId == session.Id)
            {
                string title = cancellation.Reason switch
                {
                    ExtractionCancelReason.Movement => "이동으로 귀환이 취소되었습니다.",
                    ExtractionCancelReason.Damage => "피격으로 귀환이 취소되었습니다.",
                    _ => "귀환이 취소되었습니다."
                };
                return new ExtractionStatusDisplay(ExtractionStatusKind.Cancelled, title, 0, 0);
            }

            return default;
        }
    }
}

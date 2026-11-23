using System;

namespace ProjectWeapon.Tests.Framework
{
    public sealed class ManualTestClock
    {
        public TimeSpan Elapsed { get; private set; }

        public ManualTestClock(TimeSpan initialTime)
        {
            Elapsed = initialTime;
        }

        public void Advance(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(duration));

            Elapsed += duration;
        }
    }
}

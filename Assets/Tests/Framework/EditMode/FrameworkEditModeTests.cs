using System;
using NUnit.Framework;

namespace ProjectWeapon.Tests.Framework.EditMode
{
    public sealed class FrameworkEditModeTests
    {
        [TestCase(0)]
        [TestCase(125)]
        public void InjectedClockStartsAtFixedTimeAndAdvancesOnDemand(int milliseconds)
        {
            var initialTime = TimeSpan.FromSeconds(10);
            var clock = new ManualTestClock(initialTime);
            var duration = TimeSpan.FromMilliseconds(milliseconds);

            Assert.That(clock.Elapsed, Is.EqualTo(initialTime));
            clock.Advance(duration);

            Assert.That(clock.Elapsed, Is.EqualTo(initialTime + duration));
        }

        [Test]
        public void IntentionalFailureProbe()
        {
            FailureProbe.VerifyOptInGate();
        }
    }
}

using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectWeapon.Tests.Framework.PlayMode
{
    public sealed class FrameworkPlayModeTests
    {
        [UnityTest]
        public IEnumerator InjectedClockStaysDeterministicAcrossPlayModeFrames()
        {
            var initialTime = TimeSpan.FromSeconds(10);
            var clock = new ManualTestClock(initialTime);
            var startFrame = Time.frameCount;

            Assert.That(Application.isPlaying, Is.True);
            yield return null;

            Assert.That(Time.frameCount, Is.GreaterThan(startFrame));
            Assert.That(clock.Elapsed, Is.EqualTo(initialTime));

            clock.Advance(TimeSpan.FromMilliseconds(125));
            yield return null;

            Assert.That(clock.Elapsed, Is.EqualTo(initialTime + TimeSpan.FromMilliseconds(125)));
        }

        [UnityTest]
        public IEnumerator IntentionalFailureProbe()
        {
            yield return null;

            FailureProbe.VerifyOptInGate();
        }
    }
}

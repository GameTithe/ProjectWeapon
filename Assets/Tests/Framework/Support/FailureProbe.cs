using System;
using NUnit.Framework;

namespace ProjectWeapon.Tests.Framework
{
    public static class FailureProbe
    {
        public const string OptInVariable = "PROJECTWEAPON_RUN_FAILURE_PROBE";
        public const string FailureMessage = "KAN-34 intentional failure probe";

        public static void VerifyOptInGate()
        {
            Assert.That(Environment.GetEnvironmentVariable(OptInVariable), Is.Not.EqualTo("1"), FailureMessage);
        }
    }
}

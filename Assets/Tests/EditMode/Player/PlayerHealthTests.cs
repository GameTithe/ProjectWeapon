using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace ProjectWeapon.Player.Tests
{
    public sealed class PlayerHealthTests
    {
        [TestCase(0)]
        [TestCase(-1)]
        public void RejectsNonPositiveMaximum(int maxHealth)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerHealth(maxHealth));
        }

        [Test]
        public void StartsFullAndReportsEveryAcceptedHit()
        {
            var health = new PlayerHealth(100);
            var events = new List<PlayerDamage>();
            int deaths = 0;
            health.DamageApplied += events.Add;
            health.Died += _ => deaths++;

            Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
            Assert.That(health.ApplyDamage(27, TimeSpan.FromSeconds(1)), Is.True);
            Assert.That(health.ApplyDamage(26, TimeSpan.FromSeconds(1)), Is.True);

            Assert.That(health.MaxHealth, Is.EqualTo(100));
            Assert.That(health.CurrentHealth, Is.EqualTo(47));
            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events[0].PreviousHealth, Is.EqualTo(100));
            Assert.That(events[0].CurrentHealth, Is.EqualTo(73));
            Assert.That(events[0].Amount, Is.EqualTo(27));
            Assert.That(events[0].EffectiveAt, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(events[1].PreviousHealth, Is.EqualTo(73));
            Assert.That(events[1].Amount, Is.EqualTo(26));
            Assert.That(deaths, Is.Zero);
        }

        [TestCase(0, 0)]
        [TestCase(-1, 0)]
        [TestCase(1, -1)]
        public void InvalidInputsLeaveHealthAndEventsUnchanged(int amount, int seconds)
        {
            var health = new PlayerHealth(100);
            int events = 0;
            health.DamageApplied += _ => events++;
            health.Died += _ => events++;

            Assert.That(health.ApplyDamage(amount, TimeSpan.FromSeconds(seconds)), Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(100));
            Assert.That(events, Is.Zero);
        }

        [TestCase(10)]
        [TestCase(int.MaxValue)]
        public void ExactAndOverkillHitsClampAtZeroAndPublishDeathOnce(int amount)
        {
            var health = new PlayerHealth(10);
            var order = new List<string>();
            health.DamageApplied += damage =>
            {
                Assert.That(health.CurrentHealth, Is.Zero);
                Assert.That(damage.Amount, Is.EqualTo(10));
                order.Add("damage");
            };
            health.Died += death =>
            {
                Assert.That(death.IsFatal, Is.True);
                order.Add("death");
            };

            Assert.That(health.ApplyDamage(amount, TimeSpan.Zero), Is.True);
            Assert.That(health.ApplyDamage(1, TimeSpan.FromSeconds(1)), Is.False);
            Assert.That(health.CurrentHealth, Is.Zero);
            Assert.That(order, Is.EqualTo(new[] { "damage", "death" }));
        }
    }
}

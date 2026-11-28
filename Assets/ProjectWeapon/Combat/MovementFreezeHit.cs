#nullable enable

using System;

namespace ProjectWeapon.Combat
{
    /// <summary>One accepted contact carrying damage and a movement-only stop.</summary>
    public readonly struct MovementFreezeHit
    {
        public MovementFreezeHit(DamageRequest damage, TimeSpan effectiveAt, TimeSpan resumeAt)
        {
            if (resumeAt <= effectiveAt)
                throw new ArgumentOutOfRangeException(nameof(resumeAt));

            Damage = damage;
            EffectiveAt = effectiveAt;
            ResumeAt = resumeAt;
        }

        public DamageRequest Damage { get; }
        public TimeSpan EffectiveAt { get; }
        public TimeSpan ResumeAt { get; }
    }
}

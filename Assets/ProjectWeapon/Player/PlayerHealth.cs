#nullable enable

using System;

namespace ProjectWeapon.Player
{
    public readonly struct PlayerDamage
    {
        internal PlayerDamage(int previousHealth, int currentHealth, TimeSpan effectiveAt)
        {
            PreviousHealth = previousHealth;
            CurrentHealth = currentHealth;
            EffectiveAt = effectiveAt;
        }

        public int PreviousHealth { get; }
        public int CurrentHealth { get; }
        public int Amount => PreviousHealth - CurrentHealth;
        public bool IsFatal => PreviousHealth > 0 && CurrentHealth == 0;
        public TimeSpan EffectiveAt { get; }
    }

    /// <summary>Owns the player's health for one sortie.</summary>
    public sealed class PlayerHealth
    {
        public PlayerHealth(int maxHealth)
        {
            if (maxHealth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxHealth));

            MaxHealth = maxHealth;
            CurrentHealth = maxHealth;
        }

        public int MaxHealth { get; }
        public int CurrentHealth { get; private set; }

        public event Action<PlayerDamage>? DamageApplied;
        public event Action<PlayerDamage>? Died;

        public bool ApplyDamage(int amount, TimeSpan effectiveAt)
        {
            if (!TryAcceptDamage(amount, effectiveAt, out PlayerDamage damage))
                return false;

            PublishDamage(damage);
            return true;
        }

        internal bool TryAcceptDamage(int amount, TimeSpan effectiveAt, out PlayerDamage damage)
        {
            damage = default;
            if (amount <= 0 || CurrentHealth == 0 || effectiveAt < TimeSpan.Zero)
                return false;

            int previousHealth = CurrentHealth;
            CurrentHealth -= Math.Min(amount, CurrentHealth);
            damage = new PlayerDamage(previousHealth, CurrentHealth, effectiveAt);
            return true;
        }

        internal void PublishDamage(PlayerDamage damage)
        {
            DamageApplied?.Invoke(damage);
            if (damage.IsFatal)
                Died?.Invoke(damage);
        }
    }
}

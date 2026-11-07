using System;

/// <summary>
/// Two uses share one recharge timer. Each accepted use restarts that timer;
/// expiry restores the entire pool, never one charge at a time.
/// This model owns no Unity clock, so its caller chooses pause/slow-motion policy.
/// </summary>
public sealed class PlayerDodgeChargePool
{
    public const int Capacity = 2;
    public int Charges { get; private set; } = Capacity;
    public float CooldownRemaining { get; private set; }
    public bool IsAvailable => Charges > 0;

    public bool TryConsume(float cooldownDuration)
    {
        if (!IsAvailable) return false;
        if (float.IsNaN(cooldownDuration) || float.IsInfinity(cooldownDuration)) cooldownDuration = 1f;
        Charges--;
        CooldownRemaining = Math.Max(0.01f, cooldownDuration);
        return true;
    }

    /// <returns>True when the visible cooldown or charge count changed.</returns>
    public bool Tick(float deltaTime)
    {
        if (Charges == Capacity || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return false;
        CooldownRemaining = Math.Max(0f, CooldownRemaining - deltaTime);
        if (CooldownRemaining <= 0.000001f)
        {
            CooldownRemaining = 0f;
            Charges = Capacity;
        }
        return true;
    }
}

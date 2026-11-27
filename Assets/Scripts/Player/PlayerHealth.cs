using System;
using UnityEngine;

// 1. game lost when health reaches 0
// 2. brief invulnerability after hit stops damage stacking
public class PlayerHealth : MonoBehaviour
{
    [SerializeField] int maxHealth = 3;
    [SerializeField] float invulnerabilitySeconds = 1f;

    // (remaining health, source name)
    public event Action<int, string> Damaged;

    public int MaxHealth => maxHealth;
    public int Current { get; private set; }

    float invulnerableUntil;

    void Awake() => Current = maxHealth;

    public void TakeDamage(int amount, string source)
    {
        if (Current <= 0 || Time.time < invulnerableUntil) return;
        if (ShiftManager.Instance != null && !ShiftManager.Instance.IsRunning) return;

        Current = Mathf.Max(0, Current - amount);
        invulnerableUntil = Time.time + invulnerabilitySeconds;
        Damaged?.Invoke(Current, source);

        if (Current == 0 && ShiftManager.Instance != null)
            ShiftManager.Instance.EndShift(ShiftManager.ShiftState.Lost);
    }
}

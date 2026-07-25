using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossHealth : MonoBehaviour
{
    [SerializeField] private BossStatsManager stats;

    public int CurrentHealth { get; private set; }
    public int MaxHealth => stats != null ? stats.maxHealth : 1;
    public int CurrentPhase { get; private set; }
    public bool IsInvulnerable { get; private set; }
    public bool IsDead { get; private set; }
    public float HealthNormalized => MaxHealth > 0 ? (float)CurrentHealth / MaxHealth : 0f;

    public event Action<int, int> HealthChanged;
    public event Action<int> PhaseChangeStarted;
    public event Action Died;

    private Coroutine hurtFlashCoroutine;

    private void Awake()
    {
        stats ??= GetComponent<BossStatsManager>();
        CurrentHealth = Mathf.Max(1, MaxHealth);
        HealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }

    public bool TakeDamage(int amount)
    {
        return TakeDamage(amount, Vector3.zero);
    }

    public bool TakeDamage(int amount, Vector3 damageSource)
    {
        if (amount <= 0 || IsDead || IsInvulnerable)
        {
            return false;
        }

        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        HealthChanged?.Invoke(CurrentHealth, MaxHealth);
        PlayHurtFlash();

        if (CurrentHealth <= 0)
        {
            Die();
            return true;
        }

        TryBeginNextPhase();
        return true;
    }

    public void CompletePhaseChange()
    {
        if (IsDead)
        {
            return;
        }

        IsInvulnerable = false;
        TryBeginNextPhase();
    }

    public void SetInvulnerable(bool value)
    {
        if (!IsDead)
        {
            IsInvulnerable = value;
        }
    }

    [ContextMenu("测试：Boss 受到 10 点伤害")]
    private void DebugTakeDamage()
    {
        TakeDamage(10, transform.position);
    }

    private void TryBeginNextPhase()
    {
        int targetPhase = GetTargetPhase();
        if (targetPhase <= CurrentPhase)
        {
            return;
        }

        CurrentPhase++;
        IsInvulnerable = stats != null && stats.invulnerableDuringPhaseChange;
        PhaseChangeStarted?.Invoke(CurrentPhase);
    }

    private int GetTargetPhase()
    {
        if (stats == null)
        {
            return 0;
        }

        float rate = HealthNormalized;
        if (rate <= stats.phase3HealthRate)
        {
            return 3;
        }

        if (rate <= stats.phase2HealthRate)
        {
            return 2;
        }

        if (rate <= stats.phase1HealthRate)
        {
            return 1;
        }

        return 0;
    }

    private void Die()
    {
        IsDead = true;
        IsInvulnerable = true;
        Died?.Invoke();
    }

    private void PlayHurtFlash()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (hurtFlashCoroutine != null)
        {
            StopCoroutine(hurtFlashCoroutine);
        }

        hurtFlashCoroutine = StartCoroutine(HurtFlashRoutine());
    }

    private IEnumerator HurtFlashRoutine()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        Color[] originalColors = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            Material material = renderers[i].material;
            originalColors[i] = material.HasProperty("_BaseColor")
                ? material.GetColor("_BaseColor")
                : material.color;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }
            else
            {
                material.color = Color.white;
            }
        }

        yield return new WaitForSeconds(0.08f);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            Material material = renderers[i].material;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", originalColors[i]);
            }
            else
            {
                material.color = originalColors[i];
            }
        }

        hurtFlashCoroutine = null;
    }
}

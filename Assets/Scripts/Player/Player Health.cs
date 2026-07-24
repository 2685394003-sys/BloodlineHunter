using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    // 变量必须放在class大括号内部
    public PlayerHurtInvincible invincible;

    public void ChangeHealth(int amount)
    {
        if (invincible.CanTakeDamage())
        {
            StatsManager.Instance.currentHealth -= amount;
            invincible.EnterInvincibleState();

            if (StatsManager.Instance.currentHealth <= 0)
            {
                gameObject.SetActive(false);
            }
            
            if (StatsManager.Instance.currentHealth > StatsManager.Instance.maxHealth)
            {
            ReHealth();
            }
        }
    }

    public void ReHealth()
    {
        StatsManager.Instance.currentHealth = StatsManager.Instance.maxHealth;
        gameObject.SetActive(true);
    }
}
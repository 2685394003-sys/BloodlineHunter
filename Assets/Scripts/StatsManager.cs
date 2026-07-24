using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StatsManager : MonoBehaviour
{
    public static StatsManager Instance;

    [Header("玩家数值 - Combat Stats")]
    public int damage;
    public float weaponRange;
    public float knockbackForce;
    public float knockbackTime;
    public float stunTime;
    public float cooldown;

    [Header("玩家数值 - Movement Stats")]
    public int speed;

    [Header("玩家数值 - Health Stats")]
    public int maxHealth;
    public int currentHealth;

    [Header("玩家数值 - 无敌时长")]
    public float invincibleTime;
    public float flashSpeed = 0.1f;


    [Header("敌人数值 - Combat Stats")]
    public int enemydamage;
    public float enemyAttackRange;
    public float enemyweaponRange;
    public float enemyattaCooldown;
    public float enemyattaCooldownTimer;
    public float enemyknockbackForce;
    public float enemyknockbackTime;
    public float enemystunTime;

    [Header("敌人数值 - Movement Stats")]
    public int enemyspeed;
    public float enemyplayerDetectRange = 5;

    [Header("敌人数值 - Health Stats")]
    public int enemymaxHealth;

    [Header("敌人数值 - 子弹数值")]
    public int shootDamage;
    public float enemyshootcd;
    public float bulletMaxLife;
    public float bulletspeed;
    public float enemyShootWaitTime;
    public float enemyshootRange;

    [Header("敌人数值 - 受伤闪烁")]
    public float enemyflashDuration = 0.3f;
    public float enemyflashSpeed = 0.08f;

    [Header("世界公共数值")]
    public float slowDeceleration = 3f; // 减速加速度，越大停得越快
    public LayerMask playerLayer;
    public LayerMask enemyLayer;


    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyCombat : MonoBehaviour
{
    public Transform EnemyAttackPoint;
    public LayerMask playerLayer;

    public void Attack()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(EnemyAttackPoint.position, StatsManager.Instance.enemyweaponRange, StatsManager.Instance.playerLayer);
        
        if(hits.Length > 0)
        {
            PlayerHealth playerHp = hits[0].GetComponent<PlayerHealth>();
            if(playerHp != null)
            {
                playerHp.ChangeHealth(StatsManager.Instance.enemydamage);
                hits[0].GetComponent<PlayerMovement>().Knockback(transform, StatsManager.Instance.enemyknockbackForce,StatsManager.Instance.enemystunTime);
                
            }

        }
    }   
}
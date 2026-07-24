using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


public class PlayerAttact : MonoBehaviour
{
    public Animator anim;
    public Animator attackPointAnim;
    public Transform AttackPoint;

    private float timer;

    private void Update()
    {

        if(timer > 0)
        {
            timer -= Time.deltaTime;
        }
    }
    
    public void Attack()
    {
        if(timer <= 0)
        {
        anim.SetBool("isAttacting",true);
        attackPointAnim.SetBool("isAttacking",true);
        SFXManager.Instance.PlayAttackSFX();

        timer = StatsManager.Instance.cooldown;
        }
    }

    public void Attackfalse()
    {
        anim.SetBool("isAttacting",false);
        attackPointAnim.SetBool("isAttacking",false);
    }

    public void DealDamage()
    {
        if (AttackPoint == null || StatsManager.Instance == null)
        return;

        Collider2D[] enemies = Physics2D.OverlapCircleAll(AttackPoint.position, StatsManager.Instance.weaponRange, StatsManager.Instance.enemyLayer);

        if(enemies.Length > 0)
        {
            enemies[0].GetComponent<EnemyHealth>().ChangeEnemyHealth(StatsManager.Instance.damage);
            enemies[0].GetComponent<EnemyKnockBack>().EnemyKnockback(transform,StatsManager.Instance.knockbackForce,StatsManager.Instance.stunTime,StatsManager.Instance.knockbackTime);

        }

    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(AttackPoint.position, StatsManager.Instance.weaponRange);
    }    

    public class PlayerAttack : MonoBehaviour
    {
        public void OnAttack(InputValue value)
        {
            if (value.isPressed)
            {
                Debug.Log("鼠标左键攻击触发！");
                // 在这里写你的攻击逻辑
            }
        }
    }

}

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
        if (anim != null)
            anim.SetBool("isAttacting",true);
        if (attackPointAnim != null)
            attackPointAnim.SetBool("isAttacking",true);
        if (SFXManager.Instance != null)
            SFXManager.Instance.PlayAttackSFX();

        timer = StatsManager.Instance.cooldown;
        }
    }

    public void Attackfalse()
    {
        if (anim != null)
            anim.SetBool("isAttacting",false);
        if (attackPointAnim != null)
            attackPointAnim.SetBool("isAttacking",false);
    }

    public void DealDamage()
    {
        if (AttackPoint == null || StatsManager.Instance == null)
        return;

        Collider2D[] enemies = Physics2D.OverlapCircleAll(AttackPoint.position, StatsManager.Instance.weaponRange, StatsManager.Instance.enemyLayer);

        if(enemies.Length > 0)
        {
            EnemyHealth enemyHealth = enemies[0].GetComponent<EnemyHealth>();
            if (enemyHealth != null)
                enemyHealth.ChangeEnemyHealth(StatsManager.Instance.damage);

            EnemyKnockBack enemyKnockBack = enemies[0].GetComponent<EnemyKnockBack>();
            if (enemyKnockBack != null)
                enemyKnockBack.EnemyKnockback(transform,StatsManager.Instance.knockbackForce,StatsManager.Instance.stunTime,StatsManager.Instance.knockbackTime);

        }

    }

    private void OnDrawGizmosSelected()
    {
        if (AttackPoint == null || StatsManager.Instance == null)
            return;

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

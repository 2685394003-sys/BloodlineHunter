using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyKnockBack : MonoBehaviour
{
    private Rigidbody2D rb;
    private EnemyMovement Enemymovement;

    private void Start()
    {
        rb=GetComponent<Rigidbody2D>();
        Enemymovement = GetComponent<EnemyMovement>();
    }

    public void EnemyKnockback(Transform playerTransform, float knockbackForce,float Stuntime,float knockbackTime)
    {
        Enemymovement.ChangeState(EnemyState.Knockback);
        if(gameObject.activeSelf)
        {
            StartCoroutine(StunTime(Stuntime,knockbackTime));
        }
        Vector2 direction = (transform.position - playerTransform.position).normalized;
        rb.linearVelocity = direction * knockbackForce;
    }

    IEnumerator StunTime(float Stuntime,float knockbackTime)
    {
        yield return new WaitForSeconds(knockbackTime);
        rb.linearVelocity = Vector2.zero;
        yield return new WaitForSeconds(Stuntime);
        Enemymovement.ChangeState(EnemyState.Idle);
    }
}

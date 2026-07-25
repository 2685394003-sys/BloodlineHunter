using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyMovement : MonoBehaviour
{
    private EnemyState enemyState;
    private FlowFieldManager flowField;
    private Vector3 smoothDirection;
    private float shoottimer;
    public float dirBlendSpeed = 7f;
    public float turnSmooth = 6f;

    private Rigidbody rb;
    public Transform EnemyDetectionPonint;
    private Transform player;
    private Animator anim;
    private Coroutine slowCoroutine; // 记录减速协程，防止重复启动

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponent<Animator>();
        ChangeState(EnemyState.Idle);
        flowField = FindObjectOfType<FlowFieldManager>();
        smoothDirection = Vector3.forward;
    }

    // Update is called once per frame
    void Update()
    {
        if (enemyState != EnemyState.Knockback)
        {
            if(player != null && shoottimer <= 0 && Vector3.Distance(transform.position, player.position) > StatsManager.Instance.enemyshootRange)
            {
                Stop();
                ChangeState(EnemyState.isShooting);
                shoottimer = StatsManager.Instance.enemyshootcd;
                return;
            }

            CheckForPlayer();

            if(StatsManager.Instance.enemyattaCooldownTimer > 0)
            {
                StatsManager.Instance.enemyattaCooldownTimer -= Time.deltaTime;
            }

            if(shoottimer > 0)
            {
                shoottimer -= Time.deltaTime;
            }

            // 移除追击距离限制，只要处于追逐状态就持续执行流场寻路
            if (enemyState == EnemyState.isChasing)
            {
                Chase();
            }
            else if(enemyState == EnemyState.isAttacking)
            {
                rb.linearVelocity = Vector3.zero;
            }
        }
    }

    private void CheckForPlayer()
    {   
        Collider[] hits = Physics.OverlapSphere(EnemyDetectionPonint.position,StatsManager.Instance.enemyplayerDetectRange,StatsManager.Instance.playerLayer);
        
        if(hits.Length > 0)
        {
            player = hits[0].transform;
        
            if(Vector3.Distance(transform.position, player.position) <= StatsManager.Instance.enemyAttackRange && StatsManager.Instance.enemyattaCooldownTimer <= 0)
            {
                Stop();
                ChangeState(EnemyState.isAttacking);
                StatsManager.Instance.enemyattaCooldownTimer = StatsManager.Instance.enemyattaCooldown;
            }
            else if(enemyState != EnemyState.isAttacking && enemyState != EnemyState.isShooting)
            {
                ChangeState(EnemyState.isChasing);
                // 玩家重新进入，立刻停止减速协程，恢复追逐
                if (slowCoroutine != null)
                {
                    StopCoroutine(slowCoroutine);
                    slowCoroutine = null;
                }
            }
        }
        else
        {
            ChangeState(EnemyState.Idle);
            // 丢失玩家，开启线性减速协程
            Stop();
        }
    }

    // 线性匀减速，一次执行到停止就结束，不占用Update
    IEnumerator SlowDownToStop()
    {
        Vector3 currentVel = rb.linearVelocity;
        // 持续匀速减小速度，直到接近0
        while (currentVel.magnitude > 0.05f)
        {
            currentVel = Vector3.MoveTowards(currentVel, Vector3.zero, StatsManager.Instance.slowDeceleration * Time.deltaTime);
            rb.linearVelocity = currentVel;
            yield return null; // 等待下一帧再执行
        }
        // 速度几乎为0，直接清零彻底停止
        rb.linearVelocity = Vector3.zero;
        slowCoroutine = null;
    }

    public void ChangeState(EnemyState newState)
    {
        //退出当前动画
        if (enemyState == EnemyState.Idle)
            anim.SetBool("isIdle", false);
        else if (enemyState == EnemyState.isChasing)
            anim.SetBool("isChasing", false);
        else if (enemyState == EnemyState.isAttacking)
            anim.SetBool("isAttacking", false);
        else if (enemyState == EnemyState.isShooting)
            anim.SetBool("isShooting", false);

        //更新当前状态
        enemyState = newState;

        //更新新动画
        if (enemyState == EnemyState.Idle)
            anim.SetBool("isIdle", true);
        else if (enemyState == EnemyState.isChasing)
            anim.SetBool("isChasing", true);
        else if (enemyState == EnemyState.isAttacking)
            anim.SetBool("isAttacking", true);
        else if (enemyState == EnemyState.isShooting)
            anim.SetBool("isShooting", true);
    }

    void Chase()
    {
        // 流场为空自动查找
        if (flowField == null)
        {
            flowField = FindObjectOfType<FlowFieldManager>();
            return;
        }

        Vector3 rawDir = flowField.GetFlowDirection(transform.position);
        // 流场失效兜底：使用脚本检测到的player
        if(rawDir.magnitude < 0.01f && player != null)
            rawDir = (player.position - transform.position).normalized;

        // 方向平滑缓冲，消除跳变
        smoothDirection = Vector3.Lerp(smoothDirection, rawDir.normalized, Time.deltaTime * dirBlendSpeed);

        // 刚体物理移动（不再直接修改transform，解决刚体冲突无法移动）
        rb.linearVelocity = smoothDirection * StatsManager.Instance.enemyspeed;

        // 平滑水平旋转朝向前进方向
        if(smoothDirection.magnitude > 0.01f)
        {
            Vector3 flatDir = Vector3.ProjectOnPlane(smoothDirection, Vector3.up);
            Quaternion targetRot = Quaternion.LookRotation(flatDir);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRot, Time.deltaTime * turnSmooth);
        }
    }

    void Stop()
    {
        slowCoroutine = StartCoroutine(SlowDownToStop());
    }

    public Transform GetPlayerTarget()
    {
        return player;
    }
}

public enum EnemyState
{
    Idle,
    isChasing,
    isAttacking,
    Knockback,
    isShooting
}

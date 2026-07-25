using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public sealed class PlayerController : MonoBehaviour
{
    [Header("输入绑定")]
    public InputAction moveAction;
    public InputAction attackAction;

    public Animator anim;
    public PlayerAttact playerAttack;

    public bool isKnockedBack;
    public Vector3 knockbackVelocity;

    [Header("攻击配置")]
    [SerializeField] private float attackRange = 2.1f;
    [SerializeField] private float attackRadius = 1.15f;
    [SerializeField] private float attackCooldown = 0.35f;
    private Transform attackMarker;
    public Vector3 facingDirection = Vector3.forward;
    private float nextAttackTime;

    private Camera viewCamera;
    private Vector2 cachedMoveInput; // 缓存移动输入

    private void Awake()
    {
        viewCamera = Camera.main;
    }

    private void Update()
    {
        // Update统一读取移动输入
        cachedMoveInput = moveAction.ReadValue<Vector2>();
    }

    private void FixedUpdate()
    {
        // 击退状态：只执行击退位移，禁止玩家控制
        if (isKnockedBack)
        {
            transform.position += knockbackVelocity * Time.fixedDeltaTime;
            return;
        }

        Vector2 input = cachedMoveInput;

        // 基于相机的俯视移动方向转换
        Vector3 cameraForward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
        Vector3 cameraRight = Vector3.ProjectOnPlane(viewCamera.transform.right, Vector3.up).normalized;
        Vector3 rawMoveDir = (cameraForward * input.y + cameraRight * input.x);
        if (rawMoveDir.sqrMagnitude > 0.001f)
            rawMoveDir.Normalize();

        // 角色左右翻转
        float horizontalInput = input.x;
        if ((horizontalInput > 0 && transform.localScale.x < 0) || (horizontalInput < 0 && transform.localScale.x > 0))
        {
            Flip();
        }

        // 动画参数
        anim.SetFloat("horizontal", horizontalInput);
        anim.SetFloat("vertical", input.y);

        // 移动执行
        if (StatsManager.Instance != null)
        {
            float speed = StatsManager.Instance.speed;
            transform.position += rawMoveDir * speed * Time.fixedDeltaTime;
        }
    }

    void Flip()
    {
        facingDirection *= -1;
        transform.localScale = new Vector3(transform.localScale.x * -1, transform.localScale.y, transform.localScale.z);
    }

    public void Knockback(Transform enemy, float force, float stunTime)
    {
        if (!gameObject.activeSelf) return;
        isKnockedBack = true;
        Vector3 dir = (transform.position - enemy.position).normalized;
        knockbackVelocity = dir * force;
        StartCoroutine(KnockbackCounter(stunTime));
    }

    IEnumerator KnockbackCounter(float stunTime)
    {
        yield return new WaitForSeconds(stunTime);
        knockbackVelocity = Vector3.zero;
        isKnockedBack = false;
    }

    public void Attack(InputAction.CallbackContext ctx)
    {
        if (Time.time < nextAttackTime)
            return;

        nextAttackTime = Time.time + attackCooldown;
        playerAttack.Attack();
        Vector3 origin = transform.position + facingDirection * attackRange;
        Collider[] hits = Physics.OverlapSphere(origin, attackRadius);
        foreach (Collider hit in hits)
        {
            if (hit.TryGetComponent(out ChasingEnemy enemy))
            {
                enemy.ReceiveHit();
            }
        }
    }

    private void OnEnable()
    {
        moveAction.Enable();
        attackAction.Enable();
        attackAction.performed += Attack;
    }

    private void OnDisable()
    {
        moveAction.Disable();
        attackAction.Disable();
        attackAction.performed -= Attack;
    }

}

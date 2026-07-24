using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public sealed class PlayerController : MonoBehaviour
{
    public Animator anim;
    public PlayerAttact playerAttack;
    public InputAction attackAction;
    private bool isKnockedBack;
    private Vector3 knockbackVelocity;
    

    [Header("攻击配置")]
    [SerializeField] private float attackRange = 2.1f;
    [SerializeField] private float attackRadius = 1.15f;
    [SerializeField] private float attackCooldown = 0.35f;
    private Transform attackMarker;
    private Vector3 facingDirection = Vector3.forward;
    private float nextAttackTime;

    private Camera viewCamera;

    private void Awake()
    {
        viewCamera = Camera.main;
        CreateAttackMarker();
    }

    private void Update()
    {
        UpdateFacingFromMouse();
        UpdateAttackMarkerTransform();
    }

    private void FixedUpdate()
    {
        // 击退眩晕时，只执行击退推力，禁止玩家操控移动
        if (isKnockedBack)
        {
            transform.position += knockbackVelocity * Time.fixedDeltaTime;
            return;
        }

        // WASD输入
        if (Keyboard.current == null) return;
        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed) input.y -= 1f;
        if (Keyboard.current.dKey.isPressed) input.x += 1f;
        if (Keyboard.current.aKey.isPressed) input.x -= 1f;

        // 相机相对移动（俯视3D）
        Vector3 cameraForward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
        Vector3 cameraRight = Vector3.ProjectOnPlane(viewCamera.transform.right, Vector3.up).normalized;
        Vector3 rawMoveDir = (cameraForward * input.y + cameraRight * input.x);
        if (rawMoveDir.sqrMagnitude > 0.001f)
            rawMoveDir.Normalize();

        // 左右翻转（适配2D面片Billboard精灵）
        float horizontalInput = input.x;
        if ((horizontalInput > 0 && transform.localScale.x < 0) || (horizontalInput < 0 && transform.localScale.x > 0))
        {
            Flip();
        }

        // 动画参数赋值（和原2D脚本一致）
        anim.SetFloat("horizontal", horizontalInput);
        anim.SetFloat("vertical", input.y);

        // StatsManager全局速度
        float speed = StatsManager.Instance.speed;
        transform.position += rawMoveDir * speed * Time.fixedDeltaTime;
    }

    /// <summary>
    /// 原2D Flip翻转逻辑，适配3D面片
    /// </summary>
    void Flip()
    {
        facingDirection *= -1;
        transform.localScale = new Vector3(transform.localScale.x * -1, transform.localScale.y, transform.localScale.z);
    }

    /// <summary>
    /// 外部调用：被敌人击退（完全复刻原Knockback逻辑，3D适配）
    /// </summary>
    public void Knockback(Transform enemy, float force, float stunTime)
    {
        if (!gameObject.activeSelf) return;
        isKnockedBack = true;
        // 3D XZ平面击退方向
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

    #region 原有鼠标瞄准、攻击预览、攻击逻辑（完全保留未改动）
    private void UpdateFacingFromMouse()
    {
        if (viewCamera == null || Mouse.current == null)
            return;

        Ray ray = viewCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float rayDistance))
        {
            Vector3 hitPoint = ray.GetPoint(rayDistance);
            Vector3 direction = Vector3.ProjectOnPlane(hitPoint - transform.position, Vector3.up);
            if (direction.sqrMagnitude > 0.01f)
            {
                facingDirection = direction.normalized;
            }
        }
    }

    private void CreateAttackMarker()
    {
        GameObject marker = new GameObject("Attack Range Preview");
        marker.transform.SetParent(transform, false);
        SpriteRenderer renderer = marker.AddComponent<SpriteRenderer>();
        renderer.sprite = TopDownPrototype.GetDefaultSprite();
        renderer.color = new Color(1f, 0.8f, 0.1f, 0.38f);
        marker.transform.localScale = new Vector3(attackRadius * 2f, attackRadius * 2f, 1f);
        marker.AddComponent<PerspectiveSpriteSorting>().SetAnchor(transform);
        attackMarker = marker.transform;
    }

    private void UpdateAttackMarkerTransform()
    {
        if (attackMarker == null || viewCamera == null)
            return;
        attackMarker.localPosition = facingDirection * attackRange;
        attackMarker.rotation = viewCamera.transform.rotation;
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
        attackAction.performed += Attack;
        attackAction.Enable();
    }

#endregion 
}
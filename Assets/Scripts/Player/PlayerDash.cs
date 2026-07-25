using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

[RequireComponent(typeof(PlayerController))]
public class PlayerDash : MonoBehaviour
{
    [Header("冲刺设置")]
    public float dashDuration = 0.3f;
    public InputAction shiftAction;
    public InputAction moveAction; // 和PlayerController共用同一套移动Action

    // 冲刺状态标记
    private bool isDashing = false;
    private Vector2 dashDir;
    private Coroutine currentDashCoroutine;

    // 内置体力系统
    [HideInInspector] public float currentStamina;
    private bool pauseStaminaRecover = false;

    // 缓存引用
    private PlayerController playerCtrl;
    private Camera viewCamera;

    private void Awake()
    {
        playerCtrl = GetComponent<PlayerController>();
        viewCamera = Camera.main;

        if (StatsManager.Instance != null)
        {
            currentStamina = StatsManager.Instance.maxStamina;
        }
    }

    private void Update()
    {
        StaminaRecoverTick();
    }

    private void OnEnable()
    {
        shiftAction.Enable();
        moveAction.Enable();
        shiftAction.performed += OnShiftPressed;
    }

    private void OnDisable()
    {
        shiftAction.Disable();
        moveAction.Disable();
        shiftAction.performed -= OnShiftPressed;

        if (currentDashCoroutine != null)
        {
            StopCoroutine(currentDashCoroutine);
            currentDashCoroutine = null;
        }
        isDashing = false;
        pauseStaminaRecover = false;
    }

    /// <summary>
    /// 体力自动恢复
    /// </summary>
    private void StaminaRecoverTick()
    {
        if (StatsManager.Instance == null) return;
        if (currentStamina >= StatsManager.Instance.maxStamina || pauseStaminaRecover)
            return;

        currentStamina += StatsManager.Instance.staminaRecoverSpeed * Time.deltaTime;
        currentStamina = Mathf.Clamp(currentStamina, 0, StatsManager.Instance.maxStamina);
    }

    /// <summary>
    /// 尝试消耗体力
    /// </summary>
    public bool TryConsumeStamina(float value)
    {
        if (StatsManager.Instance == null) return false;
        if (currentStamina < value)
            return false;

        currentStamina -= value;
        currentStamina = Mathf.Clamp(currentStamina, 0, StatsManager.Instance.maxStamina);
        return true;
    }

    /// <summary>
    /// Shift按下触发冲刺
    /// </summary>
    public void OnShiftPressed(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed || isDashing || playerCtrl.isKnockedBack)
            return;

        // 体力不足拦截冲刺
        if (!TryConsumeStamina(StatsManager.Instance.dashStaminaCost))
            return;

        // 读取移动输入（统一使用InputAction，不再硬编码Keyboard）
        Vector2 input = moveAction.ReadValue<Vector2>();

        Vector3 cameraForward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
        Vector3 cameraRight = Vector3.ProjectOnPlane(viewCamera.transform.right, Vector3.up).normalized;
        Vector3 rawMoveDir3D = (cameraForward * input.y + cameraRight * input.x);
        if (rawMoveDir3D.sqrMagnitude > 0.001f)
            rawMoveDir3D.Normalize();

        // 原地无输入时，使用角色朝向冲刺
        if (rawMoveDir3D.sqrMagnitude < 0.01f)
        {
            rawMoveDir3D = playerCtrl.facingDirection;
        }
        dashDir = new Vector2(rawMoveDir3D.x, rawMoveDir3D.z);

        currentDashCoroutine = StartCoroutine(DashCoroutine());
    }

    /// <summary>
    /// 冲刺协程
    /// </summary>
    private IEnumerator DashCoroutine()
    {
        isDashing = true;
        pauseStaminaRecover = true;
        float timer = dashDuration;
        float dashSpeed = StatsManager.Instance.speed * 2f;

        while (timer > 0f)
        {
            if (playerCtrl.isKnockedBack)
                break;

            timer -= Time.deltaTime;
            transform.Translate(new Vector3(dashDir.x, 0, dashDir.y) * dashSpeed * Time.deltaTime, Space.World);
            yield return null;
        }

        isDashing = false;
        currentDashCoroutine = null;
        pauseStaminaRecover = false;
    }
}

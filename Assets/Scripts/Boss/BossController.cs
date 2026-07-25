using System;
using System.Collections;
using UnityEngine;

public enum BossState
{
    Dormant,
    OffscreenIdle,
    Chase,
    Attack,
    PhaseChange,
    Dead
}

[RequireComponent(typeof(BossConfig))]
[RequireComponent(typeof(BossHealth))]
[RequireComponent(typeof(BossAttackController))]
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public sealed class BossController : MonoBehaviour
{
    [SerializeField] private BossConfig stats;
    [SerializeField] private BossHealth bossHealth;
    [SerializeField] private BossAttackController attackController;
    [SerializeField] private Rigidbody bossRigidbody;
    [SerializeField] private Collider bossCollider;
    [SerializeField] private Animator animator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Transform player;
    [SerializeField] private Transform visualRoot;

    public BossState CurrentState { get; private set; } = BossState.Dormant;
    public float RemainingContractSeconds { get; private set; }
    public bool ContractCountdownActive { get; private set; }
    public bool CombatEnabled => combatEnabled;
    public Transform Player => player;
    public BossHealth Health => bossHealth;
    public BossAttackController Attacks => attackController;

    public event Action<float> ContractCountdownChanged;
    public event Action ContractCountdownExpired;

    private Camera viewCamera;
    private Coroutine phaseChangeCoroutine;
    private bool combatEnabled;
    private bool contractCountdownTriggered;
    private bool runtimeSetupValidated;
    private Renderer[] visualRenderers = Array.Empty<Renderer>();

    private void Awake()
    {
        stats ??= GetComponent<BossConfig>();
        bossHealth ??= GetComponent<BossHealth>();
        attackController ??= GetComponent<BossAttackController>();
        bossRigidbody ??= GetComponent<Rigidbody>();
        bossCollider ??= GetComponent<Collider>();
        animator ??= GetComponentInChildren<Animator>(true);
        audioSource ??= GetComponent<AudioSource>();
        visualRoot ??= transform.Find("Visual");

        if (bossCollider != null)
        {
            // 策划要求 Boss 半飘浮且不与场景发生实体阻挡。
            bossCollider.isTrigger = true;
        }

        EnsureDebugVisual();
        visualRenderers = GetComponentsInChildren<Renderer>(true);
        viewCamera = Camera.main;
    }

    private void OnEnable()
    {
        if (bossHealth == null)
        {
            return;
        }

        bossHealth.PhaseChangeStarted += HandlePhaseChangeStarted;
        bossHealth.Died += HandleDeath;
        bossHealth.HealthChanged += HandleHealthChanged;
    }

    private IEnumerator Start()
    {
        ResolvePlayer();
        attackController.SetPlayer(player);
        ValidateRuntimeSetup();

        if (stats.randomSpawnOnStart)
        {
            TeleportToRandomArenaPosition();
        }

        CurrentState = BossState.Dormant;
        if (stats.initialActionDelay > 0f)
        {
            yield return new WaitForSeconds(stats.initialActionDelay);
        }

        combatEnabled = true;
    }

    private void Update()
    {
        ResolvePlayer();

        if (player != null)
        {
            attackController.SetPlayer(player);
        }

        UpdateFormat5Countdown();
    }

    private void FixedUpdate()
    {
        if (!combatEnabled ||
            player == null ||
            bossHealth == null ||
            bossHealth.IsDead ||
            CurrentState == BossState.PhaseChange)
        {
            StopMovement();
            return;
        }

        Vector3 toPlayer = Vector3.ProjectOnPlane(player.position - transform.position, Vector3.up);
        float distance = toPlayer.magnitude;
        FacePlayer(toPlayer);

        if (attackController.IsBusy)
        {
            CurrentState = BossState.Attack;
            StopMovement();
            return;
        }

        bool bossVisible = IsBossVisibleToCamera();
        if (attackController.TryStartAttack(
                bossHealth.CurrentPhase,
                bossVisible,
                distance))
        {
            CurrentState = BossState.Attack;
            StopMovement();
            return;
        }

        if (!bossVisible)
        {
            CurrentState = BossState.OffscreenIdle;
            StopMovement();
            return;
        }

        if (distance <= stats.stoppingDistance || toPlayer.sqrMagnitude < 0.001f)
        {
            CurrentState = BossState.Chase;
            StopMovement();
            return;
        }

        CurrentState = BossState.Chase;
        Vector3 direction = toPlayer.normalized;
        Vector3 nextPosition = bossRigidbody.position +
                               direction * (stats.GetMoveSpeed(bossHealth.CurrentPhase) * Time.fixedDeltaTime);
        bossRigidbody.MovePosition(nextPosition);
    }

    private void HandlePhaseChangeStarted(int newPhase)
    {
        if (bossHealth.IsDead)
        {
            return;
        }

        if (stats.logCombatEvents)
        {
            Debug.Log($"[Boss] 进入阶段 {newPhase}，当前生命 {bossHealth.CurrentHealth}/{bossHealth.MaxHealth}。", this);
        }

        if (phaseChangeCoroutine != null)
        {
            StopCoroutine(phaseChangeCoroutine);
        }

        phaseChangeCoroutine = StartCoroutine(PhaseChangeRoutine(newPhase));
    }

    private IEnumerator PhaseChangeRoutine(int newPhase)
    {
        CurrentState = BossState.PhaseChange;
        attackController.CancelCurrentAttack();
        StopMovement();

        if (audioSource != null && stats.phaseChangeClip != null)
        {
            audioSource.PlayOneShot(stats.phaseChangeClip);
        }

        SetAnimatorTrigger(stats.phaseChangeTrigger);

        if (stats.clearObstaclesOnPhaseChange)
        {
            ClearNearbyObstacles();
        }

        float elapsed = 0f;
        float nextBlinkTime = 0f;
        bool renderersEnabled = true;

        while (elapsed < stats.phaseChangeDuration)
        {
            elapsed += Time.deltaTime;
            if (elapsed >= nextBlinkTime)
            {
                renderersEnabled = !renderersEnabled;
                SetRenderersEnabled(renderersEnabled);
                nextBlinkTime = elapsed + stats.phaseBlinkInterval;
            }

            yield return null;
        }

        SetRenderersEnabled(true);

        if (stats.teleportAfterPhaseChange)
        {
            TeleportToRandomArenaPosition();
        }

        SetAnimatorInteger(stats.phaseParameter, newPhase);

        // 先清空引用，再完成阶段；这样单次超高伤害跨过多个阈值时，
        // BossHealth 可以立即排队进入下一个转阶段。
        phaseChangeCoroutine = null;
        bossHealth.CompletePhaseChange();
        if (phaseChangeCoroutine == null && !bossHealth.IsDead)
        {
            CurrentState = BossState.Chase;
        }
    }

    private void UpdateFormat5Countdown()
    {
        if (bossHealth == null || bossHealth.IsDead || stats == null)
        {
            return;
        }

        if (!contractCountdownTriggered &&
            stats.enableFormat5Countdown &&
            bossHealth.HealthNormalized <= stats.format5TriggerHealthRate)
        {
            contractCountdownTriggered = true;
            ContractCountdownActive = true;
            RemainingContractSeconds = stats.format5CountdownSeconds;
            SetAnimatorTrigger(stats.format5Trigger);
            ContractCountdownChanged?.Invoke(RemainingContractSeconds);

            if (stats.logCombatEvents)
            {
                Debug.Log($"[Boss] 契约倒计时启动：{RemainingContractSeconds:0.0} 秒。", this);
            }
        }

        if (!ContractCountdownActive)
        {
            return;
        }

        RemainingContractSeconds = Mathf.Max(
            0f,
            RemainingContractSeconds - Time.deltaTime * stats.format5CountdownRate);
        ContractCountdownChanged?.Invoke(RemainingContractSeconds);

        if (RemainingContractSeconds > 0f)
        {
            return;
        }

        ContractCountdownActive = false;
        ForceKillPlayer();
        ContractCountdownExpired?.Invoke();
    }

    private void HandleDeath()
    {
        combatEnabled = false;
        CurrentState = BossState.Dead;

        if (stats.logCombatEvents)
        {
            Debug.Log("[Boss] 已死亡，停止移动与攻击。", this);
        }

        if (phaseChangeCoroutine != null)
        {
            StopCoroutine(phaseChangeCoroutine);
            phaseChangeCoroutine = null;
        }

        attackController.CancelCurrentAttack();
        StopMovement();
        ContractCountdownActive = false;

        if (bossCollider != null)
        {
            bossCollider.enabled = false;
        }

        SetRenderersEnabled(true);
        SetAnimatorTrigger(stats.deathTrigger);

        if (audioSource != null && stats.deathClip != null)
        {
            audioSource.PlayOneShot(stats.deathClip);
        }

        StartCoroutine(DisableAfterDeathRoutine());
    }

    private IEnumerator DisableAfterDeathRoutine()
    {
        if (stats.deathDisableDelay > 0f)
        {
            yield return new WaitForSeconds(stats.deathDisableDelay);
        }

        gameObject.SetActive(false);
    }

    private void HandleHealthChanged(int currentHealth, int maxHealth)
    {
        if (stats != null && stats.logCombatEvents)
        {
            Debug.Log($"[Boss] 生命变化：{currentHealth}/{maxHealth}。", this);
        }
    }

    private void ResolvePlayer()
    {
        if (player != null && player.gameObject.activeInHierarchy)
        {
            BossCombatTarget.EnsurePlayerAdapter(player, false);
            return;
        }

        GameObject taggedPlayer = null;
        try
        {
            taggedPlayer = GameObject.FindGameObjectWithTag("Player");
        }
        catch (UnityException)
        {
            // 项目没有 Player Tag 时继续按 Layer 搜索。
        }

        if (taggedPlayer == null && stats != null)
        {
            Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Exclude);
            foreach (Collider candidate in colliders)
            {
                if (candidate != null &&
                    (stats.playerLayer.value & (1 << candidate.gameObject.layer)) != 0)
                {
                    taggedPlayer = candidate.transform.root.gameObject;
                    break;
                }
            }
        }

        player = taggedPlayer != null ? taggedPlayer.transform : null;
        if (player != null)
        {
            BossCombatTarget.EnsurePlayerAdapter(player, true);
        }
    }

    private bool IsBossVisibleToCamera()
    {
        viewCamera ??= Camera.main;
        if (viewCamera == null)
        {
            return true;
        }

        Vector3 viewport = viewCamera.WorldToViewportPoint(transform.position);
        float padding = stats.viewportPadding;
        return viewport.z > 0f &&
               viewport.x >= -padding &&
               viewport.x <= 1f + padding &&
               viewport.y >= -padding &&
               viewport.y <= 1f + padding;
    }

    private void FacePlayer(Vector3 toPlayer)
    {
        if (toPlayer.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            stats.turnSpeed * Time.fixedDeltaTime);
    }

    private void TeleportToRandomArenaPosition()
    {
        Vector3 fallback = transform.position;
        for (int i = 0; i < stats.spawnPositionAttempts; i++)
        {
            Vector3 candidate = new(
                stats.arenaCenter.x + UnityEngine.Random.Range(-stats.arenaHalfSize.x, stats.arenaHalfSize.x),
                fallback.y,
                stats.arenaCenter.z + UnityEngine.Random.Range(-stats.arenaHalfSize.y, stats.arenaHalfSize.y));

            if (player != null)
            {
                Vector3 planarOffset = Vector3.ProjectOnPlane(candidate - player.position, Vector3.up);
                if (planarOffset.magnitude < stats.spawnMinDistanceFromPlayer)
                {
                    continue;
                }
            }

            if (stats.obstacleLayer.value != 0 &&
                Physics.CheckSphere(
                    candidate,
                    stats.spawnObstacleCheckRadius,
                    stats.obstacleLayer,
                    QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            bossRigidbody.position = candidate;
            transform.position = candidate;
            return;
        }

        bossRigidbody.position = fallback;
    }

    private void ClearNearbyObstacles()
    {
        if (stats.obstacleLayer.value == 0)
        {
            return;
        }

        Collider[] obstacles = Physics.OverlapSphere(
            transform.position,
            stats.phaseClearObstacleRadius,
            stats.obstacleLayer,
            QueryTriggerInteraction.Ignore);

        foreach (Collider obstacle in obstacles)
        {
            if (obstacle != null &&
                obstacle.transform != transform &&
                !obstacle.transform.IsChildOf(transform))
            {
                obstacle.gameObject.SetActive(false);
            }
        }
    }

    private void ForceKillPlayer()
    {
        if (player == null)
        {
            return;
        }

        if (BossCombatTarget.TryGetInParent(player, out IForceKillable killable))
        {
            killable.ForceKill();
        }
        else
        {
            player.gameObject.SetActive(false);
        }
    }

    private void StopMovement()
    {
        if (bossRigidbody != null)
        {
            bossRigidbody.linearVelocity = Vector3.zero;
            bossRigidbody.angularVelocity = Vector3.zero;
        }
    }

    private void EnsureDebugVisual()
    {
        Renderer existingVisual = visualRoot != null
            ? visualRoot.GetComponentInChildren<Renderer>(true)
            : GetComponentInChildren<Renderer>(true);
        if (!stats.createDebugVisualIfMissing || existingVisual != null)
        {
            return;
        }

        Transform parent = visualRoot != null ? visualRoot : transform;
        GameObject debugVisual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        debugVisual.name = "Boss_DebugVisual_Runtime";
        debugVisual.layer = gameObject.layer;
        debugVisual.transform.SetParent(parent, false);
        debugVisual.transform.localPosition = new Vector3(0f, 1f, 0f);
        debugVisual.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);

        Collider generatedCollider = debugVisual.GetComponent<Collider>();
        if (generatedCollider != null)
        {
            Destroy(generatedCollider);
        }

        Renderer renderer = debugVisual.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = stats.debugBossColor;
        }
    }

    private void ValidateRuntimeSetup()
    {
        if (runtimeSetupValidated)
        {
            return;
        }

        runtimeSetupValidated = true;
        if (player == null)
        {
            Debug.LogError(
                "[Boss 配置] 找不到玩家。请给玩家设置 Player Tag 或放在 BossConfig.playerLayer 指定的层。",
                this);
        }
        else
        {
            BossCombatTarget.EnsurePlayerAdapter(player, true);
        }

        if (attackController != null)
        {
            string mountIssue = attackController.GetMountConfigurationIssue();
            if (!string.IsNullOrEmpty(mountIssue))
            {
                Debug.LogError($"[Boss 配置] {mountIssue}", this);
            }
        }

        if (animator == null)
        {
            Debug.LogWarning("[Boss 配置] 未找到 Animator。战斗逻辑可运行，但不会播放动画。", this);
        }
        else if (animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning(
                "[Boss 配置] Animator 没有绑定 Controller。战斗逻辑可运行，动画触发器暂不生效。",
                animator);
        }
        else
        {
            ValidateAnimatorParameter(stats.phaseParameter, AnimatorControllerParameterType.Int);
            ValidateAnimatorParameter(stats.phaseChangeTrigger, AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(stats.deathTrigger, AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(stats.format1Trigger, AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(stats.format2Trigger, AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(stats.format3Trigger, AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(stats.format4Trigger, AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(stats.format5Trigger, AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(stats.format6Trigger, AnimatorControllerParameterType.Trigger);
        }

        if (stats.logCombatEvents)
        {
            Debug.Log(
                $"[Boss] 初始化完成。玩家={(player != null ? player.name : "未找到")}，" +
                $"Animator={(animator != null && animator.runtimeAnimatorController != null ? "已配置" : "未配置")}。",
                this);
        }
    }

    private void ValidateAnimatorParameter(
        string parameterName,
        AnimatorControllerParameterType expectedType)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == parameterName)
            {
                if (parameter.type != expectedType)
                {
                    Debug.LogError(
                        $"[Boss Animator] 参数 {parameterName} 类型错误：需要 {expectedType}，当前是 {parameter.type}。",
                        animator);
                }

                return;
            }
        }

        Debug.LogError(
            $"[Boss Animator] 缺少参数 {parameterName}（{expectedType}）。请修改 Animator Controller 或 BossConfig 中的参数名。",
            animator);
    }

    [ContextMenu("Boss/自动配置五个子节点")]
    private void ConfigureFiveChildNodes()
    {
        stats ??= GetComponent<BossConfig>();
        bossHealth ??= GetComponent<BossHealth>();
        attackController ??= GetComponent<BossAttackController>();
        bossRigidbody ??= GetComponent<Rigidbody>();
        bossCollider ??= GetComponent<Collider>();
        audioSource ??= GetComponent<AudioSource>();

        visualRoot = FindOrCreateChild("Visual");
        Transform meleePoint = FindOrCreateChild("MeleePoint");
        Transform projectileOrigin = FindOrCreateChild("ProjectileOrigin");
        Transform groundIndicator = FindOrCreateChild("GroundIndicator");
        Transform vfxRoot = FindOrCreateChild("VFXRoot");

        meleePoint.localPosition = new Vector3(0f, 0f, 2f);
        projectileOrigin.localPosition = new Vector3(0f, 0.8f, 1.2f);
        groundIndicator.localPosition = Vector3.zero;
        vfxRoot.localPosition = new Vector3(0f, 0.5f, 0f);

        animator ??= visualRoot.GetComponentInChildren<Animator>(true);
        animator ??= GetComponent<Animator>();
        attackController?.ConfigureMounts(
            meleePoint,
            projectileOrigin,
            groundIndicator,
            vfxRoot,
            animator);

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.EditorUtility.SetDirty(attackController);
        }
#endif

        Debug.Log(
            "[Boss 配置] 五个子节点已连线：Visual=表现，MeleePoint=近战中心，" +
            "ProjectileOrigin=弹丸起点，GroundIndicator=预警父节点，VFXRoot=特效父节点。",
            this);
    }

    private Transform FindOrCreateChild(string childName)
    {
        Transform child = transform.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new(childName);
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.Undo.RegisterCreatedObjectUndo(childObject, $"创建 {childName}");
        }
#endif
        childObject.layer = gameObject.layer;
        child = childObject.transform;
        child.SetParent(transform, false);
        return child;
    }

    private void OnGUI()
    {
        if (stats == null || !stats.showDebugPanel)
        {
            return;
        }

        float x = stats.debugPanelPosition.x;
        float y = stats.debugPanelPosition.y;
        const float width = 360f;
        GUI.Box(new Rect(x, y, width, 218f), "Boss 运行时调试");

        string animatorState = animator == null
            ? "无 Animator"
            : animator.runtimeAnimatorController == null ? "未绑定 Controller" : "已配置";
        string healthText = bossHealth == null
            ? "生命组件缺失"
            : $"{bossHealth.CurrentHealth}/{bossHealth.MaxHealth}  阶段 {bossHealth.CurrentPhase}";

        GUI.Label(new Rect(x + 12f, y + 26f, width - 24f, 22f), $"状态：{CurrentState}  战斗：{combatEnabled}");
        GUI.Label(new Rect(x + 12f, y + 48f, width - 24f, 22f), $"生命：{healthText}");
        GUI.Label(
            new Rect(x + 12f, y + 70f, width - 24f, 22f),
            $"玩家：{(player != null ? player.name : "未找到")}  动画：{animatorState}");
        GUI.Label(
            new Rect(x + 12f, y + 92f, width - 24f, 22f),
            $"最近攻击：{(attackController != null ? attackController.LastAttackName : "无")}");

        bool previousEnabled = GUI.enabled;
        GUI.enabled = Application.isPlaying && bossHealth != null && !bossHealth.IsDead;
        if (GUI.Button(new Rect(x + 12f, y + 120f, 104f, 28f), $"Boss -{stats.debugDamageAmount} HP"))
        {
            bossHealth.DebugApplyDamage(stats.debugDamageAmount);
        }

        if (GUI.Button(new Rect(x + 124f, y + 120f, 104f, 28f), "直接击杀 Boss"))
        {
            bossHealth.DebugApplyDamage(Mathf.Max(1, bossHealth.CurrentHealth));
        }

        GUI.enabled = Application.isPlaying && attackController != null && player != null &&
                      bossHealth != null && !bossHealth.IsDead;
        BossAttackType[] debugAttacks =
        {
            BossAttackType.Format1,
            BossAttackType.Format2,
            BossAttackType.Format3,
            BossAttackType.Format4,
            BossAttackType.Format6
        };
        for (int i = 0; i < debugAttacks.Length; i++)
        {
            float buttonWidth = 64f;
            float buttonX = x + 12f + i * (buttonWidth + 4f);
            if (GUI.Button(
                    new Rect(buttonX, y + 158f, buttonWidth, 28f),
                    $"攻击 {((int)debugAttacks[i])}"))
            {
                attackController.DebugStartAttack(debugAttacks[i]);
            }
        }

        GUI.enabled = previousEnabled;
        GUI.Label(
            new Rect(x + 12f, y + 190f, width - 24f, 22f),
            "提示：Animator 未配置不会阻止移动、伤害、阶段和弹幕测试。");
    }

    private void SetRenderersEnabled(bool value)
    {
        foreach (Renderer targetRenderer in visualRenderers)
        {
            if (targetRenderer != null)
            {
                targetRenderer.enabled = value;
            }
        }
    }

    private void SetAnimatorTrigger(string triggerName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(triggerName))
        {
            return;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger &&
                parameter.name == triggerName)
            {
                animator.SetTrigger(triggerName);
                return;
            }
        }
    }

    private void SetAnimatorInteger(string parameterName, int value)
    {
        if (animator == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Int &&
                parameter.name == parameterName)
            {
                animator.SetInteger(parameterName, value);
                return;
            }
        }
    }

    private void OnDisable()
    {
        if (bossHealth != null)
        {
            bossHealth.PhaseChangeStarted -= HandlePhaseChangeStarted;
            bossHealth.Died -= HandleDeath;
            bossHealth.HealthChanged -= HandleHealthChanged;
        }
    }

    private void OnDrawGizmosSelected()
    {
        BossConfig config = stats != null ? stats : GetComponent<BossConfig>();
        if (config == null || !config.drawCombatGizmos)
        {
            return;
        }

        Gizmos.color = new Color(0.7f, 0f, 0f, 0.25f);
        Gizmos.DrawWireCube(
            config.arenaCenter,
            new Vector3(config.arenaHalfSize.x * 2f, 0.1f, config.arenaHalfSize.y * 2f));

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, config.stoppingDistance);

        BossAttackController attacks = attackController != null
            ? attackController
            : GetComponent<BossAttackController>();
        if (attacks == null)
        {
            return;
        }

        if (attacks.MeleePoint != null)
        {
            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(attacks.MeleePoint.position, config.format1Radius);
        }

        if (attacks.ProjectileOrigin != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(attacks.ProjectileOrigin.position, 0.12f);
            Gizmos.DrawRay(attacks.ProjectileOrigin.position, transform.forward * 2f);
        }

        if (attacks.GroundIndicator != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(attacks.GroundIndicator.position, 0.18f);
        }

        if (attacks.VFXRoot != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(attacks.VFXRoot.position, Vector3.one * 0.3f);
        }
    }
}

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

[RequireComponent(typeof(BossStatsManager))]
[RequireComponent(typeof(BossHealth))]
[RequireComponent(typeof(BossAttackController))]
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public sealed class BossController : MonoBehaviour
{
    [SerializeField] private BossStatsManager stats;
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

    public event Action<float> ContractCountdownChanged;
    public event Action ContractCountdownExpired;

    private Camera viewCamera;
    private Coroutine phaseChangeCoroutine;
    private bool combatEnabled;
    private bool contractCountdownTriggered;
    private Renderer[] visualRenderers = Array.Empty<Renderer>();

    private void Awake()
    {
        stats ??= GetComponent<BossStatsManager>();
        bossHealth ??= GetComponent<BossHealth>();
        attackController ??= GetComponent<BossAttackController>();
        bossRigidbody ??= GetComponent<Rigidbody>();
        bossCollider ??= GetComponent<Collider>();
        animator ??= GetComponent<Animator>();
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
    }

    private IEnumerator Start()
    {
        ResolvePlayer();
        attackController.SetPlayer(player);

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

        SetAnimatorTrigger("PhaseChange");

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

        SetAnimatorInteger("Phase", newPhase);

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
            SetAnimatorTrigger("Format5");
            ContractCountdownChanged?.Invoke(RemainingContractSeconds);
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
        SetAnimatorTrigger("Death");

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

    private void ResolvePlayer()
    {
        if (player != null && player.gameObject.activeInHierarchy)
        {
            return;
        }

        PlayerController playerController = FindAnyObjectByType<PlayerController>();
        player = playerController != null ? playerController.transform : null;
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

        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.ForceDeath();
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
        if (!stats.createDebugVisualIfMissing ||
            GetComponentInChildren<Renderer>(true) != null)
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
        if (animator == null)
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
        if (animator == null)
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
        }
    }

    private void OnDrawGizmosSelected()
    {
        BossStatsManager manager = stats != null ? stats : GetComponent<BossStatsManager>();
        if (manager == null)
        {
            return;
        }

        Gizmos.color = new Color(0.7f, 0f, 0f, 0.25f);
        Gizmos.DrawWireCube(
            manager.arenaCenter,
            new Vector3(manager.arenaHalfSize.x * 2f, 0.1f, manager.arenaHalfSize.y * 2f));

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, manager.stoppingDistance);
    }
}

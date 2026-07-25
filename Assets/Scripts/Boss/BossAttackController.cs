using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum BossAttackType
{
    Format1 = 1,
    Format2 = 2,
    Format3 = 3,
    Format4 = 4,
    Format6 = 6
}

[DisallowMultipleComponent]
public sealed class BossAttackController : MonoBehaviour
{
    [SerializeField] private BossConfig stats;
    [SerializeField] private Transform player;
    [SerializeField] private Transform meleePoint;
    [SerializeField] private Transform projectileOrigin;
    [SerializeField] private Transform groundIndicator;
    [SerializeField] private Transform vfxRoot;
    [SerializeField] private Rigidbody bossRigidbody;
    [SerializeField] private Animator animator;
    [SerializeField] private AudioSource audioSource;

    public bool IsBusy { get; private set; }
    public BossAttackType? LastAttack { get; private set; }
    public string LastAttackName => LastAttack.HasValue ? $"Format{(int)LastAttack.Value}" : "无";
    public int AttacksStarted { get; private set; }
    public Transform MeleePoint => meleePoint;
    public Transform ProjectileOrigin => projectileOrigin;
    public Transform GroundIndicator => groundIndicator;
    public Transform VFXRoot => vfxRoot;

    private readonly Dictionary<BossAttackType, float> readyTimes = new();
    private readonly List<GameObject> activeTelegraphs = new();
    private Coroutine attackCoroutine;
    private float nextDecisionTime;

    private void Awake()
    {
        stats ??= GetComponent<BossConfig>();
        bossRigidbody ??= GetComponent<Rigidbody>();
        animator ??= GetComponentInChildren<Animator>(true);
        audioSource ??= GetComponent<AudioSource>();
        meleePoint ??= transform.Find("MeleePoint");
        projectileOrigin ??= transform.Find("ProjectileOrigin");
        groundIndicator ??= transform.Find("GroundIndicator");
        vfxRoot ??= transform.Find("VFXRoot");
    }

    public void SetPlayer(Transform newPlayer)
    {
        player = newPlayer;
    }

    public void ConfigureMounts(
        Transform newMeleePoint,
        Transform newProjectileOrigin,
        Transform newGroundIndicator,
        Transform newVfxRoot,
        Animator newAnimator)
    {
        meleePoint = newMeleePoint;
        projectileOrigin = newProjectileOrigin;
        groundIndicator = newGroundIndicator;
        vfxRoot = newVfxRoot;
        if (newAnimator != null)
        {
            animator = newAnimator;
        }
    }

    public string GetMountConfigurationIssue()
    {
        List<string> missing = new();
        if (meleePoint == null)
        {
            missing.Add("MeleePoint");
        }

        if (projectileOrigin == null)
        {
            missing.Add("ProjectileOrigin");
        }

        if (groundIndicator == null)
        {
            missing.Add("GroundIndicator");
        }

        if (vfxRoot == null)
        {
            missing.Add("VFXRoot");
        }

        return missing.Count == 0
            ? string.Empty
            : $"以下 Boss 子节点未连接：{string.Join("、", missing)}。在 BossController 组件菜单执行“自动配置五个子节点”。";
    }

    public bool TryStartAttack(int phase, bool bossVisible, float playerDistance)
    {
        if (IsBusy || player == null || stats == null || Time.time < nextDecisionTime)
        {
            return false;
        }

        if (!TryChooseAttack(phase, bossVisible, playerDistance, out BossAttackType attackType))
        {
            return false;
        }

        attackCoroutine = StartCoroutine(AttackWrapper(attackType, !bossVisible));
        return true;
    }

    public bool DebugStartAttack(BossAttackType attackType)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Boss 调试] 请先进入 Play 模式再测试攻击。", this);
            return false;
        }

        if (player == null)
        {
            Debug.LogError("[Boss 调试] 尚未找到玩家，不能强制攻击。", this);
            return false;
        }

        CancelCurrentAttack();
        attackCoroutine = StartCoroutine(AttackWrapper(attackType, false));
        return true;
    }

    [ContextMenu("调试/强制攻击 1（近战）")]
    private void DebugFormat1()
    {
        DebugStartAttack(BossAttackType.Format1);
    }

    [ContextMenu("调试/强制攻击 2（弹幕）")]
    private void DebugFormat2()
    {
        DebugStartAttack(BossAttackType.Format2);
    }

    [ContextMenu("调试/强制攻击 3（十字）")]
    private void DebugFormat3()
    {
        DebugStartAttack(BossAttackType.Format3);
    }

    [ContextMenu("调试/强制攻击 4（全屏）")]
    private void DebugFormat4()
    {
        DebugStartAttack(BossAttackType.Format4);
    }

    [ContextMenu("调试/强制攻击 6（冲刺）")]
    private void DebugFormat6()
    {
        DebugStartAttack(BossAttackType.Format6);
    }

    public void CancelCurrentAttack()
    {
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        CleanupTelegraphs();
        IsBusy = false;
        nextDecisionTime = Time.time + 0.1f;
    }

    private bool TryChooseAttack(int phase, bool bossVisible, float playerDistance, out BossAttackType chosen)
    {
        chosen = BossAttackType.Format1;

        if (!bossVisible)
        {
            return IsReady(BossAttackType.Format1);
        }

        List<BossAttackType> candidates = new();
        List<float> weights = new();

        AddCandidate(BossAttackType.Format1, stats.format1Weight, candidates, weights);
        AddCandidate(BossAttackType.Format2, stats.format2Weight, candidates, weights);

        if (phase >= 1)
        {
            AddCandidate(BossAttackType.Format4, stats.format4Weight, candidates, weights);
        }

        if (phase >= 2)
        {
            AddCandidate(BossAttackType.Format3, stats.format3Weight, candidates, weights);
        }

        if (phase >= 3 && playerDistance > stats.stoppingDistance * 0.6f)
        {
            AddCandidate(BossAttackType.Format6, stats.format6Weight, candidates, weights);
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        float totalWeight = 0f;
        for (int i = 0; i < weights.Count; i++)
        {
            totalWeight += Mathf.Max(0f, weights[i]);
        }

        if (totalWeight <= 0f)
        {
            chosen = candidates[0];
            return true;
        }

        float roll = Random.Range(0f, totalWeight);
        for (int i = 0; i < candidates.Count; i++)
        {
            roll -= Mathf.Max(0f, weights[i]);
            if (roll <= 0f)
            {
                chosen = candidates[i];
                return true;
            }
        }

        chosen = candidates[^1];
        return true;
    }

    private void AddCandidate(
        BossAttackType attackType,
        float weight,
        ICollection<BossAttackType> candidates,
        ICollection<float> weights)
    {
        if (!IsReady(attackType))
        {
            return;
        }

        candidates.Add(attackType);
        weights.Add(weight);
    }

    private bool IsReady(BossAttackType attackType)
    {
        return !readyTimes.TryGetValue(attackType, out float readyTime) || Time.time >= readyTime;
    }

    private IEnumerator AttackWrapper(BossAttackType attackType, bool offscreenAttack)
    {
        IsBusy = true;
        LastAttack = attackType;
        AttacksStarted++;
        PlayAttackFeedback(attackType);

        if (stats.logCombatEvents)
        {
            Debug.Log($"[Boss] 开始攻击 Format{(int)attackType}（第 {AttacksStarted} 次攻击）。", this);
        }

        switch (attackType)
        {
            case BossAttackType.Format1:
                yield return Format1Routine();
                break;
            case BossAttackType.Format2:
                yield return Format2Routine();
                break;
            case BossAttackType.Format3:
                yield return Format3Routine();
                break;
            case BossAttackType.Format4:
                yield return Format4Routine();
                break;
            case BossAttackType.Format6:
                yield return Format6Routine();
                break;
        }

        CleanupTelegraphs();
        readyTimes[attackType] = Time.time + GetCooldown(attackType);
        float interval = offscreenAttack ? stats.offscreenAttackInterval : stats.globalAttackInterval;
        nextDecisionTime = Time.time + interval;
        IsBusy = false;
        attackCoroutine = null;
    }

    private IEnumerator Format1Routine()
    {
        Vector3 attackCenter = GetGroundPosition(
            meleePoint != null ? meleePoint.position : transform.position);
        LineRenderer warning = CreateCircleTelegraph(attackCenter, stats.format1Radius * 0.15f);
        yield return GrowCircleRoutine(
            warning,
            attackCenter,
            stats.format1Radius,
            stats.format1WarningTime);

        ApplySphereDamage(
            attackCenter,
            stats.format1Radius,
            stats.format1Damage,
            stats.format1Knockback);
    }

    private IEnumerator Format2Routine()
    {
        if (stats.format2PreDelay > 0f)
        {
            yield return new WaitForSeconds(stats.format2PreDelay);
        }

        for (int i = 0; i < stats.format2ProjectileCount; i++)
        {
            if (player == null)
            {
                yield break;
            }

            Vector3 spawnPosition = projectileOrigin != null
                ? projectileOrigin.position
                : transform.position;
            Vector3 direction = Vector3.ProjectOnPlane(player.position - spawnPosition, Vector3.up).normalized;
            SpawnProjectile(spawnPosition, direction);

            if (i < stats.format2ProjectileCount - 1 && stats.format2ProjectileInterval > 0f)
            {
                yield return new WaitForSeconds(stats.format2ProjectileInterval);
            }
        }
    }

    private IEnumerator Format3Routine()
    {
        Vector3 center = GetGroundPosition(transform.position);
        Vector3 forward = Vector3.ProjectOnPlane(player.position - center, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        CreateRectangleTelegraph(center, forward, stats.format3HalfLength * 2f, stats.format3Width);
        CreateRectangleTelegraph(center, right, stats.format3HalfLength * 2f, stats.format3Width);

        if (stats.format3WarningTime > 0f)
        {
            yield return new WaitForSeconds(stats.format3WarningTime);
        }

        ApplyCrossDamage(center, forward, right);
    }

    private IEnumerator Format4Routine()
    {
        Vector3 center = GetGroundPosition(transform.position);
        LineRenderer warning = CreateCircleTelegraph(center, stats.format4Radius * 0.2f);
        yield return GrowCircleRoutine(
            warning,
            center,
            stats.format4Radius,
            stats.format4WarningTime);

        ApplySphereDamage(
            center,
            stats.format4Radius,
            stats.format4Damage,
            stats.format4Knockback);
    }

    private IEnumerator Format6Routine()
    {
        Vector3 start = transform.position;
        Vector3 direction = Vector3.ProjectOnPlane(player.position - start, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = transform.forward;
        }

        float distance = stats.format6Distance;
        Vector3 warningCenter = GetGroundPosition(start + direction * (distance * 0.5f));
        CreateRectangleTelegraph(warningCenter, direction, distance, stats.format6Width);

        if (stats.format6WarningTime > 0f)
        {
            yield return new WaitForSeconds(stats.format6WarningTime);
        }

        Vector3 end = start + direction * distance;
        float duration = distance / Mathf.Max(0.1f, stats.format6Speed);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Vector3 nextPosition = Vector3.Lerp(start, end, Mathf.Clamp01(elapsed / duration));
            if (bossRigidbody != null)
            {
                bossRigidbody.MovePosition(nextPosition);
            }
            else
            {
                transform.position = nextPosition;
            }

            yield return null;
        }

        ApplyBoxDamage(
            start + direction * (distance * 0.5f),
            direction,
            distance,
            stats.format6Width,
            stats.format6Damage,
            stats.format6Knockback);
    }

    private void SpawnProjectile(Vector3 position, Vector3 direction)
    {
        GameObject projectileObject;
        if (stats.projectilePrefab != null)
        {
            projectileObject = Instantiate(stats.projectilePrefab, position, Quaternion.identity);
        }
        else
        {
            projectileObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectileObject.name = "Boss_格式2_弹幕";
            projectileObject.transform.position = position;
            projectileObject.transform.localScale = Vector3.one * (stats.format2ProjectileRadius * 2f);

            Renderer renderer = projectileObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (stats.projectileMaterial != null)
                {
                    renderer.sharedMaterial = stats.projectileMaterial;
                }
                else
                {
                    renderer.material.color = stats.projectileColor;
                }
            }
        }

        projectileObject.layer = gameObject.layer;

        Collider projectileCollider = projectileObject.GetComponent<Collider>();
        if (projectileCollider == null)
        {
            projectileCollider = projectileObject.AddComponent<SphereCollider>();
        }
        projectileCollider.isTrigger = true;

        Rigidbody body = projectileObject.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = projectileObject.AddComponent<Rigidbody>();
        }
        body.useGravity = false;
        body.isKinematic = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        BossProjectile projectile = projectileObject.GetComponent<BossProjectile>();
        if (projectile == null)
        {
            projectile = projectileObject.AddComponent<BossProjectile>();
        }

        projectile.Initialize(
            direction,
            stats.format2ProjectileSpeed,
            stats.format2Damage,
            stats.format2Knockback,
            stats.format2ProjectileLife,
            stats.playerLayer,
            stats.obstacleLayer,
            transform);
    }

    private void ApplySphereDamage(Vector3 center, float radius, int damage, float knockback)
    {
        Collider[] hits = Physics.OverlapSphere(
            center,
            radius,
            stats.playerLayer,
            QueryTriggerInteraction.Collide);

        ApplyDamageToUniquePlayers(hits, damage, knockback);
    }

    private void ApplyCrossDamage(Vector3 center, Vector3 forward, Vector3 right)
    {
        Quaternion forwardRotation = Quaternion.LookRotation(forward, Vector3.up);
        Quaternion rightRotation = Quaternion.LookRotation(right, Vector3.up);
        Vector3 halfExtents = new(
            stats.format3Width * 0.5f,
            2.5f,
            stats.format3HalfLength);

        Collider[] forwardHits = Physics.OverlapBox(
            center,
            halfExtents,
            forwardRotation,
            stats.playerLayer,
            QueryTriggerInteraction.Collide);
        Collider[] rightHits = Physics.OverlapBox(
            center,
            halfExtents,
            rightRotation,
            stats.playerLayer,
            QueryTriggerInteraction.Collide);

        List<Collider> combined = new(forwardHits.Length + rightHits.Length);
        combined.AddRange(forwardHits);
        combined.AddRange(rightHits);
        ApplyDamageToUniquePlayers(combined, stats.format3Damage, stats.format3Knockback);
    }

    private void ApplyBoxDamage(
        Vector3 center,
        Vector3 direction,
        float length,
        float width,
        int damage,
        float knockback)
    {
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
        Collider[] hits = Physics.OverlapBox(
            center,
            new Vector3(width * 0.5f, 2.5f, length * 0.5f),
            rotation,
            stats.playerLayer,
            QueryTriggerInteraction.Collide);

        ApplyDamageToUniquePlayers(hits, damage, knockback);
    }

    private void ApplyDamageToUniquePlayers(IEnumerable<Collider> hits, int damage, float knockback)
    {
        HashSet<Component> damagedTargets = new();
        foreach (Collider hit in hits)
        {
            if (hit == null)
            {
                continue;
            }

            if (!BossCombatTarget.TryGetInParent(hit, out IDamageable damageable))
            {
                BossCombatTarget.EnsurePlayerAdapter(hit.transform.root, true);
                BossCombatTarget.TryGetInParent(hit, out damageable);
            }

            Component damageComponent = damageable as Component;
            if (damageable == null ||
                damageComponent == null ||
                !damagedTargets.Add(damageComponent))
            {
                continue;
            }

            damageable.TakeDamage(damage);

            if (knockback > 0f &&
                BossCombatTarget.TryGetInParent(damageComponent, out IKnockbackReceiver receiver))
            {
                receiver.ApplyKnockback(transform, knockback, 0.18f);
            }
        }
    }

    private IEnumerator GrowCircleRoutine(
        LineRenderer line,
        Vector3 center,
        float targetRadius,
        float duration)
    {
        if (line == null)
        {
            if (duration > 0f)
            {
                yield return new WaitForSeconds(duration);
            }
            yield break;
        }

        if (duration <= 0f)
        {
            SetCirclePoints(line, center, targetRadius);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            SetCirclePoints(line, center, Mathf.Lerp(targetRadius * 0.15f, targetRadius, progress));
            yield return null;
        }
    }

    private LineRenderer CreateCircleTelegraph(Vector3 center, float radius)
    {
        LineRenderer line = CreateLineRenderer("Boss_圆形预警");
        if (line != null)
        {
            line.loop = true;
            line.positionCount = 64;
            SetCirclePoints(line, center, radius);
        }
        return line;
    }

    private void SetCirclePoints(LineRenderer line, Vector3 center, float radius)
    {
        center = GetGroundPosition(center);
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i / (float)line.positionCount * Mathf.PI * 2f;
            line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }

    private LineRenderer CreateRectangleTelegraph(
        Vector3 center,
        Vector3 forward,
        float length,
        float width)
    {
        LineRenderer line = CreateLineRenderer("Boss_长方形预警");
        if (line == null)
        {
            return null;
        }

        line.loop = true;
        line.positionCount = 4;

        center = GetGroundPosition(center);
        forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 halfForward = forward * (length * 0.5f);
        Vector3 halfRight = right * (width * 0.5f);

        line.SetPosition(0, center - halfForward - halfRight);
        line.SetPosition(1, center + halfForward - halfRight);
        line.SetPosition(2, center + halfForward + halfRight);
        line.SetPosition(3, center - halfForward + halfRight);
        return line;
    }

    private LineRenderer CreateLineRenderer(string objectName)
    {
        GameObject warningObject = new(objectName);
        warningObject.layer = gameObject.layer;
        if (groundIndicator != null)
        {
            warningObject.transform.SetParent(groundIndicator, false);
        }

        activeTelegraphs.Add(warningObject);

        LineRenderer line = warningObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.startWidth = stats.telegraphLineWidth;
        line.endWidth = stats.telegraphLineWidth;
        line.startColor = stats.warningColor;
        line.endColor = stats.warningColor;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.sortingOrder = 100;

        if (stats.telegraphMaterial != null)
        {
            line.sharedMaterial = stats.telegraphMaterial;
        }
        else
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                line.material = new Material(shader);
            }
        }

        return line;
    }

    private Vector3 GetGroundPosition(Vector3 position)
    {
        position.y = stats != null ? stats.telegraphHeight : 0.05f;
        return position;
    }

    private float GetCooldown(BossAttackType attackType)
    {
        return attackType switch
        {
            BossAttackType.Format1 => stats.format1Cooldown,
            BossAttackType.Format2 => stats.format2Cooldown,
            BossAttackType.Format3 => stats.format3Cooldown,
            BossAttackType.Format4 => stats.format4Cooldown,
            BossAttackType.Format6 => stats.format6Cooldown,
            _ => 0f
        };
    }

    private void PlayAttackFeedback(BossAttackType attackType)
    {
        if (audioSource != null && stats.attackClip != null)
        {
            audioSource.PlayOneShot(stats.attackClip);
        }

        CreateAttackPulse(attackType);

        string triggerName = GetAnimatorTriggerName(attackType);
        if (animator != null && HasTrigger(animator, triggerName))
        {
            animator.SetTrigger(triggerName);
        }
    }

    private string GetAnimatorTriggerName(BossAttackType attackType)
    {
        return attackType switch
        {
            BossAttackType.Format1 => stats.format1Trigger,
            BossAttackType.Format2 => stats.format2Trigger,
            BossAttackType.Format3 => stats.format3Trigger,
            BossAttackType.Format4 => stats.format4Trigger,
            BossAttackType.Format6 => stats.format6Trigger,
            _ => string.Empty
        };
    }

    private void CreateAttackPulse(BossAttackType attackType)
    {
        if (vfxRoot == null)
        {
            return;
        }

        GameObject pulseObject = new($"AttackVFX_Format{(int)attackType}");
        pulseObject.layer = gameObject.layer;
        pulseObject.transform.SetParent(vfxRoot, false);

        LineRenderer line = pulseObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 32;
        line.startWidth = stats.telegraphLineWidth * 1.6f;
        line.endWidth = line.startWidth;
        line.startColor = stats.projectileColor;
        line.endColor = stats.projectileColor;
        line.sortingOrder = 101;

        if (stats.telegraphMaterial != null)
        {
            line.sharedMaterial = stats.telegraphMaterial;
        }
        else
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                line.material = new Material(shader);
            }
        }

        const float radius = 1.1f;
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i / (float)line.positionCount * Mathf.PI * 2f;
            line.SetPosition(
                i,
                new Vector3(Mathf.Cos(angle) * radius, 0.05f, Mathf.Sin(angle) * radius));
        }

        Destroy(pulseObject, 0.25f);
    }

    private static bool HasTrigger(Animator targetAnimator, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in targetAnimator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger &&
                parameter.name == parameterName)
            {
                return true;
            }
        }

        return false;
    }

    private void CleanupTelegraphs()
    {
        foreach (GameObject telegraph in activeTelegraphs)
        {
            if (telegraph != null)
            {
                Destroy(telegraph);
            }
        }

        activeTelegraphs.Clear();
    }

    private void OnDisable()
    {
        CancelCurrentAttack();
    }
}

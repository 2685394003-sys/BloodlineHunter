using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossProjectile : MonoBehaviour
{
    private Vector3 direction;
    private float speed;
    private int damage;
    private float knockback;
    private LayerMask playerLayer;
    private LayerMask obstacleLayer;
    private Transform owner;
    private bool initialized;

    public void Initialize(
        Vector3 moveDirection,
        float moveSpeed,
        int hitDamage,
        float hitKnockback,
        float lifeTime,
        LayerMask targetPlayerLayer,
        LayerMask worldObstacleLayer,
        Transform projectileOwner)
    {
        direction = Vector3.ProjectOnPlane(moveDirection, Vector3.up).normalized;
        speed = Mathf.Max(0f, moveSpeed);
        damage = Mathf.Max(0, hitDamage);
        knockback = Mathf.Max(0f, hitKnockback);
        playerLayer = targetPlayerLayer;
        obstacleLayer = worldObstacleLayer;
        owner = projectileOwner;
        initialized = true;

        Destroy(gameObject, Mathf.Max(0.05f, lifeTime));
    }

    private void Update()
    {
        if (initialized)
        {
            transform.position += direction * (speed * Time.deltaTime);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!initialized || other == null)
        {
            return;
        }

        if (owner != null && (other.transform == owner || other.transform.IsChildOf(owner)))
        {
            return;
        }

        if (IsInLayerMask(other.gameObject.layer, playerLayer))
        {
            PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
            if (playerHealth == null)
            {
                return;
            }

            playerHealth.ChangeHealth(damage);

            PlayerController playerController = playerHealth.GetComponent<PlayerController>();
            if (playerController != null && playerController.gameObject.activeInHierarchy && knockback > 0f)
            {
                playerController.Knockback(owner != null ? owner : transform, knockback, 0.18f);
            }

            Destroy(gameObject);
            return;
        }

        if (IsInLayerMask(other.gameObject.layer, obstacleLayer))
        {
            Destroy(gameObject);
        }
    }

    private static bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }
}

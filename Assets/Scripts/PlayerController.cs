using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float attackRange = 2.1f;
    [SerializeField] private float attackRadius = 1.15f;
    [SerializeField] private float attackCooldown = 0.35f;

    private Camera viewCamera;
    private Transform attackMarker;
    private Vector3 facingDirection = Vector3.forward;
    private float nextAttackTime;

    private void Awake()
    {
        viewCamera = Camera.main;
        CreateAttackMarker();
    }

    private void Update()
    {
        UpdateFacingFromMouse();
        UpdateAttackMarkerTransform();
        Move();

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Attack();
        }
    }

    private void Move()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed) input.y -= 1f;
        if (Keyboard.current.dKey.isPressed) input.x += 1f;
        if (Keyboard.current.aKey.isPressed) input.x -= 1f;

        if (input.sqrMagnitude < 0.001f)
        {
            return;
        }

        Vector3 cameraForward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
        Vector3 cameraRight = Vector3.ProjectOnPlane(viewCamera.transform.right, Vector3.up).normalized;
        Vector3 movement = (cameraForward * input.y + cameraRight * input.x).normalized;
        transform.position += movement * (moveSpeed * Time.deltaTime);
    }

    private void UpdateFacingFromMouse()
    {
        if (viewCamera == null || Mouse.current == null)
        {
            return;
        }

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
        {
            return;
        }

        attackMarker.localPosition = facingDirection * attackRange;
        attackMarker.rotation = viewCamera.transform.rotation;
    }

    private void Attack()
    {
        if (Time.time < nextAttackTime)
        {
            return;
        }

        nextAttackTime = Time.time + attackCooldown;
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
}

using UnityEngine;

public class FlowFieldEnemy : MonoBehaviour
{
    public float moveSpeed = 3f;
    public float turnSmooth = 6f;
    public float dirBlendSpeed = 7f;
    private FlowFieldManager flowField;
    private Vector3 smoothDirection;

    void Start()
    {
        flowField = FindObjectOfType<FlowFieldManager>();
        smoothDirection = Vector3.forward;
    }

    void Update()
    {
        if (flowField == null)
        {
            flowField = FindObjectOfType<FlowFieldManager>();
            return;
        }

        Vector3 rawDir = flowField.GetFlowDirection(transform.position);
        // 流场失效兜底
        if(rawDir.magnitude < 0.01f)
            rawDir = (flowField.player.position - transform.position).normalized;

        // 方向平滑缓冲，消除跳变
        smoothDirection = Vector3.Lerp(smoothDirection, rawDir.normalized, Time.deltaTime * dirBlendSpeed);

        // 移动
        transform.position += smoothDirection * moveSpeed * Time.deltaTime;

        // 平滑水平旋转
        if(smoothDirection.magnitude > 0.01f)
        {
            Vector3 flatDir = Vector3.ProjectOnPlane(smoothDirection, Vector3.up);
            Quaternion targetRot = Quaternion.LookRotation(flatDir);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRot, Time.deltaTime * turnSmooth);
        }
    }
}

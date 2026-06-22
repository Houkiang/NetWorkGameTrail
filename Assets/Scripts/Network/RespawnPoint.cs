using UnityEngine;

[DisallowMultipleComponent]
public class RespawnPoint : MonoBehaviour
{
    [SerializeField]
    private Color gizmoColor = new Color(0.22f, 0.95f, 0.42f, 0.95f);

    [SerializeField]
    private float gizmoRadius = 0.25f;

    [SerializeField]
    private float gizmoHeight = 1.4f;

    public Vector3 Position => transform.position;

    public Quaternion Rotation => transform.rotation;

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Vector3 position = transform.position;
        Vector3 top = position + Vector3.up * gizmoHeight;
        Vector3 forward = transform.forward * gizmoRadius * 1.8f;

        Gizmos.DrawWireSphere(position, gizmoRadius);
        Gizmos.DrawLine(position, top);
        Gizmos.DrawLine(top, top + forward);
        Gizmos.DrawWireSphere(top, gizmoRadius * 0.6f);
    }
}

using UnityEngine;

/// <summary>
/// Spins this object around a configurable local axis.
/// Positive and negative speed values choose the spin direction.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Spinner : MonoBehaviour
{
    [Tooltip("Degrees rotated per second. Use a negative value to reverse direction.")]
    [SerializeField] private float degreesPerSecond = 90f;
    [Tooltip("Local axis to rotate around. For example: (0, 1, 0) spins around Y.")]
    [SerializeField] private Vector3 axis = Vector3.up;

    private Rigidbody spinnerBody;

    private void Awake()
    {
        // A collider rotated directly from Update teleports between physics
        // steps, so fast blades can pass through dynamic soft-body points.
        // Move a kinematic Rigidbody from FixedUpdate instead; PhysX then has
        // the complete angular sweep for collision detection.
        spinnerBody = GetComponent<Rigidbody>();
        if (spinnerBody == null) spinnerBody = gameObject.AddComponent<Rigidbody>();
        spinnerBody.isKinematic = true;
        spinnerBody.useGravity = false;
        spinnerBody.interpolation = RigidbodyInterpolation.Interpolate;
        spinnerBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private void FixedUpdate()
    {
        if (Mathf.Approximately(degreesPerSecond, 0f) || axis.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        Quaternion step = Quaternion.AngleAxis(
            degreesPerSecond * Time.fixedDeltaTime,
            axis.normalized);
        spinnerBody.MoveRotation(spinnerBody.rotation * step);
    }

    private void OnDrawGizmosSelected()
    {
        if (axis.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        Vector3 worldAxis = transform.TransformDirection(axis.normalized);
        float directionSign = degreesPerSecond >= 0f ? 1f : -1f;

        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.9f);
        Gizmos.DrawLine(transform.position - worldAxis, transform.position + worldAxis);
        Gizmos.DrawRay(transform.position, worldAxis * directionSign);
        Gizmos.DrawWireSphere(transform.position, 0.35f);
    }
}

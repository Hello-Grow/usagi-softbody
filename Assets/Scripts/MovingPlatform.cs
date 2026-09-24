using UnityEngine;

/// <summary>
/// Moves this object back and forth along a local direction.
/// Configure its speed, direction, and travel distance in the Inspector.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MovingPlatform : MonoBehaviour
{
    [Min(0f)] [SerializeField] private float speed = 2f;
    [SerializeField] private Vector3 direction = Vector3.right;
    [Min(0f)] [SerializeField] private float travelDistance = 3f;
    [SerializeField] private bool startMovingTowardPositiveDirection = true;

    private Rigidbody platformBody;
    private Vector3 startPosition;
    private float distanceTravelled;
    private int movementSign;

    /// <summary>The velocity supplied to riders by this kinematic platform.</summary>
    public Vector3 Velocity => platformBody != null ? platformBody.linearVelocity : Vector3.zero;

    private void Awake()
    {
        // RequireComponent is applied when a component is added. Add the body
        // here too so platforms that already existed in a scene keep working
        // after this script is updated.
        platformBody = GetComponent<Rigidbody>();
        if (platformBody == null) platformBody = gameObject.AddComponent<Rigidbody>();
        // Moving a collider through transforms in Update makes PhysX resolve
        // the next contact as an overlap. A kinematic Rigidbody moved during
        // FixedUpdate supplies a continuous platform velocity instead.
        platformBody.isKinematic = true;
        platformBody.useGravity = false;
        platformBody.interpolation = RigidbodyInterpolation.Interpolate;
        startPosition = transform.position;
        movementSign = startMovingTowardPositiveDirection ? 1 : -1;
    }

    private void FixedUpdate()
    {
        if (speed <= 0f || travelDistance <= 0f || direction.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        Vector3 movementDirection = transform.TransformDirection(direction.normalized);
        distanceTravelled += speed * Time.fixedDeltaTime * movementSign;

        if (Mathf.Abs(distanceTravelled) >= travelDistance)
        {
            distanceTravelled = Mathf.Sign(distanceTravelled) * travelDistance;
            movementSign *= -1;
        }

        platformBody.MovePosition(startPosition + movementDirection * distanceTravelled);
    }

    private void OnDrawGizmosSelected()
    {
        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        Vector3 worldDirection = transform.TransformDirection(direction.normalized);
        Vector3 start = Application.isPlaying ? startPosition : transform.position;
        Vector3 positiveEnd = start + worldDirection * travelDistance;
        Vector3 negativeEnd = start - worldDirection * travelDistance;

        Gizmos.color = new Color(0.15f, 0.8f, 1f, 0.85f);
        Gizmos.DrawLine(negativeEnd, positiveEnd);
        Gizmos.DrawWireSphere(positiveEnd, 0.15f);
        Gizmos.DrawWireSphere(negativeEnd, 0.15f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(start, worldDirection * 0.75f);
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Idle-only version of the soft-body player controller. It uses the same
/// spring forces to hold the character's rest pose, but reads no input and
/// never moves, jumps, or turns the player.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EmbeddedSoftBody), typeof(SoftBodyLegAssignments))]
public sealed class PlayerStandUpright : MonoBehaviour
{
    [Header("Physics Springs")]
    [Min(0f)] public float torsoStrength = 110f;
    [Min(0f)] public float footStrength = 700f;
    [Min(0f)] public float damping = 60f;

    [Header("Ground Contact")]
    [Range(0f, 1f)] public float groundedTorsoVerticalStrength = 1f;

    [Header("Moving Platforms")]
    [Tooltip("Avoids applying the root mesh collider's duplicate collision impulse to the particles.")]
    public bool suppressMeshCollisionImpulse = true;

    readonly Dictionary<Rigidbody, Vector3> rest = new Dictionary<Rigidbody, Vector3>();
    readonly HashSet<Rigidbody> feet = new HashSet<Rigidbody>();
    Rigidbody[] allBodies;
    Rigidbody[] leftFootBodies;
    Rigidbody[] rightFootBodies;
    Vector3 restCenter;
    Vector3 leftRestCenter;
    Vector3 rightRestCenter;
    Vector3 pelvisTarget;
    Vector3 leftPlant;
    Vector3 rightPlant;
    Transform supportingTransform;
    Rigidbody supportingBody;
    Vector3 previousSupportPosition;
    bool hasPreviousSupportPosition;
    bool ready;

    /// <summary>Re-centres the upright springs after an external reposition.</summary>
    public void ResetPoseTargets()
    {
        if (!ready) return;
        pelvisTarget = CenterOf(allBodies);
        leftPlant = CenterOf(leftFootBodies);
        rightPlant = CenterOf(rightFootBodies);
    }

    void Start()
    {
        EmbeddedSoftBody softBody = GetComponent<EmbeddedSoftBody>();
        if (suppressMeshCollisionImpulse && softBody != null)
            softBody.relayCollisionImpulse = false;
        BuildGroups();
    }

    void FixedUpdate()
    {
        if (!ready) return;

        Vector3 center = CenterOf(allBodies);
        bool grounded = TryGetGroundNormal(out _, out Transform supportTransform, out Rigidbody supportBody);
        FollowSupportingBody(grounded ? supportTransform : null, supportBody);

        BalanceTorso(grounded, center);
        HoldLeg(leftFootBodies, leftRestCenter, leftPlant, grounded, center);
        HoldLeg(rightFootBodies, rightRestCenter, rightPlant, grounded, center);
    }

    void BuildGroups()
    {
        EmbeddedSoftBody softBody = GetComponent<EmbeddedSoftBody>();
        SoftBodyLegAssignments assignments = GetComponent<SoftBodyLegAssignments>();
        var bodies = new List<Rigidbody>();

        foreach (Transform point in softBody.pointTransforms)
        {
            if (point == null) continue;
            Rigidbody body = point.GetComponent<Rigidbody>();
            if (body != null && !bodies.Contains(body)) bodies.Add(body);
        }

        allBodies = bodies.ToArray();
        leftFootBodies = BodiesFor(assignments.leftLegPoints);
        rightFootBodies = BodiesFor(assignments.rightLegPoints);
        if (allBodies.Length == 0 || leftFootBodies.Length == 0 || rightFootBodies.Length == 0)
        {
            Debug.LogWarning("Assign soft-body points to both leg arrays before playing.", this);
            return;
        }

        restCenter = Vector3.zero;
        foreach (Rigidbody body in allBodies)
        {
            rest[body] = transform.InverseTransformPoint(body.worldCenterOfMass);
            restCenter += rest[body];
        }
        restCenter /= allBodies.Length;

        AddFeet(leftFootBodies);
        AddFeet(rightFootBodies);
        AddGroundSensors(leftFootBodies);
        AddGroundSensors(rightFootBodies);
        leftRestCenter = RestCenterOf(leftFootBodies);
        rightRestCenter = RestCenterOf(rightFootBodies);
        pelvisTarget = CenterOf(allBodies);
        leftPlant = CenterOf(leftFootBodies);
        rightPlant = CenterOf(rightFootBodies);
        ready = true;
    }

    Rigidbody[] BodiesFor(Transform[] points)
    {
        var bodies = new List<Rigidbody>();
        if (points == null) return bodies.ToArray();

        foreach (Transform point in points)
        {
            if (point == null) continue;
            Rigidbody body = point.GetComponent<Rigidbody>();
            if (body != null && !bodies.Contains(body)) bodies.Add(body);
        }
        return bodies.ToArray();
    }

    void AddFeet(Rigidbody[] bodies)
    {
        foreach (Rigidbody body in bodies) feet.Add(body);
    }

    void AddGroundSensors(Rigidbody[] bodies)
    {
        foreach (Rigidbody body in bodies)
        {
            SoftBodyFootGroundSensor sensor = body.GetComponent<SoftBodyFootGroundSensor>();
            if (sensor == null) sensor = body.gameObject.AddComponent<SoftBodyFootGroundSensor>();
            sensor.SetOwner(transform);
        }
    }

    Vector3 RestCenterOf(Rigidbody[] bodies)
    {
        Vector3 center = Vector3.zero;
        foreach (Rigidbody body in bodies) center += rest[body];
        return center / bodies.Length;
    }

    static Vector3 CenterOf(Rigidbody[] bodies)
    {
        Vector3 center = Vector3.zero;
        foreach (Rigidbody body in bodies) center += body.worldCenterOfMass;
        return center / bodies.Length;
    }

    void BalanceTorso(bool grounded, Vector3 center)
    {
        foreach (Rigidbody body in allBodies)
        {
            if (feet.Contains(body) || body.isKinematic) continue;
            Vector3 offset = transform.rotation * (rest[body] - restCenter);
            Vector3 desired = pelvisTarget + offset;
            if (!grounded) desired.y = center.y + offset.y;
            Spring(body, desired, torsoStrength, grounded, groundedTorsoVerticalStrength);
        }
    }

    void HoldLeg(Rigidbody[] leg, Vector3 legRestCenter, Vector3 planted, bool grounded, Vector3 center)
    {
        foreach (Rigidbody body in leg)
        {
            if (body.isKinematic) continue;
            Vector3 offset = transform.rotation * (rest[body] - legRestCenter);
            Vector3 desired = planted + offset;
            if (!grounded) desired.y = center.y + offset.y;
            // A planted foot has no vertical spring while grounded, matching
            // the regular controller's idle stance.
            Spring(body, desired, footStrength, grounded, grounded ? 0f : 1f);
        }
    }

    void Spring(Rigidbody body, Vector3 target, float strength, bool dampVertical, float verticalStrength)
    {
        Vector3 error = target - body.worldCenterOfMass;
        Vector3 velocity = body.linearVelocity;
        error.y *= dampVertical ? verticalStrength : 1f;
        if (!dampVertical) velocity.y = 0f;
        body.AddForce(error * strength - velocity * damping, ForceMode.Acceleration);
    }

    void FollowSupportingBody(Transform support, Rigidbody supportBody)
    {
        if (support == null)
        {
            supportingTransform = null;
            supportingBody = null;
            hasPreviousSupportPosition = false;
            return;
        }

        bool changedSupport = support != supportingTransform;
        supportingTransform = support;
        supportingBody = supportBody;
        Vector3 platformDelta = changedSupport || !hasPreviousSupportPosition
            ? (supportingBody != null ? supportingBody.linearVelocity * Time.fixedDeltaTime : Vector3.zero)
            : supportingTransform.position - previousSupportPosition;
        hasPreviousSupportPosition = true;
        previousSupportPosition = supportingTransform.position;
        if (platformDelta.sqrMagnitude <= Mathf.Epsilon) return;

        foreach (Rigidbody body in allBodies)
            if (body != null && !body.isKinematic) body.position += platformDelta;
        pelvisTarget += platformDelta;
        leftPlant += platformDelta;
        rightPlant += platformDelta;
    }

    bool TryGetGroundNormal(out Vector3 normal, out Transform supportTransform, out Rigidbody supportBody)
    {
        normal = Vector3.up;
        supportTransform = null;
        supportBody = null;
        foreach (Rigidbody foot in feet)
        {
            SoftBodyFootGroundSensor sensor = foot.GetComponent<SoftBodyFootGroundSensor>();
            if (sensor != null && sensor.TryGetGroundNormal(out normal, out supportTransform, out supportBody)) return true;
        }
        return false;
    }
}

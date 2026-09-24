using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Physics-only walker using manually assigned soft-body foot points.</summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(EmbeddedSoftBody), typeof(SoftBodyLegAssignments))]
public class SoftBodyPointPlayerController : MonoBehaviour
{
    [Header("Walking")]
    [Min(0f)] public float moveSpeed = 1.8f;
    [Min(0.1f)] public float stepsPerSecond = 5f;
    [Min(0f)] public float stepDistance = 0.32f;
    [Min(0f)] public float stepHeight = 0.16f;
    [Tooltip("The third-person camera. Uses the tagged Main Camera when left empty.")]
    public Transform viewTransform;

    [Header("Camera Facing")]
    [Tooltip("Keep the character's spring-rest pose facing the third-person camera's horizontal view direction.")]
    public bool faceCamera = true;
    [Tooltip("How quickly the character turns to match camera yaw, in degrees per second.")]
    [Min(0f)] public float cameraTurnSpeed = 720f;
    [Tooltip("Use this only if the imported model's forward axis is offset from its transform forward axis.")]
    [Range(-180f, 180f)] public float modelForwardYawOffset;

    [Header("Physics Springs")]
    [Min(0f)] public float torsoStrength = 110f;
    [Min(0f)] public float footStrength = 700f;
    [Min(0f)] public float damping = 60f;
    [Min(0f)] public float jumpVelocity = 4.5f;
    [Min(0f)] public float jumpGroundGrace = 0.18f;

    [Header("Ground Contact")]
    [Range(0f, 1f)] public float footFriction = 0.15f;
    [Range(0f, 1f)] public float groundedFootVerticalStrength = 0.25f;
    [Range(0f, 1f)] public float groundedTorsoVerticalStrength = 1f;
    [Tooltip("Small gap used to align the lowest foot collider above the ground when play begins. This is applied once and does not add bounce while walking.")]
    [Min(0f)] public float footGroundClearance = 0.025f;
    [Tooltip("Maximum distance checked below each foot for a supporting surface.")]
    [Min(0.01f)] public float groundProbeDistance = 0.5f;
    [Tooltip("Layers that can support the player. Leave as Everything unless your ground uses a dedicated layer.")]
    public LayerMask groundLayers = ~0;

    [Header("Upright Pose")]
    [Tooltip("Prevents individual soft-body points from accumulating roll, pitch, or yaw while idle.")]
    public bool lockPointRotations = true;

    [Header("Moving Platforms")]
    [Tooltip("The root mesh collider duplicates the particle colliders. Disable its impulse relay so contact with a moving platform is resolved once, by the particles themselves.")]
    public bool suppressMeshCollisionImpulse = true;

    SoftBodyLegAssignments assignments;
    EmbeddedSoftBody embeddedSoftBody;
    Rigidbody[] allBodies, leftFootBodies, rightFootBodies;
    readonly Dictionary<Rigidbody, Vector3> rest = new Dictionary<Rigidbody, Vector3>();
    readonly HashSet<Rigidbody> feet = new HashSet<Rigidbody>();
    Vector3 restCenter, leftRestCenter, rightRestCenter, pelvisTarget, leftPlant, rightPlant;
    Vector2 moveInput;
    float gaitTime;
    bool leftSwinging, jumpQueued, ready, wasMoving;
    float jumpGraceTimer;
    Vector3 groundNormal = Vector3.up;
    Quaternion facingRotation;
    Transform supportingTransform;
    Rigidbody supportingBody;
    Vector3 previousSupportPosition;
    bool hasPreviousSupportPosition;

    public Vector3 CameraFocusPoint => ready ? CenterOf(allBodies) : transform.position;

    /// <summary>
    /// Re-centres the walking springs after an external reposition, such as
    /// getting back up from ragdoll mode. Without this, old world-space
    /// targets can pull the player violently toward its previous location.
    /// </summary>
    public void ResetPoseTargets()
    {
        if (!ready) return;
        Vector3 center = CenterOf(allBodies);
        pelvisTarget = center;
        leftPlant = CenterOf(leftFootBodies);
        rightPlant = CenterOf(rightFootBodies);
        jumpQueued = false;
        jumpGraceTimer = 0f;
        wasMoving = false;
        facingRotation = transform.rotation;
    }

    /// <summary>
    /// Raises the complete soft-body so its assigned foot colliders sit above
    /// the supporting surface. Used after ragdoll recovery as well as spawn.
    /// </summary>
    public void SnapFeetAboveGround()
    {
        if (!ready) return;
        AlignFeetToGroundAtSpawn();
    }

    void Start()
    {
        assignments = GetComponent<SoftBodyLegAssignments>();
        embeddedSoftBody = GetComponent<EmbeddedSoftBody>();
        if (suppressMeshCollisionImpulse && embeddedSoftBody != null)
            embeddedSoftBody.relayCollisionImpulse = false;
        if (viewTransform == null && Camera.main != null) viewTransform = Camera.main.transform;
        facingRotation = transform.rotation;
        BuildGroups();
    }

    void Update()
    {
        Keyboard key = Keyboard.current;
        if (key == null) return;
        // Camera-relative controls: W/S = forward/back, A/D = left/right.
        moveInput = new Vector2(
            (key.dKey.isPressed || key.rightArrowKey.isPressed ? 1f : 0f) - (key.aKey.isPressed || key.leftArrowKey.isPressed ? 1f : 0f),
            (key.wKey.isPressed || key.upArrowKey.isPressed ? 1f : 0f) - (key.sKey.isPressed || key.downArrowKey.isPressed ? 1f : 0f));
        moveInput = Vector2.ClampMagnitude(moveInput, 1f);
        if (key.spaceKey.wasPressedThisFrame) jumpQueued = true;
    }

    void FixedUpdate()
    {
        if (!ready) return;
        Vector3 center = CenterOf(allBodies);
        UpdateCameraFacing(center);
        jumpGraceTimer = Mathf.Max(0f, jumpGraceTimer - Time.fixedDeltaTime);
        Transform supportTransform = null;
        Rigidbody supportBody = null;
        bool grounded = jumpGraceTimer <= 0f
            && TryGetGroundNormal(out groundNormal, out supportTransform, out supportBody);
        FollowSupportingBody(grounded ? supportTransform : null, supportBody);
        UpdateGait(center, grounded);
        BalanceTorso(grounded, center);
        DriveLeg(leftFootBodies, leftRestCenter, leftPlant, leftSwinging, grounded, center);
        DriveLeg(rightFootBodies, rightRestCenter, rightPlant, !leftSwinging, grounded, center);
        if (jumpQueued && grounded)
        {
            for (int i = 0; i < allBodies.Length; i++) allBodies[i].AddForce(Vector3.up * jumpVelocity, ForceMode.VelocityChange);
            jumpGraceTimer = jumpGroundGrace;
        }
        jumpQueued = false;
    }

    /// <summary>
    /// The skin can extend a little below the small physics points that make
    /// up a foot. Correct the spawn pose once, before the walking springs run.
    /// A per-frame position correction would fight those springs and produce a
    /// visible bounce.
    /// </summary>
    void AlignFeetToGroundAtSpawn()
    {
        if (footGroundClearance <= 0f || feet.Count == 0) return;

        float requiredLift = 0f;
        foreach (Rigidbody foot in feet)
        {
            if (foot == null) continue;

            Collider[] colliders = foot.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;

                Bounds bounds = collider.bounds;
                float probeStartHeight = Mathf.Max(groundProbeDistance, footGroundClearance) + 0.01f;
                Vector3 probeStart = new Vector3(bounds.center.x, bounds.max.y + probeStartHeight, bounds.center.z);
                RaycastHit[] hits = Physics.RaycastAll(
                    probeStart,
                    Vector3.down,
                    probeStartHeight + bounds.size.y + footGroundClearance,
                    groundLayers,
                    QueryTriggerInteraction.Ignore);

                for (int h = 0; h < hits.Length; h++)
                {
                    Collider ground = hits[h].collider;
                    if (ground == null || ground.transform == transform || ground.transform.IsChildOf(transform)) continue;

                    float lift = hits[h].point.y + footGroundClearance - bounds.min.y;
                    if (lift > requiredLift) requiredLift = lift;
                }
            }
        }

        if (requiredLift <= 0f) return;

        Vector3 correction = Vector3.up * requiredLift;
        for (int i = 0; i < allBodies.Length; i++)
        {
            Rigidbody body = allBodies[i];
            if (body == null || body.isKinematic) continue;
            body.position += correction;
        }

        pelvisTarget += correction;
        leftPlant += correction;
        rightPlant += correction;
        Physics.SyncTransforms();
    }

    void BuildGroups()
    {
        // Only drive the particles used by EmbeddedSoftBody. The imported
        // character hierarchy can also contain ragdoll rigidbodies; including
        // those makes the calculated centre/facing unrelated to the rendered
        // soft body and can prevent the particle cloud from turning.
        EmbeddedSoftBody softBody = GetComponent<EmbeddedSoftBody>();
        var particleBodies = new List<Rigidbody>();
        if (softBody != null && softBody.pointTransforms != null)
        {
            for (int i = 0; i < softBody.pointTransforms.Count; i++)
            {
                Transform point = softBody.pointTransforms[i];
                if (point == null) continue;
                Rigidbody body = point.GetComponent<Rigidbody>();
                if (body != null && !particleBodies.Contains(body)) particleBodies.Add(body);
            }
        }
        allBodies = particleBodies.Count > 0
            ? particleBodies.ToArray()
            : GetComponentsInChildren<Rigidbody>(false);
        leftFootBodies = BodiesFor(assignments.leftLegPoints);
        rightFootBodies = BodiesFor(assignments.rightLegPoints);
        if (allBodies.Length == 0 || leftFootBodies.Length == 0 || rightFootBodies.Length == 0)
        {
            Debug.LogWarning("Assign one or more SoftBodyPoints to both leg arrays before playing.", this);
            return;
        }

        rest.Clear(); feet.Clear(); restCenter = Vector3.zero;
        for (int i = 0; i < allBodies.Length; i++)
        {
            rest[allBodies[i]] = transform.InverseTransformPoint(allBodies[i].worldCenterOfMass);
            restCenter += rest[allBodies[i]];
        }
        restCenter /= allBodies.Length;
        AddFeet(leftFootBodies); AddFeet(rightFootBodies);
        if (lockPointRotations) FreezePointRotations(allBodies);
        AddGroundSensors(leftFootBodies);
        AddGroundSensors(rightFootBodies);
        ApplyFootContactMaterial(leftFootBodies);
        ApplyFootContactMaterial(rightFootBodies);
        leftRestCenter = RestCenterOf(leftFootBodies);
        rightRestCenter = RestCenterOf(rightFootBodies);
        pelvisTarget = CenterOf(allBodies);
        leftPlant = CenterOf(leftFootBodies);
        rightPlant = CenterOf(rightFootBodies);
        AlignFeetToGroundAtSpawn();
        ready = true;
    }

    Rigidbody[] BodiesFor(Transform[] points)
    {
        var result = new List<Rigidbody>();
        if (points == null) return result.ToArray();
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null) continue;
            Rigidbody body = points[i].GetComponent<Rigidbody>();
            if (body != null && !result.Contains(body)) result.Add(body);
        }
        return result.ToArray();
    }

    void AddFeet(Rigidbody[] bodies) { for (int i = 0; i < bodies.Length; i++) feet.Add(bodies[i]); }
    void FreezePointRotations(Rigidbody[] bodies)
    {
        for (int i = 0; i < bodies.Length; i++)
            bodies[i].constraints |= RigidbodyConstraints.FreezeRotation;
    }
    void AddGroundSensors(Rigidbody[] bodies)
    {
        for (int i = 0; i < bodies.Length; i++)
        {
            SoftBodyFootGroundSensor sensor = bodies[i].GetComponent<SoftBodyFootGroundSensor>();
            if (sensor == null) sensor = bodies[i].gameObject.AddComponent<SoftBodyFootGroundSensor>();
            sensor.SetOwner(transform);
        }
    }
    void ApplyFootContactMaterial(Rigidbody[] bodies)
    {
        PhysicsMaterial material = new PhysicsMaterial("SoftBody Foot Contact")
        {
            dynamicFriction = footFriction,
            staticFriction = footFriction,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        for (int i = 0; i < bodies.Length; i++)
        {
            Collider[] colliders = bodies[i].GetComponentsInChildren<Collider>();
            for (int c = 0; c < colliders.Length; c++) colliders[c].material = material;
        }
    }
    Vector3 RestCenterOf(Rigidbody[] bodies)
    {
        Vector3 center = Vector3.zero;
        for (int i = 0; i < bodies.Length; i++) center += rest[bodies[i]];
        return center / bodies.Length;
    }
    Vector3 CenterOf(Rigidbody[] bodies)
    {
        Vector3 center = Vector3.zero;
        for (int i = 0; i < bodies.Length; i++) center += bodies[i].worldCenterOfMass;
        return center / bodies.Length;
    }

    void UpdateGait(Vector3 center, bool grounded)
    {
        if (moveInput.sqrMagnitude < 0.01f)
        {
            if (wasMoving) pelvisTarget = center;
            wasMoving = false;
            return;
        }
        wasMoving = true;
        Vector3 direction = MoveDirection();
        if (grounded) direction = Vector3.ProjectOnPlane(direction, groundNormal).normalized;
        pelvisTarget += direction * (moveSpeed * Time.fixedDeltaTime);
        gaitTime += stepsPerSecond * Mathf.PI * Time.fixedDeltaTime;
        bool newLeftSwinging = Mathf.Sin(gaitTime) > 0f;
        if (newLeftSwinging == leftSwinging) return;
        if (newLeftSwinging) rightPlant = CenterOf(rightFootBodies);
        else leftPlant = CenterOf(leftFootBodies);
        leftSwinging = newLeftSwinging;
    }

    Vector3 RotatedRestVector(Vector3 localVector)
    {
        return transform.rotation * localVector;
    }

    Vector3 MoveDirection()
    {
        Vector3 forward = ViewForward();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        return (right * moveInput.x + forward * moveInput.y).normalized;
    }

    Vector3 ViewForward()
    {
        if (viewTransform == null) return Vector3.forward;
        Vector3 forward = Vector3.ProjectOnPlane(viewTransform.forward, Vector3.up);
        return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
    }

    void UpdateCameraFacing(Vector3 bodyCenter)
    {
        if (!faceCamera || viewTransform == null) return;

        Vector3 forward = ViewForward();
        Quaternion targetRotation = Quaternion.LookRotation(forward, Vector3.up)
            * Quaternion.Euler(0f, modelForwardYawOffset, 0f);
        if (embeddedSoftBody == null) return;

        Quaternion previousRotation = transform.rotation;
        facingRotation = embeddedSoftBody.RotateWholeBody(
            targetRotation,
            cameraTurnSpeed * Time.fixedDeltaTime);
        Quaternion turn = facingRotation * Quaternion.Inverse(previousRotation);
        RotateGaitTargets(bodyCenter, turn);

        // The particle cloud now owns the facing, so no extra mesh-only yaw
        // is applied on top of the physical turn.
        embeddedSoftBody.SetVisualFacing(facingRotation);

    }

    void RotateGaitTargets(Vector3 pivot, Quaternion turn)
    {
        if (Quaternion.Angle(Quaternion.identity, turn) < 0.001f) return;
        pelvisTarget = pivot + turn * (pelvisTarget - pivot);
        leftPlant = pivot + turn * (leftPlant - pivot);
        rightPlant = pivot + turn * (rightPlant - pivot);
    }

    Vector3 VisualCompensatedMoveDirection()
    {
        // A swing offset is rotated once more when the soft-body mesh is
        // rendered. Counter-rotate it here so its final visual direction still
        // matches the camera-relative movement direction.
        Vector3 direction = MoveDirection();
        return embeddedSoftBody != null
            ? embeddedSoftBody.RenderedDirectionToPhysicsDirection(direction)
            : Quaternion.Inverse(facingRotation) * direction;
    }

    void BalanceTorso(bool grounded, Vector3 center)
    {
        for (int i = 0; i < allBodies.Length; i++)
        {
            Rigidbody body = allBodies[i];
            if (feet.Contains(body) || body.isKinematic) continue;
            Vector3 offset = RotatedRestVector(rest[body] - restCenter);
            Vector3 desired = pelvisTarget + offset;
            if (!grounded) desired.y = center.y + offset.y;
            Spring(body, desired, torsoStrength, true, grounded, groundedTorsoVerticalStrength);
        }
    }

    void DriveLeg(Rigidbody[] leg, Vector3 legRestCenter, Vector3 planted, bool swinging, bool grounded, Vector3 center)
    {
        Vector3 target = planted;
        if (swinging && moveInput.sqrMagnitude > 0.01f)
        {
            float phase = Mathf.Abs(Mathf.Sin(gaitTime));
            Vector3 direction = VisualCompensatedMoveDirection();
            target = pelvisTarget + RotatedRestVector(legRestCenter - restCenter)
                + direction * (stepDistance * phase)
                + Vector3.up * (stepHeight * Mathf.Sin(phase * Mathf.PI));
        }
        for (int i = 0; i < leg.Length; i++)
        {
            if (leg[i].isKinematic) continue;
            Vector3 offset = RotatedRestVector(rest[leg[i]] - legRestCenter);
            Vector3 desired = target + offset;
            if (!grounded) desired.y = center.y + offset.y;
            // A planted foot should damp vertical velocity, but never be
            // pulled into the ground by a vertical spring. The torso spring
            // supports the body while the stance foot stays stable.
            float verticalStrength = grounded && !swinging ? 0f : groundedFootVerticalStrength;
            Spring(leg[i], desired, footStrength, true, grounded, verticalStrength);
        }
    }

    void Spring(Rigidbody body, Vector3 target, float strength, bool controlVertical, bool dampVertical, float verticalStrength = 1f)
    {
        Vector3 error = target - body.worldCenterOfMass;
        Vector3 velocity = body.linearVelocity;
        if (!controlVertical) { error.y = 0f; velocity.y = 0f; }
        else
        {
            error.y *= dampVertical ? verticalStrength : 1f;
            if (!dampVertical) velocity.y = 0f;
        }
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

/// <summary>Reports only real, upward-facing collisions for a soft-body foot.</summary>
[DisallowMultipleComponent]
public class SoftBodyFootGroundSensor : MonoBehaviour
{
    Transform owner;
    Vector3 latestGroundNormal = Vector3.up;
    Transform latestGroundTransform;
    Rigidbody latestGroundBody;
    float lastGroundContactTime = float.NegativeInfinity;

    public void SetOwner(Transform ownerTransform) => owner = ownerTransform;

    void OnCollisionEnter(Collision collision) => RecordGroundContact(collision);
    void OnCollisionStay(Collision collision) => RecordGroundContact(collision);

    void RecordGroundContact(Collision collision)
    {
        if (owner == null || collision.collider == null) return;
        Transform other = collision.collider.transform;
        if (other == owner || other.IsChildOf(owner)) return;

        ContactPoint[] contacts = collision.contacts;
        for (int i = 0; i < contacts.Length; i++)
        {
            if (contacts[i].normal.y <= 0.1f) continue;
            latestGroundNormal = contacts[i].normal;
            latestGroundTransform = other;
            latestGroundBody = collision.rigidbody;
            lastGroundContactTime = Time.fixedTime;
            return;
        }
    }

    public bool TryGetGroundNormal(out Vector3 normal, out Transform groundTransform, out Rigidbody groundBody)
    {
        normal = latestGroundNormal;
        groundTransform = latestGroundTransform;
        groundBody = latestGroundBody;
        return Time.fixedTime - lastGroundContactTime <= Time.fixedDeltaTime * 1.5f;
    }
}

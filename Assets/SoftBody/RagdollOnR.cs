using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Turns this soft-body player into a physics-only ragdoll when R is pressed.
/// Press R again to restore the controller state that existed before ragdoll.
/// </summary>
[DisallowMultipleComponent]
public class RagdollOnR : MonoBehaviour
{
    [Tooltip("Optional extra movement or animation behaviours to turn off with the player controller.")]
    [SerializeField] private Behaviour[] additionalBehavioursToDisable = System.Array.Empty<Behaviour>();

    [Tooltip("Remove Rigidbody rotation locks so the ragdoll can tumble.")]
    [SerializeField] private bool unfreezeRigidbodyRotations = true;

    [Tooltip("Gravity multiplier while ragdolled. Lower this if the unassisted ragdoll falls faster than the controlled character.")]
    [Range(0f, 1f)] [SerializeField] private float ragdollGravityScale = 0.75f;

    [Tooltip("When leaving ragdoll mode, snap back to the upright pose saved when ragdoll began.")]
    [SerializeField] private bool standUpOnRecovery = true;

    [Tooltip("Extra space left above the floor or object when standing back up.")]
    [Min(0f)] [SerializeField] private float recoveryClearance = 0.05f;

    [Tooltip("Maximum distance the recovery may lift the player to escape an overlap.")]
    [Min(0f)] [SerializeField] private float maximumRecoveryLift = 5f;

    [Tooltip("How far above the ragdoll to begin searching for a supporting surface.")]
    [Min(1f)] [SerializeField] private float groundSearchHeight = 30f;

    private bool isRagdoll;
    private Vector3 savedRootPosition;
    private Quaternion savedRootRotation;
    private Vector3 savedBodyCenter;
    private float savedLowestBodyY;
    private readonly Dictionary<Behaviour, bool> savedBehaviourStates = new Dictionary<Behaviour, bool>();
    private readonly Dictionary<Rigidbody, RigidbodyState> savedBodyStates = new Dictionary<Rigidbody, RigidbodyState>();

    private struct RigidbodyState
    {
        public bool isKinematic;
        public RigidbodyConstraints constraints;
        public CollisionDetectionMode collisionDetectionMode;
        public Vector3 position;
        public Quaternion rotation;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            if (isRagdoll) RestoreControls();
            else ActivateRagdoll();
        }
    }

    private void FixedUpdate()
    {
        if (!isRagdoll || ragdollGravityScale >= 1f)
        {
            return;
        }

        // The regular controller supplies stabilizing spring forces while in
        // the air. Once it is disabled, the particle cloud receives raw
        // world gravity and visibly drops faster. Counteract only the excess
        // gravity here, so collisions and the remaining ragdoll physics stay
        // completely physical.
        Vector3 compensation = Physics.gravity * (ragdollGravityScale - 1f);
        foreach (Rigidbody body in savedBodyStates.Keys)
        {
            if (body != null && !body.isKinematic && body.useGravity)
            {
                body.AddForce(compensation, ForceMode.Acceleration);
            }
        }
    }

    /// <summary>Can also be called by UI buttons, triggers, or other scripts.</summary>
    public void ActivateRagdoll()
    {
        if (isRagdoll)
        {
            return;
        }

        isRagdoll = true;
        savedBehaviourStates.Clear();
        savedBodyStates.Clear();
        savedRootPosition = transform.position;
        savedRootRotation = transform.rotation;

        // Stop all forces that make the player walk, face the camera, or stand upright.
        SoftBodyPointPlayerController[] playerControllers =
            GetComponentsInChildren<SoftBodyPointPlayerController>(true);
        for (int i = 0; i < playerControllers.Length; i++)
        {
            SaveAndDisable(playerControllers[i]);
        }

        PlayerStandUpright[] uprightControllers =
            GetComponentsInChildren<PlayerStandUpright>(true);
        for (int i = 0; i < uprightControllers.Length; i++)
        {
            SaveAndDisable(uprightControllers[i]);
        }

        for (int i = 0; i < additionalBehavioursToDisable.Length; i++)
        {
            SaveAndDisable(additionalBehavioursToDisable[i]);
        }

        Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(true);
        Vector3 bodyCenter = Vector3.zero;
        int bodyCount = 0;
        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody body = bodies[i];
            savedBodyStates[body] = new RigidbodyState
            {
                isKinematic = body.isKinematic,
                constraints = body.constraints,
                collisionDetectionMode = body.collisionDetectionMode,
                position = body.position,
                rotation = body.rotation
            };
            bodyCenter += body.worldCenterOfMass;
            bodyCount++;
            body.isKinematic = false;
            // Speculative continuous collision also accounts for the fast
            // angular movement of an upside-down soft body. ContinuousDynamic
            // can still tunnel when a point rotates through a cube or plane.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            if (unfreezeRigidbodyRotations)
            {
                body.constraints &= ~RigidbodyConstraints.FreezeRotation;
            }
        }
        savedBodyCenter = bodyCount > 0 ? bodyCenter / bodyCount : transform.position;
        savedLowestBodyY = LowestBodyY(savedBodyStates.Keys, savedBodyCenter.y);
    }

    /// <summary>Returns to the exact controller and Rigidbody settings saved at ragdoll activation.</summary>
    public void RestoreControls()
    {
        if (!isRagdoll)
        {
            return;
        }

        Vector3 recoveryOffset = Vector3.zero;
        if (standUpOnRecovery)
        {
            Vector3 currentCenter = CurrentBodyCenter();
            recoveryOffset = currentCenter - savedBodyCenter;
            // Use a real surface hit rather than the fallen body's lowest
            // point, which may already be penetrating terrain or an object.
            if (TryFindSupportingSurface(currentCenter, out float surfaceY))
                recoveryOffset.y = surfaceY + recoveryClearance - savedLowestBodyY;
            else
                recoveryOffset.y = CurrentLowestBodyY() - savedLowestBodyY + recoveryClearance;
        }

        if (standUpOnRecovery)
        {
            // Keep the ragdoll's final location (for example, at the bottom of
            // a slope), but restore the upright shape and facing at that point.
            ApplyRecoveredPose(recoveryOffset);

            // The upright shape can be taller than a fallen body. Lift it out
            // of any floor, rock, or other collider before enabling physics.
            for (int pass = 0; pass < 4; pass++)
            {
                float requiredLift = FindRequiredRecoveryLift();
                if (requiredLift <= 0f) break;
                recoveryOffset.y += requiredLift;
                ApplyRecoveredPose(recoveryOffset);
            }
        }

        foreach (KeyValuePair<Rigidbody, RigidbodyState> entry in savedBodyStates)
        {
            if (entry.Key == null) continue;
            entry.Key.isKinematic = entry.Value.isKinematic;
            entry.Key.constraints = entry.Value.constraints;
            entry.Key.collisionDetectionMode = entry.Value.collisionDetectionMode;
        }

        if (standUpOnRecovery) Physics.SyncTransforms();

        SoftBodyPointPlayerController[] playerControllers =
            GetComponentsInChildren<SoftBodyPointPlayerController>(true);
        for (int i = 0; i < playerControllers.Length; i++)
        {
            playerControllers[i].SnapFeetAboveGround();
            playerControllers[i].ResetPoseTargets();
        }

        PlayerStandUpright[] uprightControllers =
            GetComponentsInChildren<PlayerStandUpright>(true);
        for (int i = 0; i < uprightControllers.Length; i++) uprightControllers[i].ResetPoseTargets();

        foreach (KeyValuePair<Behaviour, bool> entry in savedBehaviourStates)
        {
            if (entry.Key != null) entry.Key.enabled = entry.Value;
        }

        isRagdoll = false;
    }

    private void SaveAndDisable(Behaviour behaviour)
    {
        if (behaviour == null) return;
        savedBehaviourStates[behaviour] = behaviour.enabled;
        behaviour.enabled = false;
    }

    private Vector3 CurrentBodyCenter()
    {
        Vector3 center = Vector3.zero;
        int count = 0;
        foreach (Rigidbody body in savedBodyStates.Keys)
        {
            if (body == null) continue;
            center += body.worldCenterOfMass;
            count++;
        }
        return count > 0 ? center / count : transform.position;
    }

    private float CurrentLowestBodyY()
    {
        return LowestBodyY(savedBodyStates.Keys, transform.position.y);
    }

    private static float LowestBodyY(IEnumerable<Rigidbody> bodies, float fallback)
    {
        float lowestY = float.PositiveInfinity;
        foreach (Rigidbody body in bodies)
        {
            if (body == null) continue;
            Collider[] colliders = body.GetComponentsInChildren<Collider>();
            if (colliders.Length == 0)
            {
                lowestY = Mathf.Min(lowestY, body.worldCenterOfMass.y);
                continue;
            }
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && colliders[i].enabled)
                    lowestY = Mathf.Min(lowestY, colliders[i].bounds.min.y);
            }
        }
        return float.IsPositiveInfinity(lowestY) ? fallback : lowestY;
    }

    private void ApplyRecoveredPose(Vector3 offset)
    {
        transform.SetPositionAndRotation(savedRootPosition + offset, savedRootRotation);
        foreach (KeyValuePair<Rigidbody, RigidbodyState> entry in savedBodyStates)
        {
            if (entry.Key == null) continue;
            entry.Key.isKinematic = true;
            entry.Key.constraints = entry.Value.constraints;
            entry.Key.position = entry.Value.position + offset;
            entry.Key.rotation = entry.Value.rotation;
            entry.Key.linearVelocity = Vector3.zero;
            entry.Key.angularVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
    }

    private float FindRequiredRecoveryLift()
    {
        float lift = 0f;
        foreach (Rigidbody body in savedBodyStates.Keys)
        {
            if (body == null) continue;
            Collider[] ownColliders = body.GetComponentsInChildren<Collider>();
            for (int i = 0; i < ownColliders.Length; i++)
            {
                Collider own = ownColliders[i];
                if (own == null || !own.enabled || own.isTrigger) continue;

                Collider[] overlaps = Physics.OverlapBox(
                    own.bounds.center,
                    own.bounds.extents,
                    own.transform.rotation,
                    ~0,
                    QueryTriggerInteraction.Ignore);
                for (int j = 0; j < overlaps.Length; j++)
                {
                    Collider other = overlaps[j];
                    if (other == null || other.transform == transform || other.transform.IsChildOf(transform)) continue;

                    if (Physics.ComputePenetration(
                        own, own.transform.position, own.transform.rotation,
                        other, other.transform.position, other.transform.rotation,
                        out Vector3 separationDirection, out float separationDistance))
                    {
                        // Raising the entire character is stable for floors,
                        // slopes, and obstacles, even when their normal is not up.
                        float upwardDistance = separationDirection.y > 0.01f
                            ? separationDistance / separationDirection.y
                            : separationDistance;
                        lift = Mathf.Max(lift, upwardDistance + recoveryClearance);
                    }
                }
            }
        }
        return Mathf.Min(lift, maximumRecoveryLift);
    }

    private bool TryFindSupportingSurface(Vector3 aroundPosition, out float surfaceY)
    {
        Vector3 rayStart = new Vector3(
            aroundPosition.x,
            aroundPosition.y + groundSearchHeight,
            aroundPosition.z);
        RaycastHit[] hits = Physics.RaycastAll(
            rayStart,
            Vector3.down,
            groundSearchHeight * 2f,
            ~0,
            QueryTriggerInteraction.Ignore);

        surfaceY = float.NegativeInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null || collider.transform == transform || collider.transform.IsChildOf(transform))
                continue;
            // The highest non-player hit is the surface the restored player
            // should stand on. It works for terrain, platforms, and props.
            surfaceY = Mathf.Max(surfaceY, hits[i].point.y);
        }
        return !float.IsNegativeInfinity(surfaceY);
    }
}

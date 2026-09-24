using UnityEngine;

/// <summary>
/// Makes a visual-only prop follow a soft-body player without inheriting the
/// player's root rotation. Add this to a prop such as a fake shoe, hat, or
/// held display object, then assign its player (or leave it blank when the
/// prop starts somewhere under that player's hierarchy).
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public class SoftBodyStationaryDisplayObject : MonoBehaviour
{
    [Header("Follow Target")]
    [Tooltip("The soft-body player to follow. This is discovered from the parent hierarchy when possible.")]
    public SoftBodyPointPlayerController player;

    [Tooltip("World-space offset from the centre of the soft-body. World space is intentional: camera/player yaw will not orbit this prop.")]
    public Vector3 worldOffset;

    [Tooltip("Use the prop's starting world-space offset from the player centre when play begins.")]
    public bool captureStartingWorldOffset = true;

    [Header("Stationary Appearance")]
    [Tooltip("Keeps the prop's starting world rotation, even when the player turns.")]
    public bool lockWorldRotation = true;

    [Tooltip("Keeps the prop at the same vertical offset from the soft-body centre as it moves or jumps.")]
    public bool followVerticalPosition = true;

    Quaternion lockedWorldRotation;
    float lockedWorldY;
    bool initialized;

    void Awake()
    {
        if (player == null) player = GetComponentInParent<SoftBodyPointPlayerController>();

        // A child transform always inherits its parent's rotation before this
        // script can correct it, causing the visible orbit described above.
        // Keep the current world pose while removing that inheritance.
        transform.SetParent(null, true);
        lockedWorldRotation = transform.rotation;
        lockedWorldY = transform.position.y;
    }

    void LateUpdate()
    {
        if (player == null) player = FindFirstObjectByType<SoftBodyPointPlayerController>();
        if (player == null) return;

        Vector3 centre = player.CameraFocusPoint;
        if (!initialized)
        {
            if (captureStartingWorldOffset)
                worldOffset = transform.position - centre;
            initialized = true;
        }

        Vector3 desiredPosition = centre + worldOffset;
        if (!followVerticalPosition) desiredPosition.y = lockedWorldY;
        transform.position = desiredPosition;

        if (lockWorldRotation) transform.rotation = lockedWorldRotation;
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Mouse-orbit third-person camera that follows a soft-body player.</summary>
[RequireComponent(typeof(Camera))]
public class ThirdPersonCameraController : MonoBehaviour
{
    [Header("Target")]
    public SoftBodyPointPlayerController player;
    public Transform fallbackTarget;
    [Min(0f)] public float focusHeight = 0.55f;

    [Header("Orbit")]
    [Min(0.5f)] public float distance = 4.5f;
    [Range(-80f, 80f)] public float pitch = 18f;
    public float yaw = 180f;
    [Min(0.01f)] public float mouseSensitivity = 0.14f;
    [Range(-85f, 0f)] public float minPitch = -35f;
    [Range(0f, 85f)] public float maxPitch = 60f;
    [Min(0f)] public float followSharpness = 12f;

    void Start()
    {
        // Keep the orbit camera independent from any rotation/scale on the
        // imported player mesh hierarchy.
        transform.SetParent(null, true);
        LockCursor();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // A normal click restores mouse-look after Escape releases the cursor.
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            LockCursor();
    }

    void LateUpdate()
    {
        Vector3 focus = player != null ? player.CameraFocusPoint : fallbackTarget != null ? fallbackTarget.position : transform.position;
        focus += Vector3.up * focusHeight;

        Mouse mouse = Mouse.current;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, minPitch, maxPitch);
        }

        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredPosition = focus - orbit * Vector3.forward * distance;
        float blend = 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, blend);
        transform.rotation = orbit;
    }

    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}

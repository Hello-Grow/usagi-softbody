using UnityEngine;

public class TimeController : MonoBehaviour
{
    [Header("Time Toggle Settings")]
    [Tooltip("Press this key to toggle slow motion on/off.")]
    public KeyCode toggleKey = KeyCode.T;

    [Header("Time Scale Limits")]
    [Tooltip("The default slow motion scale when toggled on.")]
    public float currentSlowTimeScale = 0.3f;

    [Tooltip("Minimum allowed slow motion scale.")]
    public float minTimeScale = 0.05f;

    [Tooltip("Maximum allowed time scale in slow motion mode.")]
    public float maxTimeScale = 1.0f;

    [Header("Scroll Sensitivity")]
    [Tooltip("How fast scrolling adjusts the time scale.")]
    public float scrollSensitivity = 0.1f;

    private bool isSlowed = false;
    private float defaultFixedDeltaTime;

    void Start()
    {
        // Store the default fixedDeltaTime for physics consistency
        defaultFixedDeltaTime = Time.fixedDeltaTime;
    }

    void Update()
    {
        // Toggle slow motion mode
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleTimeMode();
        }

        // Only allow scroll adjustments while slow motion is active
        if (isSlowed)
        {
            HandleScrollInput();
        }
    }

    void ToggleTimeMode()
    {
        isSlowed = !isSlowed;

        if (isSlowed)
        {
            SetTimeScale(currentSlowTimeScale);
        }
        else
        {
            SetTimeScale(1.0f);
        }
    }

    void HandleScrollInput()
    {
        // Get raw scroll wheel delta (returns positive for scrolling up, negative for down)
        float scrollDelta = Input.GetAxis("Mouse ScrollWheel");

        if (scrollDelta != 0f)
        {
            // Adjust and clamp the slow time scale within boundaries
            currentSlowTimeScale += scrollDelta * scrollSensitivity;
            currentSlowTimeScale = Mathf.Clamp(currentSlowTimeScale, minTimeScale, maxTimeScale);

            // Apply the new scale immediately
            SetTimeScale(currentSlowTimeScale);
        }
    }

    void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = defaultFixedDeltaTime * Time.timeScale;
    }
}
using UnityEngine;

/// <summary>
/// Activates a player's <see cref="RagdollOnR"/> component when this object
/// touches it. Works with ordinary colliders and trigger colliders.
/// </summary>
[DisallowMultipleComponent]
public class ActivateRagdollOnTouch : MonoBehaviour
{
    [Tooltip("Only objects on these layers can activate ragdoll. Leave as Everything to allow all layers.")]
    [SerializeField] private LayerMask targetLayers = ~0;

    [Tooltip("Optional tag filter. Leave empty to ragdoll any object that has RagdollOnR in its hierarchy.")]
    [SerializeField] private string requiredTag = "";

    [Tooltip("When enabled, this touch object can activate only one ragdoll before it is disabled.")]
    [SerializeField] private bool disableAfterActivation;

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider != null)
            TryActivate(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryActivate(other);
    }

    private void TryActivate(Collider other)
    {
        if (other == null || !IsAllowed(other.gameObject)) return;

        RagdollOnR ragdoll = other.GetComponentInParent<RagdollOnR>();
        if (ragdoll == null) return;

        ragdoll.ActivateRagdoll();

        if (disableAfterActivation)
            enabled = false;
    }

    private bool IsAllowed(GameObject candidate)
    {
        if ((targetLayers.value & (1 << candidate.layer)) == 0) return false;
        return string.IsNullOrEmpty(requiredTag) || candidate.CompareTag(requiredTag);
    }
}

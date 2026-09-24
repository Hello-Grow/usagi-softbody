using UnityEngine;

/// <summary>
/// Inspector-only leg grouping for an EmbeddedSoftBody mesh.
/// Drag child transforms from SoftBodyPoints into each array. This component
/// deliberately applies no forces or animation.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EmbeddedSoftBody))]
public class SoftBodyLegAssignments : MonoBehaviour
{
    [Header("Assign SoftBodyPoints manually")]
    [Tooltip("Points that belong to the character's left leg.")]
    public Transform[] leftLegPoints;

    [Tooltip("Points that belong to the character's right leg.")]
    public Transform[] rightLegPoints;
}

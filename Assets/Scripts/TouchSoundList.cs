using UnityEngine;

/// <summary>
/// Stores the sound choices for one touchable object. The sound controller
/// plays the selected clip through its assigned AudioSource.
/// </summary>
[DisallowMultipleComponent]
public class TouchSoundList : MonoBehaviour
{
    [Tooltip("Sounds the player can randomly play after touching this object. Empty entries are ignored.")]
    [SerializeField] private AudioClip[] touchSounds;

    public AudioClip GetRandomClip()
    {
        if (touchSounds == null || touchSounds.Length == 0)
            return null;

        int startIndex = Random.Range(0, touchSounds.Length);
        for (int offset = 0; offset < touchSounds.Length; offset++)
        {
            AudioClip clip = touchSounds[(startIndex + offset) % touchSounds.Length];
            if (clip != null)
                return clip;
        }

        return null;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider != null)
            RandomTouchSoundPlayer.HandleTouchableObjectContact(this, collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        RandomTouchSoundPlayer.HandleTouchableObjectContact(this, other);
    }
}

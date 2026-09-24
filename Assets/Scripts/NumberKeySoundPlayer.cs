using UnityEngine;

/// <summary>
/// Plays the sound assigned to each number key. Assign clips in the Inspector:
/// element 0 is key 1, element 8 is key 9, and element 9 is key 0.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class NumberKeySoundPlayer : MonoBehaviour
{
    [Tooltip("Sound clips for keys 1-9 then 0. Empty slots do nothing.")]
    [SerializeField] private AudioClip[] soundClips = new AudioClip[10];

    private AudioSource audioSource;

    private static readonly KeyCode[] NumberKeys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
        KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0
    };

    private static readonly KeyCode[] KeypadKeys =
    {
        KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5,
        KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9, KeyCode.Keypad0
    };

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    private void Update()
    {
        for (int index = 0; index < NumberKeys.Length; index++)
        {
            if (Input.GetKeyDown(NumberKeys[index]) || Input.GetKeyDown(KeypadKeys[index]))
            {
                PlaySound(index);
            }
        }
    }

    private void PlaySound(int index)
    {
        if (index >= soundClips.Length || soundClips[index] == null)
        {
            return;
        }

        // PlayOneShot lets an already-playing clip finish when another key is pressed.
        audioSource.PlayOneShot(soundClips[index]);
    }
}

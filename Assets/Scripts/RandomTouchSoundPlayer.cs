using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Plays a random sound from the <see cref="TouchSoundList"/> on an object the
/// assigned player touches. Add this to the sound-follow object, then assign
/// the player in the Inspector.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class RandomTouchSoundPlayer : MonoBehaviour
{
    private static readonly List<RandomTouchSoundPlayer> ActivePlayers = new List<RandomTouchSoundPlayer>();

    [Tooltip("The player whose colliders should detect the touch. This can be different from the object holding this AudioSource.")]
    [SerializeField] private GameObject player;

    [Tooltip("Minimum time between sounds, preventing repeated physics contacts from spamming audio.")]
    [Min(0f)]
    [SerializeField] private float cooldown = 0.1f;

    private AudioSource audioSource;
    private float nextPlayTime;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        // Keep this source positioned with the player, so the effect is 3D.
        audioSource.spatialBlend = 1f;
        audioSource.mute = false;
        audioSource.volume = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        audioSource.minDistance = 1f;
        audioSource.maxDistance = 20f;
    }

    private void OnEnable()
    {
        if (!ActivePlayers.Contains(this))
            ActivePlayers.Add(this);
    }

    private void OnDisable()
    {
        ActivePlayers.Remove(this);
    }

    private void Start()
    {
        if (player == null)
        {
            Debug.LogWarning($"{nameof(RandomTouchSoundPlayer)} on '{name}' needs a Player assigned.", this);
            enabled = false;
            return;
        }

        // Soft-body players collide through colliders on their child points.
        // Give every assigned-player collider a relay so contacts reach this source.
        foreach (Collider playerCollider in player.GetComponentsInChildren<Collider>(true))
        {
            PlayerTouchSoundContactRelay relay = playerCollider.GetComponent<PlayerTouchSoundContactRelay>();
            if (relay == null)
                relay = playerCollider.gameObject.AddComponent<PlayerTouchSoundContactRelay>();

            relay.Initialize(this);
        }
    }

    private void TryPlay(Collider other)
    {
        if (other == null || Time.time < nextPlayTime)
            return;

        // The list belongs to the object the player touched. Searching parents
        // also supports objects whose collider is on a child GameObject.
        TouchSoundList soundList = other.GetComponentInParent<TouchSoundList>();
        if (soundList == null)
            return;

        Play(soundList);
    }

    private void Play(TouchSoundList soundList)
    {
        // PlayOneShot normally overlaps clips. Waiting here keeps each touch
        // sound complete and prevents a new one from cutting in mid-clip.
        if (soundList == null || audioSource.isPlaying)
            return;

        AudioClip clip = soundList.GetRandomClip();
        if (clip == null)
            return;

        audioSource.PlayOneShot(clip);
        nextPlayTime = Time.time + cooldown;
    }

    internal void HandleContact(Collider other)
    {
        TryPlay(other);
    }

    /// <summary>
    /// Called by a touchable object's collider. This path catches contacts even
    /// when a soft-body child Rigidbody, rather than the player root, owns it.
    /// </summary>
    internal static void HandleTouchableObjectContact(TouchSoundList soundList, Collider other)
    {
        if (soundList == null || other == null)
            return;

        for (int index = 0; index < ActivePlayers.Count; index++)
        {
            RandomTouchSoundPlayer soundPlayer = ActivePlayers[index];
            if (soundPlayer == null || soundPlayer.player == null)
                continue;

            bool belongsToPlayer = other.transform == soundPlayer.player.transform ||
                                   other.transform.IsChildOf(soundPlayer.player.transform);
            if (Time.time >= soundPlayer.nextPlayTime && !soundPlayer.audioSource.isPlaying && belongsToPlayer)
                soundPlayer.Play(soundList);
        }
    }
}

/// <summary>
/// Forwards contacts from a player's child collider to its root sound player.
/// This component is installed automatically at runtime.
/// </summary>
[DisallowMultipleComponent]
public class PlayerTouchSoundContactRelay : MonoBehaviour
{
    private RandomTouchSoundPlayer soundPlayer;

    public void Initialize(RandomTouchSoundPlayer player)
    {
        soundPlayer = player;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider != null)
            soundPlayer?.HandleContact(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        soundPlayer?.HandleContact(other);
    }
}

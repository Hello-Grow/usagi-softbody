using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays hit audio through this object's AudioSource when the assigned player
/// is struck by an object on a selected layer. Add this component to the
/// separate Sound GameObject, not to the player or the attacking object.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class PlayerHitSoundPlayer : MonoBehaviour
{
    [System.Serializable]
    public class HitSpeedSoundList
    {
        [Tooltip("This list is eligible when the collision's relative speed is at least this value. Higher matching thresholds take priority.")]
        [Min(0f)] public float minimumImpactSpeed;
        [Tooltip("One non-empty sound is selected at random from this list.")]
        public AudioClip[] sounds;
    }

    [System.Serializable]
    public class HitFrequencySoundList
    {
        [Tooltip("This list is eligible after this many valid hits occur within Hit Frequency Window. Higher matching thresholds take priority.")]
        [Min(1)] public int minimumHits = 2;
        [Tooltip("One non-empty sound is selected at random from this list.")]
        public AudioClip[] sounds;
    }

    [Header("Target")]
    [Tooltip("The player that receives hits. Its child colliders are detected automatically.")]
    [SerializeField] private GameObject player;
    [Tooltip("Only hits from colliders on these layers can produce a sound.")]
    [SerializeField] private LayerMask hitLayers = ~0;
    [Tooltip("Ignores collision-enter events slower than this relative speed.")]
    [Min(0f)] [SerializeField] private float minimumImpactSpeed = 0.5f;

    [Header("Sound Lists")]
    [Tooltip("Normal sound lists selected by impact speed. Set increasing minimum speeds, such as 0, 5, and 12.")]
    [SerializeField] private HitSpeedSoundList[] hitSpeedSounds = new HitSpeedSoundList[0];
    [Tooltip("Rapid-hit sound lists. A matching rapid-hit list overrides the speed list.")]
    [SerializeField] private HitFrequencySoundList[] hitFrequencySounds = new HitFrequencySoundList[0];
    [Tooltip("The rolling time period used for rapid-hit list thresholds.")]
    [Min(0.01f)] [SerializeField] private float hitFrequencyWindow = 1f;
    [Tooltip("Rejects immediate duplicate enter events from the same attacking collider.")]
    [Min(0f)] [SerializeField] private float sameColliderDebounce = 0.08f;

    [Tooltip("Loads every configured hit clip before gameplay so its first hit does not wait for audio-data loading.")]
    [SerializeField] private bool preloadAudio = true;

    private readonly List<float> hitTimes = new List<float>();
    private readonly Dictionary<int, float> lastHitTimeByCollider = new Dictionary<int, float>();
    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;

        if (preloadAudio)
            PreloadAudio();
    }

    private void Start()
    {
        if (player == null)
        {
            Debug.LogWarning($"{nameof(PlayerHitSoundPlayer)} on '{name}' needs a Player assigned.", this);
            enabled = false;
            return;
        }

        foreach (Collider playerCollider in player.GetComponentsInChildren<Collider>(true))
        {
            PlayerHitSoundContactRelay relay = playerCollider.GetComponent<PlayerHitSoundContactRelay>();
            if (relay == null)
                relay = playerCollider.gameObject.AddComponent<PlayerHitSoundContactRelay>();

            relay.Initialize(this);
        }
    }

    internal void HandleHit(Collision collision)
    {
        if (collision == null || collision.collider == null)
            return;

        Collider hitter = collision.collider;
        if ((hitLayers.value & (1 << hitter.gameObject.layer)) == 0)
            return;

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < minimumImpactSpeed)
            return;

        float now = Time.time;
        int hitterId = hitter.GetInstanceID();
        if (lastHitTimeByCollider.TryGetValue(hitterId, out float lastHitTime) &&
            now - lastHitTime < sameColliderDebounce)
            return;

        lastHitTimeByCollider[hitterId] = now;
        RecordHit(now);

        // Do not allow PlayOneShot to overlap or interrupt the current hit.
        if (audioSource.isPlaying)
            return;

        AudioClip clip = GetRandomClip(GetFrequencyList(), impactSpeed);
        if (clip != null)
            audioSource.PlayOneShot(clip);
    }

    private void RecordHit(float time)
    {
        hitTimes.Add(time);
        hitTimes.RemoveAll(hitTime => hitTime < time - hitFrequencyWindow);
    }

    private HitFrequencySoundList GetFrequencyList()
    {
        HitFrequencySoundList bestMatch = null;
        for (int i = 0; i < hitFrequencySounds.Length; i++)
        {
            HitFrequencySoundList candidate = hitFrequencySounds[i];
            if (candidate == null || hitTimes.Count < candidate.minimumHits || !HasClip(candidate.sounds))
                continue;

            if (bestMatch == null || candidate.minimumHits > bestMatch.minimumHits)
                bestMatch = candidate;
        }

        return bestMatch;
    }

    private AudioClip GetRandomClip(HitFrequencySoundList frequencyList, float impactSpeed)
    {
        if (frequencyList != null)
            return GetRandomClip(frequencyList.sounds);

        HitSpeedSoundList bestMatch = null;
        for (int i = 0; i < hitSpeedSounds.Length; i++)
        {
            HitSpeedSoundList candidate = hitSpeedSounds[i];
            if (candidate == null || impactSpeed < candidate.minimumImpactSpeed || !HasClip(candidate.sounds))
                continue;

            if (bestMatch == null || candidate.minimumImpactSpeed > bestMatch.minimumImpactSpeed)
                bestMatch = candidate;
        }

        return bestMatch == null ? null : GetRandomClip(bestMatch.sounds);
    }

    private static bool HasClip(AudioClip[] clips)
    {
        if (clips == null) return false;
        for (int i = 0; i < clips.Length; i++)
            if (clips[i] != null) return true;
        return false;
    }

    private static AudioClip GetRandomClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
            return null;

        int startIndex = Random.Range(0, clips.Length);
        for (int offset = 0; offset < clips.Length; offset++)
        {
            AudioClip clip = clips[(startIndex + offset) % clips.Length];
            if (clip != null) return clip;
        }

        return null;
    }

    private void PreloadAudio()
    {
        HashSet<AudioClip> clipsToLoad = new HashSet<AudioClip>();

        for (int i = 0; i < hitSpeedSounds.Length; i++)
            AddClips(hitSpeedSounds[i] == null ? null : hitSpeedSounds[i].sounds, clipsToLoad);

        for (int i = 0; i < hitFrequencySounds.Length; i++)
            AddClips(hitFrequencySounds[i] == null ? null : hitFrequencySounds[i].sounds, clipsToLoad);

        foreach (AudioClip clip in clipsToLoad)
            clip.LoadAudioData();
    }

    private static void AddClips(AudioClip[] clips, HashSet<AudioClip> destination)
    {
        if (clips == null) return;
        for (int i = 0; i < clips.Length; i++)
            if (clips[i] != null) destination.Add(clips[i]);
    }
}

/// <summary>Forwards collision-enter events from a player collider.</summary>
[DisallowMultipleComponent]
public class PlayerHitSoundContactRelay : MonoBehaviour
{
    private PlayerHitSoundPlayer soundPlayer;

    public void Initialize(PlayerHitSoundPlayer player)
    {
        soundPlayer = player;
    }

    // No OnCollisionStay: a resting contact does not create continuous hits.
    private void OnCollisionEnter(Collision collision)
    {
        soundPlayer?.HandleHit(collision);
    }
}


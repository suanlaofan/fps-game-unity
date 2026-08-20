using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class HorrorBgmPlayer : MonoBehaviour
{
    public AudioClip bgmClip;
    [Range(0f, 1f)] public float volume = 0.7f;
    [Min(1f)] public float volumeMultiplier = 4f;
    [Min(0f)] public float fadeInDuration = 0.8f;

    private AudioSource[] audioSources;
    private float fadeProgress;
    private bool playbackScheduled;

    public bool IsPlaying
    {
        get
        {
            if (audioSources == null)
            {
                return false;
            }

            foreach (AudioSource source in audioSources)
            {
                if (source != null && source.isPlaying)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public float CurrentVolume
    {
        get
        {
            float total = 0f;
            if (audioSources != null)
            {
                foreach (AudioSource source in audioSources)
                {
                    if (source != null)
                    {
                        total += source.volume;
                    }
                }
            }
            return total;
        }
    }

    public float EffectiveVolume => volume * volumeMultiplier;
    public int PlaybackVoiceCount => Mathf.Max(1, Mathf.CeilToInt(volumeMultiplier));

    private void Awake()
    {
        audioSources = new AudioSource[PlaybackVoiceCount];
        audioSources[0] = GetComponent<AudioSource>();
        for (int i = 1; i < audioSources.Length; i++)
        {
            audioSources[i] = gameObject.AddComponent<AudioSource>();
            audioSources[i].hideFlags = HideFlags.HideInInspector;
        }

        foreach (AudioSource source in audioSources)
        {
            ConfigureSource(source);
        }

        fadeProgress = fadeInDuration > 0f ? 0f : 1f;
        ApplyVoiceVolumes();
    }

    private void Start()
    {
        if (!playbackScheduled)
        {
            StartPlayback();
        }
    }

    /// <summary>
    /// Starts the synchronized BGM voices. Exposed so the Level 0 menu can keep
    /// the title screen silent and begin the soundtrack only after START GAME.
    /// </summary>
    public void StartPlayback()
    {
        if (bgmClip == null)
        {
            Debug.LogError("[HorrorBGM] No BGM clip is assigned.", this);
            return;
        }

        if (audioSources == null || audioSources.Length == 0)
        {
            // A flow controller can request playback before this component's
            // Awake on a fresh scene load. Awake will call StartPlayback again
            // once its runtime voices have been created.
            return;
        }

        StopPlayback();
        foreach (AudioSource source in audioSources)
        {
            ConfigureSource(source);
        }
        fadeProgress = fadeInDuration > 0f ? 0f : 1f;
        ApplyVoiceVolumes();

        double startTime = AudioSettings.dspTime + 0.1d;
        foreach (AudioSource source in audioSources)
        {
            source.PlayScheduled(startTime);
        }
        playbackScheduled = true;
        Debug.Log($"[HorrorBGM] Started '{bgmClip.name}', sourceVolume={volume:F2}, " +
                  $"multiplier={volumeMultiplier:F2}x, effectiveVolume={EffectiveVolume:F2}, " +
                  $"voices={audioSources.Length}, fadeIn={fadeInDuration:F2}s.", this);
    }

    /// <summary>
    /// Stops every runtime voice, including voices that have been scheduled but
    /// have not begun playing yet.
    /// </summary>
    public void StopPlayback()
    {
        playbackScheduled = false;
        fadeProgress = 0f;
        if (audioSources == null)
        {
            return;
        }

        foreach (AudioSource source in audioSources)
        {
            if (source != null)
            {
                source.Stop();
            }
        }
    }

    private void OnDisable()
    {
        StopPlayback();
    }

    private void Update()
    {
        if (!playbackScheduled || fadeProgress >= 1f)
        {
            return;
        }

        float speed = fadeInDuration > 0f ? 1f / fadeInDuration : 1f;
        fadeProgress = Mathf.MoveTowards(fadeProgress, 1f, speed * Time.unscaledDeltaTime);
        ApplyVoiceVolumes();
    }

    private void ConfigureSource(AudioSource source)
    {
        source.clip = bgmClip;
        source.playOnAwake = false;
        source.loop = true;
        source.mute = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.priority = 64;
        source.ignoreListenerPause = true;
    }

    private void ApplyVoiceVolumes()
    {
        float remainingGain = volumeMultiplier;
        foreach (AudioSource source in audioSources)
        {
            float voiceGain = Mathf.Clamp01(remainingGain);
            source.volume = volume * fadeProgress * voiceGain;
            remainingGain -= voiceGain;
        }
    }
}

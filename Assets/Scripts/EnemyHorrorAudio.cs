using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class EnemyHorrorAudio : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.64f;
    [Min(0f)] public float volumeMultiplier = 2f;
    [Min(0f)] public float chaseRoarMultiplier = 4f;
    [Min(0f)] public float hurtStingMultiplier = 4f;
    public float minDistance = 2f;
    public float maxDistance = 38f;

    [Header("Ange Horror Clips")]
    public AudioClip[] idleGrowls;
    public AudioClip attackClip;
    public AudioClip meleeImpactClip;
    public AudioClip hurtClip;
    public AudioClip deathClip;
    [Min(0.5f)] public float minIdleDelay = 3.5f;
    [Min(0.5f)] public float maxIdleDelay = 7.5f;

    private AudioSource audioSource;
    private AudioClip generatedAmbientClip;
    private AudioClip generatedAttackClip;
    private Coroutine idleRoutine;
    private bool isDead;
    private bool isChasing;

    public bool UsesExternalClips => HasIdleGrowls() && attackClip != null && hurtClip != null && deathClip != null;
    public float EffectiveVolume => volume * volumeMultiplier;
    public float EffectiveChaseVolume => EffectiveVolume * chaseRoarMultiplier;
    public float EffectiveHurtVolume => EffectiveVolume * hurtStingMultiplier;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 1f;
        audioSource.dopplerLevel = 0f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        audioSource.minDistance = minDistance;
        audioSource.maxDistance = maxDistance;
        audioSource.volume = volume;
        audioSource.priority = 32;

        if (attackClip == null || hurtClip == null || deathClip == null)
        {
            generatedAttackClip = CreateAttackClip();
        }
    }

    private void Start()
    {
        if (HasIdleGrowls())
        {
            audioSource.loop = false;
            idleRoutine = StartCoroutine(PlayIdleGrowls());
        }
        else
        {
            generatedAmbientClip = CreateAmbientClip();
            audioSource.clip = generatedAmbientClip;
            audioSource.loop = true;
            audioSource.Play();
        }
    }

    private void OnDisable()
    {
        if (idleRoutine != null)
        {
            StopCoroutine(idleRoutine);
            idleRoutine = null;
        }

        isChasing = false;
        if (audioSource != null)
        {
            audioSource.Stop();
        }
    }

    public void PlayAttackSting()
    {
        if (!isDead)
        {
            PlayOneShot(attackClip != null ? attackClip : generatedAttackClip, 0.95f);
        }
    }

    public void PlayHurtSting()
    {
        if (!isDead)
        {
            PlayOneShot(hurtClip != null ? hurtClip : generatedAttackClip, 0.72f * hurtStingMultiplier);
        }
    }

    public void SetChasing(bool chasing)
    {
        if (isDead || isChasing == chasing)
        {
            return;
        }

        isChasing = chasing;
        if (isChasing)
        {
            AudioClip clip = hurtClip != null ? hurtClip : GetRandomIdleGrowl();
            PlayOneShot(clip != null ? clip : generatedAttackClip, 0.95f * chaseRoarMultiplier);
        }
    }

    public void PlayMeleeImpact()
    {
        if (!isDead)
        {
            PlayOneShot(meleeImpactClip, 0.82f);
        }
    }

    public void PlayDeathSting()
    {
        if (audioSource == null || isDead)
        {
            return;
        }

        isDead = true;
        isChasing = false;
        if (idleRoutine != null)
        {
            StopCoroutine(idleRoutine);
            idleRoutine = null;
        }
        audioSource.loop = false;
        audioSource.Stop();
        audioSource.pitch = 0.82f;
        audioSource.volume = volume;
        AudioClip clip = deathClip != null ? deathClip : generatedAttackClip;
        audioSource.clip = null;
        if (clip != null)
        {
            audioSource.PlayOneShot(clip, volumeMultiplier);
        }
    }

    private void OnDestroy()
    {
        if (generatedAmbientClip != null)
        {
            Destroy(generatedAmbientClip);
        }
        if (generatedAttackClip != null)
        {
            Destroy(generatedAttackClip);
        }
    }

    private IEnumerator PlayIdleGrowls()
    {
        yield return new WaitForSeconds(Random.Range(1.5f, 3f));
        while (!isDead)
        {
            AudioClip clip = isChasing && hurtClip != null ? hurtClip : GetRandomIdleGrowl();
            if (clip != null)
            {
                float chaseGain = isChasing ? chaseRoarMultiplier : 1f;
                PlayOneShot(clip, Random.Range(0.5f, 0.72f) * chaseGain);
            }

            float delay = Random.Range(Mathf.Min(minIdleDelay, maxIdleDelay), Mathf.Max(minIdleDelay, maxIdleDelay));
            yield return new WaitForSeconds((clip != null ? clip.length : 0f) + delay);
        }
    }

    private void PlayOneShot(AudioClip clip, float volumeScale)
    {
        if (clip == null || audioSource == null)
        {
            return;
        }

        audioSource.pitch = 1f;
        audioSource.PlayOneShot(clip, volumeScale * volumeMultiplier);
    }

    private bool HasIdleGrowls()
    {
        if (idleGrowls == null)
        {
            return false;
        }

        foreach (AudioClip clip in idleGrowls)
        {
            if (clip != null)
            {
                return true;
            }
        }
        return false;
    }

    private AudioClip GetRandomIdleGrowl()
    {
        if (!HasIdleGrowls())
        {
            return null;
        }

        int start = Random.Range(0, idleGrowls.Length);
        for (int offset = 0; offset < idleGrowls.Length; offset++)
        {
            AudioClip clip = idleGrowls[(start + offset) % idleGrowls.Length];
            if (clip != null)
            {
                return clip;
            }
        }
        return null;
    }

    private static AudioClip CreateAmbientClip()
    {
        const int sampleRate = 22050;
        const float duration = 8f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float pulse = 0.42f + 0.58f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.25f * t), 3f);
            float drone = Mathf.Sin(2f * Mathf.PI * 43f * t + 1.8f * Mathf.Sin(2f * Mathf.PI * 0.25f * t));
            float growl = Mathf.Sin(2f * Mathf.PI * 61f * t + 2.4f * Mathf.Sin(2f * Mathf.PI * 0.375f * t));
            float breath = Mathf.Sin(2f * Mathf.PI * 113f * t) * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.5f * t), 5f);
            samples[i] = Mathf.Clamp((drone * 0.34f + growl * 0.22f + breath * 0.12f) * pulse, -0.72f, 0.72f);
        }

        AudioClip clip = AudioClip.Create("Enemy Horror Drone", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static AudioClip CreateAttackClip()
    {
        const int sampleRate = 22050;
        const float duration = 0.9f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float normalized = t / duration;
            float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(normalized));
            float frequency = Mathf.Lerp(170f, 48f, normalized);
            float rasp = Mathf.Sin(2f * Mathf.PI * frequency * t + 5f * Mathf.Sin(2f * Mathf.PI * 31f * t));
            float grit = Mathf.Sin(2f * Mathf.PI * 389f * t) * Mathf.Sin(2f * Mathf.PI * 73f * t);
            samples[i] = Mathf.Clamp((rasp * 0.55f + grit * 0.24f) * envelope, -0.9f, 0.9f);
        }

        AudioClip clip = AudioClip.Create("Enemy Horror Attack", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}

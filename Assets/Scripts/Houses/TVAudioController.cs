using UnityEngine;

// 1. play audio when TV on, generate sound if no clip assigned
// 2. muffle sound through walls if occlusion enabled
[RequireComponent(typeof(HouseInteractable))]
public class TVAudioController : MonoBehaviour
{
    [SerializeField] AudioClip tvClip;
    [SerializeField, Range(0f, 1f)] float volume = 0.8f;

    [Header("Occlusion (optional)")]
    [SerializeField] bool useOcclusion = true;
    [SerializeField, Range(0f, 1f)] float occludedVolumeScale = 0.55f;
    [SerializeField] float occludedCutoff = 1200f;
    [SerializeField] float openCutoff = 8000f;
    [SerializeField] LayerMask occlusionMask = ~0;

    static AudioClip placeholderClip;

    HouseInteractable house;
    AudioSource source;
    AudioLowPassFilter lowPass;
    Transform listener;
    // 0 = clear line, 1 = fully blocked (smoothed)
    float occlusion;

    void Start()
    {
        house = GetComponent<HouseInteractable>();
        source = house.TvAudioSource;
        if (source == null || !house.TvIsPlaying) return;

        source.clip = tvClip != null ? tvClip : GetPlaceholderClip();
        source.loop = true;
        source.volume = volume;
        // Small per-house variation so neighbouring TVs don't sound identical or phase together.
        source.pitch = Random.Range(0.9f, 1.1f);
        source.time = Random.Range(0f, source.clip.length);
        source.Play();

        if (useOcclusion)
        {
            lowPass = source.GetComponent<AudioLowPassFilter>();
            if (lowPass == null) lowPass = source.gameObject.AddComponent<AudioLowPassFilter>();
            var audioListener = FindFirstObjectByType<AudioListener>();
            if (audioListener != null) listener = audioListener.transform;
        }
    }

    void Update()
    {
        if (lowPass == null || listener == null || !source.isPlaying) return;

        // The source sits just outside the front door, so standing in front is "clear"
        // and standing behind or beside the house is blocked by the house itself.
        bool blocked = false;
        foreach (var hit in Physics.RaycastAll(listener.position, source.transform.position - listener.position,
                     Vector3.Distance(listener.position, source.transform.position), occlusionMask, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.CompareTag("Player")) { blocked = true; break; }
        }

        occlusion = Mathf.MoveTowards(occlusion, blocked ? 1f : 0f, Time.deltaTime * 3f);
        source.volume = volume * Mathf.Lerp(1f, occludedVolumeScale, occlusion);
        lowPass.cutoffFrequency = Mathf.Lerp(openCutoff, occludedCutoff, occlusion);
    }

    // make shared 6-second muffled TV loop
    static AudioClip GetPlaceholderClip()
    {
        if (placeholderClip != null) return placeholderClip;

        const int sampleRate = 22050;
        const float length = 6f;
        int count = (int)(sampleRate * length);
        var data = new float[count];
        var rng = new System.Random(1234);

        float noiseState = 0f;
        float syllableEnd = 0f, syllablePitch = 140f, syllableGain = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)sampleRate;

            // new syllable every 0.12 to 0.3 seconds, pause between phrases
            if (t >= syllableEnd)
            {
                float duration = 0.12f + (float)rng.NextDouble() * 0.18f;
                syllableEnd = t + duration;
                syllablePitch = 110f + (float)rng.NextDouble() * 90f;
                syllableGain = rng.NextDouble() < 0.15 ? 0f : 0.6f + (float)rng.NextDouble() * 0.4f;
            }
            float syllablePhase = 1f - (syllableEnd - t) / 0.3f;
            float envelope = syllableGain * Mathf.Sin(Mathf.Clamp01(syllablePhase) * Mathf.PI);

            // Voice: fundamental plus two rough formants.
            float voice = Mathf.Sin(2f * Mathf.PI * syllablePitch * t) * 0.5f
                        + Mathf.Sin(2f * Mathf.PI * syllablePitch * 3.7f * t) * 0.25f
                        + Mathf.Sin(2f * Mathf.PI * syllablePitch * 8.3f * t) * 0.1f;

            // Low-passed noise bed (one-pole filter) for the "through a wall" hiss.
            float white = (float)rng.NextDouble() * 2f - 1f;
            noiseState += (white - noiseState) * 0.08f;

            // Quiet background music: slow alternating chord tones.
            float chord = (int)(t / 1.5f) % 2 == 0 ? 220f : 196f;
            float music = Mathf.Sin(2f * Mathf.PI * chord * t) * 0.08f;

            data[i] = Mathf.Clamp(voice * envelope * 0.5f + noiseState * 0.35f + music, -1f, 1f);
        }

        // Crossfade the last 0.1 s into the start so the loop has no click.
        int fade = sampleRate / 10;
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            data[count - fade + i] = Mathf.Lerp(data[count - fade + i], data[i], k);
        }

        placeholderClip = AudioClip.Create("TV_Placeholder", count, 1, sampleRate, false);
        placeholderClip.SetData(data, 0);
        return placeholderClip;
    }
}

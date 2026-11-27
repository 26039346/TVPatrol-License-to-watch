using UnityEngine;

// make a short bark sound if no clip assigned
public static class BarkClip
{
    static AudioClip clip;

    public static AudioClip Get()
    {
        if (clip != null) return clip;

        const int sampleRate = 22050;
        int count = (int)(sampleRate * 0.25f);
        var data = new float[count];
        var rng = new System.Random(42);
        float phase = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)sampleRate;
            float progress = i / (float)count;
            float pitch = Mathf.Lerp(650f, 280f, progress);
            phase += 2f * Mathf.PI * pitch / sampleRate;

            float envelope = Mathf.Min(1f, t / 0.01f) * Mathf.Pow(1f - progress, 2f);
            // harsh harmonics
            float tone = Mathf.Sign(Mathf.Sin(phase)) * 0.4f + Mathf.Sin(phase * 2f) * 0.3f;
            float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.3f;
            data[i] = (tone + noise) * envelope * 0.8f;
        }

        clip = AudioClip.Create("Bark_Placeholder", count, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}

using UnityEngine;

// make two metallic clicks if no resident warning clip assigned
public static class WarningClip
{
    static AudioClip clip;

    public static AudioClip Get()
    {
        if (clip != null) return clip;

        const int sampleRate = 22050;
        int count = (int)(sampleRate * 0.6f);
        var data = new float[count];
        var rng = new System.Random(7);

        // Two clicks: one at 0 s ("chk") and one at 0.22 s ("chk"), each a noise burst plus a metallic ring.
        AddClick(data, sampleRate, rng, 0.00f, 2400f);
        AddClick(data, sampleRate, rng, 0.22f, 1800f);

        clip = AudioClip.Create("Warning_Placeholder", count, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static void AddClick(float[] data, int sampleRate, System.Random rng, float start, float ringHz)
    {
        int begin = (int)(start * sampleRate);
        int length = (int)(0.12f * sampleRate);
        for (int i = 0; i < length && begin + i < data.Length; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t * 45f);
            float noise = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 120f);
            float ring = Mathf.Sin(2f * Mathf.PI * ringHz * t) * 0.5f;
            data[begin + i] += (noise * 0.8f + ring) * envelope * 0.9f;
        }
    }
}

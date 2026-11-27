using System;
using UnityEngine;

// sound types opponents react to
public enum StimulusKind
{
    // player footsteps, sprinting louder than walking
    Footsteps,
    // dog bark or resident warning about player
    Alarm
}

// sound event heard by Perception components within radius
public readonly struct Stimulus
{
    public readonly StimulusKind Kind;
    // where the sound came from
    public readonly Vector3 Position;
    // how far it carries
    public readonly float Radius;
    // 1. alarms use last seen player position
    // 2. footsteps use sound position
    public readonly Vector3 TargetPosition;
    // sound source, ignore own sounds
    public readonly GameObject Source;

    public Stimulus(StimulusKind kind, Vector3 position, float radius, Vector3 targetPosition, GameObject source)
    {
        Kind = kind;
        Position = position;
        Radius = radius;
        TargetPosition = targetPosition;
        Source = source;
    }
}

// send sounds to listeners, each checks distance
public static class StimulusBus
{
    public static event Action<Stimulus> Emitted;

    public static void Emit(Stimulus stimulus) => Emitted?.Invoke(stimulus);

    // clear old subscribers on play if domain reload disabled
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => Emitted = null;
}

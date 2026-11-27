using System;
using UnityEngine;

// 1. sight, hearing and memory for dog and resident
// 2. see player inside view cone with clear sight, or within close range
// 3. hear sounds within radius times hearingMultiplier
// 4. remember last sensed position, confidence fades over memoryDurationSeconds
public class Perception : MonoBehaviour
{
    [Header("Sight")]
    [UnityEngine.Serialization.FormerlySerializedAs("sightRange")]
    [SerializeField] float viewDistance = 9f;
    [UnityEngine.Serialization.FormerlySerializedAs("fieldOfView")]
    [SerializeField, Range(10f, 360f)] float viewAngle = 140f;
    [UnityEngine.Serialization.FormerlySerializedAs("proximityRange")]
    [SerializeField] float closeDetectionRange = 3f;
    [SerializeField] float eyeHeight = 0.6f;
    [UnityEngine.Serialization.FormerlySerializedAs("sightBlockers")]
    [SerializeField] LayerMask visionBlockingLayers = ~0;

    [Header("Hearing")]
    // >1 increases hearing range
    [SerializeField] float hearingMultiplier = 1f;

    [Header("Sensory memory")]
    // seconds before forgetting sight or sound
    [UnityEngine.Serialization.FormerlySerializedAs("memorySpan")]
    [SerializeField] float memoryDurationSeconds = 6f;

    // fire when sound heard, ignore own sounds
    public event Action<Stimulus> SoundHeard;

    public float ViewDistance => viewDistance;
    public float ViewAngle => viewAngle;
    public float CloseDetectionRange => closeDetectionRange;

    // last seen position or position from heard sound
    public Vector3 LastKnownPlayerPosition { get; private set; }
    // true if seen, false if heard
    public bool WasLastSeen { get; private set; }
    public float SecondsSinceDetected => Time.time - lastDetectedTime;
    // seconds since player last seen, hearing does not reset this
    public float SecondsSinceSeen => Time.time - lastSeenTime;
    public bool HasRecentMemory => SecondsSinceDetected <= memoryDurationSeconds;
    // confidence fades from 1 to 0 over memoryDurationSeconds
    public float MemoryConfidence => HasRecentMemory ? 1f - SecondsSinceDetected / memoryDurationSeconds : 0f;
    public void ClearMemory() => lastDetectedTime = lastSeenTime = float.NegativeInfinity;

    float lastDetectedTime = float.NegativeInfinity;
    float lastSeenTime = float.NegativeInfinity;

    void Remember(Vector3 position, bool wasSeen)
    {
        LastKnownPlayerPosition = position;
        WasLastSeen = wasSeen;
        lastDetectedTime = Time.time;
        if (wasSeen) lastSeenTime = Time.time;
    }

    void OnEnable() => StimulusBus.Emitted += OnStimulus;
    void OnDisable() => StimulusBus.Emitted -= OnStimulus;

    void OnStimulus(Stimulus s)
    {
        if (s.Source == gameObject) return;
        if (Vector3.Distance(transform.position, s.Position) > s.Radius * hearingMultiplier) return;
        // keep fresh sighting instead of approximate sound position
        if (!(WasLastSeen && SecondsSinceDetected < 0.5f)) Remember(s.TargetPosition, false);
        SoundHeard?.Invoke(s);
    }

    // true if target visible, save sighting to memory
    public bool CanSee(Transform target)
    {
        bool seen = Sees(target);
        if (seen) Remember(target.position, true);
        return seen;
    }

    bool Sees(Transform target)
    {
        if (target == null) return false;
        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (distance <= closeDetectionRange) return true;
        if (distance > viewDistance) return false;
        if (Vector3.Angle(transform.forward, toTarget) > viewAngle * 0.5f) return false;

        // Anything solid between the eyes and the target's chest blocks sight.
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 chest = target.position + Vector3.up * 1f;
        if (Physics.Linecast(eye, chest, out RaycastHit hit, visionBlockingLayers, QueryTriggerInteraction.Ignore))
            return hit.transform == target || hit.transform.IsChildOf(target);
        return true;
    }

    void OnDrawGizmosSelected()
    {
        Vector3 p = transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(p, viewDistance);
        Gizmos.color = new Color(1f, 0.6f, 0f);
        Gizmos.DrawWireSphere(p, closeDetectionRange);

        Vector3 left = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * transform.forward;
        Vector3 right = Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * transform.forward;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(p, p + left * viewDistance);
        Gizmos.DrawLine(p, p + right * viewDistance);

        // Memory: a sphere at the remembered position, solid for sight and wire for hearing.
        if (Application.isPlaying && HasRecentMemory)
        {
            Gizmos.color = new Color(1f, 0f, 1f, MemoryConfidence);
            if (WasLastSeen) Gizmos.DrawSphere(LastKnownPlayerPosition, 0.3f);
            else Gizmos.DrawWireSphere(LastKnownPlayerPosition, 0.5f);
        }
    }
}

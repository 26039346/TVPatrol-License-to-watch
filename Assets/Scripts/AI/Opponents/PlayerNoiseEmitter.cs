using UnityEngine;

// 1. make footstep sounds based on player speed
// 2. sprinting heard further away than walking
[RequireComponent(typeof(PlayerController))]
public class PlayerNoiseEmitter : MonoBehaviour
{
    [SerializeField] float walkNoiseRadius = 3f;
    [SerializeField] float sprintNoiseRadius = 12f;
    // walk speed is 4, sprint is 6.5
    [SerializeField] float sprintSpeedThreshold = 4.5f;
    [SerializeField] float minimumMovingSpeed = 0.5f;
    [UnityEngine.Serialization.FormerlySerializedAs("stepInterval")]
    [SerializeField] float stepIntervalSeconds = 0.35f;

    PlayerController controller;
    float nextFootstepTime;

    // last footstep radius for HUD, 0 if standing still
    public float CurrentNoiseRadius { get; private set; }

    void Awake() => controller = GetComponent<PlayerController>();

    void Update()
    {
        float speed = controller.CurrentSpeed;
        if (speed < minimumMovingSpeed)
        {
            CurrentNoiseRadius = 0f;
            return;
        }

        CurrentNoiseRadius = speed >= sprintSpeedThreshold ? sprintNoiseRadius : walkNoiseRadius;
        if (Time.time < nextFootstepTime) return;
        nextFootstepTime = Time.time + stepIntervalSeconds;

        StimulusBus.Emit(new Stimulus(StimulusKind.Footsteps, transform.position, CurrentNoiseRadius, transform.position, gameObject));
    }

    void OnDrawGizmosSelected()
    {
        if (CurrentNoiseRadius <= 0f) return;
        Gizmos.color = new Color(0.4f, 0.8f, 1f);
        Gizmos.DrawWireSphere(transform.position, CurrentNoiseRadius);
    }
}

using System;
using UnityEngine;
using UnityEngine.InputSystem;

// 1. hold E to inspect nearest unchecked house
// 2. inspection makes noise, cancel if released, too far away or hurt
// 3. InspectionManager handles result
public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] InputActionAsset inputActions;
    [SerializeField] float interactionRange = 2.5f;

    [Header("Timed check")]
    // seconds of holding E per house
    [SerializeField] float inspectDuration = 4f;
    // inspection noise radius
    [SerializeField] float inspectNoiseRadius = 5f;
    // seconds between inspection sounds
    [SerializeField] float inspectNoiseInterval = 1f;

    InputAction interactAction;
    PlayerHealth health;
    float nextNoiseTime;

    // current house target, null if none
    public HouseInteractable CurrentTarget { get; private set; }
    // true while inspecting house
    public bool IsInspecting { get; private set; }
    // inspection progress from 0 to 1
    public float InspectProgress { get; private set; }
    // fire with reason if inspection cancelled
    public event Action<string> InspectionCancelled;

    void Awake()
    {
        interactAction = inputActions.FindActionMap("Player", throwIfNotFound: true).FindAction("Interact", throwIfNotFound: true);
        health = GetComponent<PlayerHealth>();
    }

    void OnEnable()
    {
        interactAction.Enable();
        if (health != null) health.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (health != null) health.Damaged -= OnDamaged;
    }

    void Update()
    {
        // keep current house while inspecting, otherwise pick nearest
        if (!IsInspecting) CurrentTarget = FindNearestHouse();

        // IsPressed reads the key itself, ignoring the action's Hold interaction.
        bool holding = interactAction.IsPressed();

        if (IsInspecting)
        {
            if (!holding) { Cancel("Check abandoned"); return; }
            if (CurrentTarget == null || CurrentTarget.IsInspected || !InRange(CurrentTarget, 1.2f))
            {
                Cancel("Moved away from the door");
                return;
            }

            InspectProgress += Time.deltaTime / inspectDuration;
            EmitNoise();
            if (InspectProgress >= 1f)
            {
                var house = CurrentTarget;
                ResetCheck();
                if (InspectionManager.Instance != null) InspectionManager.Instance.TryInspect(house);
            }
        }
        // Start only on a fresh press, so holding E while walking up to a door doesn't auto-start.
        else if (CurrentTarget != null && interactAction.WasPressedThisFrame() && Time.timeScale > 0f)
        {
            IsInspecting = true;
            InspectProgress = 0f;
            // the first knock is heard immediately
            nextNoiseTime = Time.time;
        }
    }

    void EmitNoise()
    {
        if (Time.time < nextNoiseTime) return;
        nextNoiseTime = Time.time + inspectNoiseInterval;
        StimulusBus.Emit(new Stimulus(StimulusKind.Footsteps, transform.position, inspectNoiseRadius, transform.position, gameObject));
    }

    void OnDamaged(int remaining, string source)
    {
        if (IsInspecting) Cancel("Interrupted!");
    }

    void Cancel(string reason)
    {
        ResetCheck();
        InspectionCancelled?.Invoke(reason);
    }

    void ResetCheck()
    {
        IsInspecting = false;
        InspectProgress = 0f;
    }

    bool InRange(HouseInteractable house, float slack)
    {
        Vector3 offset = house.InspectionPosition - transform.position;
        offset.y = 0f;
        float range = interactionRange * slack;
        return offset.sqrMagnitude <= range * range;
    }

    HouseInteractable FindNearestHouse()
    {
        HouseInteractable nearest = null;
        float bestSqr = interactionRange * interactionRange;
        foreach (var house in HouseInteractable.All)
        {
            if (house.IsInspected) continue;
            Vector3 offset = house.InspectionPosition - transform.position;
            offset.y = 0f;
            float sqr = offset.sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                nearest = house;
            }
        }
        return nearest;
    }
}

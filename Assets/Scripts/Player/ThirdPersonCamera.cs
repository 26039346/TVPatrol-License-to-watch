using UnityEngine;
using UnityEngine.InputSystem;

// 1. rotate camera around player with mouse or right stick
// 2. pull camera closer if wall or fence in the way
public class ThirdPersonCamera : MonoBehaviour
{
    [SerializeField] InputActionAsset inputActions;
    [SerializeField] Transform target;

    [Header("Orbit")]
    [SerializeField] Vector3 targetOffset = new Vector3(0f, 1.6f, 0f);
    [SerializeField] float distance = 6f;
    [SerializeField] float mouseSensitivity = 0.12f;
    // degrees per second
    [SerializeField] float stickSensitivity = 120f;
    [SerializeField] float minPitch = 5f;
    [SerializeField] float maxPitch = 65f;

    [Header("Collision")]
    [SerializeField] float collisionRadius = 0.3f;
    [SerializeField] LayerMask collisionMask = ~0;

    InputAction lookAction;
    Transform listener;
    float yaw;
    float pitch = 20f;

    void Awake()
    {
        lookAction = inputActions.FindActionMap("Player", throwIfNotFound: true).FindAction("Look", throwIfNotFound: true);
        if (target != null) yaw = target.eulerAngles.y;

        // 1. hear from player head so camera distance does not affect TV range
        // 2. use camera rotation so left and right sound match screen
        var cameraListener = GetComponent<AudioListener>();
        // remove now so other Start calls find new listener
        if (cameraListener != null) DestroyImmediate(cameraListener);
        listener = new GameObject("PlayerAudioListener").transform;
        listener.gameObject.AddComponent<AudioListener>();
    }

    void OnEnable()
    {
        lookAction.Enable();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void LateUpdate()
    {
        if (target == null) return;
        // Leave the cursor free on the end-of-shift screen.
        if (ShiftManager.Instance != null && !ShiftManager.Instance.IsRunning) return;

        // 1. Escape releases cursor
        // 2. click game view to capture cursor
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            Cursor.lockState = CursorLockMode.None;
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            Cursor.lockState = CursorLockMode.Locked;

        Vector2 look = lookAction.ReadValue<Vector2>();
        // 1. mouse delta already per frame
        // 2. multiply gamepad stick input by deltaTime
        bool fromMouse = lookAction.activeControl != null && lookAction.activeControl.device is Mouse;
        float scale = fromMouse ? mouseSensitivity : stickSensitivity * Time.deltaTime;
        if (Cursor.lockState == CursorLockMode.Locked || !fromMouse)
        {
            yaw += look.x * scale;
            pitch = Mathf.Clamp(pitch - look.y * scale, minPitch, maxPitch);
        }

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + targetOffset;
        Vector3 desiredDirection = rotation * Vector3.back;

        float actualDistance = distance;
        if (Physics.SphereCast(pivot, collisionRadius, desiredDirection, out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore)
            && !hit.transform.IsChildOf(target))
        {
            actualDistance = Mathf.Max(hit.distance, 0.5f);
        }

        transform.SetPositionAndRotation(pivot + desiredDirection * actualDistance, rotation);
        listener.SetPositionAndRotation(pivot, Quaternion.Euler(0f, yaw, 0f));
    }
}

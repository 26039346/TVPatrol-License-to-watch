using UnityEngine;
using UnityEngine.InputSystem;

// 1. move player relative to camera using CharacterController
// 2. read Move and Sprint from Player input map
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] InputActionAsset inputActions;
    [SerializeField] Transform cameraTransform;

    [Header("Movement")]
    [SerializeField] float walkSpeed = 4f;
    [SerializeField] float sprintSpeed = 6.5f;
    [SerializeField] float acceleration = 12f;
    // degrees per second
    [SerializeField] float turnSpeed = 720f;
    [SerializeField] float gravity = -20f;

    CharacterController controller;
    InputAction moveAction;
    InputAction sprintAction;
    Vector3 horizontalVelocity;
    float verticalVelocity;

    public float CurrentSpeed => horizontalVelocity.magnitude;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        var playerMap = inputActions.FindActionMap("Player", throwIfNotFound: true);
        moveAction = playerMap.FindAction("Move", throwIfNotFound: true);
        sprintAction = playerMap.FindAction("Sprint", throwIfNotFound: true);
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    void OnEnable() => inputActions.FindActionMap("Player").Enable();
    void OnDisable() => inputActions.FindActionMap("Player").Disable();

    void Update()
    {
        Vector2 input = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);

        // Flatten the camera axes onto the ground plane.
        Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
        Vector3 right = cameraTransform != null ? cameraTransform.right : Vector3.right;
        forward.y = 0f;
        right.y = 0f;
        Vector3 moveDirection = (forward.normalized * input.y + right.normalized * input.x);

        float targetSpeed = sprintAction.IsPressed() ? sprintSpeed : walkSpeed;
        Vector3 targetVelocity = moveDirection * targetSpeed;
        // Frame-rate independent smoothing towards the target velocity.
        horizontalVelocity = Vector3.Lerp(horizontalVelocity, targetVelocity, 1f - Mathf.Exp(-acceleration * Time.deltaTime));

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        }

        // Small constant downward push while grounded keeps the controller snapped to the ground.
        verticalVelocity = controller.isGrounded ? -2f : verticalVelocity + gravity * Time.deltaTime;

        controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
    }
}

using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NetworkObject))]
public class PlayerController : NetworkBehaviour
{
    [SerializeField]
    private float moveSpeed = 4f;

    [SerializeField]
    private float sprintSpeed = 6f;

    [SerializeField]
    private float rotationSpeed = 720f;

    [SerializeField]
    private float acceleration = 24f;

    [SerializeField]
    private float deceleration = 30f;

    [SerializeField]
    private float directionChangeRate = 1440f;

    [SerializeField]
    private float motionSpeedMultiplier = 1f;

    [SerializeField]
    private float jumpHeight = 1.2f;

    [SerializeField]
    private float gravity = -15f;

    [SerializeField]
    private float jumpTimeout = 0.3f;

    [SerializeField]
    private float fallTimeout = 0.15f;

    private CharacterController characterController;
    private Animator characterAnimator;
    private Vector3 serverMoveInput;
    private Vector3 serverPlanarVelocity;
    private Vector3 previousPosition;
    private float visualSpeed;
    private float verticalVelocity;
    private float jumpTimeoutDelta;
    private float fallTimeoutDelta;
    private bool serverSprintHeld;
    private bool serverJumpRequested;

    private readonly NetworkVariable<bool> networkGrounded = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> networkJump = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> networkFreeFall = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int FreeFallHash = Animator.StringToHash("FreeFall");

    private const float TerminalVelocity = -53f;

    public float MoveSpeed => moveSpeed;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        characterAnimator = GetComponentInChildren<Animator>(true);
        jumpTimeoutDelta = jumpTimeout;
        fallTimeoutDelta = fallTimeout;
    }

    private void Update()
    {
        if (IsOwner)
        {
            ReadLocalInput();
        }

        if (IsServer)
        {
            ApplyServerMovement(Time.deltaTime);
        }

        UpdateMovementAnimation(Time.deltaTime);
    }

    public override void OnNetworkSpawn()
    {
        DisableEmbeddedLocalControllers();
        previousPosition = transform.position;
        jumpTimeoutDelta = jumpTimeout;
        fallTimeoutDelta = fallTimeout;
        enabled = true;
    }

    private void Reset()
    {
        if (TryGetComponent(out CharacterController controller))
        {
            controller.minMoveDistance = 0f;
        }
    }

    private void ReadLocalInput()
    {
        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        input = Vector2.ClampMagnitude(input, 1f);
        Vector3 moveDirection = ResolveMoveDirection(input);
        bool jumpPressed = Input.GetKeyDown(KeyCode.Space);
        bool sprintHeld = Input.GetKey(KeyCode.LeftShift);

        if (IsServer)
        {
            serverMoveInput = moveDirection;
            serverSprintHeld = sprintHeld;
            serverJumpRequested |= jumpPressed;
            return;
        }

        SubmitMovementServerRpc(moveDirection, jumpPressed, sprintHeld);
    }

    [ServerRpc]
    private void SubmitMovementServerRpc(Vector3 moveDirection, bool jumpPressed, bool sprintHeld)
    {
        serverMoveInput = Vector3.ClampMagnitude(new Vector3(moveDirection.x, 0f, moveDirection.z), 1f);
        serverSprintHeld = sprintHeld;
        serverJumpRequested |= jumpPressed;
    }

    private void ApplyServerMovement(float deltaTime)
    {
        if (characterController == null || deltaTime <= 0f)
        {
            return;
        }

        bool grounded = characterController.isGrounded;
        networkGrounded.Value = grounded;

        if (grounded)
        {
            fallTimeoutDelta = fallTimeout;
            networkFreeFall.Value = false;

            if (verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }

            if (serverJumpRequested && jumpTimeoutDelta <= 0f)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                networkJump.Value = true;
                networkGrounded.Value = false;
            }

            if (jumpTimeoutDelta > 0f)
            {
                jumpTimeoutDelta -= deltaTime;
            }
        }
        else
        {
            jumpTimeoutDelta = jumpTimeout;

            if (fallTimeoutDelta > 0f)
            {
                fallTimeoutDelta -= deltaTime;
            }
            else
            {
                networkFreeFall.Value = true;
            }
        }

        if (verticalVelocity > TerminalVelocity)
        {
            verticalVelocity += gravity * deltaTime;
        }

        float targetSpeed = 0f;
        bool hasMoveInput = serverMoveInput.sqrMagnitude > 0.0001f;
        if (hasMoveInput)
        {
            targetSpeed = serverSprintHeld ? sprintSpeed : moveSpeed;
        }

        float currentSpeed = serverPlanarVelocity.magnitude;
        float speedRate = targetSpeed > currentSpeed ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, speedRate * deltaTime);

        if (hasMoveInput)
        {
            Vector3 desiredDirection = serverMoveInput.normalized;
            Vector3 currentDirection = serverPlanarVelocity.sqrMagnitude > 0.0001f
                ? serverPlanarVelocity.normalized
                : desiredDirection;

            float maxRadiansDelta = directionChangeRate * Mathf.Deg2Rad * deltaTime;
            Vector3 rotatedDirection = Vector3.RotateTowards(currentDirection, desiredDirection, maxRadiansDelta, 0f);
            serverPlanarVelocity = rotatedDirection.normalized * currentSpeed;
        }
        else
        {
            Vector3 currentDirection = serverPlanarVelocity.sqrMagnitude > 0.0001f
                ? serverPlanarVelocity.normalized
                : transform.forward;
            serverPlanarVelocity = currentDirection * currentSpeed;
        }

        if (serverPlanarVelocity.sqrMagnitude > 0.0001f)
        {
            Vector3 facingDirection = hasMoveInput ? serverMoveInput.normalized : serverPlanarVelocity.normalized;
            Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * deltaTime);
        }

        Vector3 motion = serverPlanarVelocity * deltaTime;
        motion.y = verticalVelocity * deltaTime;
        CollisionFlags collisionFlags = characterController.Move(motion);

        bool landedThisFrame = !grounded && (collisionFlags & CollisionFlags.Below) != 0;
        if (landedThisFrame)
        {
            networkJump.Value = false;
            networkFreeFall.Value = false;
            networkGrounded.Value = true;
            verticalVelocity = -2f;
        }

        serverJumpRequested = false;
    }

    private void UpdateMovementAnimation(float deltaTime)
    {
        if (characterAnimator == null || deltaTime <= 0f)
        {
            return;
        }

        Vector3 planarDelta = transform.position - previousPosition;
        planarDelta.y = 0f;
        float currentSpeed = planarDelta.magnitude / deltaTime;
        previousPosition = transform.position;

        // Blend using actual replicated movement so local and remote players stay visually consistent.
        visualSpeed = Mathf.MoveTowards(visualSpeed, currentSpeed, 12f * deltaTime);
        characterAnimator.SetFloat(SpeedHash, visualSpeed);
        characterAnimator.SetBool(GroundedHash, networkGrounded.Value);
        characterAnimator.SetBool(JumpHash, networkJump.Value);
        characterAnimator.SetBool(FreeFallHash, networkFreeFall.Value);

        float motionSpeed = 0f;
        if (visualSpeed > 0.05f && moveSpeed > 0.001f)
        {
            motionSpeed = Mathf.Max(0.1f, (visualSpeed / moveSpeed) * motionSpeedMultiplier);
        }

        characterAnimator.SetFloat(MotionSpeedHash, motionSpeed);
    }

    private Vector3 ResolveMoveDirection(Vector2 input)
    {
        if (input.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return new Vector3(input.x, 0f, input.y).normalized;
        }

        Vector3 forward = mainCamera.transform.forward;
        Vector3 right = mainCamera.transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 moveDirection = forward * input.y + right * input.x;
        return moveDirection.sqrMagnitude > 0.0001f ? moveDirection.normalized : Vector3.zero;
    }

    private void DisableEmbeddedLocalControllers()
    {
        foreach (ThirdPersonController controller in GetComponentsInChildren<ThirdPersonController>(true))
        {
            controller.enabled = false;
        }

        foreach (StarterAssetsInputs inputs in GetComponentsInChildren<StarterAssetsInputs>(true))
        {
            inputs.enabled = false;
        }

        foreach (PlayerInput input in GetComponentsInChildren<PlayerInput>(true))
        {
            input.enabled = false;
        }

        foreach (BasicRigidBodyPush push in GetComponentsInChildren<BasicRigidBodyPush>(true))
        {
            push.enabled = false;
        }

        foreach (CharacterController controller in GetComponentsInChildren<CharacterController>(true))
        {
            if (controller != characterController)
            {
                controller.enabled = false;
            }
        }
    }
}

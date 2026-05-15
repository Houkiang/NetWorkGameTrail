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
    private float rotationSpeed = 720f;

    private CharacterController characterController;
    private Animator characterAnimator;
    private Vector2 serverMoveInput;
    private Vector3 previousPosition;
    private float visualSpeed;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");

    public float MoveSpeed => moveSpeed;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        characterAnimator = GetComponentInChildren<Animator>(true);
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

        if (IsServer)
        {
            serverMoveInput = input;
            return;
        }

        SubmitMovementServerRpc(input);
    }

    [ServerRpc]
    private void SubmitMovementServerRpc(Vector2 input)
    {
        serverMoveInput = Vector2.ClampMagnitude(input, 1f);
    }

    private void ApplyServerMovement(float deltaTime)
    {
        if (characterController == null)
        {
            return;
        }

        Vector3 planarMotion = new Vector3(serverMoveInput.x, 0f, serverMoveInput.y);
        if (planarMotion.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(planarMotion, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * deltaTime);
        }

        characterController.Move(planarMotion * moveSpeed * deltaTime);
    }

    private void UpdateMovementAnimation(float deltaTime)
    {
        if (characterAnimator == null || deltaTime <= 0f)
        {
            return;
        }

        float currentSpeed = Vector3.Distance(transform.position, previousPosition) / deltaTime;
        previousPosition = transform.position;

        // Blend using actual replicated movement so local and remote players stay visually consistent.
        visualSpeed = Mathf.MoveTowards(visualSpeed, currentSpeed, 12f * deltaTime);
        characterAnimator.SetFloat(SpeedHash, visualSpeed);

        float normalizedMotionSpeed = moveSpeed > 0f ? Mathf.Clamp01(visualSpeed / moveSpeed) : 0f;
        characterAnimator.SetFloat(MotionSpeedHash, normalizedMotionSpeed);
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

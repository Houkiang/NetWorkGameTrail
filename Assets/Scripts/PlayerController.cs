using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NetworkObject))]
public class PlayerController : NetworkBehaviour
{
    private static readonly Rect DebugWindowRect = new Rect(20f, 260f, 400f, 340f);
    private const float TerminalVelocity = -53f;
    private const float PredictionErrorTeleportThreshold = 1.5f;

    private struct PredictedInput : INetworkSerializable
    {
        public uint Sequence;
        public Vector3 MoveDirection;
        public bool JumpPressed;
        public bool SprintHeld;
        public float DeltaTime;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref MoveDirection);
            serializer.SerializeValue(ref JumpPressed);
            serializer.SerializeValue(ref SprintHeld);
            serializer.SerializeValue(ref DeltaTime);
        }
    }

    private struct ReconciliationState : INetworkSerializable, IEquatable<ReconciliationState>
    {
        public uint LastProcessedInputSequence;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 PlanarVelocity;
        public float VerticalVelocity;
        public float JumpTimeoutDelta;
        public float FallTimeoutDelta;
        public bool Grounded;
        public bool Jump;
        public bool FreeFall;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref LastProcessedInputSequence);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);
            serializer.SerializeValue(ref PlanarVelocity);
            serializer.SerializeValue(ref VerticalVelocity);
            serializer.SerializeValue(ref JumpTimeoutDelta);
            serializer.SerializeValue(ref FallTimeoutDelta);
            serializer.SerializeValue(ref Grounded);
            serializer.SerializeValue(ref Jump);
            serializer.SerializeValue(ref FreeFall);
        }

        public bool Equals(ReconciliationState other)
        {
            return LastProcessedInputSequence == other.LastProcessedInputSequence
                && Position == other.Position
                && Rotation == other.Rotation
                && PlanarVelocity == other.PlanarVelocity
                && Mathf.Approximately(VerticalVelocity, other.VerticalVelocity)
                && Mathf.Approximately(JumpTimeoutDelta, other.JumpTimeoutDelta)
                && Mathf.Approximately(FallTimeoutDelta, other.FallTimeoutDelta)
                && Grounded == other.Grounded
                && Jump == other.Jump
                && FreeFall == other.FreeFall;
        }
    }

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

    [SerializeField]
    private bool showNetworkDebugOverlay;

    [SerializeField]
    private KeyCode debugOverlayToggleKey = KeyCode.F3;

    private CharacterController characterController;
    private Animator characterAnimator;
    private NetworkTransform networkTransform;
    private Transform visualRoot;
    private Transform leftFootBone;
    private Transform rightFootBone;
    private Vector3 initialVisualRootLocalPosition;
    private Vector3 sampledMoveInput;
    private PredictedInput lastServerInput;
    private Vector3 serverMoveInput;
    private Vector3 serverPlanarVelocity;
    private readonly List<PredictedInput> pendingInputs = new List<PredictedInput>();
    private readonly Queue<PredictedInput> serverInputQueue = new Queue<PredictedInput>();
    private float visualSpeed;
    private float verticalVelocity;
    private float jumpTimeoutDelta;
    private float fallTimeoutDelta;
    private float ownerSimulationAccumulator;
    private float serverSimulationAccumulator;
    private Vector2 debugScrollPosition;
    private float lastReconciliationPositionError;
    private float lastReconciliationRotationError;
    private uint nextInputSequence;
    private uint lastAppliedReconciliationSequence;
    private uint lastProcessedServerInputSequence;
    private bool debugOverlayVisible;
    private bool localGroundedState = true;
    private bool localJumpState;
    private bool localFreeFallState;
    private bool sampledJumpPressed;
    private bool sampledSprintHeld;
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

    private readonly NetworkVariable<float> networkPlanarSpeed = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> networkVerticalSpeed = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> networkGroundGap = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> networkVisualRootLocalY = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> networkLowestFootGap = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ReconciliationState> authoritativeState = new NetworkVariable<ReconciliationState>(
        new ReconciliationState(),
        NetworkVariableReadPermission.Owner,
        NetworkVariableWritePermission.Server);

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int FreeFallHash = Animator.StringToHash("FreeFall");

    public float MoveSpeed => moveSpeed;
    private bool UsesPredictedMovement => IsOwner && !IsServer;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        characterAnimator = GetComponentInChildren<Animator>(true);
        networkTransform = GetComponent<NetworkTransform>();
        visualRoot = transform.childCount > 0 ? transform.GetChild(0) : null;
        if (visualRoot != null)
        {
            initialVisualRootLocalPosition = visualRoot.localPosition;
        }

        CacheFootBones();
        jumpTimeoutDelta = jumpTimeout;
        fallTimeoutDelta = fallTimeout;
        debugOverlayVisible = showNetworkDebugOverlay;
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        if (IsOwner)
        {
            HandleDebugOverlayToggle();
            ReadLocalInput();

            if (IsServer)
            {
                serverMoveInput = sampledMoveInput;
                serverSprintHeld = sampledSprintHeld;
                serverJumpRequested |= sampledJumpPressed;
                sampledJumpPressed = false;
                ApplyMovementStep(deltaTime, true);
            }
            else if (UsesPredictedMovement)
            {
                ownerSimulationAccumulator += deltaTime;
                StepOwnerPrediction();
            }
        }
        else if (IsServer)
        {
            serverSimulationAccumulator += deltaTime;
            StepRemoteServerSimulation();
        }

        UpdateMovementAnimation(deltaTime);
    }

    public override void OnNetworkSpawn()
    {
        DisableEmbeddedLocalControllers();
        CacheFootBones();
        if (UsesPredictedMovement && networkTransform != null)
        {
            networkTransform.enabled = false;
        }

        jumpTimeoutDelta = jumpTimeout;
        fallTimeoutDelta = fallTimeout;
        ownerSimulationAccumulator = 0f;
        serverSimulationAccumulator = 0f;
        enabled = true;
    }

    public override void OnNetworkDespawn()
    {
        if (networkTransform != null)
        {
            networkTransform.enabled = true;
        }
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
        sampledMoveInput = ResolveMoveDirection(input);
        sampledJumpPressed |= Input.GetKeyDown(KeyCode.Space);
        sampledSprintHeld = Input.GetKey(KeyCode.LeftShift);
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void SubmitMovementServerRpc(PredictedInput predictedInput)
    {
        serverInputQueue.Enqueue(predictedInput);
    }

    private void ApplyOwnerPrediction(float deltaTime)
    {
        PredictedInput predictedInput = new PredictedInput
        {
            Sequence = ++nextInputSequence,
            MoveDirection = sampledMoveInput,
            JumpPressed = sampledJumpPressed,
            SprintHeld = sampledSprintHeld,
            DeltaTime = deltaTime
        };

        pendingInputs.Add(predictedInput);
        serverMoveInput = predictedInput.MoveDirection;
        serverSprintHeld = predictedInput.SprintHeld;
        serverJumpRequested |= predictedInput.JumpPressed;
        ApplyMovementStep(deltaTime, false);
        SubmitMovementServerRpc(predictedInput);
        sampledJumpPressed = false;
    }

    private void StepOwnerPrediction()
    {
        float simulationStep = GetSimulationStepDelta();
        int maxSteps = 4;
        int stepCount = 0;

        while (ownerSimulationAccumulator >= simulationStep && stepCount < maxSteps)
        {
            ApplyOwnerPrediction(simulationStep);
            ownerSimulationAccumulator -= simulationStep;
            stepCount++;
        }

        ReconcileOwnerPrediction();
    }

    private void StepRemoteServerSimulation()
    {
        float simulationStep = GetSimulationStepDelta();
        int maxSteps = 4;
        int stepCount = 0;

        while (serverSimulationAccumulator >= simulationStep && stepCount < maxSteps)
        {
            ConsumeNextServerInput();
            ApplyMovementStep(simulationStep, true);
            serverSimulationAccumulator -= simulationStep;
            stepCount++;
        }
    }

    private void ConsumeNextServerInput()
    {
        PredictedInput nextInput = lastServerInput;
        bool consumedNewInput = false;

        nextInput.JumpPressed = false;

        if (serverInputQueue.Count > 0)
        {
            nextInput = serverInputQueue.Dequeue();
            consumedNewInput = true;
        }

        serverMoveInput = Vector3.ClampMagnitude(new Vector3(nextInput.MoveDirection.x, 0f, nextInput.MoveDirection.z), 1f);
        serverSprintHeld = nextInput.SprintHeld;
        serverJumpRequested |= nextInput.JumpPressed;

        if (consumedNewInput)
        {
            lastServerInput = nextInput;
            lastServerInput.JumpPressed = false;
            lastProcessedServerInputSequence = nextInput.Sequence;
        }
    }

    private void ApplyMovementStep(float deltaTime, bool updateNetworkState)
    {
        if (characterController == null || deltaTime <= 0f)
        {
            return;
        }

        bool grounded = characterController.isGrounded;
        localGroundedState = grounded;

        if (grounded)
        {
            fallTimeoutDelta = fallTimeout;
            localFreeFallState = false;

            if (verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }

            if (serverJumpRequested && jumpTimeoutDelta <= 0f)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                localJumpState = true;
                localGroundedState = false;
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
                localFreeFallState = true;
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
            localJumpState = false;
            localFreeFallState = false;
            localGroundedState = true;
            verticalVelocity = -2f;
        }

        if (updateNetworkState)
        {
            networkGrounded.Value = localGroundedState;
            networkJump.Value = localJumpState;
            networkFreeFall.Value = localFreeFallState;
            networkPlanarSpeed.Value = serverPlanarVelocity.magnitude;
            networkVerticalSpeed.Value = verticalVelocity;
            networkGroundGap.Value = SampleGroundGap();
            networkVisualRootLocalY.Value = GetVisualRootLocalYOffset();
            networkLowestFootGap.Value = SampleLowestFootGap();
            authoritativeState.Value = new ReconciliationState
            {
                LastProcessedInputSequence = lastProcessedServerInputSequence,
                Position = transform.position,
                Rotation = transform.rotation,
                PlanarVelocity = serverPlanarVelocity,
                VerticalVelocity = verticalVelocity,
                JumpTimeoutDelta = jumpTimeoutDelta,
                FallTimeoutDelta = fallTimeoutDelta,
                Grounded = localGroundedState,
                Jump = localJumpState,
                FreeFall = localFreeFallState
            };
        }

        serverJumpRequested = false;
    }

    private void ReconcileOwnerPrediction()
    {
        ReconciliationState state = authoritativeState.Value;
        if (state.LastProcessedInputSequence == 0 || state.LastProcessedInputSequence == lastAppliedReconciliationSequence)
        {
            return;
        }

        lastAppliedReconciliationSequence = state.LastProcessedInputSequence;
        lastReconciliationPositionError = Vector3.Distance(transform.position, state.Position);
        lastReconciliationRotationError = Quaternion.Angle(transform.rotation, state.Rotation);

        bool largeError = lastReconciliationPositionError > PredictionErrorTeleportThreshold;
        bool smallError = lastReconciliationPositionError < 0.04f && lastReconciliationRotationError < 2f;

        if (!smallError)
        {
            ApplyAuthoritativeState(state);
        }
        else
        {
            serverPlanarVelocity = state.PlanarVelocity;
            verticalVelocity = state.VerticalVelocity;
            jumpTimeoutDelta = state.JumpTimeoutDelta;
            fallTimeoutDelta = state.FallTimeoutDelta;
            localGroundedState = state.Grounded;
            localJumpState = state.Jump;
            localFreeFallState = state.FreeFall;
            TrimAcknowledgedInputs(state.LastProcessedInputSequence);
            return;
        }

        TrimAcknowledgedInputs(state.LastProcessedInputSequence);

        for (int i = 0; i < pendingInputs.Count; i++)
        {
            PredictedInput predictedInput = pendingInputs[i];
            serverMoveInput = predictedInput.MoveDirection;
            serverSprintHeld = predictedInput.SprintHeld;
            serverJumpRequested |= predictedInput.JumpPressed;
            ApplyMovementStep(predictedInput.DeltaTime, false);
        }

    }

    private void ApplyAuthoritativeState(ReconciliationState state)
    {
        transform.SetPositionAndRotation(state.Position, state.Rotation);
        serverPlanarVelocity = state.PlanarVelocity;
        verticalVelocity = state.VerticalVelocity;
        jumpTimeoutDelta = state.JumpTimeoutDelta;
        fallTimeoutDelta = state.FallTimeoutDelta;
        localGroundedState = state.Grounded;
        localJumpState = state.Jump;
        localFreeFallState = state.FreeFall;
    }

    private void TrimAcknowledgedInputs(uint processedSequence)
    {
        while (pendingInputs.Count > 0 && pendingInputs[0].Sequence <= processedSequence)
        {
            pendingInputs.RemoveAt(0);
        }
    }

    private void UpdateMovementAnimation(float deltaTime)
    {
        if (characterAnimator == null || deltaTime <= 0f)
        {
            return;
        }

        float targetVisualSpeed = GetAnimationSpeedSource();
        visualSpeed = Mathf.MoveTowards(visualSpeed, targetVisualSpeed, 18f * deltaTime);
        characterAnimator.SetFloat(SpeedHash, visualSpeed);
        characterAnimator.SetBool(GroundedHash, UsesPredictedMovement ? localGroundedState : networkGrounded.Value);
        characterAnimator.SetBool(JumpHash, UsesPredictedMovement ? localJumpState : networkJump.Value);
        characterAnimator.SetBool(FreeFallHash, UsesPredictedMovement ? localFreeFallState : networkFreeFall.Value);

        float motionSpeed = 0f;
        if (visualSpeed > 0.05f && moveSpeed > 0.001f)
        {
            motionSpeed = Mathf.Max(0.1f, (visualSpeed / moveSpeed) * motionSpeedMultiplier);
        }

        characterAnimator.SetFloat(MotionSpeedHash, motionSpeed);
    }

    private float GetAnimationSpeedSource()
    {
        if (UsesPredictedMovement || IsServer)
        {
            return serverPlanarVelocity.magnitude;
        }

        return networkPlanarSpeed.Value;
    }

    private float GetSimulationStepDelta()
    {
        int tickRate = GetCurrentTickRate();
        if (tickRate <= 0)
        {
            return 1f / 60f;
        }

        return 1f / tickRate;
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

    private void HandleDebugOverlayToggle()
    {
        if (Input.GetKeyDown(debugOverlayToggleKey))
        {
            debugOverlayVisible = !debugOverlayVisible;
        }
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !IsOwner || !debugOverlayVisible)
        {
            return;
        }

        GUILayout.BeginArea(DebugWindowRect, "Network Movement Debug", GUI.skin.window);
        debugScrollPosition = GUILayout.BeginScrollView(debugScrollPosition, false, true);
        GUILayout.Label($"Role: {GetLocalRoleLabel()}");
        GUILayout.Label($"RTT: {GetCurrentRttMs()} ms");
        GUILayout.Label($"Pos: {FormatVector3(transform.position)}");
        GUILayout.Label($"Planar Speed: local {visualSpeed:F2} | server {networkPlanarSpeed.Value:F2}");
        GUILayout.Label($"Vertical Speed: server {networkVerticalSpeed.Value:F2}");
        GUILayout.Label($"Grounded: server {networkGrounded.Value}");
        GUILayout.Label($"Jump/FreeFall: {networkJump.Value} / {networkFreeFall.Value}");
        GUILayout.Label($"Ground Gap: local {SampleGroundGap():F3} | server {networkGroundGap.Value:F3}");
        GUILayout.Label($"Visual Root Y: local {GetVisualRootLocalYOffset():F3} | server {networkVisualRootLocalY.Value:F3}");
        GUILayout.Label($"Lowest Foot Gap: local {SampleLowestFootGap():F3} | server {networkLowestFootGap.Value:F3}");
        GUILayout.Label($"Pending Inputs: {pendingInputs.Count}");
        GUILayout.Label($"Reconcile Error: pos {lastReconciliationPositionError:F3} | rot {lastReconciliationRotationError:F1}");
        GUILayout.Label($"Authoritative Pos: {FormatVector3(authoritativeState.Value.Position)}");
        GUILayout.Label($"TickRate: {GetCurrentTickRate()}");
        GUILayout.Label($"Toggle: {debugOverlayToggleKey}");
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private float SampleGroundGap()
    {
        if (characterController == null)
        {
            return -1f;
        }

        Bounds bounds = characterController.bounds;
        Vector3 origin = bounds.center + Vector3.up * (bounds.extents.y + 0.05f);
        float castDistance = bounds.size.y + 2f;

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, castDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return -1f;
        }

        float gap = hit.distance - bounds.size.y;
        return Mathf.Max(0f, gap);
    }

    private float SampleLowestFootGap()
    {
        CacheFootBones();

        float leftGap = SampleBoneGroundGap(leftFootBone);
        float rightGap = SampleBoneGroundGap(rightFootBone);

        if (leftGap < 0f)
        {
            return rightGap;
        }

        if (rightGap < 0f)
        {
            return leftGap;
        }

        return Mathf.Min(leftGap, rightGap);
    }

    private float SampleBoneGroundGap(Transform bone)
    {
        if (bone == null)
        {
            return -1f;
        }

        Vector3 origin = bone.position + Vector3.up * 0.05f;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return -1f;
        }

        return Mathf.Max(0f, hit.distance - 0.05f);
    }

    private float GetVisualRootLocalYOffset()
    {
        return visualRoot != null ? visualRoot.localPosition.y - initialVisualRootLocalPosition.y : 0f;
    }

    private void CacheFootBones()
    {
        if (characterAnimator == null || !characterAnimator.isHuman)
        {
            return;
        }

        if (leftFootBone == null)
        {
            leftFootBone = characterAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
        }

        if (rightFootBone == null)
        {
            rightFootBone = characterAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
        }
    }

    private int GetCurrentRttMs()
    {
        if (NetworkManager == null || !NetworkManager.IsClient)
        {
            return 0;
        }

        return unchecked((int)NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId));
    }

    private int GetCurrentTickRate()
    {
        return NetworkManager != null ? unchecked((int)NetworkManager.NetworkConfig.TickRate) : 0;
    }

    private string GetLocalRoleLabel()
    {
        if (IsHost)
        {
            return "Host";
        }

        if (IsServer)
        {
            return "Server";
        }

        if (IsClient)
        {
            return "Client";
        }

        return "Offline";
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"{value.x:F2}, {value.y:F2}, {value.z:F2}";
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

using System;
using System.Collections.Generic;
using StarterAssets;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NetworkObject))]
public class PlayerController : NetworkBehaviour, IDebugPanelProvider
{
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int FreeFallHash = Animator.StringToHash("FreeFall");

    private const float TerminalVelocity = -53f;
    private const float GroundStickVelocity = 0f;

    private struct InputCommand : INetworkSerializable
    {
        public uint Sequence;
        public Vector3 MoveDirection;
        public float AimYaw;
        public bool SprintHeld;
        public bool JumpPressed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref MoveDirection);
            serializer.SerializeValue(ref AimYaw);
            serializer.SerializeValue(ref SprintHeld);
            serializer.SerializeValue(ref JumpPressed);
        }
    }

    private struct MotorState : INetworkSerializable, IEquatable<MotorState>
    {
        public uint LastProcessedInputSequence;
        public uint LastConsumedJumpSequence;
        public Vector3 Position;
        public float Yaw;
        public Vector3 PlanarVelocity;
        public float VerticalVelocity;
        public float JumpTimeoutDelta;
        public float FallTimeoutDelta;
        public float GroundedGraceDelta;
        public bool Grounded;
        public bool Jump;
        public bool FreeFall;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref LastProcessedInputSequence);
            serializer.SerializeValue(ref LastConsumedJumpSequence);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Yaw);
            serializer.SerializeValue(ref PlanarVelocity);
            serializer.SerializeValue(ref VerticalVelocity);
            serializer.SerializeValue(ref JumpTimeoutDelta);
            serializer.SerializeValue(ref FallTimeoutDelta);
            serializer.SerializeValue(ref GroundedGraceDelta);
            serializer.SerializeValue(ref Grounded);
            serializer.SerializeValue(ref Jump);
            serializer.SerializeValue(ref FreeFall);
        }

        public bool Equals(MotorState other)
        {
            return LastProcessedInputSequence == other.LastProcessedInputSequence
                && LastConsumedJumpSequence == other.LastConsumedJumpSequence
                && Position == other.Position
                && Mathf.Approximately(Yaw, other.Yaw)
                && PlanarVelocity == other.PlanarVelocity
                && Mathf.Approximately(VerticalVelocity, other.VerticalVelocity)
                && Mathf.Approximately(JumpTimeoutDelta, other.JumpTimeoutDelta)
                && Mathf.Approximately(FallTimeoutDelta, other.FallTimeoutDelta)
                && Mathf.Approximately(GroundedGraceDelta, other.GroundedGraceDelta)
                && Grounded == other.Grounded
                && Jump == other.Jump
                && FreeFall == other.FreeFall;
        }
    }

    [SerializeField]
    private float moveSpeed = 5f;

    [SerializeField]
    private float sprintSpeed = 10f;

    [SerializeField]
    private float rotationSpeed = 1080f;

    [SerializeField]
    private float acceleration = 24f;

    [SerializeField]
    private float deceleration = 30f;

    [SerializeField]
    private float directionChangeRate = 1440f;

    [SerializeField]
    private float motionSpeedMultiplier = 0.8f;

    [SerializeField]
    private float jumpHeight = 2f;

    [SerializeField]
    private float gravity = -24f;

    [SerializeField]
    private float jumpTimeout = 0.2f;

    [SerializeField]
    private float fallTimeout = 0.12f;

    [SerializeField]
    private float groundedGraceTime = 0.05f;

    [SerializeField]
    private float groundProbeDistance = 0.3f;

    [SerializeField]
    private float groundSnapDistance = 0.2f;

    [SerializeField]
    private float collisionSkin = 0.02f;

    [SerializeField]
    private float remotePositionLerp = 18f;

    [SerializeField]
    private float remoteRotationLerp = 18f;

    [SerializeField]
    private float ownerCorrectionHardSnapDistance = 8f;

    [SerializeField]
    private float ownerCorrectionOffsetMax = 3f;

    [SerializeField]
    private float ownerCorrectionDecay = 10f;

    [SerializeField]
    private float serverRemoteVisualSmoothTime = 0.06f;

    [SerializeField]
    private LayerMask collisionLayers = Physics.DefaultRaycastLayers;

    [SerializeField]
    private bool showNetworkDebugOverlay;

    [SerializeField]
    private KeyCode debugOverlayToggleKey = KeyCode.F3;

    private readonly List<InputCommand> pendingInputs = new List<InputCommand>();
    private readonly Collider[] overlapResults = new Collider[16];
    private readonly NetworkVariable<MotorState> authoritativeState = new NetworkVariable<MotorState>(
        new MotorState(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private CharacterController characterController;
    private Animator characterAnimator;
    private NetworkTransform networkTransform;
    private Transform visualRoot;
    private Transform leftFootBone;
    private Transform rightFootBone;
    private Vector3 initialVisualRootLocalPosition;
    private Quaternion initialVisualRootLocalRotation = Quaternion.identity;
    private Vector3 ownerRenderPositionVelocity;
    private Vector3 ownerVisualCorrectionOffset;
    private Vector3 serverRemoteVisualRootVelocity;
    private Vector3 sampledMoveInput;
    private float sampledAimYaw;
    private Vector3 remoteRenderPositionVelocity;
    private InputCommand latestServerInput;
    private InputCommand latestReceivedServerInput;
    private MotorState predictedState;
    private MotorState serverState;
    private MotorState remoteVisualState;
    private float localTickAccumulator;
    private float serverTickAccumulator;
    private float visualSpeed;
    private Vector2 visualMoveInput;
    private float lastReconciliationPositionError;
    private float lastReconciliationRotationError;
    private uint nextInputSequence;
    private uint latestQueuedServerSequence;
    private uint latestConsumedServerSequence;
    private uint jumpRequestSequence;
    private int jumpResendTicksRemaining;
    private bool jumpAwaitingServerConsume;
    private bool jumpQueued;
    private bool sampledSprintHeld;
    private CapsuleCollider penetrationProbe;
    private GameObject penetrationProbeObject;
    private float cachedControllerRadius;
    private float cachedControllerHeight;
    private Vector3 cachedControllerCenter;
    private float cachedSlopeLimit;
    private float cachedStepOffset;

    public float MoveSpeed => moveSpeed;

    public int DebugSortOrder => 100;

    public string DebugSectionTitle => "Player";

    public bool ShouldDisplayInDebugOverlay => Application.isPlaying && IsOwner;

    private bool UsesPrediction => IsOwner && !IsServer;

    private float TickInterval
    {
        get
        {
            int tickRate = GetCurrentTickRate();
            return tickRate > 0 ? 1f / tickRate : 1f / 60f;
        }
    }

    private float CollisionRadius => Mathf.Max(0.05f, cachedControllerRadius - collisionSkin);

    private float CollisionHeight => Mathf.Max(cachedControllerHeight, CollisionRadius * 2f + 0.01f);

    private Vector3 CapsuleCenter => cachedControllerCenter;

    private float SlopeLimitDegrees => cachedSlopeLimit;

    private float StepOffsetHeight => cachedStepOffset;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        CacheControllerGeometry();
        EnsurePenetrationProbe();
        characterAnimator = GetComponentInChildren<Animator>(true);
        networkTransform = GetComponent<NetworkTransform>();
        visualRoot = transform.childCount > 0 ? transform.GetChild(0) : null;
        if (visualRoot != null)
        {
            initialVisualRootLocalPosition = visualRoot.localPosition;
            initialVisualRootLocalRotation = visualRoot.localRotation;
        }

        CacheFootBones();
    }

    private void OnEnable()
    {
        DebugPanelRegistry.Register(this);
    }

    private void OnDisable()
    {
        DebugPanelRegistry.Unregister(this);
    }

    public override void OnNetworkSpawn()
    {
        DisableEmbeddedLocalControllers();
        CacheFootBones();
        DisableRootCharacterController();

        if (networkTransform != null)
        {
            networkTransform.enabled = false;
        }

        predictedState = CreateInitialState(transform.position, transform.eulerAngles.y);
        serverState = predictedState;
        remoteVisualState = predictedState;
        ownerRenderPositionVelocity = Vector3.zero;
        ownerVisualCorrectionOffset = Vector3.zero;
        authoritativeState.OnValueChanged += OnAuthoritativeStateChanged;
        enabled = true;
    }

    public override void OnNetworkDespawn()
    {
        authoritativeState.OnValueChanged -= OnAuthoritativeStateChanged;

        if (characterController != null)
        {
            characterController.enabled = true;
        }

        if (networkTransform != null)
        {
            networkTransform.enabled = true;
        }
    }

    private void OnDestroy()
    {
        if (penetrationProbeObject != null)
        {
            Destroy(penetrationProbeObject);
        }
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        if (IsOwner)
        {
            SampleOwnerInput();
        }

        if (IsServer)
        {
            if (IsOwner)
            {
                SampleHostAuthoritativeInput(deltaTime);
            }
            else
            {
                serverTickAccumulator += deltaTime;
                StepServerTicks();
                ApplyServerRemoteVisualSmoothing(deltaTime);
            }
        }
        else if (UsesPrediction)
        {
            localTickAccumulator += deltaTime;
            StepPredictedTicks();
            ApplyPredictedStateToTransform(deltaTime);
        }
        else
        {
            ApplyRemoteSmoothing(deltaTime);
        }

        UpdateMovementAnimation(deltaTime);
    }

    private void SampleOwnerInput()
    {
        if (RuntimeUIState.BlocksGameplayInput)
        {
            sampledMoveInput = Vector3.zero;
            sampledSprintHeld = false;
            jumpQueued = false;
            return;
        }

        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        input = Vector2.ClampMagnitude(input, 1f);
        sampledMoveInput = ResolveMoveDirection(input);
        sampledAimYaw = ResolveAimYaw();
        sampledSprintHeld = Input.GetKey(KeyCode.LeftShift);

        if (Input.GetKeyDown(KeyCode.Space))
        {
            jumpQueued = true;
        }
    }

    private void SampleHostAuthoritativeInput(float deltaTime)
    {
        latestServerInput = new InputCommand
        {
            Sequence = ++nextInputSequence,
            MoveDirection = sampledMoveInput,
            AimYaw = sampledAimYaw,
            SprintHeld = sampledSprintHeld,
            JumpPressed = jumpQueued
        };

        jumpQueued = false;
        SimulateTick(ref serverState, latestServerInput, deltaTime);
        ApplyStateToTransform(serverState);
        PublishAuthoritativeState();
    }

    private void StepPredictedTicks()
    {
        float tickInterval = TickInterval;
        int steps = 0;

        while (localTickAccumulator >= tickInterval && steps < 4)
        {
            InputCommand command = BuildPredictedCommand();
            pendingInputs.Add(command);
            SimulateTick(ref predictedState, command, tickInterval);
            SubmitInputServerRpc(command);
            localTickAccumulator -= tickInterval;
            steps++;
        }
    }

    private void StepServerTicks()
    {
        float tickInterval = TickInterval;
        int steps = 0;

        while (serverTickAccumulator >= tickInterval && steps < 4)
        {
            ConsumeLatestServerInputState();
            SimulateTick(ref serverState, latestServerInput, tickInterval);
            ApplyStateToTransform(serverState);
            PublishAuthoritativeState();
            serverTickAccumulator -= tickInterval;
            steps++;
        }
    }

    private InputCommand BuildPredictedCommand()
    {
        bool jumpPressed = jumpQueued || (jumpAwaitingServerConsume && jumpResendTicksRemaining > 0);
        InputCommand command = new InputCommand
        {
            Sequence = ++nextInputSequence,
            MoveDirection = sampledMoveInput,
            AimYaw = sampledAimYaw,
            SprintHeld = sampledSprintHeld,
            JumpPressed = jumpPressed
        };

        if (jumpPressed)
        {
            if (jumpQueued)
            {
                jumpRequestSequence = command.Sequence;
                jumpAwaitingServerConsume = true;
                jumpResendTicksRemaining = 3;
                jumpQueued = false;
            }
            else if (jumpAwaitingServerConsume && jumpResendTicksRemaining > 0)
            {
                jumpResendTicksRemaining--;
            }
        }

        return command;
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void SubmitInputServerRpc(InputCommand command)
    {
        if (command.Sequence <= latestQueuedServerSequence)
        {
            return;
        }

        latestQueuedServerSequence = command.Sequence;
        latestReceivedServerInput = command;
    }

    private void ConsumeLatestServerInputState()
    {
        if (latestReceivedServerInput.Sequence > latestConsumedServerSequence)
        {
            latestServerInput = latestReceivedServerInput;
            latestConsumedServerSequence = latestReceivedServerInput.Sequence;
            return;
        }

        latestServerInput = new InputCommand
        {
            Sequence = latestServerInput.Sequence,
            MoveDirection = latestServerInput.MoveDirection,
            AimYaw = latestServerInput.AimYaw,
            SprintHeld = latestServerInput.SprintHeld,
            JumpPressed = false
        };
    }

    private void SimulateTick(ref MotorState state, InputCommand command, float deltaTime)
    {
        float currentSpeed = state.PlanarVelocity.magnitude;
        float targetSpeed = command.MoveDirection.sqrMagnitude > 0.0001f
            ? (command.SprintHeld ? sprintSpeed : moveSpeed)
            : 0f;
        float speedRate = targetSpeed > currentSpeed ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, speedRate * deltaTime);

        Vector3 desiredDirection = command.MoveDirection.sqrMagnitude > 0.0001f
            ? command.MoveDirection.normalized
            : Vector3.zero;

        Vector3 planarDirection;
        if (desiredDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 currentDirection = state.PlanarVelocity.sqrMagnitude > 0.0001f
                ? state.PlanarVelocity.normalized
                : ForwardFromYaw(state.Yaw);
            float maxRadiansDelta = directionChangeRate * Mathf.Deg2Rad * deltaTime;
            planarDirection = Vector3.RotateTowards(currentDirection, desiredDirection, maxRadiansDelta, 0f).normalized;
        }
        else
        {
            planarDirection = state.PlanarVelocity.sqrMagnitude > 0.0001f
                ? state.PlanarVelocity.normalized
                : ForwardFromYaw(state.Yaw);
        }

        state.PlanarVelocity = planarDirection * currentSpeed;
        if (state.Grounded && targetSpeed <= 0f && currentSpeed <= 0.05f)
        {
            state.PlanarVelocity = Vector3.zero;
        }

        state.Yaw = Mathf.MoveTowardsAngle(state.Yaw, command.AimYaw, rotationSpeed * deltaTime);

        UpdateGroundAndVerticalState(ref state, command, deltaTime);

        Vector3 planarVelocity = state.PlanarVelocity;
        if (state.Grounded && ProbeGround(state.Position, out _, out RaycastHit groundHit))
        {
            Vector3 slopeVelocity = Vector3.ProjectOnPlane(state.PlanarVelocity, groundHit.normal);
            if (slopeVelocity.sqrMagnitude > 0.0001f)
            {
                planarVelocity = slopeVelocity.normalized * state.PlanarVelocity.magnitude;
            }
        }

        Vector3 planarMotion = planarVelocity * deltaTime;
        Vector3 motion;
        if (state.Grounded && state.VerticalVelocity <= 0f)
        {
            motion = TryBuildGroundFollowMotion(state.Position, planarMotion, out Vector3 groundFollowMotion)
                ? groundFollowMotion
                : planarMotion;
        }
        else
        {
            motion = planarMotion;
            motion.y = state.VerticalVelocity * deltaTime;
        }

        CollisionFlags flags;
        state.Position = MoveWithCollision(state.Position, motion, state.Grounded, out flags);

        if ((flags & CollisionFlags.Above) != 0 && state.VerticalVelocity > 0f)
        {
            state.VerticalVelocity = 0f;
        }

        ResolvePenetration(ref state.Position, state.Grounded);

        if ((flags & CollisionFlags.Below) != 0 && state.VerticalVelocity <= 0f)
        {
            state.Grounded = true;
            state.FreeFall = false;
            state.Jump = false;
            state.VerticalVelocity = GroundStickVelocity;
            state.FallTimeoutDelta = fallTimeout;
        }

        FinalizeGroundState(ref state);
        state.LastProcessedInputSequence = command.Sequence;
    }

    private void UpdateGroundAndVerticalState(ref MotorState state, InputCommand command, float deltaTime)
    {
        bool wasGrounded = state.Grounded;
        bool grounded = state.VerticalVelocity <= 0f && TrySnapToGround(ref state, groundSnapDistance);

        if (grounded)
        {
            state.Grounded = true;
            state.FreeFall = false;
            state.FallTimeoutDelta = fallTimeout;
            state.GroundedGraceDelta = groundedGraceTime;

            if (state.VerticalVelocity < 0f)
            {
                state.VerticalVelocity = GroundStickVelocity;
            }

            if (command.JumpPressed && command.Sequence > state.LastConsumedJumpSequence && state.JumpTimeoutDelta <= 0f)
            {
                state.VerticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                state.Jump = true;
                state.Grounded = false;
                state.LastConsumedJumpSequence = command.Sequence;
            }

            if (state.JumpTimeoutDelta > 0f)
            {
                state.JumpTimeoutDelta -= deltaTime;
            }
        }
        else if (wasGrounded && state.VerticalVelocity <= 0f && state.GroundedGraceDelta > 0f)
        {
            state.Grounded = true;
            state.FreeFall = false;
            state.Jump = false;
            state.FallTimeoutDelta = fallTimeout;
            state.GroundedGraceDelta = Mathf.Max(0f, state.GroundedGraceDelta - deltaTime);

            if (state.VerticalVelocity < 0f)
            {
                state.VerticalVelocity = GroundStickVelocity;
            }
        }
        else
        {
            state.Grounded = false;
            state.JumpTimeoutDelta = jumpTimeout;
            state.GroundedGraceDelta = 0f;

            if (state.FallTimeoutDelta > 0f)
            {
                state.FallTimeoutDelta -= deltaTime;
            }
            else
            {
                state.FreeFall = true;
            }
        }

        if (state.VerticalVelocity > TerminalVelocity)
        {
            state.VerticalVelocity += gravity * deltaTime;
        }
    }

    private void FinalizeGroundState(ref MotorState state)
    {
        bool grounded = state.VerticalVelocity <= 0f && TrySnapToGround(ref state, groundSnapDistance + collisionSkin);
        if (grounded && state.VerticalVelocity <= 0f)
        {
            state.Grounded = true;
            state.FreeFall = false;
            state.Jump = false;
            state.VerticalVelocity = GroundStickVelocity;
            state.FallTimeoutDelta = fallTimeout;
            state.GroundedGraceDelta = groundedGraceTime;
        }
    }

    private Vector3 MoveWithCollision(Vector3 position, Vector3 motion, bool grounded, out CollisionFlags collisionFlags)
    {
        collisionFlags = CollisionFlags.None;
        Vector3 currentPosition = position;
        Vector3 remainingMotion = motion;

        for (int i = 0; i < 4; i++)
        {
            if (remainingMotion.sqrMagnitude <= 0.000001f)
            {
                break;
            }

            GetCapsulePoints(currentPosition, out Vector3 bottom, out Vector3 top);
            float distance = remainingMotion.magnitude;
            Vector3 direction = remainingMotion / distance;

            if (Physics.CapsuleCast(bottom, top, CollisionRadius, direction, out RaycastHit hit, distance + collisionSkin, collisionLayers, QueryTriggerInteraction.Ignore))
            {
                if (grounded && TryStepUp(ref currentPosition, remainingMotion, hit, out Vector3 steppedMotion))
                {
                    remainingMotion = steppedMotion;
                    continue;
                }

                float travel = Mathf.Max(hit.distance - collisionSkin, 0f);
                currentPosition += direction * travel;

                if (hit.normal.y >= GetGroundNormalThreshold())
                {
                    collisionFlags |= CollisionFlags.Below;
                    currentPosition += hit.normal * collisionSkin;

                    Vector3 slopeLeftover = remainingMotion - direction * travel;
                    Vector3 slopeMotion = Vector3.ProjectOnPlane(slopeLeftover, hit.normal);
                    if (slopeMotion.y < 0f)
                    {
                        slopeMotion.y = 0f;
                    }

                    remainingMotion = slopeMotion;
                    continue;
                }

                if (hit.normal.y <= -0.5f)
                {
                    collisionFlags |= CollisionFlags.Above;
                }
                else
                {
                    collisionFlags |= CollisionFlags.Sides;
                }

                currentPosition += hit.normal * collisionSkin;
                Vector3 leftover = remainingMotion - direction * travel;
                remainingMotion = Vector3.ProjectOnPlane(leftover, hit.normal);
            }
            else
            {
                currentPosition += remainingMotion;
                break;
            }
        }

        return currentPosition;
    }

    private bool TryStepUp(ref Vector3 currentPosition, Vector3 intendedMotion, RaycastHit blockingHit, out Vector3 steppedMotion)
    {
        steppedMotion = intendedMotion;

        if (StepOffsetHeight <= 0.01f || blockingHit.normal.y >= GetGroundNormalThreshold())
        {
            return false;
        }

        Vector3 horizontalMotion = Vector3.ProjectOnPlane(intendedMotion, Vector3.up);
        if (horizontalMotion.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        Vector3 stepUp = Vector3.up * (StepOffsetHeight + collisionSkin);
        Vector3 steppedPosition = currentPosition + stepUp;
        GetCapsulePoints(steppedPosition, out Vector3 steppedBottom, out Vector3 steppedTop);

        if (Physics.CheckCapsule(steppedBottom, steppedTop, CollisionRadius, collisionLayers, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        Vector3 horizontalDirection = horizontalMotion.normalized;
        float horizontalDistance = horizontalMotion.magnitude;
        if (Physics.CapsuleCast(steppedBottom, steppedTop, CollisionRadius, horizontalDirection, out RaycastHit stepHit, horizontalDistance + collisionSkin, collisionLayers, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        currentPosition = steppedPosition;
        steppedMotion = horizontalMotion;
        return true;
    }

    private bool ProbeGround(Vector3 position, out float distanceToGround, out RaycastHit hit)
    {
        GetCapsulePoints(position, out Vector3 bottom, out _);
        Vector3 origin = bottom + Vector3.up * (groundProbeDistance + collisionSkin);
        float castDistance = groundProbeDistance + groundSnapDistance + collisionSkin;

        if (Physics.SphereCast(origin, CollisionRadius, Vector3.down, out hit, castDistance, collisionLayers, QueryTriggerInteraction.Ignore))
        {
            distanceToGround = Mathf.Max(hit.distance - groundProbeDistance - collisionSkin, 0f);
            return hit.normal.y >= GetGroundNormalThreshold();
        }

        distanceToGround = float.MaxValue;
        return false;
    }

    private bool TrySnapToGround(ref MotorState state, float maxSnapDistance)
    {
        if (!ProbeGround(state.Position, out float groundDistance, out _))
        {
            return false;
        }

        if (groundDistance > 0f && groundDistance <= maxSnapDistance)
        {
            state.Position += Vector3.down * groundDistance;
        }

        return groundDistance <= maxSnapDistance + collisionSkin;
    }

    private bool TryBuildGroundFollowMotion(Vector3 startPosition, Vector3 planarMotion, out Vector3 adjustedMotion)
    {
        adjustedMotion = planarMotion;

        Vector3 horizontalMotion = Vector3.ProjectOnPlane(planarMotion, Vector3.up);
        if (horizontalMotion.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        Vector3 targetPosition = startPosition + horizontalMotion;
        GetCapsulePoints(targetPosition, out Vector3 bottom, out _);

        float castUp = StepOffsetHeight + groundProbeDistance + groundSnapDistance + collisionSkin + 0.05f;
        Vector3 origin = bottom + Vector3.up * castUp;
        float castDistance = castUp + StepOffsetHeight + groundProbeDistance + groundSnapDistance + 0.05f;

        if (!Physics.SphereCast(origin, CollisionRadius, Vector3.down, out RaycastHit hit, castDistance, collisionLayers, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (hit.normal.y < GetGroundNormalThreshold())
        {
            return false;
        }

        float resolvedY = origin.y - hit.distance - CollisionRadius;
        float heightDelta = resolvedY - startPosition.y;
        float maxClimb = StepOffsetHeight + groundSnapDistance + 0.1f;
        float maxDrop = groundSnapDistance + 0.1f;
        if (heightDelta > maxClimb || heightDelta < -maxDrop)
        {
            return false;
        }

        adjustedMotion = new Vector3(horizontalMotion.x, heightDelta, horizontalMotion.z);
        return true;
    }

    private void GetCapsulePoints(Vector3 position, out Vector3 bottom, out Vector3 top)
    {
        Vector3 center = position + CapsuleCenter;
        float halfHeight = Mathf.Max(0f, CollisionHeight * 0.5f - CollisionRadius);
        bottom = center + Vector3.down * halfHeight;
        top = center + Vector3.up * halfHeight;
    }

    private float GetGroundNormalThreshold()
    {
        return Mathf.Cos(SlopeLimitDegrees * Mathf.Deg2Rad);
    }

    private void EnsurePenetrationProbe()
    {
        if (penetrationProbe != null)
        {
            UpdatePenetrationProbeShape();
            return;
        }

        penetrationProbeObject = new GameObject($"{name}_PenetrationProbe");
        penetrationProbeObject.hideFlags = HideFlags.HideAndDontSave;
        penetrationProbeObject.layer = gameObject.layer;
        penetrationProbe = penetrationProbeObject.AddComponent<CapsuleCollider>();
        penetrationProbe.isTrigger = true;
        penetrationProbe.direction = 1;
        UpdatePenetrationProbeShape();
    }

    private void UpdatePenetrationProbeShape()
    {
        if (penetrationProbe == null)
        {
            return;
        }

        penetrationProbe.center = cachedControllerCenter;
        penetrationProbe.radius = cachedControllerRadius;
        penetrationProbe.height = cachedControllerHeight;
    }

    private void ResolvePenetration(ref Vector3 position, bool grounded)
    {
        EnsurePenetrationProbe();
        if (penetrationProbe == null)
        {
            return;
        }

        for (int iteration = 0; iteration < 4; iteration++)
        {
            GetCapsulePoints(position, out Vector3 bottom, out Vector3 top);
            int overlapCount = Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                CollisionRadius + collisionSkin,
                overlapResults,
                collisionLayers,
                QueryTriggerInteraction.Ignore);

            if (overlapCount == 0)
            {
                break;
            }

            Vector3 totalSeparation = Vector3.zero;
            int separationCount = 0;

            for (int i = 0; i < overlapCount; i++)
            {
                Collider other = overlapResults[i];
                overlapResults[i] = null;

                if (other == null || other == penetrationProbe || other.transform == transform || other.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (!Physics.ComputePenetration(
                    penetrationProbe,
                    position,
                    Quaternion.identity,
                    other,
                    other.transform.position,
                    other.transform.rotation,
                    out Vector3 direction,
                    out float distance))
                {
                    continue;
                }

                Vector3 separation = direction * (distance + collisionSkin);
                if (grounded && direction.y >= GetGroundNormalThreshold())
                {
                    separation = Vector3.up * Mathf.Max(separation.y, 0f);
                }

                totalSeparation += separation;
                separationCount++;
            }

            if (separationCount == 0)
            {
                break;
            }

            position += totalSeparation / separationCount;
        }
    }

    private void CacheControllerGeometry()
    {
        if (characterController == null)
        {
            cachedControllerRadius = 0.45f;
            cachedControllerHeight = 2f;
            cachedControllerCenter = new Vector3(0f, 1f, 0f);
            cachedSlopeLimit = 45f;
            cachedStepOffset = 0.3f;
            return;
        }

        cachedControllerRadius = characterController.radius;
        cachedControllerHeight = characterController.height;
        cachedControllerCenter = characterController.center;
        cachedSlopeLimit = characterController.slopeLimit;
        cachedStepOffset = characterController.stepOffset;
        UpdatePenetrationProbeShape();
    }

    private void DisableRootCharacterController()
    {
        if (characterController != null && characterController.enabled)
        {
            characterController.enabled = false;
        }
    }

    private void PublishAuthoritativeState()
    {
        authoritativeState.Value = serverState;
    }

    private void OnAuthoritativeStateChanged(MotorState previousState, MotorState newState)
    {
        if (IsServer)
        {
            return;
        }

        if (UsesPrediction)
        {
            ApplyOwnerReconciliation(newState);
        }
        else
        {
            remoteVisualState = newState;
        }
    }

    private void ApplyOwnerReconciliation(MotorState newState)
    {
        Vector3 previousRenderTarget = GetPredictedRenderPosition();
        float previousYaw = predictedState.Yaw;

        predictedState = newState;
        TrimAcknowledgedInputs(newState.LastProcessedInputSequence);

        for (int i = 0; i < pendingInputs.Count; i++)
        {
            SimulateTick(ref predictedState, pendingInputs[i], TickInterval);
        }

        Vector3 correctedRenderTarget = GetPredictedRenderPosition();
        lastReconciliationPositionError = Vector3.Distance(previousRenderTarget, correctedRenderTarget);
        lastReconciliationRotationError = Mathf.Abs(Mathf.DeltaAngle(previousYaw, predictedState.Yaw));
        if (lastReconciliationPositionError > ownerCorrectionHardSnapDistance)
        {
            ownerVisualCorrectionOffset = Vector3.zero;
            ownerRenderPositionVelocity = Vector3.zero;
        }
        else
        {
            ownerVisualCorrectionOffset += previousRenderTarget - correctedRenderTarget;
            ownerVisualCorrectionOffset = Vector3.ClampMagnitude(ownerVisualCorrectionOffset, ownerCorrectionOffsetMax);
        }

        if (jumpAwaitingServerConsume && jumpRequestSequence > 0 && newState.LastConsumedJumpSequence >= jumpRequestSequence)
        {
            jumpAwaitingServerConsume = false;
            jumpResendTicksRemaining = 0;
            jumpRequestSequence = 0;
        }
    }

    private void TrimAcknowledgedInputs(uint processedSequence)
    {
        while (pendingInputs.Count > 0 && pendingInputs[0].Sequence <= processedSequence)
        {
            pendingInputs.RemoveAt(0);
        }
    }

    private void ApplyPredictedStateToTransform(float deltaTime)
    {
        float offsetDecay = 1f - Mathf.Exp(-ownerCorrectionDecay * deltaTime);
        ownerVisualCorrectionOffset = Vector3.Lerp(ownerVisualCorrectionOffset, Vector3.zero, offsetDecay);
        Vector3 targetPosition = GetPredictedRenderPosition() + ownerVisualCorrectionOffset;

        if (lastReconciliationPositionError > ownerCorrectionHardSnapDistance)
        {
            transform.position = targetPosition;
            ownerRenderPositionVelocity = Vector3.zero;
        }
        else
        {
            float smoothTime = predictedState.Grounded ? 0.028f : 0.02f;
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref ownerRenderPositionVelocity,
                smoothTime,
                Mathf.Infinity,
                deltaTime);
        }

        Quaternion targetRotation = Quaternion.Euler(0f, predictedState.Yaw, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 1f - Mathf.Exp(-36f * deltaTime));
    }

    private void ApplyServerRemoteVisualSmoothing(float deltaTime)
    {
        if (visualRoot == null || deltaTime <= 0f)
        {
            return;
        }

        Vector3 targetPosition = transform.TransformPoint(initialVisualRootLocalPosition);
        Quaternion targetRotation = transform.rotation * initialVisualRootLocalRotation;
        if (Vector3.Distance(visualRoot.position, targetPosition) > ownerCorrectionHardSnapDistance)
        {
            visualRoot.position = targetPosition;
            visualRoot.rotation = targetRotation;
            serverRemoteVisualRootVelocity = Vector3.zero;
            return;
        }

        visualRoot.position = Vector3.SmoothDamp(
            visualRoot.position,
            targetPosition,
            ref serverRemoteVisualRootVelocity,
            Mathf.Max(0.001f, serverRemoteVisualSmoothTime),
            Mathf.Infinity,
            deltaTime);
        float rotationBlend = 1f - Mathf.Exp(-(1f / Mathf.Max(0.001f, serverRemoteVisualSmoothTime)) * deltaTime);
        visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, targetRotation, rotationBlend);
    }

    private Vector3 GetPredictedRenderPosition()
    {
        Vector3 renderPosition = predictedState.Position;
        float extrapolationTime = Mathf.Clamp(localTickAccumulator, 0f, TickInterval);
        if (extrapolationTime <= 0f)
        {
            return renderPosition;
        }

        Vector3 extrapolatedMotion = predictedState.PlanarVelocity * extrapolationTime;
        if (!predictedState.Grounded || predictedState.VerticalVelocity > 0f)
        {
            extrapolatedMotion.y = predictedState.VerticalVelocity * extrapolationTime;
        }

        return renderPosition + extrapolatedMotion;
    }

    private void ApplyRemoteSmoothing(float deltaTime)
    {
        Vector3 targetPosition = remoteVisualState.Position;
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref remoteRenderPositionVelocity, 1f / Mathf.Max(remotePositionLerp, 0.01f), Mathf.Infinity, deltaTime);
        Quaternion targetRotation = Quaternion.Euler(0f, remoteVisualState.Yaw, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 1f - Mathf.Exp(-remoteRotationLerp * deltaTime));
    }

    private void ApplyStateToTransform(MotorState state)
    {
        transform.position = state.Position;
        transform.rotation = Quaternion.Euler(0f, state.Yaw, 0f);
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

        MotorState animState = GetAnimationState();
        Vector3 localVelocity = Quaternion.Euler(0f, -animState.Yaw, 0f) * animState.PlanarVelocity;
        float animationSpeedRange = Mathf.Max(sprintSpeed, 0.001f);
        Vector2 targetMoveInput = Vector2.ClampMagnitude(
            new Vector2(localVelocity.x, localVelocity.z) / animationSpeedRange,
            1f);
        visualMoveInput = Vector2.MoveTowards(visualMoveInput, targetMoveInput, 8f * deltaTime);
        characterAnimator.SetFloat(MoveXHash, visualMoveInput.x);
        characterAnimator.SetFloat(MoveYHash, visualMoveInput.y);
        characterAnimator.SetBool(GroundedHash, animState.Grounded);
        characterAnimator.SetBool(JumpHash, animState.Jump);
        characterAnimator.SetBool(FreeFallHash, animState.FreeFall);

        float motionSpeed = 0f;
        if (visualSpeed > 0.05f && moveSpeed > 0.001f)
        {
            motionSpeed = Mathf.Max(0.1f, (visualSpeed / moveSpeed) * motionSpeedMultiplier);
        }

        characterAnimator.SetFloat(MotionSpeedHash, motionSpeed);
    }

    private MotorState GetAnimationState()
    {
        if (IsServer)
        {
            return serverState;
        }

        return UsesPrediction ? predictedState : remoteVisualState;
    }

    private float GetAnimationSpeedSource()
    {
        if (IsServer)
        {
            return serverState.PlanarVelocity.magnitude;
        }

        return UsesPrediction ? predictedState.PlanarVelocity.magnitude : remoteVisualState.PlanarVelocity.magnitude;
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

    private float ResolveAimYaw()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return transform.eulerAngles.y;
        }

        Vector3 forward = mainCamera.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
        {
            return transform.eulerAngles.y;
        }

        forward.Normalize();
        return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }

    private MotorState CreateInitialState(Vector3 position, float yaw)
    {
        MotorState state = new MotorState
        {
            Position = position,
            Yaw = yaw,
            PlanarVelocity = Vector3.zero,
            VerticalVelocity = GroundStickVelocity,
            JumpTimeoutDelta = jumpTimeout,
            FallTimeoutDelta = fallTimeout,
            GroundedGraceDelta = groundedGraceTime,
            Grounded = true,
            Jump = false,
            FreeFall = false
        };

        TrySnapToGround(ref state, groundProbeDistance + groundSnapDistance + 0.5f);
        ResolvePenetration(ref state.Position, state.Grounded);
        FinalizeGroundState(ref state);
        return state;
    }

    private static Vector3 ForwardFromYaw(float yaw)
    {
        float radians = yaw * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
    }

    public void AppendDebugLines(List<string> lines)
    {
        lines.Add($"Role: {GetLocalRoleLabel()}");
        lines.Add($"TickRate: {GetCurrentTickRate()}");
        lines.Add($"RTT: {GetCurrentRttMs()} ms");
        lines.Add($"Pos: {FormatVector3(transform.position)}");
        lines.Add($"Predicted Pos: {FormatVector3(predictedState.Position)}");
        lines.Add($"Authoritative Pos: {FormatVector3(authoritativeState.Value.Position)}");
        lines.Add($"Planar Speed: {GetAnimationSpeedSource():F2}");
        lines.Add($"Move Blend: {visualMoveInput.x:F2}, {visualMoveInput.y:F2}");
        lines.Add($"Pending Inputs: {pendingInputs.Count}");
        lines.Add($"Reconcile Error: pos {lastReconciliationPositionError:F3} | rot {lastReconciliationRotationError:F1}");
        lines.Add($"Grounded: {GetAnimationState().Grounded}");
        lines.Add($"Jump / FreeFall: {GetAnimationState().Jump} / {GetAnimationState().FreeFall}");
        lines.Add($"Ground Gap: {SampleGroundGap():F3}");
        lines.Add($"Visual Root Y: {GetVisualRootLocalYOffset():F3}");
        lines.Add($"Lowest Foot Gap: {SampleLowestFootGap():F3}");
    }

    private float SampleGroundGap()
    {
        ProbeGround(transform.position, out float distanceToGround, out _);
        return distanceToGround == float.MaxValue ? -1f : distanceToGround;
    }

    private float GetVisualRootLocalYOffset()
    {
        return visualRoot != null ? visualRoot.localPosition.y - initialVisualRootLocalPosition.y : 0f;
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
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2f, collisionLayers, QueryTriggerInteraction.Ignore))
        {
            return -1f;
        }

        return Mathf.Max(0f, hit.distance - 0.05f);
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

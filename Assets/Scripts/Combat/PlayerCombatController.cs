using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PlayerWeaponController))]
[RequireComponent(typeof(PlayerHealth))]
public class PlayerCombatController : NetworkBehaviour, IDebugPanelProvider
{
    private static readonly int FireTriggerHash = Animator.StringToHash("Fire");

    private const float DefaultFireOriginHeight = 1.35f;
    private const float DefaultMuzzleFxLifetime = 2f;
    private const float DefaultImpactFxLifetime = 5f;
    private const float DefaultTracerLifetime = 2f;
    private const float DefaultFireAudioVolume = 1f;
    private const float DefaultReloadAudioVolume = 1f;
    private const float DefaultDebugTracerLifetime = 0.08f;
    private const float DefaultDebugTracerWidth = 0.018f;

    private static Material debugTracerMaterial;

    [SerializeField]
    private Transform fireOrigin;

    [SerializeField]
    private KeyCode fireKey = KeyCode.Mouse0;

    [SerializeField]
    private KeyCode reloadKey = KeyCode.R;

    [Header("Animation")]
    [SerializeField]
    [Tooltip("换弹动画 Trigger 参数名。Animator Controller 中需要有同名 Trigger 才会播放换弹动画。")]
    private string reloadTriggerName = "Reload";

    [SerializeField]
    [Tooltip("是否在换弹状态开始时触发换弹动画。没有换弹动画时可以关闭。")]
    private bool playReloadAnimation = true;

    [SerializeField]
    [Tooltip("换弹动画速度倍率参数名。Animator Controller 的 Reloading 状态需要把 Speed Multiplier 绑定到这个 Float 参数。")]
    private string reloadSpeedParameterName = "ReloadSpeed";

    [SerializeField]
    [Tooltip("是否根据武器配置的 Reload Animation Duration 与 Reload Time 自动设置换弹动画速度。")]
    private bool matchReloadAnimationToReloadTime = true;

    [SerializeField]
    private bool logServerHits = true;

    [SerializeField]
    [Min(0.001f)]
    private float muzzleOverlapRadius = 0.05f;

    [Header("Debug Tracer")]
    [SerializeField]
    private bool showDebugTracers = true;

    [SerializeField]
    private Color predictedTracerColor = new Color(0.22f, 0.85f, 1f, 0.92f);

    [SerializeField]
    private Color confirmedTracerColor = new Color(1f, 0.85f, 0.24f, 0.96f);

    [SerializeField]
    [Min(0.001f)]
    private float debugTracerWidth = DefaultDebugTracerWidth;

    [SerializeField]
    [Min(0.01f)]
    private float debugTracerLifetime = DefaultDebugTracerLifetime;

    private PlayerWeaponController weaponController;
    private PlayerHealth health;
    private Animator characterAnimator;
    private bool lastShotDidHit;
    private bool lastShotHitPlayer;
    private Vector3 lastShotEndPoint;

    public Transform FireOrigin => ResolveFireOriginTransform() ?? transform;

    public bool CanAcceptCombatInput => IsOwner && health != null && health.IsAlive && !RuntimeUIState.BlocksGameplayInput;

    public int DebugSortOrder => 120;

    public string DebugSectionTitle => "Combat";

    public bool ShouldDisplayInDebugOverlay => Application.isPlaying && IsOwner;

    private void Awake()
    {
        weaponController = GetComponent<PlayerWeaponController>();
        health = GetComponent<PlayerHealth>();
        characterAnimator = GetComponentInChildren<Animator>(true);
    }

    public override void OnNetworkSpawn()
    {
        if (weaponController == null)
        {
            weaponController = GetComponent<PlayerWeaponController>();
        }

        if (weaponController != null)
        {
            weaponController.ReloadStateChanged += OnReloadStateChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (weaponController != null)
        {
            weaponController.ReloadStateChanged -= OnReloadStateChanged;
        }
    }

    private void OnEnable()
    {
        DebugPanelRegistry.Register(this);
    }

    private void OnDisable()
    {
        DebugPanelRegistry.Unregister(this);
    }

    private void Update()
    {
        if (!CanAcceptCombatInput)
        {
            return;
        }

        if (Input.GetKeyDown(reloadKey))
        {
            weaponController.TryStartReload();
        }

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            weaponController.TrySwitchWeapon(0);
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            weaponController.TrySwitchWeapon(1);
        }

        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            weaponController.TrySwitchWeapon(2);
        }

        WeaponDefinition weapon = weaponController != null ? weaponController.CurrentWeapon : null;
        bool wantsToFire = weapon != null && weapon.Automatic
            ? Input.GetKey(fireKey)
            : Input.GetKeyDown(fireKey);

        if (wantsToFire)
        {
            TryFire();
        }
    }

    public bool TryFire()
    {
        if (!CanAcceptCombatInput || weaponController == null || !weaponController.HasWeapon)
        {
            return false;
        }

        double localTime = Time.unscaledTimeAsDouble;
        if (!weaponController.CanFireLocal(localTime))
        {
            return false;
        }

        if (!TryBuildAimPoint(out Vector3 aimPoint))
        {
            return false;
        }

        weaponController.MarkLocalShotFired(localTime);
        PlayPredictedFireFeedback(weaponController.CurrentWeapon, aimPoint);
        RequestFireServerRpc(aimPoint);
        return true;
    }

    private bool TryBuildAimPoint(out Vector3 aimPoint)
    {
        WeaponDefinition weapon = weaponController.CurrentWeapon;
        Camera aimCamera = Camera.main;
        if (weapon == null || aimCamera == null)
        {
            aimPoint = Vector3.zero;
            return false;
        }

        Ray aimRay = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit[] hits = Physics.RaycastAll(
            aimRay,
            weapon.Range,
            weapon.HitMask,
            QueryTriggerInteraction.Collide);

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (IsSelfCollider(hitCollider))
            {
                continue;
            }

            aimPoint = hits[i].point;
            return IsFinite(aimPoint);
        }

        aimPoint = aimRay.origin + aimRay.direction * weapon.Range;
        return IsFinite(aimPoint);
    }

    [ServerRpc]
    private void RequestFireServerRpc(Vector3 requestedAimPoint)
    {
        if (weaponController == null || health == null || !health.IsAlive || !IsFinite(requestedAimPoint))
        {
            return;
        }

        WeaponDefinition weapon = weaponController.CurrentWeapon;
        if (weapon == null)
        {
            return;
        }

        double serverTime = NetworkManager.ServerTime.Time;
        weaponController.TryCompleteReloadServer(serverTime);
        if (!weaponController.CanFireServer(serverTime))
        {
            return;
        }

        Vector3 serverOrigin = GetServerFireOrigin();
        Vector3 shotVector = requestedAimPoint - serverOrigin;
        if (shotVector.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        weaponController.MarkServerShotFired(serverTime);
        Vector3 shotDirection = ApplySpread(shotVector.normalized, weapon.SpreadAngle);
        Vector3 traceEndPoint = serverOrigin + shotDirection * weapon.Range;
        bool didHit = ResolveServerShot(
            serverOrigin,
            shotDirection,
            weapon,
            out Vector3 impactPoint,
            out Vector3 impactNormal,
            out bool hitPlayer);
        if (didHit)
        {
            traceEndPoint = impactPoint;
        }

        BroadcastFireFeedbackClientRpc(serverOrigin, traceEndPoint, didHit, hitPlayer, impactPoint, impactNormal);
    }

    [ClientRpc]
    private void BroadcastFireFeedbackClientRpc(
        Vector3 origin,
        Vector3 traceEndPoint,
        bool didHit,
        bool hitPlayer,
        Vector3 impactPoint,
        Vector3 impactNormal)
    {
        WeaponDefinition weapon = weaponController != null ? weaponController.CurrentWeapon : null;
        if (weapon == null)
        {
            return;
        }

        bool isLocalShooter = NetworkManager != null && OwnerClientId == NetworkManager.LocalClientId;
        if (!isLocalShooter)
        {
            TryPlayFireAnimation();
            PlayShotPresentation(weapon, origin, traceEndPoint, playAudioAndMuzzle: true, tracerColorOverride: confirmedTracerColor);
        }
        else if (showDebugTracers)
        {
            TryPlayDebugTracer(origin, traceEndPoint, confirmedTracerColor, debugTracerLifetime);
        }

        if (didHit)
        {
            PlayImpactPresentation(weapon, impactPoint, impactNormal, hitPlayer);
        }

        if (isLocalShooter)
        {
            lastShotDidHit = didHit;
            lastShotHitPlayer = hitPlayer;
            lastShotEndPoint = didHit ? impactPoint : traceEndPoint;
        }
    }

    private bool ResolveServerShot(
        Vector3 origin,
        Vector3 direction,
        WeaponDefinition weapon,
        out Vector3 impactPoint,
        out Vector3 impactNormal,
        out bool hitPlayer)
    {
        impactPoint = origin + direction * weapon.Range;
        impactNormal = -direction;
        hitPlayer = false;

        if (TryResolveMuzzleOverlap(origin, direction, weapon, out impactPoint, out impactNormal, out hitPlayer))
        {
            return true;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            weapon.Range,
            weapon.HitMask,
            QueryTriggerInteraction.Collide);

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (IsSelfCollider(hitCollider))
            {
                continue;
            }

            impactPoint = hits[i].point;
            impactNormal = hits[i].normal;

            PlayerHitbox hitbox = hitCollider.GetComponentInParent<PlayerHitbox>();
            if (hitbox == null || !hitbox.TryGetOwnerHealth(out PlayerHealth targetHealth))
            {
                hitPlayer = false;
                return true;
            }

            if (targetHealth == health || !targetHealth.CanTakeDamageFrom(OwnerClientId))
            {
                hitPlayer = false;
                return true;
            }

            int damage = hitbox.ApplyDamageMultiplier(weapon.Damage);
            targetHealth.TakeDamageServer(damage, OwnerClientId);
            hitPlayer = true;

            if (logServerHits)
            {
                Debug.Log(
                    $"[Combat] Client {OwnerClientId} hit Client {targetHealth.OwnerClientId} " +
                    $"for {damage}. Health: {targetHealth.CurrentHealth}/{targetHealth.MaxHealth}",
                    this);
            }

            return true;
        }

        return false;
    }

    private bool TryResolveMuzzleOverlap(
        Vector3 origin,
        Vector3 direction,
        WeaponDefinition weapon,
        out Vector3 impactPoint,
        out Vector3 impactNormal,
        out bool hitPlayer)
    {
        impactPoint = origin;
        impactNormal = -direction;
        hitPlayer = false;

        Collider[] overlaps = Physics.OverlapSphere(
            origin,
            GetMuzzleOverlapRadius(weapon),
            weapon.HitMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider overlapCollider = overlaps[i];
            if (IsSelfCollider(overlapCollider))
            {
                continue;
            }

            Vector3 closestPoint = overlapCollider.ClosestPoint(origin);
            impactPoint = (closestPoint - origin).sqrMagnitude > 0.000001f ? closestPoint : origin;
            impactNormal = -direction;

            PlayerHitbox hitbox = overlapCollider.GetComponentInParent<PlayerHitbox>();
            if (hitbox == null || !hitbox.TryGetOwnerHealth(out PlayerHealth targetHealth))
            {
                hitPlayer = false;
                return true;
            }

            if (targetHealth == health || !targetHealth.CanTakeDamageFrom(OwnerClientId))
            {
                hitPlayer = false;
                return true;
            }

            int damage = hitbox.ApplyDamageMultiplier(weapon.Damage);
            targetHealth.TakeDamageServer(damage, OwnerClientId);
            hitPlayer = true;

            if (logServerHits)
            {
                Debug.Log(
                    $"[Combat] Client {OwnerClientId} overlap-hit Client {targetHealth.OwnerClientId} " +
                    $"for {damage}. Health: {targetHealth.CurrentHealth}/{targetHealth.MaxHealth}",
                    this);
            }

            return true;
        }

        return false;
    }

    private Vector3 GetServerFireOrigin()
    {
        Transform resolvedOrigin = ResolveFireOriginTransform();
        return resolvedOrigin != null
            ? resolvedOrigin.position
            : transform.position + Vector3.up * DefaultFireOriginHeight;
    }

    private Transform ResolveFireOriginTransform()
    {
        if (weaponController != null && weaponController.CurrentMuzzleTransform != null)
        {
            return weaponController.CurrentMuzzleTransform;
        }

        return fireOrigin;
    }

    private float GetMuzzleOverlapRadius(WeaponDefinition weapon)
    {
        if (weapon == null)
        {
            return muzzleOverlapRadius;
        }

        return Mathf.Max(0.001f, weapon.MuzzleOverlapRadius);
    }

    private bool IsSelfCollider(Collider collider)
    {
        return collider == null
            || collider.transform == transform
            || collider.transform.IsChildOf(transform);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static Vector3 ApplySpread(Vector3 direction, float spreadAngle)
    {
        if (spreadAngle <= 0f || direction.sqrMagnitude <= 0.0001f)
        {
            return direction;
        }

        direction.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, direction);
        if (right.sqrMagnitude <= 0.0001f)
        {
            right = Vector3.Cross(Vector3.right, direction);
        }

        right.Normalize();
        Vector3 up = Vector3.Cross(direction, right).normalized;
        Vector2 randomOffset = UnityEngine.Random.insideUnitCircle * Mathf.Tan(spreadAngle * Mathf.Deg2Rad);
        return (direction + right * randomOffset.x + up * randomOffset.y).normalized;
    }

    private void PlayPredictedFireFeedback(WeaponDefinition weapon, Vector3 aimPoint)
    {
        if (weapon == null)
        {
            return;
        }

        TryPlayFireAnimation();

        Vector3 origin = FireOrigin.position;
        Vector3 direction = aimPoint - origin;
        Vector3 traceEndPoint = direction.sqrMagnitude > 0.0001f
            ? origin + direction.normalized * weapon.Range
            : origin + FireOrigin.forward * weapon.Range;
        PlayShotPresentation(weapon, origin, traceEndPoint, playAudioAndMuzzle: true, tracerColorOverride: predictedTracerColor);
    }

    private void PlayShotPresentation(
        WeaponDefinition weapon,
        Vector3 origin,
        Vector3 traceEndPoint,
        bool playAudioAndMuzzle,
        Color tracerColorOverride)
    {
        if (weapon == null)
        {
            return;
        }

        if (playAudioAndMuzzle)
        {
            TryPlayMuzzleFlash(weapon);
            TryPlayFireAudio(weapon, origin);
        }

        TryPlayTracer(weapon, origin, traceEndPoint, tracerColorOverride);
    }

    private void PlayImpactPresentation(WeaponDefinition weapon, Vector3 impactPoint, Vector3 impactNormal, bool hitPlayer)
    {
        if (weapon == null)
        {
            return;
        }

        GameObject impactPrefab = hitPlayer ? weapon.PlayerImpactPrefab : weapon.ImpactPrefab;
        if (impactPrefab == null)
        {
            return;
        }

        Quaternion impactRotation = impactNormal.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(impactNormal)
            : Quaternion.identity;

        GameObject impactInstance = Instantiate(impactPrefab, impactPoint, impactRotation);
        Destroy(impactInstance, DefaultImpactFxLifetime);
    }

    private void TryPlayMuzzleFlash(WeaponDefinition weapon)
    {
        if (weapon == null || weapon.MuzzleFlashPrefab == null)
        {
            return;
        }

        Transform origin = FireOrigin;
        GameObject muzzleInstance = Instantiate(
            weapon.MuzzleFlashPrefab,
            origin.position,
            origin.rotation,
            origin);

        Destroy(muzzleInstance, DefaultMuzzleFxLifetime);
    }

    private void TryPlayTracer(WeaponDefinition weapon, Vector3 origin, Vector3 traceEndPoint, Color tracerColorOverride)
    {
        if (showDebugTracers)
        {
            TryPlayDebugTracer(origin, traceEndPoint, tracerColorOverride, debugTracerLifetime);
        }

        if (weapon == null || weapon.TracerPrefab == null)
        {
            return;
        }

        Vector3 direction = traceEndPoint - origin;
        Quaternion rotation = direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction.normalized)
            : Quaternion.identity;

        GameObject tracerInstance = Instantiate(weapon.TracerPrefab, origin, rotation);
        Destroy(tracerInstance, DefaultTracerLifetime);
    }

    private static void TryPlayFireAudio(WeaponDefinition weapon, Vector3 origin)
    {
        if (weapon == null || weapon.FireAudioClip == null)
        {
            return;
        }

        AudioSource.PlayClipAtPoint(weapon.FireAudioClip, origin, DefaultFireAudioVolume);
    }

    private void TryPlayFireAnimation()
    {
        if (characterAnimator == null)
        {
            return;
        }

        characterAnimator.ResetTrigger(FireTriggerHash);
        characterAnimator.SetTrigger(FireTriggerHash);
    }

    private void OnReloadStateChanged(bool wasReloading, bool isReloading)
    {
        WeaponDefinition weapon = weaponController != null ? weaponController.CurrentWeapon : null;
        if (isReloading)
        {
            TryPlayReloadAnimation(weapon);
            TryPlayReloadAudio(weapon, started: true);
        }
        else if (wasReloading)
        {
            ResetReloadAnimationSpeed();
            TryPlayReloadAudio(weapon, started: false);
        }
    }

    private void TryPlayReloadAnimation(WeaponDefinition weapon)
    {
        if (!playReloadAnimation || characterAnimator == null || string.IsNullOrWhiteSpace(reloadTriggerName))
        {
            return;
        }

        ApplyReloadAnimationSpeed(weapon);

        int reloadTriggerHash = Animator.StringToHash(reloadTriggerName);
        characterAnimator.ResetTrigger(reloadTriggerHash);
        characterAnimator.SetTrigger(reloadTriggerHash);
    }

    private void ApplyReloadAnimationSpeed(WeaponDefinition weapon)
    {
        if (!matchReloadAnimationToReloadTime
            || weapon == null
            || characterAnimator == null
            || string.IsNullOrWhiteSpace(reloadSpeedParameterName)
            || !HasAnimatorParameter(characterAnimator, reloadSpeedParameterName, AnimatorControllerParameterType.Float))
        {
            return;
        }

        float animationDuration = Mathf.Max(0.01f, weapon.ReloadAnimationDuration);
        float reloadTime = Mathf.Max(0.01f, weapon.ReloadTime);
        float speedMultiplier = Mathf.Clamp(animationDuration / reloadTime, 0.05f, 10f);
        characterAnimator.SetFloat(reloadSpeedParameterName, speedMultiplier);
    }

    private void ResetReloadAnimationSpeed()
    {
        if (characterAnimator == null
            || string.IsNullOrWhiteSpace(reloadSpeedParameterName)
            || !HasAnimatorParameter(characterAnimator, reloadSpeedParameterName, AnimatorControllerParameterType.Float))
        {
            return;
        }

        characterAnimator.SetFloat(reloadSpeedParameterName, 1f);
    }

    private static bool HasAnimatorParameter(Animator animator, string parameterName, AnimatorControllerParameterType parameterType)
    {
        if (animator == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == parameterType && parameter.name == parameterName)
            {
                return true;
            }
        }

        return false;
    }

    private void TryPlayReloadAudio(WeaponDefinition weapon, bool started)
    {
        if (weapon == null)
        {
            return;
        }

        AudioClip clip = started ? weapon.ReloadStartAudioClip : weapon.ReloadCompleteAudioClip;
        if (clip == null)
        {
            return;
        }

        AudioSource.PlayClipAtPoint(clip, FireOrigin.position, DefaultReloadAudioVolume);
    }

    private void TryPlayDebugTracer(Vector3 origin, Vector3 traceEndPoint, Color color, float lifetime)
    {
        if ((traceEndPoint - origin).sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Material tracerMaterial = GetDebugTracerMaterial();
        if (tracerMaterial == null)
        {
            return;
        }

        GameObject tracerObject = new GameObject("DebugTracer", typeof(LineRenderer));
        LineRenderer lineRenderer = tracerObject.GetComponent<LineRenderer>();
        lineRenderer.sharedMaterial = tracerMaterial;
        lineRenderer.useWorldSpace = true;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        lineRenderer.widthMultiplier = debugTracerWidth;
        lineRenderer.numCapVertices = 2;
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, origin);
        lineRenderer.SetPosition(1, traceEndPoint);
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;

        Destroy(tracerObject, lifetime);
    }

    private static Material GetDebugTracerMaterial()
    {
        if (debugTracerMaterial != null)
        {
            return debugTracerMaterial;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            Debug.LogWarning("Debug tracer shader not found.");
            return null;
        }

        debugTracerMaterial = new Material(shader)
        {
            name = "DebugTracerRuntimeMaterial"
        };

        return debugTracerMaterial;
    }

    public void AppendDebugLines(List<string> lines)
    {
        WeaponDefinition weapon = weaponController != null ? weaponController.CurrentWeapon : null;

        lines.Add($"Alive: {health != null && health.IsAlive}");
        lines.Add($"Can Fire Input: {CanAcceptCombatInput}");
        lines.Add($"Health: {(health != null ? $"{health.CurrentHealth}/{health.MaxHealth}" : "N/A")}");
        lines.Add($"Kills / Damage: {(health != null ? $"{health.KillCount} / {health.TotalDamageDealt}" : "N/A")}");

        if (weapon == null)
        {
            lines.Add("Weapon: None");
            return;
        }

        lines.Add($"Weapon: {weapon.WeaponName}");
        lines.Add($"Slot / Ammo: {(weaponController != null ? $"{weaponController.CurrentWeaponIndex + 1}/{weaponController.WeaponSlotCount} / {weaponController.AmmoDisplayText}" : "N/A")}");
        lines.Add($"Reloading: {(weaponController != null && weaponController.IsReloading)}");
        lines.Add($"Reload Anim Match: {matchReloadAnimationToReloadTime} ({reloadSpeedParameterName})");
        lines.Add($"Damage / Range: {weapon.Damage} / {weapon.Range:F1}");
        lines.Add($"Fire Rate: {weapon.FireRate:F2}/s");
        lines.Add($"Spread / Recoil: {weapon.SpreadAngle:F2}° / {weapon.RecoilPitch:F2}°x{weapon.RecoilYaw:F2}°");
        lines.Add($"Muzzle Overlap Radius: {GetMuzzleOverlapRadius(weapon):F3}");
        lines.Add($"Local Cooldown: {(weaponController != null ? weaponController.GetLocalCooldownRemaining(Time.unscaledTimeAsDouble).ToString("F3") : "N/A")}");
        lines.Add($"Fire Origin: {GetFireOriginSourceLabel()} @ {FormatVector3(FireOrigin.position)}");
        lines.Add($"Debug Tracer: {showDebugTracers}");
        lines.Add($"Last Shot: {DescribeLastShotResult()} @ {FormatVector3(lastShotEndPoint)}");
    }

    private string GetFireOriginSourceLabel()
    {
        if (weaponController != null && weaponController.CurrentMuzzleTransform != null)
        {
            return $"Weapon Muzzle ({weaponController.CurrentMuzzleTransform.name})";
        }

        if (fireOrigin != null)
        {
            return $"Prefab FireOrigin ({fireOrigin.name})";
        }

        return "Fallback";
    }

    private string DescribeLastShotResult()
    {
        if (!lastShotDidHit)
        {
            return "Miss";
        }

        return lastShotHitPlayer ? "Hit Player" : "Hit World";
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"{value.x:F2}, {value.y:F2}, {value.z:F2}";
    }
}

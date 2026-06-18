using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PlayerWeaponController))]
[RequireComponent(typeof(PlayerHealth))]
public class PlayerCombatController : NetworkBehaviour
{
    private const float DefaultFireOriginHeight = 1.35f;
    private const float DefaultMuzzleFxLifetime = 2f;
    private const float DefaultImpactFxLifetime = 5f;
    private const float DefaultTracerLifetime = 2f;
    private const float DefaultFireAudioVolume = 1f;
    private const float DefaultDebugTracerLifetime = 0.08f;
    private const float DefaultDebugTracerWidth = 0.018f;

    private static Material debugTracerMaterial;

    [SerializeField]
    private Transform fireOrigin;

    [SerializeField]
    private KeyCode fireKey = KeyCode.Mouse0;

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

    public Transform FireOrigin => fireOrigin != null ? fireOrigin : transform;

    public bool CanAcceptCombatInput => IsOwner && health != null && health.IsAlive && !RuntimeUIState.BlocksGameplayInput;

    private void Awake()
    {
        weaponController = GetComponent<PlayerWeaponController>();
        health = GetComponent<PlayerHealth>();
    }

    private void Update()
    {
        if (!CanAcceptCombatInput || !Input.GetKey(fireKey))
        {
            return;
        }

        TryFire();
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
        Vector3 shotDirection = shotVector.normalized;
        Vector3 traceEndPoint = serverOrigin + shotDirection * weapon.Range;
        bool didHit = ResolveServerShot(serverOrigin, shotDirection, weapon, out Vector3 impactPoint, out Vector3 impactNormal);
        if (didHit)
        {
            traceEndPoint = impactPoint;
        }

        BroadcastFireFeedbackClientRpc(serverOrigin, traceEndPoint, didHit, impactPoint, impactNormal);
    }

    [ClientRpc]
    private void BroadcastFireFeedbackClientRpc(
        Vector3 origin,
        Vector3 traceEndPoint,
        bool didHit,
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
            PlayShotPresentation(weapon, origin, traceEndPoint, playAudioAndMuzzle: true, tracerColorOverride: confirmedTracerColor);
        }
        else if (showDebugTracers)
        {
            TryPlayDebugTracer(origin, traceEndPoint, confirmedTracerColor, debugTracerLifetime);
        }

        if (didHit)
        {
            PlayImpactPresentation(weapon, impactPoint, impactNormal);
        }
    }

    private bool ResolveServerShot(
        Vector3 origin,
        Vector3 direction,
        WeaponDefinition weapon,
        out Vector3 impactPoint,
        out Vector3 impactNormal)
    {
        impactPoint = origin + direction * weapon.Range;
        impactNormal = -direction;

        if (TryResolveMuzzleOverlap(origin, direction, weapon, out impactPoint, out impactNormal))
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
                return true;
            }

            if (targetHealth == health || !targetHealth.CanTakeDamageFrom(OwnerClientId))
            {
                return true;
            }

            int damage = hitbox.ApplyDamageMultiplier(weapon.Damage);
            targetHealth.TakeDamageServer(damage, OwnerClientId);

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
        out Vector3 impactNormal)
    {
        impactPoint = origin;
        impactNormal = -direction;

        Collider[] overlaps = Physics.OverlapSphere(
            origin,
            muzzleOverlapRadius,
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
                return true;
            }

            if (targetHealth == health || !targetHealth.CanTakeDamageFrom(OwnerClientId))
            {
                return true;
            }

            int damage = hitbox.ApplyDamageMultiplier(weapon.Damage);
            targetHealth.TakeDamageServer(damage, OwnerClientId);

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
        return fireOrigin != null
            ? fireOrigin.position
            : transform.position + Vector3.up * DefaultFireOriginHeight;
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

    private void PlayPredictedFireFeedback(WeaponDefinition weapon, Vector3 aimPoint)
    {
        if (weapon == null)
        {
            return;
        }

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

    private void PlayImpactPresentation(WeaponDefinition weapon, Vector3 impactPoint, Vector3 impactNormal)
    {
        if (weapon == null || weapon.ImpactPrefab == null)
        {
            return;
        }

        Quaternion impactRotation = impactNormal.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(impactNormal)
            : Quaternion.identity;

        GameObject impactInstance = Instantiate(weapon.ImpactPrefab, impactPoint, impactRotation);
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
}

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

    [SerializeField]
    private Transform fireOrigin;

    [SerializeField]
    private KeyCode fireKey = KeyCode.Mouse0;

    [SerializeField]
    private bool logServerHits = true;

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
        PlayPredictedFireFeedback(weaponController.CurrentWeapon);
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
            PlayShotPresentation(weapon, origin, traceEndPoint);
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
            if (hitCollider == null || hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            PlayerHitbox hitbox = hitCollider.GetComponentInParent<PlayerHitbox>();
            if (hitbox == null || !hitbox.TryGetOwnerHealth(out PlayerHealth targetHealth))
            {
                continue;
            }

            if (targetHealth == health || !targetHealth.CanTakeDamageFrom(OwnerClientId))
            {
                continue;
            }

            int damage = hitbox.ApplyDamageMultiplier(weapon.Damage);
            targetHealth.TakeDamageServer(damage, OwnerClientId);
            impactPoint = hits[i].point;
            impactNormal = hits[i].normal;

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

    private Vector3 GetServerFireOrigin()
    {
        return fireOrigin != null
            ? fireOrigin.position
            : transform.position + Vector3.up * DefaultFireOriginHeight;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void PlayPredictedFireFeedback(WeaponDefinition weapon)
    {
        if (weapon == null)
        {
            return;
        }

        Vector3 origin = FireOrigin.position;
        PlayShotPresentation(weapon, origin, origin + FireOrigin.forward * weapon.Range);
    }

    private void PlayShotPresentation(WeaponDefinition weapon, Vector3 origin, Vector3 traceEndPoint)
    {
        if (weapon == null)
        {
            return;
        }

        TryPlayMuzzleFlash(weapon);
        TryPlayTracer(weapon, origin, traceEndPoint);
        TryPlayFireAudio(weapon, origin);
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

    private void TryPlayTracer(WeaponDefinition weapon, Vector3 origin, Vector3 traceEndPoint)
    {
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
}

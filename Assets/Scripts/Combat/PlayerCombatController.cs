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
        ResolveServerShot(serverOrigin, shotVector.normalized, weapon);
    }

    private void ResolveServerShot(Vector3 origin, Vector3 direction, WeaponDefinition weapon)
    {
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

            if (logServerHits)
            {
                Debug.Log(
                    $"[Combat] Client {OwnerClientId} hit Client {targetHealth.OwnerClientId} " +
                    $"for {damage}. Health: {targetHealth.CurrentHealth}/{targetHealth.MaxHealth}",
                    this);
            }

            return;
        }
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
}

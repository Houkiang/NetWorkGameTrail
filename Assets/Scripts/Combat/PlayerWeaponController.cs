using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerWeaponController : NetworkBehaviour
{
    [SerializeField]
    private WeaponDefinition currentWeapon;

    private double nextServerFireTime;
    private double nextLocalFireTime;

    public WeaponDefinition CurrentWeapon => currentWeapon;

    public bool HasWeapon => currentWeapon != null;

    public override void OnNetworkSpawn()
    {
        nextServerFireTime = 0d;
        nextLocalFireTime = 0d;
    }

    public bool CanFireServer(double serverTime)
    {
        return currentWeapon != null && serverTime >= nextServerFireTime;
    }

    public bool CanFireLocal(double localTime)
    {
        return currentWeapon != null && localTime >= nextLocalFireTime;
    }

    public void MarkServerShotFired(double serverTime)
    {
        if (currentWeapon == null)
        {
            return;
        }

        nextServerFireTime = serverTime + currentWeapon.FireInterval;
    }

    public void MarkLocalShotFired(double localTime)
    {
        if (currentWeapon == null)
        {
            return;
        }

        nextLocalFireTime = localTime + currentWeapon.FireInterval;
    }

    public float GetLocalCooldownRemaining(double localTime)
    {
        if (currentWeapon == null)
        {
            return 0f;
        }

        return Mathf.Max(0f, (float)(nextLocalFireTime - localTime));
    }

    public float GetServerCooldownRemaining(double serverTime)
    {
        if (currentWeapon == null)
        {
            return 0f;
        }

        return Mathf.Max(0f, (float)(nextServerFireTime - serverTime));
    }
}

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
}

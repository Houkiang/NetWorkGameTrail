using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PlayerWeaponController))]
[RequireComponent(typeof(PlayerHealth))]
public class PlayerCombatController : NetworkBehaviour
{
    [SerializeField]
    private Transform fireOrigin;

    [SerializeField]
    private KeyCode fireKey = KeyCode.Mouse0;

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
        // M1 only builds the combat module skeleton. Fire requests are implemented in M2.
        if (!CanAcceptCombatInput || !Input.GetKey(fireKey))
        {
            return;
        }
    }
}

using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerHealth : NetworkBehaviour
{
    [SerializeField]
    private int maxHealth = 100;

    [SerializeField]
    private float respawnDelay = 3f;

    private readonly NetworkVariable<int> currentHealth = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> isDead = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public int MaxHealth => maxHealth;

    public int CurrentHealth => currentHealth.Value;

    public float RespawnDelay => respawnDelay;

    public bool IsDead => isDead.Value;

    public bool IsAlive => !isDead.Value && currentHealth.Value > 0;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            ResetHealthServer();
        }
    }

    public bool CanTakeDamageFrom(ulong attackerClientId)
    {
        return IsServer && IsAlive && attackerClientId != OwnerClientId;
    }

    public void TakeDamageServer(int amount, ulong attackerClientId)
    {
        if (!CanTakeDamageFrom(attackerClientId))
        {
            return;
        }

        int clampedAmount = Mathf.Max(0, amount);
        currentHealth.Value = Mathf.Max(0, currentHealth.Value - clampedAmount);

        if (currentHealth.Value <= 0)
        {
            SetDeadServer(true);
        }
    }

    public void ResetHealthServer()
    {
        if (!IsServer)
        {
            return;
        }

        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth.Value = maxHealth;
        isDead.Value = false;
    }

    public void SetDeadServer(bool dead)
    {
        if (!IsServer)
        {
            return;
        }

        isDead.Value = dead;
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        respawnDelay = Mathf.Max(0f, respawnDelay);
    }
}

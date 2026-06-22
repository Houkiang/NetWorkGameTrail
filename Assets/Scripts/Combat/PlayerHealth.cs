using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerHealth : NetworkBehaviour
{
    private static readonly int HitTriggerHash = Animator.StringToHash("Hit");

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

    private readonly NetworkVariable<int> totalDamageDealt = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> killCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Animator characterAnimator;
    private PlayerController playerController;
    private PlayerHitbox[] hitboxes;
    private Renderer[] modelRenderers;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private Coroutine respawnCoroutine;

    public event Action<bool> DeadStateChanged;
    public event Action<int, int> HealthChanged;
    public event Action<int, int> CombatStatsChanged;

    public int MaxHealth => maxHealth;

    public int CurrentHealth => currentHealth.Value;

    public int TotalDamageDealt => totalDamageDealt.Value;

    public int KillCount => killCount.Value;

    public float RespawnDelay => respawnDelay;

    public bool IsDead => isDead.Value;

    public bool IsAlive => !isDead.Value && currentHealth.Value > 0;

    public override void OnNetworkSpawn()
    {
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        currentHealth.OnValueChanged += OnCurrentHealthChanged;
        isDead.OnValueChanged += OnDeadStateChanged;
        totalDamageDealt.OnValueChanged += OnCombatStatsValueChanged;
        killCount.OnValueChanged += OnCombatStatsValueChanged;

        if (IsServer)
        {
            ResetHealthServer();
        }

        ApplyDeadStateLocally(isDead.Value);
        NotifyHealthChanged();
        NotifyCombatStatsChanged();
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= OnCurrentHealthChanged;
        isDead.OnValueChanged -= OnDeadStateChanged;
        totalDamageDealt.OnValueChanged -= OnCombatStatsValueChanged;
        killCount.OnValueChanged -= OnCombatStatsValueChanged;

        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
        }
    }

    private void Awake()
    {
        characterAnimator = GetComponentInChildren<Animator>(true);
        playerController = GetComponent<PlayerController>();
        hitboxes = GetComponentsInChildren<PlayerHitbox>(true);
        modelRenderers = GetComponentsInChildren<Renderer>(true);
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
        if (clampedAmount <= 0)
        {
            return;
        }

        int previousHealth = currentHealth.Value;
        currentHealth.Value = Mathf.Max(0, currentHealth.Value - clampedAmount);
        int damageApplied = Mathf.Max(0, previousHealth - currentHealth.Value);

        PlayerHealth attackerHealth = GetPlayerHealthForClient(attackerClientId);
        if (attackerHealth != null && damageApplied > 0)
        {
            attackerHealth.RegisterDamageDealtServer(damageApplied);
        }

        PlayHitFeedbackClientRpc();

        if (currentHealth.Value <= 0)
        {
            if (attackerHealth != null)
            {
                attackerHealth.RegisterKillServer();
            }

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
        SetDeadServer(false);
    }

    public void RegisterDamageDealtServer(int amount)
    {
        if (!IsServer || amount <= 0)
        {
            return;
        }

        totalDamageDealt.Value += amount;
    }

    public void RegisterKillServer()
    {
        if (!IsServer)
        {
            return;
        }

        killCount.Value += 1;
    }

    public void SetDeadServer(bool dead)
    {
        if (!IsServer)
        {
            return;
        }

        if (isDead.Value == dead)
        {
            return;
        }

        isDead.Value = dead;

        if (dead)
        {
            currentHealth.Value = 0;
            playerController?.ResetMovementStateServer(transform.position, transform.eulerAngles.y);

            if (respawnCoroutine != null)
            {
                StopCoroutine(respawnCoroutine);
            }

            respawnCoroutine = StartCoroutine(RespawnAfterDelayServer());
        }
        else
        {
            if (respawnCoroutine != null)
            {
                StopCoroutine(respawnCoroutine);
                respawnCoroutine = null;
            }
        }

        ApplyDeadStateLocally(dead);
    }

    [ClientRpc]
    private void PlayHitFeedbackClientRpc()
    {
        if (characterAnimator == null)
        {
            characterAnimator = GetComponentInChildren<Animator>(true);
        }

        if (characterAnimator == null)
        {
            return;
        }

        characterAnimator.ResetTrigger(HitTriggerHash);
        characterAnimator.SetTrigger(HitTriggerHash);
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        respawnDelay = Mathf.Max(0f, respawnDelay);
    }

    private void OnCurrentHealthChanged(int previousValue, int newValue)
    {
        NotifyHealthChanged();
    }

    private void OnDeadStateChanged(bool previousValue, bool newValue)
    {
        ApplyDeadStateLocally(newValue);
    }

    private void OnCombatStatsValueChanged(int previousValue, int newValue)
    {
        NotifyCombatStatsChanged();
    }

    private void ApplyDeadStateLocally(bool dead)
    {
        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }

        if (hitboxes == null || hitboxes.Length == 0)
        {
            hitboxes = GetComponentsInChildren<PlayerHitbox>(true);
        }

        if (modelRenderers == null || modelRenderers.Length == 0)
        {
            modelRenderers = GetComponentsInChildren<Renderer>(true);
        }

        if (playerController != null)
        {
            playerController.HandleDeadStateChanged(dead);
        }

        if (hitboxes != null)
        {
            for (int i = 0; i < hitboxes.Length; i++)
            {
                if (hitboxes[i] != null)
                {
                    hitboxes[i].SetHitboxEnabled(!dead);
                }
            }
        }

        if (modelRenderers != null)
        {
            for (int i = 0; i < modelRenderers.Length; i++)
            {
                Renderer renderer = modelRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (renderer.transform == transform || renderer.transform.IsChildOf(transform))
                {
                    renderer.enabled = !dead;
                }
            }
        }

        DeadStateChanged?.Invoke(dead);
    }

    private IEnumerator RespawnAfterDelayServer()
    {
        if (respawnDelay > 0f)
        {
            yield return new WaitForSeconds(respawnDelay);
        }

        if (!IsServer || !IsSpawned)
        {
            respawnCoroutine = null;
            yield break;
        }

        playerController?.ResetMovementStateServer(spawnPosition, spawnRotation.eulerAngles.y);
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth.Value = maxHealth;
        isDead.Value = false;
        ApplyDeadStateLocally(false);
        respawnCoroutine = null;
    }

    private PlayerHealth GetPlayerHealthForClient(ulong clientId)
    {
        if (!IsServer || NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
        {
            return null;
        }

        NetworkObject playerObject = client.PlayerObject;
        return playerObject != null ? playerObject.GetComponent<PlayerHealth>() : null;
    }

    private void NotifyHealthChanged()
    {
        HealthChanged?.Invoke(currentHealth.Value, maxHealth);
    }

    private void NotifyCombatStatsChanged()
    {
        CombatStatsChanged?.Invoke(totalDamageDealt.Value, killCount.Value);
    }
}

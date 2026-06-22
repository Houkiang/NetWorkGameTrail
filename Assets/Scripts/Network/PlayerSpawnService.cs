 using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
[RequireComponent(typeof(NetworkBootstrap))]
public class PlayerSpawnService : MonoBehaviour
{
    [SerializeField]
    private Vector3 firstSpawnPosition = new Vector3(0f, 1f, 0f);

    [SerializeField]
    private float spawnSpacing = 2.5f;

    [SerializeField]
    private bool logRespawnSelection;

    private readonly Dictionary<ulong, NetworkObject> spawnedPlayers = new Dictionary<ulong, NetworkObject>();
    private NetworkManager networkManager;
    private NetworkBootstrap bootstrap;
    private bool wasListening;

    private void Awake()
    {
        networkManager = GetComponent<NetworkManager>();
        bootstrap = GetComponent<NetworkBootstrap>();
        wasListening = networkManager != null && networkManager.IsListening;
    }

    private void OnEnable()
    {
        networkManager = GetComponent<NetworkManager>();
        bootstrap = GetComponent<NetworkBootstrap>();
        wasListening = networkManager != null && networkManager.IsListening;

        if (networkManager == null)
        {
            return;
        }

        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void OnDisable()
    {
        CleanupSpawnedPlayers();

        if (networkManager == null)
        {
            return;
        }

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void Update()
    {
        if (networkManager == null)
        {
            return;
        }

        bool isListening = networkManager.IsListening;
        if (wasListening && !isListening)
        {
            CleanupSpawnedPlayers();
        }

        wasListening = isListening;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        if (spawnedPlayers.TryGetValue(clientId, out NetworkObject existingNetworkObject))
        {
            if (existingNetworkObject != null)
            {
                return;
            }

            spawnedPlayers.Remove(clientId);
        }

        GameObject playerPrefab = bootstrap != null ? bootstrap.PlayerPrefab : null;
        if (playerPrefab == null)
        {
            Debug.LogError("PlayerSpawnService could not spawn a player because no PlayerPrefab is configured.");
            return;
        }

        Vector3 spawnPosition = firstSpawnPosition + new Vector3(spawnedPlayers.Count * spawnSpacing, 0f, 0f);
        Quaternion spawnRotation = Quaternion.identity;
        GameObject instance = Instantiate(playerPrefab, spawnPosition, spawnRotation);
        instance.name = $"Player_{clientId}";

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError("PlayerSpawnService requires PlayerPrefab to include a NetworkObject component.");
            Destroy(instance);
            return;
        }

        networkObject.SpawnAsPlayerObject(clientId, true);
        spawnedPlayers[clientId] = networkObject;

        Debug.Log($"Spawned player object for client {clientId} at {spawnPosition}.");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!spawnedPlayers.TryGetValue(clientId, out NetworkObject networkObject))
        {
            return;
        }

        spawnedPlayers.Remove(clientId);

        if (networkObject == null)
        {
            return;
        }

        if (networkObject.IsSpawned)
        {
            networkObject.Despawn(true);
            return;
        }

        Destroy(networkObject.gameObject);
    }

    private void CleanupSpawnedPlayers()
    {
        if (spawnedPlayers.Count == 0)
        {
            return;
        }

        foreach (NetworkObject networkObject in spawnedPlayers.Values)
        {
            if (networkObject == null)
            {
                continue;
            }

            if (networkObject.IsSpawned)
            {
                networkObject.Despawn(true);
            }
            else
            {
                Destroy(networkObject.gameObject);
            }
        }

        spawnedPlayers.Clear();
    }

    public bool TryGetRandomRespawnPose(out Vector3 position, out Quaternion rotation)
    {
        RespawnPoint[] respawnPoints = FindObjectsOfType<RespawnPoint>(true);
        if (respawnPoints == null || respawnPoints.Length == 0)
        {
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        List<RespawnPoint> validPoints = new List<RespawnPoint>(respawnPoints.Length);
        for (int i = 0; i < respawnPoints.Length; i++)
        {
            RespawnPoint point = respawnPoints[i];
            if (point == null || !point.isActiveAndEnabled || !point.gameObject.activeInHierarchy)
            {
                continue;
            }

            validPoints.Add(point);
        }

        if (validPoints.Count == 0)
        {
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        int selectedIndex = UnityEngine.Random.Range(0, validPoints.Count);
        RespawnPoint selectedPoint = validPoints[selectedIndex];
        position = selectedPoint.Position;
        rotation = selectedPoint.Rotation;

        if (logRespawnSelection)
        {
            Debug.Log($"Selected respawn point '{selectedPoint.name}' at {position}.");
        }

        return true;
    }
}

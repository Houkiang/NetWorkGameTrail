using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
public class ConnectionApprovalHandler : MonoBehaviour
{
    [SerializeField]
    private int maxPlayers = 4;

    [SerializeField]
    private bool enableConnectionApproval = true;

    [SerializeField]
    private string serverFullReason = "Server is full.";

    private readonly HashSet<ulong> reservedClientIds = new HashSet<ulong>();
    private NetworkManager networkManager;

    private void Awake()
    {
        networkManager = GetComponent<NetworkManager>();
        ApplyConfiguration();
    }

    private void OnEnable()
    {
        networkManager = GetComponent<NetworkManager>();
        ApplyConfiguration();

        if (networkManager == null)
        {
            return;
        }

        networkManager.ConnectionApprovalCallback = ApprovalCheck;
        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void OnDisable()
    {
        if (networkManager == null)
        {
            return;
        }

        if (networkManager.ConnectionApprovalCallback == ApprovalCheck)
        {
            networkManager.ConnectionApprovalCallback = null;
        }

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void OnValidate()
    {
        maxPlayers = Mathf.Max(1, maxPlayers);
        ApplyConfiguration();
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        if (request.ClientNetworkId == NetworkManager.ServerClientId)
        {
            response.Approved = true;
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = string.Empty;
            return;
        }

        bool approved = GetActivePlayerCount() + reservedClientIds.Count < maxPlayers;
        if (approved)
        {
            reservedClientIds.Add(request.ClientNetworkId);
        }

        response.Approved = approved;
        response.CreatePlayerObject = false;
        response.Pending = false;
        response.Reason = approved ? string.Empty : serverFullReason;

        Debug.Log(approved
            ? $"Connection approved for client {request.ClientNetworkId}. Slots used: {GetActivePlayerCount() + reservedClientIds.Count}/{maxPlayers}"
            : $"Connection rejected for client {request.ClientNetworkId}. Reason: {serverFullReason}");
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        reservedClientIds.Remove(clientId);
        Debug.Log($"Client connected: {clientId}. Active players: {GetActivePlayerCount()}/{maxPlayers}");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        reservedClientIds.Remove(clientId);
        string reason = networkManager != null ? networkManager.DisconnectReason : string.Empty;
        Debug.Log(string.IsNullOrWhiteSpace(reason)
            ? $"Client disconnected: {clientId}"
            : $"Client disconnected: {clientId}. Reason: {reason}");
    }

    private int GetActivePlayerCount()
    {
        if (networkManager == null || !networkManager.IsServer)
        {
            return 0;
        }

        int count = 0;
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            bool isDedicatedServerOnly = !networkManager.IsHost && clientId == NetworkManager.ServerClientId;
            if (!isDedicatedServerOnly)
            {
                count++;
            }
        }

        return count;
    }

    private void ApplyConfiguration()
    {
        if (networkManager == null)
        {
            return;
        }

        if (networkManager.NetworkConfig == null)
        {
            networkManager.NetworkConfig = new NetworkConfig();
        }

        networkManager.NetworkConfig.ConnectionApproval = enableConnectionApproval;
    }
}

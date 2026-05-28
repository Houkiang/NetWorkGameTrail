using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkBootstrap))]
public class NetworkSessionService : MonoBehaviour, IDebugPanelProvider
{
    [SerializeField]
    private string clientAddress = "127.0.0.1";

    [SerializeField]
    private ushort port = 7777;

    [SerializeField]
    private string listenAddress = "0.0.0.0";

    [SerializeField]
    private bool allowServerMode = true;

    private NetworkBootstrap bootstrap;
    private string lastStatusMessage = "Network is stopped.";
    private string lanAddressSummary = "Detecting...";

    public string ClientAddress
    {
        get => clientAddress;
        set => clientAddress = string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value.Trim();
    }

    public ushort Port
    {
        get => port;
        set => port = value;
    }

    public string ListenAddress
    {
        get => listenAddress;
        set => listenAddress = string.IsNullOrWhiteSpace(value) ? "0.0.0.0" : value.Trim();
    }

    public bool AllowServerMode => allowServerMode;

    public string LastStatusMessage => lastStatusMessage;

    public string LanAddressSummary => lanAddressSummary;

    public NetworkManager NetworkManager => bootstrap != null ? bootstrap.NetworkManager : null;

    public bool IsListening => NetworkManager != null && NetworkManager.IsListening;

    public bool IsHost => NetworkManager != null && NetworkManager.IsHost;

    public bool IsServerOnly => NetworkManager != null && NetworkManager.IsServer && !NetworkManager.IsClient;

    public bool IsClientOnly => NetworkManager != null && NetworkManager.IsClient && !NetworkManager.IsServer;

    public bool HasLocalGameplayRole => NetworkManager != null && NetworkManager.IsClient;

    public int DebugSortOrder => 10;

    public string DebugSectionTitle => "Session";

    public bool ShouldDisplayInDebugOverlay => Application.isPlaying;

    private void Awake()
    {
        bootstrap = GetComponent<NetworkBootstrap>();
        if (bootstrap != null)
        {
            ClientAddress = bootstrap.Address;
            Port = bootstrap.Port;
            ListenAddress = bootstrap.ListenAddress;
        }

        lanAddressSummary = GetLanAddressSummary();
    }

    private void OnEnable()
    {
        DebugPanelRegistry.Register(this);
    }

    private void OnDisable()
    {
        DebugPanelRegistry.Unregister(this);
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        lanAddressSummary = GetLanAddressSummary();
    }

    public bool TryStartHost(out string message)
    {
        ApplyConnectionData("127.0.0.1");
        bootstrap.StartHost();
        message = BuildPostStartMessage("Host");
        lastStatusMessage = message;
        return IsListening;
    }

    public bool TryStartServer(out string message)
    {
        ApplyConnectionData("127.0.0.1");
        bootstrap.StartServer();
        message = BuildPostStartMessage("Server");
        lastStatusMessage = message;
        return IsListening;
    }

    public bool TryStartClient(out string message)
    {
        if (string.IsNullOrWhiteSpace(clientAddress))
        {
            message = "Enter the host IP before starting a client.";
            lastStatusMessage = message;
            return false;
        }

        ApplyConnectionData(clientAddress);
        bootstrap.StartClient();
        message = BuildPostStartMessage("Client");
        lastStatusMessage = message;
        return IsListening;
    }

    public void Shutdown()
    {
        NetworkManager?.Shutdown();
        lastStatusMessage = "Network shutdown.";
    }

    public string GetActiveModeLabel()
    {
        if (NetworkManager == null)
        {
            return "NetworkManager unavailable.";
        }

        if (NetworkManager.IsHost)
        {
            return "Running as Host";
        }

        if (NetworkManager.IsServer)
        {
            return "Running as Server";
        }

        if (NetworkManager.IsClient)
        {
            return "Running as Client";
        }

        return "Network is stopped";
    }

    public string GetConnectionSummary()
    {
        if (!IsListening)
        {
            return "Offline";
        }

        if (IsHost)
        {
            int clientCount = NetworkManager != null ? NetworkManager.ConnectedClientsIds.Count : 0;
            return $"Host active, clients: {clientCount}";
        }

        if (IsServerOnly)
        {
            int clientCount = NetworkManager != null ? NetworkManager.ConnectedClientsIds.Count : 0;
            return $"Server active, clients: {clientCount}";
        }

        if (IsClientOnly)
        {
            return $"Connected to {clientAddress}:{port}";
        }

        return GetActiveModeLabel();
    }

    public void AppendDebugLines(List<string> lines)
    {
        lines.Add($"Mode: {GetActiveModeLabel()}");
        lines.Add($"Status: {GetConnectionSummary()}");
        lines.Add($"LAN: {lanAddressSummary}");
        lines.Add($"Client Address: {clientAddress}");
        lines.Add($"Listen Address: {listenAddress}");
        lines.Add($"Port: {port}");
    }

    private void ApplyConnectionData(string hostAddress)
    {
        if (bootstrap == null)
        {
            bootstrap = GetComponent<NetworkBootstrap>();
        }

        bootstrap.SetConnectionData(hostAddress, port, listenAddress);
    }

    private string BuildPostStartMessage(string requestedMode)
    {
        return IsListening
            ? $"{requestedMode} started."
            : $"{requestedMode} start requested. Check the Console if it does not connect.";
    }

    private static string GetLanAddressSummary()
    {
        try
        {
            List<string> addresses = new List<string>();
            IPAddress[] hostAddresses = Dns.GetHostAddresses(Dns.GetHostName());
            for (int i = 0; i < hostAddresses.Length; i++)
            {
                IPAddress address = hostAddresses[i];
                if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                {
                    addresses.Add(address.ToString());
                }
            }

            return addresses.Count > 0 ? string.Join(" / ", addresses) : "No IPv4 LAN address detected.";
        }
        catch (SocketException)
        {
            return "LAN IP unavailable.";
        }
    }
}

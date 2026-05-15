using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
[RequireComponent(typeof(UnityTransport))]
[RequireComponent(typeof(ConnectionApprovalHandler))]
[RequireComponent(typeof(PlayerSpawnService))]
public class NetworkBootstrap : MonoBehaviour
{
    public enum StartupMode
    {
        Manual,
        Host,
        Server,
        Client,
        CommandLine
    }

    [Header("Startup")]
    [SerializeField]
    private bool autoStartOnPlay = true;

    [SerializeField]
    private StartupMode startupMode = StartupMode.Manual;

    [Header("Connection")]
    [SerializeField]
    private string address = "127.0.0.1";

    [SerializeField]
    private string listenAddress = "0.0.0.0";

    [SerializeField]
    private ushort port = 7777;

    [SerializeField]
    private GameObject playerPrefab;

    [SerializeField]
    private bool persistAcrossScenes = true;

    private NetworkManager networkManager;
    private UnityTransport transport;
    private bool startupAttempted;

    public NetworkManager NetworkManager => networkManager;

    public GameObject PlayerPrefab => playerPrefab;

    public string Address => address;

    public string ListenAddress => listenAddress;

    public ushort Port => port;

    private void Awake()
    {
        EnsureConfigured();

        if (persistAcrossScenes)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private void Start()
    {
        if (!autoStartOnPlay)
        {
            return;
        }

        TryStartConfiguredMode();
    }

    private void OnValidate()
    {
        EnsureConfigured();
    }

    public void ApplyEditorDefaults(GameObject prefab, string hostAddress = "127.0.0.1", ushort listenPort = 7777, string bindAddress = "0.0.0.0")
    {
        playerPrefab = prefab;
        address = hostAddress;
        port = listenPort;
        listenAddress = bindAddress;
        startupMode = StartupMode.Manual;
        EnsureConfigured();
    }

    public void SetConnectionData(string hostAddress, ushort listenPort, string bindAddress = "0.0.0.0")
    {
        address = string.IsNullOrWhiteSpace(hostAddress) ? "127.0.0.1" : hostAddress;
        port = listenPort;
        listenAddress = string.IsNullOrWhiteSpace(bindAddress) ? "0.0.0.0" : bindAddress;
        EnsureConfigured();
    }

    public void StartHost()
    {
        StartNetwork(NetworkStartupKind.Host);
    }

    public void StartServer()
    {
        StartNetwork(NetworkStartupKind.Server);
    }

    public void StartClient()
    {
        StartNetwork(NetworkStartupKind.Client);
    }

    public void TryStartConfiguredMode()
    {
        if (startupAttempted)
        {
            return;
        }

        NetworkStartupKind? startupKind = ResolveStartupKind();
        if (!startupKind.HasValue)
        {
            return;
        }

        StartNetwork(startupKind.Value);
    }

    private NetworkStartupKind? ResolveStartupKind()
    {
        if (startupMode == StartupMode.CommandLine)
        {
            CommandLineConfig config = CommandLineConfig.FromEnvironment(address, port, listenAddress);
            SetConnectionData(config.Address, config.Port, config.ListenAddress);
            return config.StartupKind;
        }

        switch (startupMode)
        {
            case StartupMode.Host:
                return NetworkStartupKind.Host;
            case StartupMode.Server:
                return NetworkStartupKind.Server;
            case StartupMode.Client:
                return NetworkStartupKind.Client;
            default:
                return null;
        }
    }

    private void StartNetwork(NetworkStartupKind startupKind)
    {
        EnsureConfigured();

        if (networkManager == null || networkManager.IsListening)
        {
            return;
        }

        startupAttempted = true;

        bool started = false;
        switch (startupKind)
        {
            case NetworkStartupKind.Host:
                started = networkManager.StartHost();
                break;
            case NetworkStartupKind.Server:
                started = networkManager.StartServer();
                break;
            case NetworkStartupKind.Client:
                started = networkManager.StartClient();
                break;
        }

        Debug.Log(started
            ? $"Network start succeeded: mode={startupKind}, address={address}, listenAddress={listenAddress}, port={port}"
            : $"Network start failed: mode={startupKind}, address={address}, listenAddress={listenAddress}, port={port}");
    }

    private void EnsureConfigured()
    {
        networkManager = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();

        if (transport != null)
        {
            transport.SetConnectionData(address, port, listenAddress);
        }

        if (networkManager == null)
        {
            return;
        }

        if (networkManager.NetworkConfig == null)
        {
            networkManager.NetworkConfig = new NetworkConfig();
        }

        networkManager.NetworkConfig.NetworkTransport = transport;
        networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
    }

    private enum NetworkStartupKind
    {
        Host,
        Server,
        Client
    }

    private readonly struct CommandLineConfig
    {
        public CommandLineConfig(NetworkStartupKind? startupKind, string address, ushort port, string listenAddress)
        {
            StartupKind = startupKind;
            Address = address;
            Port = port;
            ListenAddress = listenAddress;
        }

        public NetworkStartupKind? StartupKind { get; }

        public string Address { get; }

        public ushort Port { get; }

        public string ListenAddress { get; }

        public static CommandLineConfig FromEnvironment(string fallbackAddress, ushort fallbackPort, string fallbackListenAddress)
        {
            string resolvedAddress = fallbackAddress;
            ushort resolvedPort = fallbackPort;
            string resolvedListenAddress = fallbackListenAddress;
            NetworkStartupKind? startupKind = null;

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (Matches(arg, "-mode", "--mode") && TryGetValue(args, i, out string modeValue))
                {
                    startupKind = ParseStartupKind(modeValue);
                    i++;
                    continue;
                }

                if (Matches(arg, "-ip", "--ip", "-address", "--address") && TryGetValue(args, i, out string addressValue))
                {
                    resolvedAddress = addressValue;
                    i++;
                    continue;
                }

                if (Matches(arg, "-port", "--port") && TryGetValue(args, i, out string portValue) && ushort.TryParse(portValue, out ushort parsedPort))
                {
                    resolvedPort = parsedPort;
                    i++;
                    continue;
                }

                if (Matches(arg, "-listen", "--listen", "-listenAddress", "--listenAddress") && TryGetValue(args, i, out string listenValue))
                {
                    resolvedListenAddress = listenValue;
                    i++;
                }
            }

            return new CommandLineConfig(startupKind, resolvedAddress, resolvedPort, resolvedListenAddress);
        }

        private static NetworkStartupKind? ParseStartupKind(string value)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "host":
                    return NetworkStartupKind.Host;
                case "server":
                    return NetworkStartupKind.Server;
                case "client":
                    return NetworkStartupKind.Client;
                default:
                    Debug.LogWarning($"Unknown startup mode '{value}'. Supported values: host, server, client.");
                    return null;
            }
        }

        private static bool Matches(string value, params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(value, candidates[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetValue(string[] args, int currentIndex, out string value)
        {
            int nextIndex = currentIndex + 1;
            if (nextIndex < args.Length)
            {
                value = args[nextIndex];
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}

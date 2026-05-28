using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkBootstrap))]
public class NetworkStartupPanel : MonoBehaviour
{
    [SerializeField]
    private bool legacyPanelEnabled;

    private enum StartupChoice
    {
        Host,
        Client,
        Server
    }

    [SerializeField]
    private bool showDuringPlay = true;

    [SerializeField]
    private StartupChoice defaultMode = StartupChoice.Host;

    [SerializeField]
    private string clientAddress = "127.0.0.1";

    [SerializeField]
    private ushort port = 7777;

    [SerializeField]
    private string listenAddress = "0.0.0.0";

    [SerializeField]
    private bool allowServerMode = true;

    [SerializeField]
    private KeyCode toggleKey = KeyCode.F1;

    [SerializeField]
    private bool showOnStart = true;

    private readonly Rect dragRect = new Rect(0f, 0f, 10000f, 24f);

    private NetworkBootstrap bootstrap;
    private Rect windowRect = new Rect(20f, 20f, 380f, 240f);
    private StartupChoice selectedMode;
    private string portInput = "7777";
    private string statusMessage = string.Empty;
    private string lanAddressSummary = "Detecting...";
    private bool panelVisible;

    private void Awake()
    {
        if (!legacyPanelEnabled && GetComponent<NetworkSessionService>() != null)
        {
            enabled = false;
            return;
        }

        bootstrap = GetComponent<NetworkBootstrap>();
        selectedMode = defaultMode;
        panelVisible = showOnStart;

        if (bootstrap != null)
        {
            clientAddress = string.IsNullOrWhiteSpace(clientAddress) ? bootstrap.Address : clientAddress;
            port = port == 0 ? bootstrap.Port : port;
            listenAddress = string.IsNullOrWhiteSpace(listenAddress) ? bootstrap.ListenAddress : listenAddress;
        }

        portInput = port.ToString();
        lanAddressSummary = GetLanAddressSummary();
    }

    private void Update()
    {
        if (!Application.isPlaying || !showDuringPlay)
        {
            return;
        }

        if (Input.GetKeyDown(toggleKey))
        {
            panelVisible = !panelVisible;
        }

        EnsureCursorState(panelVisible);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!Application.isPlaying || !showDuringPlay || !hasFocus)
        {
            return;
        }

        EnsureCursorState(panelVisible);
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !showDuringPlay || !panelVisible)
        {
            return;
        }

        windowRect = GUI.Window(GetInstanceID(), windowRect, DrawWindowContents, "Network Startup");
    }

    private void DrawWindowContents(int windowId)
    {
        if (bootstrap == null)
        {
            GUILayout.Label("NetworkBootstrap is missing.");
            GUI.DragWindow(dragRect);
            return;
        }

        if (IsListening())
        {
            DrawConnectedState();
        }
        else
        {
            DrawStartupControls();
        }

        GUI.DragWindow(dragRect);
    }

    private void DrawStartupControls()
    {
        selectedMode = DrawModeToolbar(selectedMode);

        GUILayout.Space(10f);
        GUILayout.Label("Choose a role for this build, then start networking.");
        GUILayout.Label($"Press {toggleKey} to hide/show this panel.");

        if (selectedMode == StartupChoice.Client)
        {
            clientAddress = DrawTextField("Host IP", clientAddress);
        }
        else
        {
            GUILayout.Label($"LAN IP: {lanAddressSummary}");
            GUILayout.Label("Share the LAN IP above with clients on the same network.");
        }

        portInput = DrawTextField("Port", portInput);

        GUILayout.Space(8f);
        if (GUILayout.Button($"Start {selectedMode}"))
        {
            StartSelectedMode();
        }

        DrawStatusMessage();
    }

    private void DrawConnectedState()
    {
        GUILayout.Label(GetActiveModeLabel());
        GUILayout.Label($"Press {toggleKey} to hide/show this panel.");
        GUILayout.Label($"LAN IP: {lanAddressSummary}");
        GUILayout.Label($"Port: {port}");

        if (bootstrap.NetworkManager != null && bootstrap.NetworkManager.IsHost)
        {
            GUILayout.Label("Clients should join using the LAN IP above.");
        }

        GUILayout.Space(8f);
        if (GUILayout.Button("Shutdown"))
        {
            bootstrap.NetworkManager?.Shutdown();
            panelVisible = true;
            statusMessage = "Network shutdown.";
            lanAddressSummary = GetLanAddressSummary();
        }

        DrawStatusMessage();
    }

    private StartupChoice DrawModeToolbar(StartupChoice currentChoice)
    {
        if (allowServerMode)
        {
            return (StartupChoice)GUILayout.Toolbar((int)currentChoice, new[] { "Host", "Client", "Server" });
        }

        int toolbarValue = GUILayout.Toolbar(currentChoice == StartupChoice.Client ? 1 : 0, new[] { "Host", "Client" });
        return toolbarValue == 1 ? StartupChoice.Client : StartupChoice.Host;
    }

    private string DrawTextField(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(90f));
        string updatedValue = GUILayout.TextField(value ?? string.Empty, GUILayout.MinWidth(220f));
        GUILayout.EndHorizontal();
        return updatedValue;
    }

    private void DrawStatusMessage()
    {
        if (string.IsNullOrWhiteSpace(statusMessage))
        {
            return;
        }

        GUILayout.Space(8f);
        GUILayout.Label(statusMessage);
    }

    private void StartSelectedMode()
    {
        if (!ushort.TryParse(portInput, out ushort parsedPort))
        {
            statusMessage = "Port must be a number between 0 and 65535.";
            return;
        }

        port = parsedPort;
        lanAddressSummary = GetLanAddressSummary();

        switch (selectedMode)
        {
            case StartupChoice.Host:
                bootstrap.SetConnectionData("127.0.0.1", port, listenAddress);
                bootstrap.StartHost();
                break;
            case StartupChoice.Client:
                if (string.IsNullOrWhiteSpace(clientAddress))
                {
                    statusMessage = "Enter the host IP before starting a client.";
                    return;
                }

                bootstrap.SetConnectionData(clientAddress.Trim(), port, listenAddress);
                bootstrap.StartClient();
                break;
            case StartupChoice.Server:
                bootstrap.SetConnectionData("127.0.0.1", port, listenAddress);
                bootstrap.StartServer();
                break;
        }

        statusMessage = GetActiveModeLabel();
        if (!IsListening())
        {
            statusMessage = $"Network start requested for {selectedMode}. Check the Console if it does not connect.";
            return;
        }

        panelVisible = false;
        EnsureCursorState(false);
    }

    private static void EnsureCursorState(bool shouldShowPanel)
    {
        Cursor.visible = shouldShowPanel;
        Cursor.lockState = shouldShowPanel ? CursorLockMode.None : CursorLockMode.Locked;
    }

    private bool IsListening()
    {
        return bootstrap != null && bootstrap.NetworkManager != null && bootstrap.NetworkManager.IsListening;
    }

    private string GetActiveModeLabel()
    {
        if (bootstrap == null || bootstrap.NetworkManager == null)
        {
            return "NetworkManager is unavailable.";
        }

        if (bootstrap.NetworkManager.IsHost)
        {
            return "Running as Host.";
        }

        if (bootstrap.NetworkManager.IsServer)
        {
            return "Running as Dedicated Server.";
        }

        if (bootstrap.NetworkManager.IsClient)
        {
            return "Running as Client.";
        }

        return "Network is stopped.";
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

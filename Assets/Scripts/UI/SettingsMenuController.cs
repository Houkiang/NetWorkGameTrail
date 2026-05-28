using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkSessionService))]
public class SettingsMenuController : MonoBehaviour
{
    private enum MenuPage
    {
        Gameplay,
        Host,
        Server,
        Client,
        Status
    }

    [SerializeField]
    private KeyCode toggleKey = KeyCode.Escape;

    [SerializeField]
    private bool showOnStartWhenOffline = true;

    [SerializeField]
    [TextArea(6, 12)]
    private string gameplayHelpText =
        "Gameplay Help\n\n" +
        "WASD: Move\n" +
        "Space: Jump\n" +
        "LeftShift: Sprint\n" +
        "Esc: Open settings\n" +
        "F1: Toggle debug overlay\n\n" +
        "Host: local player plus host\n" +
        "Server: visual server without local player\n" +
        "Client: connect to another host";

    [Header("Text")]
    [SerializeField]
    private string menuTitleText = "Settings";

    [SerializeField]
    private string gameplayTabLabel = "Gameplay Help";

    [SerializeField]
    private string hostTabLabel = "Create Host";

    [SerializeField]
    private string serverTabLabel = "Create Server";

    [SerializeField]
    private string clientTabLabel = "Create Client";

    [SerializeField]
    private string statusTabLabel = "Connection Status";

    [SerializeField]
    private string hostShareHintText = "Share the LAN IP above with clients on the same network.";

    [SerializeField]
    private string serverDescriptionText = "Visual server mode keeps a local observer window and debug UI, but does not spawn a local player.";

    [SerializeField]
    private string clientDescriptionText = "Enter the host IP and matching port before starting a client connection.";

    [SerializeField]
    private string statusDescriptionText = "Review the current network mode and connection details here. You can also disconnect from this page.";

    [SerializeField]
    private string hostIpLabelText = "Host IP";

    [SerializeField]
    private string portLabelText = "Port";

    [SerializeField]
    private string listenAddressLabelText = "Listen Address";

    [SerializeField]
    private string startHostButtonText = "Start Host";

    [SerializeField]
    private string startServerButtonText = "Start Server";

    [SerializeField]
    private string startClientButtonText = "Start Client";

    [SerializeField]
    private string shutdownButtonText = "Shutdown / Disconnect";

    [SerializeField]
    private string closeButtonText = "Close";

    [SerializeField]
    private string lanPrefixText = "LAN IP:";

    [SerializeField]
    private string clientAddressPrefixText = "Client Address:";

    [SerializeField]
    private string listenAddressPrefixText = "Listen Address:";

    [SerializeField]
    private string portPrefixText = "Port:";

    [Header("Layout")]
    [SerializeField]
    private Vector2 windowSize = new Vector2(920f, 560f);

    [SerializeField]
    private float outerPadding = 18f;

    [SerializeField]
    private float bodySpacing = 16f;

    [SerializeField]
    private float sidebarWidth = 210f;

    [SerializeField]
    private float sidebarButtonHeight = 46f;

    [SerializeField]
    private float actionButtonHeight = 40f;

    [SerializeField]
    private float closeButtonHeight = 42f;

    [Header("Typography")]
    [SerializeField]
    private int titleFontSize = 28;

    [SerializeField]
    private int pageTitleFontSize = 24;

    [SerializeField]
    private int bodyFontSize = 18;

    [SerializeField]
    private int buttonFontSize = 18;

    private readonly Dictionary<MenuPage, GameObject> pageRoots = new Dictionary<MenuPage, GameObject>();
    private readonly Dictionary<MenuPage, Button> pageButtons = new Dictionary<MenuPage, Button>();

    private NetworkSessionService sessionService;
    private MenuPage currentPage = MenuPage.Gameplay;

    private Canvas canvas;
    private CanvasGroup menuGroup;
    private Text gameplayText;
    private Text hostLanText;
    private Text hostStatusText;
    private TMP_InputField hostPortField;
    private TMP_InputField hostListenAddressField;
    private Text serverLanText;
    private Text serverStatusText;
    private TMP_InputField serverPortField;
    private TMP_InputField serverListenAddressField;
    private Text clientStatusText;
    private TMP_InputField clientAddressField;
    private TMP_InputField clientPortField;
    private Text statusSummaryText;
    private Text statusDetailText;
    private Button closeButton;

    private void Awake()
    {
        sessionService = GetComponent<NetworkSessionService>();
        RuntimeCanvasUIFactory.EnsureEventSystem();
        RebuildCanvas();
        RefreshFieldsFromSession();
        ApplyVisibilityState();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        RebuildCanvas();
        RefreshFieldsFromSession();
        RefreshDynamicTexts();
        ApplyVisibilityState();
    }

    private void Start()
    {
        if (showOnStartWhenOffline && sessionService != null && !sessionService.IsListening)
        {
            OpenMenu(MenuPage.Host);
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            if (RuntimeUIState.IsSettingsMenuOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu(sessionService != null && sessionService.IsListening ? MenuPage.Status : MenuPage.Host);
            }
        }

        RefreshDynamicTexts();
        ApplyCursorState();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            ApplyCursorState();
        }
    }

    private void OnDestroy()
    {
        if (RuntimeUIState.IsSettingsMenuOpen)
        {
            RuntimeUIState.SetSettingsMenuOpen(false);
        }
    }

    private void RebuildCanvas()
    {
        if (canvas != null)
        {
            Destroy(canvas.gameObject);
        }

        pageRoots.Clear();
        pageButtons.Clear();
        gameplayText = null;
        hostLanText = null;
        hostStatusText = null;
        hostPortField = null;
        hostListenAddressField = null;
        serverLanText = null;
        serverStatusText = null;
        serverPortField = null;
        serverListenAddressField = null;
        clientStatusText = null;
        clientAddressField = null;
        clientPortField = null;
        statusSummaryText = null;
        statusDetailText = null;
        closeButton = null;
        menuGroup = null;
        canvas = null;

        BuildCanvas();
    }

    private void BuildCanvas()
    {
        canvas = RuntimeCanvasUIFactory.CreateScreenCanvas("SettingsMenuCanvas", transform, 120);
        menuGroup = canvas.gameObject.AddComponent<CanvasGroup>();

        Image dim = RuntimeCanvasUIFactory.CreateImage("Dim", canvas.transform, new Color(0f, 0f, 0f, 0.58f));
        RuntimeCanvasUIFactory.StretchToParent(dim.rectTransform);

        Image window = RuntimeCanvasUIFactory.CreateImage("Window", canvas.transform, new Color(0.09f, 0.11f, 0.14f, 0.96f));
        RectTransform windowRect = window.rectTransform;
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.sizeDelta = windowSize;

        RectTransform contentRoot = RuntimeCanvasUIFactory.CreateUIObject("ContentRoot", window.transform).GetComponent<RectTransform>();
        RuntimeCanvasUIFactory.StretchToParent(contentRoot, outerPadding, outerPadding, outerPadding, outerPadding);
        RuntimeCanvasUIFactory.AddVerticalLayout(contentRoot, 14f, new RectOffset(0, 0, 0, 0));

        Text title = RuntimeCanvasUIFactory.CreateText("Title", contentRoot, menuTitleText, titleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
        titleLayout.preferredHeight = 40f;

        RectTransform body = RuntimeCanvasUIFactory.CreateUIObject("Body", contentRoot).GetComponent<RectTransform>();
        RuntimeCanvasUIFactory.AddHorizontalLayout(body, bodySpacing);
        LayoutElement bodyLayout = body.gameObject.AddComponent<LayoutElement>();
        bodyLayout.flexibleHeight = 1f;

        RectTransform sidebar = RuntimeCanvasUIFactory.CreateUIObject("Sidebar", body).GetComponent<RectTransform>();
        LayoutElement sidebarLayout = sidebar.gameObject.AddComponent<LayoutElement>();
        sidebarLayout.preferredWidth = sidebarWidth;
        RuntimeCanvasUIFactory.AddVerticalLayout(sidebar, 8f);

        RectTransform pagesRoot = RuntimeCanvasUIFactory.CreateUIObject("PagesRoot", body).GetComponent<RectTransform>();
        LayoutElement pagesLayout = pagesRoot.gameObject.AddComponent<LayoutElement>();
        pagesLayout.flexibleWidth = 1f;
        pagesLayout.flexibleHeight = 1f;

        CreatePageButton(sidebar, MenuPage.Gameplay, gameplayTabLabel);
        CreatePageButton(sidebar, MenuPage.Host, hostTabLabel);
        CreatePageButton(sidebar, MenuPage.Server, serverTabLabel);
        CreatePageButton(sidebar, MenuPage.Client, clientTabLabel);
        CreatePageButton(sidebar, MenuPage.Status, statusTabLabel);

        CreateGameplayPage(pagesRoot);
        CreateHostPage(pagesRoot);
        CreateServerPage(pagesRoot);
        CreateClientPage(pagesRoot);
        CreateStatusPage(pagesRoot);

        closeButton = RuntimeCanvasUIFactory.CreateButton("CloseButton", contentRoot, closeButtonText, new Color(0.18f, 0.24f, 0.31f, 1f), Color.white, buttonFontSize);
        closeButton.onClick.AddListener(CloseMenu);
        LayoutElement closeLayout = closeButton.gameObject.AddComponent<LayoutElement>();
        closeLayout.preferredHeight = closeButtonHeight;
    }

    private void CreateGameplayPage(Transform parent)
    {
        GameObject page = CreatePageRoot(parent, MenuPage.Gameplay, gameplayTabLabel);
        gameplayText = CreateBodyText(page.transform, gameplayHelpText);
    }

    private void CreateHostPage(Transform parent)
    {
        GameObject page = CreatePageRoot(parent, MenuPage.Host, hostTabLabel);
        hostLanText = CreateBodyText(page.transform, string.Empty);
        CreateBodyText(page.transform, hostShareHintText);
        hostPortField = CreateLabeledInput(page.transform, portLabelText, "7777");
        hostListenAddressField = CreateLabeledInput(page.transform, listenAddressLabelText, "0.0.0.0");
        Button button = RuntimeCanvasUIFactory.CreateButton("StartHostButton", page.transform, startHostButtonText, new Color(0.2f, 0.33f, 0.28f, 1f), Color.white, buttonFontSize);
        button.onClick.AddListener(StartHostFromUI);
        LayoutElement buttonLayout = button.gameObject.AddComponent<LayoutElement>();
        buttonLayout.preferredHeight = actionButtonHeight;
        hostStatusText = CreateBodyText(page.transform, string.Empty);
    }

    private void CreateServerPage(Transform parent)
    {
        GameObject page = CreatePageRoot(parent, MenuPage.Server, serverTabLabel);
        serverLanText = CreateBodyText(page.transform, string.Empty);
        CreateBodyText(page.transform, serverDescriptionText);
        serverPortField = CreateLabeledInput(page.transform, portLabelText, "7777");
        serverListenAddressField = CreateLabeledInput(page.transform, listenAddressLabelText, "0.0.0.0");
        Button button = RuntimeCanvasUIFactory.CreateButton("StartServerButton", page.transform, startServerButtonText, new Color(0.29f, 0.25f, 0.16f, 1f), Color.white, buttonFontSize);
        button.onClick.AddListener(StartServerFromUI);
        LayoutElement buttonLayout = button.gameObject.AddComponent<LayoutElement>();
        buttonLayout.preferredHeight = actionButtonHeight;
        serverStatusText = CreateBodyText(page.transform, string.Empty);
    }

    private void CreateClientPage(Transform parent)
    {
        GameObject page = CreatePageRoot(parent, MenuPage.Client, clientTabLabel);
        CreateBodyText(page.transform, clientDescriptionText);
        clientAddressField = CreateLabeledInput(page.transform, hostIpLabelText, "127.0.0.1");
        clientPortField = CreateLabeledInput(page.transform, portLabelText, "7777");
        Button button = RuntimeCanvasUIFactory.CreateButton("StartClientButton", page.transform, startClientButtonText, new Color(0.17f, 0.27f, 0.36f, 1f), Color.white, buttonFontSize);
        button.onClick.AddListener(StartClientFromUI);
        LayoutElement buttonLayout = button.gameObject.AddComponent<LayoutElement>();
        buttonLayout.preferredHeight = actionButtonHeight;
        clientStatusText = CreateBodyText(page.transform, string.Empty);
    }

    private void CreateStatusPage(Transform parent)
    {
        GameObject page = CreatePageRoot(parent, MenuPage.Status, statusTabLabel);
        CreateBodyText(page.transform, statusDescriptionText);
        statusSummaryText = CreateBodyText(page.transform, string.Empty);
        statusDetailText = CreateBodyText(page.transform, string.Empty);
        Button button = RuntimeCanvasUIFactory.CreateButton("ShutdownButton", page.transform, shutdownButtonText, new Color(0.35f, 0.17f, 0.17f, 1f), Color.white, buttonFontSize);
        button.onClick.AddListener(ShutdownFromUI);
        LayoutElement buttonLayout = button.gameObject.AddComponent<LayoutElement>();
        buttonLayout.preferredHeight = actionButtonHeight;
    }

    private GameObject CreatePageRoot(Transform parent, MenuPage page, string title)
    {
        Image pageImage = RuntimeCanvasUIFactory.CreateImage($"{page}Page", parent, new Color(0.13f, 0.15f, 0.18f, 0.96f));
        RectTransform pageRect = pageImage.rectTransform;
        RuntimeCanvasUIFactory.StretchToParent(pageRect);
        RuntimeCanvasUIFactory.AddVerticalLayout(pageRect, 10f, new RectOffset(20, 20, 20, 20));

        LayoutElement layout = pageImage.gameObject.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.flexibleHeight = 1f;

        Text header = RuntimeCanvasUIFactory.CreateText("Header", pageRect, title, pageTitleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        LayoutElement headerLayout = header.gameObject.AddComponent<LayoutElement>();
        headerLayout.preferredHeight = 34f;

        pageRoots[page] = pageImage.gameObject;
        return pageImage.gameObject;
    }

    private void CreatePageButton(Transform parent, MenuPage page, string label)
    {
        Button button = RuntimeCanvasUIFactory.CreateButton($"{page}Button", parent, label, new Color(0.18f, 0.2f, 0.25f, 1f), Color.white, buttonFontSize);
        button.onClick.AddListener(() => SwitchPage(page));
        LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = sidebarButtonHeight;
        pageButtons[page] = button;
    }

    private Text CreateBodyText(Transform parent, string content)
    {
        Text text = RuntimeCanvasUIFactory.CreateText("BodyText", parent, content, bodyFontSize, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.92f));
        ContentSizeFitter fitter = text.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return text;
    }

    private TMP_InputField CreateLabeledInput(Transform parent, string label, string placeholder)
    {
        RectTransform row = RuntimeCanvasUIFactory.CreateUIObject($"{label}Row", parent).GetComponent<RectTransform>();
        RuntimeCanvasUIFactory.AddHorizontalLayout(row, 12f);
        LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
        rowLayout.preferredHeight = 44f;

        Text labelText = RuntimeCanvasUIFactory.CreateText("Label", row, label, bodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        LayoutElement labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 140f;

        TMP_InputField inputField = RuntimeCanvasUIFactory.CreateInputField("Input", row, placeholder, new Color(0.88f, 0.9f, 0.93f, 1f), bodyFontSize);
        LayoutElement inputLayout = inputField.gameObject.AddComponent<LayoutElement>();
        inputLayout.flexibleWidth = 1f;
        return inputField;
    }

    private void RefreshFieldsFromSession()
    {
        if (sessionService == null)
        {
            return;
        }

        string port = sessionService.Port.ToString();
        string listenAddress = sessionService.ListenAddress;
        string clientAddress = sessionService.ClientAddress;

        ApplyInputFieldValue(hostPortField, port);
        ApplyInputFieldValue(serverPortField, port);
        ApplyInputFieldValue(clientPortField, port);
        ApplyInputFieldValue(hostListenAddressField, listenAddress);
        ApplyInputFieldValue(serverListenAddressField, listenAddress);
        ApplyInputFieldValue(clientAddressField, clientAddress);
    }

    private void RefreshDynamicTexts()
    {
        if (sessionService == null)
        {
            return;
        }

        if (gameplayText != null)
        {
            gameplayText.text = gameplayHelpText;
        }

        if (hostLanText != null)
        {
            hostLanText.text = $"{lanPrefixText} {sessionService.LanAddressSummary}";
        }

        if (serverLanText != null)
        {
            serverLanText.text = $"{lanPrefixText} {sessionService.LanAddressSummary}";
        }

        if (hostStatusText != null)
        {
            hostStatusText.text = sessionService.LastStatusMessage;
        }

        if (serverStatusText != null)
        {
            serverStatusText.text = sessionService.LastStatusMessage;
        }

        if (clientStatusText != null)
        {
            clientStatusText.text = sessionService.LastStatusMessage;
        }

        if (statusSummaryText != null)
        {
            statusSummaryText.text = $"{sessionService.GetActiveModeLabel()}\n{sessionService.GetConnectionSummary()}";
        }

        if (statusDetailText != null)
        {
            statusDetailText.text =
                $"{lanPrefixText} {sessionService.LanAddressSummary}\n" +
                $"{clientAddressPrefixText} {sessionService.ClientAddress}\n" +
                $"{listenAddressPrefixText} {sessionService.ListenAddress}\n" +
                $"{portPrefixText} {sessionService.Port}\n\n" +
                $"{sessionService.LastStatusMessage}";
        }

        if (pageButtons.TryGetValue(MenuPage.Server, out Button serverButton))
        {
            serverButton.interactable = sessionService.AllowServerMode;
        }

        if (closeButton != null)
        {
            closeButton.interactable = true;
        }

        ApplyVisibilityState();
    }

    private void SwitchPage(MenuPage page)
    {
        currentPage = page;
        ApplyVisibilityState();
        RefreshFieldsFromSession();
        RefreshDynamicTexts();
    }

    private void ApplyVisibilityState()
    {
        bool isVisible = RuntimeUIState.IsSettingsMenuOpen;
        if (menuGroup != null)
        {
            menuGroup.alpha = isVisible ? 1f : 0f;
            menuGroup.interactable = isVisible;
            menuGroup.blocksRaycasts = isVisible;
        }

        foreach (KeyValuePair<MenuPage, GameObject> entry in pageRoots)
        {
            entry.Value.SetActive(entry.Key == currentPage);
        }

        foreach (KeyValuePair<MenuPage, Button> entry in pageButtons)
        {
            Color targetColor = entry.Key == currentPage
                ? new Color(0.33f, 0.44f, 0.58f, 1f)
                : new Color(0.18f, 0.2f, 0.25f, 1f);
            Image image = entry.Value.GetComponent<Image>();
            if (image != null)
            {
                image.color = targetColor;
            }
        }
    }

    private bool TryApplyConnectionInputs()
    {
        if (sessionService == null)
        {
            return false;
        }

        TMP_InputField activePortField = currentPage == MenuPage.Client ? clientPortField : currentPage == MenuPage.Server ? serverPortField : hostPortField;
        TMP_InputField activeListenField = currentPage == MenuPage.Server ? serverListenAddressField : hostListenAddressField;

        if (activePortField == null || !ushort.TryParse(activePortField.text, out ushort parsedPort))
        {
            return false;
        }

        sessionService.Port = parsedPort;

        if (activeListenField != null)
        {
            sessionService.ListenAddress = activeListenField.text;
        }

        if (clientAddressField != null)
        {
            sessionService.ClientAddress = clientAddressField.text;
        }

        return true;
    }

    private void StartHostFromUI()
    {
        if (TryApplyConnectionInputs() && sessionService.TryStartHost(out _))
        {
            CloseMenu();
        }

        SwitchPage(MenuPage.Status);
    }

    private void StartServerFromUI()
    {
        if (TryApplyConnectionInputs())
        {
            sessionService.TryStartServer(out _);
        }

        SwitchPage(MenuPage.Status);
    }

    private void StartClientFromUI()
    {
        if (TryApplyConnectionInputs() && sessionService.TryStartClient(out _))
        {
            CloseMenu();
        }

        SwitchPage(MenuPage.Status);
    }

    private void ShutdownFromUI()
    {
        sessionService.Shutdown();
        SwitchPage(MenuPage.Status);
    }

    private void OpenMenu(MenuPage fallbackPage)
    {
        if (currentPage == MenuPage.Server && !sessionService.AllowServerMode)
        {
            currentPage = fallbackPage;
        }
        else if (!sessionService.IsListening)
        {
            currentPage = fallbackPage;
        }

        RuntimeUIState.SetSettingsMenuOpen(true);
        ApplyCursorState();
        RefreshFieldsFromSession();
        RefreshDynamicTexts();
        ApplyVisibilityState();
    }

    private void CloseMenu()
    {
        RuntimeUIState.SetSettingsMenuOpen(false);
        ApplyCursorState();
        ApplyVisibilityState();
    }

    private void ApplyCursorState()
    {
        if (RuntimeUIState.IsSettingsMenuOpen)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            return;
        }

        if (sessionService != null && sessionService.HasLocalGameplayRole)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        else
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
    }

    private static void ApplyInputFieldValue(TMP_InputField inputField, string value)
    {
        if (inputField == null)
        {
            return;
        }

        inputField.SetTextWithoutNotify(value ?? string.Empty);
        inputField.ForceLabelUpdate();
    }
}

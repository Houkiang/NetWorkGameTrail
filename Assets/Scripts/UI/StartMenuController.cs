using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class StartMenuController : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField]
    private string gameplaySceneName = "Playground";

    [Header("Background")]
    [SerializeField]
    private Sprite backgroundSprite;

    [SerializeField]
    private Color fallbackBackgroundColor = new Color(0.06f, 0.08f, 0.12f, 1f);

    [SerializeField]
    private bool preserveBackgroundAspect = false;

    [Header("Button")]
    [SerializeField]
    private Sprite startButtonSprite;

    [SerializeField]
    private string startButtonText = "开始游戏";

    [SerializeField]
    private Vector2 startButtonSize = new Vector2(320f, 80f);

    [SerializeField]
    private Vector2 startButtonOffset = new Vector2(0f, 120f);

    [SerializeField]
    private Color startButtonColor = new Color(0.18f, 0.54f, 0.28f, 1f);

    [SerializeField]
    private Color startButtonTextColor = Color.white;

    [SerializeField]
    private bool showStartButtonText = false;

    [SerializeField]
    private bool preserveStartButtonAspect = true;

    [SerializeField]
    private int startButtonFontSize = 28;

    [Header("Overlay")]
    [SerializeField]
    private Color dimOverlayColor = new Color(0f, 0f, 0f, 0.18f);

    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private Button startButton;
    private bool isLoading;

    private void Awake()
    {
        RuntimeUIState.SetSettingsMenuOpen(false);
        RuntimeUIState.SetDebugOverlayVisible(false);
        RuntimeCanvasUIFactory.EnsureEventSystem();
        BuildMenu();
        ApplyCursorState();
    }

    private void OnDestroy()
    {
        ApplyCursorState(resetToGameplay: true);
    }

    private void BuildMenu()
    {
        if (canvas != null)
        {
            Destroy(canvas.gameObject);
        }

        canvas = RuntimeCanvasUIFactory.CreateScreenCanvas("StartMenuCanvas", transform, 300);
        canvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();

        Image background = RuntimeCanvasUIFactory.CreateImage("Background", canvas.transform, fallbackBackgroundColor);
        RuntimeCanvasUIFactory.StretchToParent(background.rectTransform);
        background.sprite = backgroundSprite;
        background.preserveAspect = preserveBackgroundAspect;
        background.type = Image.Type.Simple;

        Image overlay = RuntimeCanvasUIFactory.CreateImage("DimOverlay", canvas.transform, dimOverlayColor);
        RuntimeCanvasUIFactory.StretchToParent(overlay.rectTransform);
        overlay.raycastTarget = false;

        startButton = CreateStartButton();

        RectTransform buttonRect = startButton.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = startButtonSize;
        buttonRect.anchoredPosition = startButtonOffset;

        startButton.onClick.AddListener(HandleStartClicked);
    }

    private Button CreateStartButton()
    {
        Image image = RuntimeCanvasUIFactory.CreateImage("StartButton", canvas.transform, startButtonColor);
        image.sprite = startButtonSprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = preserveStartButtonAspect;

        Button button = image.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.85f);
        button.colors = colors;

        if (showStartButtonText && !string.IsNullOrWhiteSpace(startButtonText))
        {
            Text label = RuntimeCanvasUIFactory.CreateText(
                "Label",
                button.transform,
                startButtonText,
                startButtonFontSize,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                startButtonTextColor);
            RuntimeCanvasUIFactory.StretchToParent(label.rectTransform, 8f, 8f, 8f, 8f);
        }

        return button;
    }

    private void HandleStartClicked()
    {
        if (isLoading)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(gameplaySceneName))
        {
            Debug.LogError("StartMenuController gameplaySceneName is empty.");
            return;
        }

        isLoading = true;
        if (startButton != null)
        {
            startButton.interactable = false;
        }

        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        RuntimeUIState.SetSettingsMenuOpen(false);
        RuntimeUIState.SetDebugOverlayVisible(false);
        SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
    }

    private static void ApplyCursorState(bool resetToGameplay = false)
    {
        if (resetToGameplay)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            return;
        }

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
}

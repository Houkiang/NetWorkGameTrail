using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class DebugOverlayController : MonoBehaviour
{
    private sealed class SectionView
    {
        public GameObject Root;
        public Text Title;
        public Text Body;
    }

    [SerializeField]
    private KeyCode toggleKey = KeyCode.F1;

    [SerializeField]
    private bool visibleOnStart;

    [SerializeField]
    private float panelWidth = 360f;

    [SerializeField]
    private float panelMargin = 16f;

    [Header("Text")]
    [SerializeField]
    private string overlayTitleText = "Debug Overlay";

    [SerializeField]
    private string overlaySubtitleText = "F1 Toggle / Esc Settings";

    [Header("Typography")]
    [SerializeField]
    private int titleFontSize = 22;

    [SerializeField]
    private int subtitleFontSize = 14;

    [SerializeField]
    private int sectionTitleFontSize = 18;

    [SerializeField]
    private int bodyFontSize = 15;

    private readonly List<string> sharedLineBuffer = new List<string>(32);
    private readonly List<SectionView> sectionViews = new List<SectionView>(8);

    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private RectTransform contentRoot;
    private bool rebuildQueued;

    private void Awake()
    {
        RuntimeCanvasUIFactory.EnsureEventSystem();
        RebuildCanvas();
        RuntimeUIState.SetDebugOverlayVisible(visibleOnStart);
        ApplyVisibilityState();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        QueueRebuildCanvas();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            RuntimeUIState.ToggleDebugOverlay();
        }

        ApplyVisibilityState();
        RefreshDebugContents();
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= PerformQueuedRebuild;
#endif
    }

    private void RebuildCanvas()
    {
        rebuildQueued = false;

        if (canvas != null)
        {
            Destroy(canvas.gameObject);
        }

        sectionViews.Clear();
        contentRoot = null;
        canvasGroup = null;
        canvas = null;
        BuildCanvas();
    }

    private void QueueRebuildCanvas()
    {
        if (rebuildQueued)
        {
            return;
        }

        rebuildQueued = true;

#if UNITY_EDITOR
        EditorApplication.delayCall += PerformQueuedRebuild;
#else
        Invoke(nameof(PerformQueuedRebuild), 0f);
#endif
    }

    private void PerformQueuedRebuild()
    {
        if (this == null)
        {
            return;
        }

#if UNITY_EDITOR
        EditorApplication.delayCall -= PerformQueuedRebuild;
#endif

        if (!Application.isPlaying)
        {
            rebuildQueued = false;
            return;
        }

        RebuildCanvas();
        ApplyVisibilityState();
        RefreshDebugContents();
    }

    private void BuildCanvas()
    {
        canvas = RuntimeCanvasUIFactory.CreateScreenCanvas("DebugOverlayCanvas", transform, 90);
        canvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        Image panel = RuntimeCanvasUIFactory.CreateImage("Panel", canvas.transform, new Color(0f, 0f, 0f, 0.42f));
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(panelMargin, -panelMargin);
        panelRect.sizeDelta = new Vector2(panelWidth, -panelMargin * 2f);

        RectTransform header = RuntimeCanvasUIFactory.CreateUIObject("Header", panel.transform).GetComponent<RectTransform>();
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.anchoredPosition = new Vector2(0f, -12f);
        header.sizeDelta = new Vector2(0f, 42f);
        Text title = RuntimeCanvasUIFactory.CreateText("Title", header, overlayTitleText, titleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        RuntimeCanvasUIFactory.StretchToParent(title.rectTransform, 12f, 12f, 0f, 0f);

        RectTransform subtitle = RuntimeCanvasUIFactory.CreateUIObject("Subtitle", panel.transform).GetComponent<RectTransform>();
        subtitle.anchorMin = new Vector2(0f, 1f);
        subtitle.anchorMax = new Vector2(1f, 1f);
        subtitle.pivot = new Vector2(0.5f, 1f);
        subtitle.anchoredPosition = new Vector2(0f, -48f);
        subtitle.sizeDelta = new Vector2(0f, 24f);
        Text subtitleText = RuntimeCanvasUIFactory.CreateText("SubtitleText", subtitle, overlaySubtitleText, subtitleFontSize, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.92f, 0.92f, 0.92f));
        RuntimeCanvasUIFactory.StretchToParent(subtitleText.rectTransform, 12f, 12f, 0f, 0f);

        ScrollRect scrollRect = RuntimeCanvasUIFactory.CreateScrollView("ScrollView", panel.transform, out contentRoot);
        RectTransform scrollRectTransform = scrollRect.GetComponent<RectTransform>();
        scrollRectTransform.anchorMin = new Vector2(0f, 0f);
        scrollRectTransform.anchorMax = new Vector2(1f, 1f);
        scrollRectTransform.offsetMin = new Vector2(0f, 12f);
        scrollRectTransform.offsetMax = new Vector2(0f, -80f);
    }

    private void ApplyVisibilityState()
    {
        bool visible = RuntimeUIState.IsDebugOverlayVisible;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
        }
    }

    private void RefreshDebugContents()
    {
        if (contentRoot == null || !RuntimeUIState.IsDebugOverlayVisible)
        {
            return;
        }

        IReadOnlyList<IDebugPanelProvider> providers = DebugPanelRegistry.RegisteredProviders;
        int sectionIndex = 0;
        for (int i = 0; i < providers.Count; i++)
        {
            IDebugPanelProvider provider = providers[i];
            if (provider == null || !provider.ShouldDisplayInDebugOverlay)
            {
                continue;
            }

            sharedLineBuffer.Clear();
            provider.AppendDebugLines(sharedLineBuffer);
            if (sharedLineBuffer.Count == 0)
            {
                continue;
            }

            SectionView sectionView = GetOrCreateSectionView(sectionIndex++);
            sectionView.Root.SetActive(true);
            sectionView.Title.text = provider.DebugSectionTitle;
            sectionView.Body.text = string.Join("\n", sharedLineBuffer);
        }

        for (int i = sectionIndex; i < sectionViews.Count; i++)
        {
            sectionViews[i].Root.SetActive(false);
        }
    }

    private SectionView GetOrCreateSectionView(int index)
    {
        if (index < sectionViews.Count)
        {
            return sectionViews[index];
        }

        RectTransform sectionRoot = RuntimeCanvasUIFactory.CreateUIObject($"Section{index}", contentRoot).GetComponent<RectTransform>();
        RuntimeCanvasUIFactory.AddVerticalLayout(sectionRoot, 4f, new RectOffset(0, 0, 0, 6));

        Text title = RuntimeCanvasUIFactory.CreateText("SectionTitle", sectionRoot, string.Empty, sectionTitleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

        Text body = RuntimeCanvasUIFactory.CreateText("SectionBody", sectionRoot, string.Empty, bodyFontSize, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.93f, 0.93f, 0.93f));
        ContentSizeFitter fitter = body.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        SectionView sectionView = new SectionView
        {
            Root = sectionRoot.gameObject,
            Title = title,
            Body = body,
        };

        sectionViews.Add(sectionView);
        return sectionView;
    }
}

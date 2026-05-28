using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System;

public static class RuntimeCanvasUIFactory
{
    private static Font cachedFont;
    private static TMP_FontAsset cachedTmpFontAsset;

    public static Font DefaultFont
    {
        get
        {
            if (cachedFont == null)
            {
                cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            return cachedFont;
        }
    }

    public static TMP_FontAsset DefaultTmpFont
    {
        get
        {
            if (cachedTmpFontAsset == null)
            {
                cachedTmpFontAsset = TMP_FontAsset.CreateFontAsset(DefaultFont);
            }

            return cachedTmpFontAsset;
        }
    }

    public static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();

        Type inputSystemModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
        if (inputSystemModuleType != null)
        {
            eventSystemObject.AddComponent(inputSystemModuleType);
        }
        else
        {
            eventSystemObject.AddComponent<StandaloneInputModule>();
        }
    }

    public static Canvas CreateScreenCanvas(string name, Transform parent, int sortingOrder)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(parent, false);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform rect = root.GetComponent<RectTransform>();
        StretchToParent(rect);

        return canvas;
    }

    public static GameObject CreateUIObject(string name, Transform parent, params System.Type[] extraComponents)
    {
        System.Type[] componentTypes = new System.Type[extraComponents.Length + 1];
        componentTypes[0] = typeof(RectTransform);
        for (int i = 0; i < extraComponents.Length; i++)
        {
            componentTypes[i + 1] = extraComponents[i];
        }

        GameObject gameObject = new GameObject(name, componentTypes);
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    public static Image CreateImage(string name, Transform parent, Color color)
    {
        Image image = CreateUIObject(name, parent, typeof(Image)).GetComponent<Image>();
        image.color = color;
        return image;
    }

    public static Text CreateText(string name, Transform parent, string content, int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color)
    {
        Text text = CreateUIObject(name, parent, typeof(Text)).GetComponent<Text>();
        text.font = DefaultFont;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    public static Button CreateButton(string name, Transform parent, string label, Color backgroundColor, Color textColor, int fontSize = 18)
    {
        Image image = CreateImage(name, parent, backgroundColor);
        Button button = image.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = backgroundColor;
        colors.highlightedColor = backgroundColor * 1.1f;
        colors.pressedColor = backgroundColor * 0.9f;
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(backgroundColor.r * 0.5f, backgroundColor.g * 0.5f, backgroundColor.b * 0.5f, backgroundColor.a * 0.8f);
        button.colors = colors;

        Text text = CreateText("Label", button.transform, label, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, textColor);
        StretchToParent(text.rectTransform, 8f, 8f, 8f, 8f);
        return button;
    }

    public static TMP_InputField CreateInputField(string name, Transform parent, string placeholder, Color backgroundColor, int fontSize = 18)
    {
        Image background = CreateImage(name, parent, backgroundColor);
        TMP_InputField inputField = background.gameObject.AddComponent<TMP_InputField>();

        RectTransform textArea = CreateUIObject("Text Area", background.transform).GetComponent<RectTransform>();
        StretchToParent(textArea, 14f, 14f, 10f, 10f);

        TextMeshProUGUI text = CreateUIObject("Text", textArea, typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.font = DefaultTmpFont;
        text.text = string.Empty;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Normal;
        text.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        StretchToParent(text.rectTransform);

        TextMeshProUGUI placeholderText = CreateUIObject("Placeholder", textArea, typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        placeholderText.font = DefaultTmpFont;
        placeholderText.text = placeholder;
        placeholderText.fontSize = fontSize;
        placeholderText.fontStyle = FontStyles.Italic;
        placeholderText.color = new Color(0.45f, 0.45f, 0.45f, 1f);
        placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
        StretchToParent(placeholderText.rectTransform);

        inputField.textComponent = text;
        inputField.placeholder = placeholderText;
        inputField.targetGraphic = background;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.interactable = true;
        inputField.customCaretColor = true;
        inputField.caretColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        inputField.selectionColor = new Color(0.24f, 0.48f, 0.82f, 0.35f);
        inputField.ForceLabelUpdate();

        return inputField;
    }

    public static ScrollRect CreateScrollView(string name, Transform parent, out RectTransform contentRoot)
    {
        Image root = CreateImage(name, parent, new Color(0f, 0f, 0f, 0.15f));
        ScrollRect scrollRect = root.gameObject.AddComponent<ScrollRect>();

        GameObject viewportObject = CreateUIObject("Viewport", root.transform, typeof(Image), typeof(RectMask2D));
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        StretchToParent(viewport);
        Image viewportImage = viewportObject.GetComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.01f);

        GameObject contentObject = CreateUIObject("Content", viewport, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentRoot = contentObject.GetComponent<RectTransform>();
        contentRoot.anchorMin = new Vector2(0f, 1f);
        contentRoot.anchorMax = new Vector2(1f, 1f);
        contentRoot.pivot = new Vector2(0.5f, 1f);
        contentRoot.anchoredPosition = Vector2.zero;
        contentRoot.sizeDelta = new Vector2(0f, 0f);

        VerticalLayoutGroup layoutGroup = contentObject.GetComponent<VerticalLayoutGroup>();
        layoutGroup.childAlignment = TextAnchor.UpperLeft;
        layoutGroup.childControlHeight = true;
        layoutGroup.childControlWidth = true;
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childForceExpandWidth = true;
        layoutGroup.spacing = 8f;
        layoutGroup.padding = new RectOffset(12, 12, 12, 12);

        ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        scrollRect.viewport = viewport;
        scrollRect.content = contentRoot;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 24f;

        return scrollRect;
    }

    public static VerticalLayoutGroup AddVerticalLayout(RectTransform rectTransform, float spacing, RectOffset padding = null)
    {
        VerticalLayoutGroup layout = rectTransform.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        if (padding != null)
        {
            layout.padding = padding;
        }

        return layout;
    }

    public static HorizontalLayoutGroup AddHorizontalLayout(RectTransform rectTransform, float spacing, RectOffset padding = null)
    {
        HorizontalLayoutGroup layout = rectTransform.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        if (padding != null)
        {
            layout.padding = padding;
        }

        return layout;
    }

    public static void StretchToParent(RectTransform rectTransform, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = new Vector2(left, bottom);
        rectTransform.offsetMax = new Vector2(-right, -top);
    }
}

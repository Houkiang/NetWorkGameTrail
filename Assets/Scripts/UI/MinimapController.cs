using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class MinimapController : NetworkBehaviour
{
    private const float RemoteRefreshInterval = 0.25f;

    [Header("Layout")]
    [SerializeField]
    private float minimapDiameter = 220f;

    [SerializeField]
    private float minimapMargin = 24f;

    [SerializeField]
    private float minimapInset = 8f;

    [SerializeField]
    private float playerIconSize = 36f;

    [Header("Camera")]
    [SerializeField]
    private float cameraHeight = 90f;

    [SerializeField]
    private float orthographicSize = 32f;

    [SerializeField]
    private int renderTextureSize = 512;

    [SerializeField]
    private LayerMask minimapCullingMask = ~0;

    [Header("Style")]
    [SerializeField]
    private Color backgroundColor = new Color(0.04f, 0.09f, 0.18f, 1f);

    [SerializeField]
    private Color borderColor = new Color(0f, 0f, 0f, 1f);

    [SerializeField]
    private Color sceneOverlayColor = new Color(0.02f, 0.08f, 0.18f, 0.45f);

    [SerializeField]
    private float borderThickness = 6f;

    [SerializeField]
    private Color selfArrowColor = new Color(0.24f, 1f, 0.52f, 1f);

    [SerializeField]
    private Color otherPlayerArrowColor = new Color(1f, 0.72f, 0.22f, 1f);

    private static Sprite circleSprite;
    private static Sprite arrowSprite;

    private readonly Dictionary<ulong, RectTransform> remoteIcons = new Dictionary<ulong, RectTransform>();
    private readonly List<PlayerController> cachedPlayers = new List<PlayerController>();

    private Camera minimapCamera;
    private RenderTexture minimapTexture;
    private Canvas minimapCanvas;
    private CanvasGroup minimapCanvasGroup;
    private RectTransform minimapRootRect;
    private RectTransform iconRoot;
    private RectTransform selfIcon;
    private RawImage mapImage;
    private Image mapOverlay;

    private float playerRefreshTimer;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        BuildMinimapCamera();
        BuildMinimapCanvas();
        RefreshTrackedPlayers(force: true);
        ApplyVisibility();
    }

    public override void OnNetworkDespawn()
    {
        CleanupRuntimeObjects();
        base.OnNetworkDespawn();
    }

    private void OnDestroy()
    {
        CleanupRuntimeObjects();
    }

    private void Update()
    {
        if (!IsOwner || minimapCamera == null || iconRoot == null)
        {
            return;
        }

        UpdateCameraTransform();
        RefreshTrackedPlayers(force: false);
        UpdatePlayerIcons();
        ApplyVisibility();
    }

    private void BuildMinimapCamera()
    {
        GameObject cameraObject = new GameObject("MinimapCamera");
        cameraObject.transform.SetParent(transform, false);

        minimapCamera = cameraObject.AddComponent<Camera>();
        minimapCamera.orthographic = true;
        minimapCamera.orthographicSize = orthographicSize;
        minimapCamera.clearFlags = CameraClearFlags.SolidColor;
        minimapCamera.backgroundColor = backgroundColor;
        minimapCamera.nearClipPlane = 0.1f;
        minimapCamera.farClipPlane = cameraHeight + 250f;
        minimapCamera.allowHDR = false;
        minimapCamera.allowMSAA = false;
        minimapCamera.useOcclusionCulling = false;
        minimapCamera.cullingMask = minimapCullingMask.value == 0 ? ~0 : minimapCullingMask.value;

        minimapTexture = new RenderTexture(renderTextureSize, renderTextureSize, 16, RenderTextureFormat.ARGB32);
        minimapTexture.name = "MinimapTexture";
        minimapTexture.Create();
        minimapCamera.targetTexture = minimapTexture;

        UpdateCameraTransform();
    }

    private void BuildMinimapCanvas()
    {
        RuntimeCanvasUIFactory.EnsureEventSystem();

        minimapCanvas = RuntimeCanvasUIFactory.CreateScreenCanvas("MinimapCanvas", transform, 70);
        minimapCanvasGroup = minimapCanvas.gameObject.AddComponent<CanvasGroup>();
        minimapCanvasGroup.interactable = false;
        minimapCanvasGroup.blocksRaycasts = false;

        Image frame = RuntimeCanvasUIFactory.CreateImage("MinimapFrame", minimapCanvas.transform, Color.black);
        frame.sprite = GetCircleSprite();
        frame.type = Image.Type.Simple;
        minimapRootRect = frame.rectTransform;
        minimapRootRect.anchorMin = new Vector2(1f, 1f);
        minimapRootRect.anchorMax = new Vector2(1f, 1f);
        minimapRootRect.pivot = new Vector2(1f, 1f);
        minimapRootRect.sizeDelta = Vector2.one * minimapDiameter;
        minimapRootRect.anchoredPosition = new Vector2(-minimapMargin, -minimapMargin);

        GameObject viewportObject = RuntimeCanvasUIFactory.CreateUIObject("Viewport", frame.transform, typeof(Image), typeof(Mask));
        RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
        float totalInset = Mathf.Max(minimapInset, borderThickness);
        RuntimeCanvasUIFactory.StretchToParent(viewportRect, totalInset, totalInset, totalInset, totalInset);

        Image viewportImage = viewportObject.GetComponent<Image>();
        viewportImage.sprite = GetCircleSprite();
        viewportImage.color = backgroundColor;

        Mask viewportMask = viewportObject.GetComponent<Mask>();
        viewportMask.showMaskGraphic = true;

        mapImage = RuntimeCanvasUIFactory.CreateUIObject("MapImage", viewportRect, typeof(RawImage)).GetComponent<RawImage>();
        mapImage.texture = minimapTexture;
        mapImage.color = Color.white;
        mapImage.raycastTarget = false;
        RuntimeCanvasUIFactory.StretchToParent(mapImage.rectTransform);

        mapOverlay = RuntimeCanvasUIFactory.CreateImage("MapOverlay", viewportRect, sceneOverlayColor);
        RuntimeCanvasUIFactory.StretchToParent(mapOverlay.rectTransform);
        mapOverlay.raycastTarget = false;

        iconRoot = RuntimeCanvasUIFactory.CreateUIObject("IconRoot", viewportRect).GetComponent<RectTransform>();
        RuntimeCanvasUIFactory.StretchToParent(iconRoot);

        selfIcon = CreateArrowIcon("SelfIcon", iconRoot, selfArrowColor);
        selfIcon.anchoredPosition = Vector2.zero;
        selfIcon.sizeDelta = Vector2.one * playerIconSize;
    }

    private void UpdateCameraTransform()
    {
        Vector3 position = transform.position;
        minimapCamera.transform.position = new Vector3(position.x, position.y + cameraHeight, position.z);
        minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private void RefreshTrackedPlayers(bool force)
    {
        playerRefreshTimer -= Time.deltaTime;
        if (!force && playerRefreshTimer > 0f)
        {
            return;
        }

        playerRefreshTimer = RemoteRefreshInterval;
        cachedPlayers.Clear();
        cachedPlayers.AddRange(FindObjectsOfType<PlayerController>(true));

        HashSet<ulong> activeOwners = new HashSet<ulong>();
        foreach (PlayerController player in cachedPlayers)
        {
            if (player == null || !player.IsSpawned)
            {
                continue;
            }

            activeOwners.Add(player.OwnerClientId);

            if (player.OwnerClientId == OwnerClientId || remoteIcons.ContainsKey(player.OwnerClientId))
            {
                continue;
            }

            RectTransform icon = CreateArrowIcon($"RemoteIcon_{player.OwnerClientId}", iconRoot, otherPlayerArrowColor);
            icon.sizeDelta = Vector2.one * playerIconSize;
            remoteIcons[player.OwnerClientId] = icon;
        }

        List<ulong> staleOwners = new List<ulong>();
        foreach (KeyValuePair<ulong, RectTransform> entry in remoteIcons)
        {
            if (!activeOwners.Contains(entry.Key))
            {
                staleOwners.Add(entry.Key);
            }
        }

        foreach (ulong ownerId in staleOwners)
        {
            if (remoteIcons.TryGetValue(ownerId, out RectTransform icon) && icon != null)
            {
                Destroy(icon.gameObject);
            }

            remoteIcons.Remove(ownerId);
        }
    }

    private void UpdatePlayerIcons()
    {
        Vector3 ownerPosition = transform.position;
        float radius = Mathf.Max(0f, minimapDiameter * 0.5f - Mathf.Max(minimapInset, borderThickness) - playerIconSize * 0.5f);

        if (selfIcon != null)
        {
            selfIcon.anchoredPosition = Vector2.zero;
            selfIcon.localRotation = Quaternion.Euler(0f, 0f, -transform.eulerAngles.y);
        }

        foreach (PlayerController player in cachedPlayers)
        {
            if (player == null || !player.IsSpawned || player.OwnerClientId == OwnerClientId)
            {
                continue;
            }

            if (!remoteIcons.TryGetValue(player.OwnerClientId, out RectTransform icon) || icon == null)
            {
                continue;
            }

            Vector3 delta = player.transform.position - ownerPosition;
            Vector2 minimapOffset = new Vector2(delta.x, delta.z) / Mathf.Max(orthographicSize, 0.01f) * radius;
            if (minimapOffset.sqrMagnitude > radius * radius)
            {
                minimapOffset = minimapOffset.normalized * radius;
            }

            icon.anchoredPosition = minimapOffset;
            icon.localRotation = Quaternion.Euler(0f, 0f, -player.transform.eulerAngles.y);
        }
    }

    private void ApplyVisibility()
    {
        if (minimapCanvasGroup == null)
        {
            return;
        }

        bool shouldShow = !RuntimeUIState.IsSettingsMenuOpen;
        minimapCanvasGroup.alpha = shouldShow ? 1f : 0f;
        minimapCanvasGroup.blocksRaycasts = false;
        minimapCanvasGroup.interactable = false;
    }

    private RectTransform CreateArrowIcon(string objectName, Transform parent, Color color)
    {
        Image icon = RuntimeCanvasUIFactory.CreateImage(objectName, parent, color);
        icon.sprite = GetArrowSprite();
        icon.type = Image.Type.Simple;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        return icon.rectTransform;
    }

    private static Sprite GetCircleSprite()
    {
        if (circleSprite != null)
        {
            return circleSprite;
        }

        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
        texture.name = "MinimapCircleSprite";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = (size - 1) * 0.5f;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                pixels[y * size + x] = distance <= radius ? Color.white : Color.clear;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        circleSprite.name = "MinimapCircleSprite";
        return circleSprite;
    }

    private static Sprite GetArrowSprite()
    {
        if (arrowSprite != null)
        {
            return arrowSprite;
        }

        const int width = 96;
        const int height = 128;
        Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
        texture.name = "MinimapArrowSprite";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[width * height];
        Vector2 tip = new Vector2(width * 0.5f, height - 8f);
        Vector2 left = new Vector2(16f, 52f);
        Vector2 right = new Vector2(width - 16f, 52f);
        Vector2 tailTopLeft = new Vector2(width * 0.5f - 14f, 12f);
        Vector2 tailTopRight = new Vector2(width * 0.5f + 14f, 12f);
        Vector2 tailBottomRight = new Vector2(width * 0.5f + 9f, 52f);
        Vector2 tailBottomLeft = new Vector2(width * 0.5f - 9f, 52f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                bool insideHead = PointInTriangle(point, tip, left, right);
                bool insideTail = PointInQuad(point, tailTopLeft, tailTopRight, tailBottomRight, tailBottomLeft);
                pixels[y * width + x] = insideHead || insideTail ? Color.white : Color.clear;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        arrowSprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.18f), height);
        arrowSprite.name = "MinimapArrowSprite";
        return arrowSprite;
    }

    private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(point, a, b);
        float d2 = Sign(point, b, c);
        float d3 = Sign(point, c, a);

        bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNegative && hasPositive);
    }

    private static bool PointInQuad(Vector2 point, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        return PointInTriangle(point, a, b, c) || PointInTriangle(point, a, c, d);
    }

    private static float Sign(Vector2 point1, Vector2 point2, Vector2 point3)
    {
        return (point1.x - point3.x) * (point2.y - point3.y) - (point2.x - point3.x) * (point1.y - point3.y);
    }

    private void CleanupRuntimeObjects()
    {
        foreach (RectTransform icon in remoteIcons.Values)
        {
            if (icon != null)
            {
                Destroy(icon.gameObject);
            }
        }

        remoteIcons.Clear();
        cachedPlayers.Clear();
        selfIcon = null;
        iconRoot = null;
        mapImage = null;
        mapOverlay = null;
        minimapRootRect = null;

        if (minimapCanvas != null)
        {
            Destroy(minimapCanvas.gameObject);
            minimapCanvas = null;
        }

        minimapCanvasGroup = null;

        if (minimapCamera != null)
        {
            Destroy(minimapCamera.gameObject);
            minimapCamera = null;
        }

        if (minimapTexture != null)
        {
            if (minimapTexture.IsCreated())
            {
                minimapTexture.Release();
            }

            Destroy(minimapTexture);
            minimapTexture = null;
        }
    }
}

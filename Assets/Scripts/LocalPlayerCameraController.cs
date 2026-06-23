using Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class LocalPlayerCameraController : NetworkBehaviour
{
    private const int CrosshairCanvasSortingOrder = 80;
    private const int CombatHudCanvasSortingOrder = 75;

    [SerializeField]
    private GameObject mainCameraPrefab;

    [SerializeField]
    private GameObject virtualCameraPrefab;

    [SerializeField]
    private float lookSensitivity = 1.2f;

    [SerializeField]
    private float topClamp = 70f;

    [SerializeField]
    private float bottomClamp = -30f;

    [SerializeField]
    private float initialPitch = 12f;

    [SerializeField]
    private float cameraTargetHeight = 1.45f;

    [SerializeField]
    private bool invertY;

    [Header("Crosshair")]
    [SerializeField]
    private Color crosshairColor = new Color(1f, 1f, 1f, 0.92f);

    [SerializeField]
    private float crosshairThickness = 3f;

    [SerializeField]
    private float crosshairLength = 10f;

    [SerializeField]
    private float crosshairGap = 6f;

    [Header("Combat HUD")]
    [SerializeField]
    private Color hudTextColor = new Color(1f, 1f, 1f, 0.96f);

    [SerializeField]
    private Color hudPanelColor = new Color(0f, 0f, 0f, 0.28f);

    [SerializeField]
    private Vector2 hudPanelSize = new Vector2(260f, 120f);

    [SerializeField]
    private Vector2 hudPanelOffset = new Vector2(24f, -24f);

    [SerializeField]
    private int hudFontSize = 22;

    [Header("Weapon HUD")]
    [SerializeField]
    [Tooltip("是否显示武器 HUD。关闭后只保留生命值/伤害/击杀 HUD。")]
    private bool showWeaponHud = true;

    [SerializeField]
    [Tooltip("是否显示当前武器名称。")]
    private bool showWeaponName = true;

    [SerializeField]
    [Tooltip("是否在武器名称后显示当前槽位，例如 [1/3]。")]
    private bool showWeaponSlot = true;

    [SerializeField]
    [Tooltip("是否显示当前弹匣弹药。无限弹药会显示为 ∞。")]
    private bool showWeaponAmmo = true;

    [SerializeField]
    [Tooltip("是否在换弹时显示进度条。")]
    private bool showReloadProgress = true;

    [SerializeField]
    [Tooltip("武器 HUD 面板大小。默认放在屏幕右下角。")]
    private Vector2 weaponHudPanelSize = new Vector2(320f, 128f);

    [SerializeField]
    [Tooltip("武器 HUD 面板偏移。默认右下角，X 为负数表示向左，Y 为正数表示向上。")]
    private Vector2 weaponHudPanelOffset = new Vector2(-24f, 24f);

    [SerializeField]
    [Tooltip("武器 HUD 面板背景颜色。")]
    private Color weaponHudPanelColor = new Color(0f, 0f, 0f, 0.34f);

    [SerializeField]
    [Tooltip("武器 HUD 文字颜色。")]
    private Color weaponHudTextColor = new Color(1f, 1f, 1f, 0.96f);

    [SerializeField]
    [Tooltip("换弹进度条背景颜色。")]
    private Color reloadBarBackgroundColor = new Color(1f, 1f, 1f, 0.18f);

    [SerializeField]
    [Tooltip("换弹进度条填充颜色。")]
    private Color reloadBarFillColor = new Color(0.35f, 0.8f, 1f, 0.95f);

    [SerializeField]
    [Tooltip("武器 HUD 字号。")]
    private int weaponHudFontSize = 24;

    [SerializeField]
    [Tooltip("换弹进度条高度。")]
    private float reloadBarHeight = 8f;

    [SerializeField]
    [Tooltip("武器名称前缀。可改成中文，例如“武器:”。")]
    private string weaponNamePrefix = "Weapon:";

    [SerializeField]
    [Tooltip("弹药显示前缀。可改成中文，例如“弹药:”。")]
    private string ammoPrefix = "Ammo:";

    [SerializeField]
    [Tooltip("换弹中显示文本。可改成中文，例如“换弹中”。")]
    private string reloadingText = "Reloading";

    [SerializeField]
    [Tooltip("空弹匣提示文本。可改成中文，例如“按 R 换弹”。")]
    private string emptyMagazineHintText = "Press R to Reload";

    private Transform cameraTarget;
    private Camera runtimeCamera;
    private GameObject spawnedMainCamera;
    private GameObject spawnedVirtualCamera;
    private CinemachineBrain runtimeCameraBrain;
    private CinemachineVirtualCamera virtualCamera;
    private Canvas crosshairCanvas;
    private CanvasGroup crosshairCanvasGroup;
    private Canvas combatHudCanvas;
    private CanvasGroup combatHudCanvasGroup;
    private Text healthText;
    private Text damageText;
    private Text killText;
    private Text weaponNameText;
    private Text weaponAmmoText;
    private Text weaponReloadText;
    private Image reloadProgressBackground;
    private Image reloadProgressFill;
    private PlayerHealth playerHealth;
    private PlayerWeaponController playerWeapon;
    private float yaw;
    private float pitch;

    private void Awake()
    {
        cameraTarget = FindCameraTarget();
        playerHealth = GetComponent<PlayerHealth>();
        playerWeapon = GetComponent<PlayerWeaponController>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        SetupOwnerCamera();
        BuildCrosshairCanvas();
        BuildCombatHudCanvas();
        enabled = true;
    }

    private void LateUpdate()
    {
        ApplyCrosshairVisibility();
        ApplyCombatHudVisibility();
        RefreshCombatHud();
        RefreshWeaponHud();

        if (!IsOwner || cameraTarget == null || RuntimeUIState.BlocksGameplayInput || Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        yaw += mouseX * lookSensitivity;
        pitch += (invertY ? mouseY : -mouseY) * lookSensitivity;
        pitch = ClampAngle(pitch, bottomClamp, topClamp);

        cameraTarget.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    public override void OnNetworkDespawn()
    {
        CleanupSpawnedCameraObjects();
    }

    public override void OnDestroy()
    {
        CleanupSpawnedCameraObjects();
    }

    private void SetupOwnerCamera()
    {
        if (cameraTarget == null)
        {
            cameraTarget = FindOrCreateCameraTarget();
        }

        if (cameraTarget == null)
        {
            Debug.LogError("LocalPlayerCameraController could not find a Cinemachine target on the player.");
            return;
        }

        runtimeCamera = Camera.main;
        if (runtimeCamera == null && mainCameraPrefab != null)
        {
            spawnedMainCamera = Instantiate(mainCameraPrefab);
            runtimeCamera = spawnedMainCamera.GetComponent<Camera>();
        }

        if (runtimeCamera == null)
        {
            Debug.LogError("LocalPlayerCameraController could not find or create a main camera.");
            return;
        }

        runtimeCameraBrain = runtimeCamera.GetComponent<CinemachineBrain>();
        if (runtimeCameraBrain == null)
        {
            runtimeCameraBrain = runtimeCamera.gameObject.AddComponent<CinemachineBrain>();
        }

        runtimeCamera.gameObject.SetActive(true);

        if (spawnedVirtualCamera == null && virtualCameraPrefab != null)
        {
            spawnedVirtualCamera = Instantiate(virtualCameraPrefab);
            virtualCamera = spawnedVirtualCamera.GetComponent<CinemachineVirtualCamera>();
        }

        if (virtualCamera == null && spawnedVirtualCamera != null)
        {
            virtualCamera = spawnedVirtualCamera.GetComponent<CinemachineVirtualCamera>();
        }

        if (virtualCamera == null)
        {
            Debug.LogError("LocalPlayerCameraController could not create a Cinemachine virtual camera.");
            return;
        }

        yaw = transform.eulerAngles.y;
        pitch = initialPitch;
        cameraTarget.rotation = Quaternion.Euler(pitch, yaw, 0f);

        virtualCamera.Follow = cameraTarget;
        virtualCamera.LookAt = cameraTarget;
        virtualCamera.Priority = 100;
    }

    private void BuildCrosshairCanvas()
    {
        if (crosshairCanvas != null)
        {
            ApplyCrosshairVisibility();
            return;
        }

        crosshairCanvas = RuntimeCanvasUIFactory.CreateScreenCanvas("CrosshairCanvas", transform, CrosshairCanvasSortingOrder);
        crosshairCanvasGroup = crosshairCanvas.gameObject.AddComponent<CanvasGroup>();
        crosshairCanvasGroup.interactable = false;
        crosshairCanvasGroup.blocksRaycasts = false;

        RectTransform root = RuntimeCanvasUIFactory.CreateUIObject("CrosshairRoot", crosshairCanvas.transform).GetComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(48f, 48f);
        root.anchoredPosition = Vector2.zero;

        CreateCrosshairBar("Top", root, new Vector2(0f, crosshairGap + crosshairLength * 0.5f), new Vector2(crosshairThickness, crosshairLength));
        CreateCrosshairBar("Bottom", root, new Vector2(0f, -(crosshairGap + crosshairLength * 0.5f)), new Vector2(crosshairThickness, crosshairLength));
        CreateCrosshairBar("Left", root, new Vector2(-(crosshairGap + crosshairLength * 0.5f), 0f), new Vector2(crosshairLength, crosshairThickness));
        CreateCrosshairBar("Right", root, new Vector2(crosshairGap + crosshairLength * 0.5f, 0f), new Vector2(crosshairLength, crosshairThickness));

        ApplyCrosshairVisibility();
    }

    private void BuildCombatHudCanvas()
    {
        if (combatHudCanvas != null)
        {
            ApplyCombatHudVisibility();
            RefreshCombatHud();
            return;
        }

        combatHudCanvas = RuntimeCanvasUIFactory.CreateScreenCanvas("CombatHudCanvas", transform, CombatHudCanvasSortingOrder);
        combatHudCanvasGroup = combatHudCanvas.gameObject.AddComponent<CanvasGroup>();
        combatHudCanvasGroup.interactable = false;
        combatHudCanvasGroup.blocksRaycasts = false;

        Image panel = RuntimeCanvasUIFactory.CreateImage("CombatHudPanel", combatHudCanvas.transform, hudPanelColor);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = hudPanelOffset;
        panelRect.sizeDelta = hudPanelSize;

        RectTransform content = RuntimeCanvasUIFactory.CreateUIObject("Content", panel.transform).GetComponent<RectTransform>();
        RuntimeCanvasUIFactory.StretchToParent(content, 16f, 16f, 12f, 12f);
        RuntimeCanvasUIFactory.AddVerticalLayout(content, 8f);

        healthText = RuntimeCanvasUIFactory.CreateText("HealthText", content, string.Empty, hudFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, hudTextColor);
        damageText = RuntimeCanvasUIFactory.CreateText("DamageText", content, string.Empty, hudFontSize - 2, FontStyle.Normal, TextAnchor.MiddleLeft, hudTextColor);
        killText = RuntimeCanvasUIFactory.CreateText("KillText", content, string.Empty, hudFontSize - 2, FontStyle.Normal, TextAnchor.MiddleLeft, hudTextColor);

        BuildWeaponHudPanel();

        ApplyCombatHudVisibility();
        RefreshCombatHud();
        RefreshWeaponHud();
    }

    private void BuildWeaponHudPanel()
    {
        if (!showWeaponHud || combatHudCanvas == null)
        {
            return;
        }

        Image panel = RuntimeCanvasUIFactory.CreateImage("WeaponHudPanel", combatHudCanvas.transform, weaponHudPanelColor);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(1f, 0f);
        panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot = new Vector2(1f, 0f);
        panelRect.anchoredPosition = weaponHudPanelOffset;
        panelRect.sizeDelta = weaponHudPanelSize;

        RectTransform content = RuntimeCanvasUIFactory.CreateUIObject("Content", panel.transform).GetComponent<RectTransform>();
        RuntimeCanvasUIFactory.StretchToParent(content, 16f, 16f, 12f, 12f);
        RuntimeCanvasUIFactory.AddVerticalLayout(content, 7f);

        weaponNameText = RuntimeCanvasUIFactory.CreateText(
            "WeaponNameText",
            content,
            string.Empty,
            weaponHudFontSize,
            FontStyle.Bold,
            TextAnchor.MiddleRight,
            weaponHudTextColor);

        weaponAmmoText = RuntimeCanvasUIFactory.CreateText(
            "WeaponAmmoText",
            content,
            string.Empty,
            weaponHudFontSize + 4,
            FontStyle.Bold,
            TextAnchor.MiddleRight,
            weaponHudTextColor);

        weaponReloadText = RuntimeCanvasUIFactory.CreateText(
            "WeaponReloadText",
            content,
            string.Empty,
            Mathf.Max(10, weaponHudFontSize - 4),
            FontStyle.Normal,
            TextAnchor.MiddleRight,
            weaponHudTextColor);

        reloadProgressBackground = RuntimeCanvasUIFactory.CreateImage("ReloadProgressBackground", content, reloadBarBackgroundColor);
        LayoutElement backgroundLayout = reloadProgressBackground.gameObject.AddComponent<LayoutElement>();
        backgroundLayout.minHeight = reloadBarHeight;
        backgroundLayout.preferredHeight = reloadBarHeight;

        reloadProgressFill = RuntimeCanvasUIFactory.CreateImage("ReloadProgressFill", reloadProgressBackground.transform, reloadBarFillColor);
        RectTransform fillRect = reloadProgressFill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
    }

    private void CreateCrosshairBar(string name, Transform parent, Vector2 anchoredPosition, Vector2 size)
    {
        Image image = RuntimeCanvasUIFactory.CreateImage(name, parent, crosshairColor);
        image.raycastTarget = false;

        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private void ApplyCrosshairVisibility()
    {
        if (crosshairCanvasGroup == null)
        {
            return;
        }

        bool shouldShow = IsOwner && !RuntimeUIState.IsSettingsMenuOpen;
        crosshairCanvasGroup.alpha = shouldShow ? 1f : 0f;
        crosshairCanvasGroup.interactable = false;
        crosshairCanvasGroup.blocksRaycasts = false;
    }

    private void ApplyCombatHudVisibility()
    {
        if (combatHudCanvasGroup == null)
        {
            return;
        }

        bool shouldShow = IsOwner && !RuntimeUIState.IsSettingsMenuOpen;
        combatHudCanvasGroup.alpha = shouldShow ? 1f : 0f;
        combatHudCanvasGroup.interactable = false;
        combatHudCanvasGroup.blocksRaycasts = false;
    }

    private void RefreshCombatHud()
    {
        if (!IsOwner || healthText == null)
        {
            return;
        }

        if (playerHealth == null)
        {
            playerHealth = GetComponent<PlayerHealth>();
        }

        if (playerHealth == null)
        {
            healthText.text = "HP: --";
            damageText.text = "Damage: --";
            killText.text = "Kills: --";
            return;
        }

        healthText.text = playerHealth.IsDead
            ? $"HP: 0 / {playerHealth.MaxHealth}  [DEAD]"
            : $"HP: {playerHealth.CurrentHealth} / {playerHealth.MaxHealth}";
        damageText.text = $"Damage: {playerHealth.TotalDamageDealt}";
        killText.text = $"Kills: {playerHealth.KillCount}";
        healthText.color = playerHealth.IsDead ? new Color(1f, 0.4f, 0.4f, hudTextColor.a) : hudTextColor;
    }

    private void RefreshWeaponHud()
    {
        if (!showWeaponHud || !IsOwner || weaponNameText == null)
        {
            return;
        }

        if (playerWeapon == null)
        {
            playerWeapon = GetComponent<PlayerWeaponController>();
        }

        WeaponDefinition weapon = playerWeapon != null ? playerWeapon.CurrentWeapon : null;
        if (weapon == null)
        {
            SetTextActive(weaponNameText, showWeaponName || showWeaponSlot);
            SetTextActive(weaponAmmoText, showWeaponAmmo);
            weaponNameText.text = $"{weaponNamePrefix} --";
            weaponAmmoText.text = $"{ammoPrefix} --";
            weaponReloadText.text = string.Empty;
            SetReloadProgressVisible(false);
            return;
        }

        bool displayNameLine = showWeaponName || showWeaponSlot;
        SetTextActive(weaponNameText, displayNameLine);
        if (displayNameLine)
        {
            string namePart = showWeaponName ? weapon.WeaponName : string.Empty;
            string slotPart = showWeaponSlot ? $" [{playerWeapon.CurrentWeaponIndex + 1}/{playerWeapon.WeaponSlotCount}]" : string.Empty;
            weaponNameText.text = $"{weaponNamePrefix} {namePart}{slotPart}".TrimEnd();
        }

        SetTextActive(weaponAmmoText, showWeaponAmmo);
        if (showWeaponAmmo)
        {
            string ammoText = weapon.InfiniteAmmo ? "∞" : $"{playerWeapon.CurrentAmmoInMagazine} / {weapon.MagazineSize}";
            weaponAmmoText.text = $"{ammoPrefix} {ammoText}";
        }

        double serverTime = NetworkManager != null ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;
        bool isReloading = playerWeapon.IsReloading;
        if (isReloading)
        {
            float remaining = playerWeapon.GetReloadRemaining(serverTime);
            weaponReloadText.text = $"{reloadingText} {remaining:F1}s";
        }
        else if (!weapon.InfiniteAmmo && playerWeapon.CurrentAmmoInMagazine <= 0)
        {
            weaponReloadText.text = emptyMagazineHintText;
        }
        else
        {
            weaponReloadText.text = string.Empty;
        }

        SetReloadProgressVisible(showReloadProgress && isReloading);
        if (showReloadProgress && isReloading && reloadProgressFill != null)
        {
            float progress = playerWeapon.GetReloadProgress(serverTime);
            RectTransform fillRect = reloadProgressFill.rectTransform;
            fillRect.anchorMax = new Vector2(progress, 1f);
        }
    }

    private static void SetTextActive(Text text, bool active)
    {
        if (text != null && text.gameObject.activeSelf != active)
        {
            text.gameObject.SetActive(active);
        }
    }

    private void SetReloadProgressVisible(bool visible)
    {
        if (reloadProgressBackground != null && reloadProgressBackground.gameObject.activeSelf != visible)
        {
            reloadProgressBackground.gameObject.SetActive(visible);
        }
    }

    private Transform FindCameraTarget()
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child.CompareTag("CinemachineTarget") || child.name == "CinemachineCameraTarget")
            {
                return child;
            }
        }

        return null;
    }

    private Transform FindOrCreateCameraTarget()
    {
        Transform existingTarget = FindCameraTarget();
        if (existingTarget != null)
        {
            return existingTarget;
        }

        GameObject targetObject = new GameObject("CinemachineCameraTarget");
        targetObject.tag = "CinemachineTarget";

        Transform target = targetObject.transform;
        target.SetParent(transform, false);
        target.localPosition = Vector3.up * Mathf.Max(0f, cameraTargetHeight);
        target.localRotation = Quaternion.identity;
        return target;
    }

    private void CleanupSpawnedCameraObjects()
    {
        if (crosshairCanvas != null)
        {
            Destroy(crosshairCanvas.gameObject);
            crosshairCanvas = null;
            crosshairCanvasGroup = null;
        }

        if (combatHudCanvas != null)
        {
            Destroy(combatHudCanvas.gameObject);
            combatHudCanvas = null;
            combatHudCanvasGroup = null;
            healthText = null;
            damageText = null;
            killText = null;
            weaponNameText = null;
            weaponAmmoText = null;
            weaponReloadText = null;
            reloadProgressBackground = null;
            reloadProgressFill = null;
        }

        if (spawnedVirtualCamera != null)
        {
            Destroy(spawnedVirtualCamera);
            spawnedVirtualCamera = null;
            virtualCamera = null;
        }

        if (spawnedMainCamera != null)
        {
            Destroy(spawnedMainCamera);
            spawnedMainCamera = null;
            runtimeCamera = null;
            runtimeCameraBrain = null;
        }
    }

    private static float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360f)
        {
            angle += 360f;
        }
        else if (angle > 360f)
        {
            angle -= 360f;
        }

        return Mathf.Clamp(angle, min, max);
    }
}

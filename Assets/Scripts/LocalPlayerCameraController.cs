using Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class LocalPlayerCameraController : NetworkBehaviour
{
    private const int CrosshairCanvasSortingOrder = 80;

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

    private Transform cameraTarget;
    private Camera runtimeCamera;
    private GameObject spawnedMainCamera;
    private GameObject spawnedVirtualCamera;
    private CinemachineBrain runtimeCameraBrain;
    private CinemachineVirtualCamera virtualCamera;
    private Canvas crosshairCanvas;
    private CanvasGroup crosshairCanvasGroup;
    private float yaw;
    private float pitch;

    private void Awake()
    {
        cameraTarget = FindCameraTarget();
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
        enabled = true;
    }

    private void LateUpdate()
    {
        ApplyCrosshairVisibility();

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

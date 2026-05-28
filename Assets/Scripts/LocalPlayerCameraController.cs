using Cinemachine;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class LocalPlayerCameraController : NetworkBehaviour
{
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
    private bool invertY;

    private Transform cameraTarget;
    private Camera runtimeCamera;
    private GameObject spawnedMainCamera;
    private GameObject spawnedVirtualCamera;
    private CinemachineBrain runtimeCameraBrain;
    private CinemachineVirtualCamera virtualCamera;
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
        enabled = true;
    }

    private void LateUpdate()
    {
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

    private void OnDestroy()
    {
        CleanupSpawnedCameraObjects();
    }

    private void SetupOwnerCamera()
    {
        if (cameraTarget == null)
        {
            cameraTarget = FindCameraTarget();
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

    private Transform FindCameraTarget()
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child.CompareTag("CinemachineTarget"))
            {
                return child;
            }
        }

        return null;
    }

    private void CleanupSpawnedCameraObjects()
    {
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

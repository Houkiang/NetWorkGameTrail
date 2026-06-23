using Unity.Netcode;
using UnityEngine;
using System;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerWeaponController : NetworkBehaviour
{
    [Header("Inventory")]
    [SerializeField]
    private WeaponDefinition currentWeapon;

    [SerializeField]
    private WeaponDefinition[] weaponSlots;

    [Header("Weapon Model")]
    [SerializeField]
    [Tooltip("武器模型挂点。为空时会按名称自动查找 WeaponSocket、R_Hand_Con、RightHand 等节点。")]
    private Transform weaponModelSocket;

    [SerializeField]
    [Tooltip("未手动指定挂点时，是否自动查找常见手部/武器挂点。")]
    private bool autoFindWeaponModelSocket = true;

    [SerializeField]
    [Tooltip("自动查找武器挂点时使用的节点名称，按顺序匹配。")]
    private string[] weaponModelSocketNameHints =
    {
        "WeaponSocket",
        "RightHandWeaponSocket",
        "R_Hand_Con",
        "RightHand",
        "mixamorig:RightHand",
        "Jnt_R_Hand"
    };

    [SerializeField]
    [Tooltip("是否禁用运行时生成武器模型上的碰撞体。建议开启，避免手持武器碰撞体卡住角色或相机。")]
    private bool disableSpawnedModelColliders = true;

    [SerializeField]
    [Tooltip("切换武器时，如果新武器没有模型预制体，是否清掉当前已经生成的武器模型。")]
    private bool clearModelWhenWeaponHasNoPrefab = true;

    [SerializeField]
    [Tooltip("换枪音效音量。实际音效资源在每个 WeaponDefinition 的 Switch Audio Clip 中配置。")]
    [Min(0f)]
    private float switchAudioVolume = 1f;

    private readonly NetworkVariable<int> currentWeaponIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> currentAmmoInMagazine = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> isReloading = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<double> reloadCompleteServerTime = new NetworkVariable<double>(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private double nextServerFireTime;
    private double nextLocalFireTime;
    private GameObject spawnedWeaponModel;
    private GameObject spawnedWeaponModelPrefab;
    private Transform currentMuzzleTransform;
    private PlayerHealth playerHealth;
    private int[] serverAmmoInMagazineBySlot;

    public event Action<int, int> WeaponIndexChanged;

    public event Action<int, int> AmmoInMagazineChanged;

    public event Action<bool, bool> ReloadStateChanged;

    public WeaponDefinition CurrentWeapon => GetWeaponAt(currentWeaponIndex.Value) ?? GetWeaponAt(0);

    public int CurrentWeaponIndex => Mathf.Clamp(currentWeaponIndex.Value, 0, Mathf.Max(0, WeaponSlotCount - 1));

    public int WeaponSlotCount
    {
        get
        {
            if (weaponSlots != null && weaponSlots.Length > 0)
            {
                return weaponSlots.Length;
            }

            return currentWeapon != null ? 1 : 0;
        }
    }

    public int CurrentAmmoInMagazine => CurrentWeapon != null && CurrentWeapon.InfiniteAmmo
        ? CurrentWeapon.MagazineSize
        : currentAmmoInMagazine.Value;

    public bool IsReloading => isReloading.Value;

    public bool HasWeapon => CurrentWeapon != null;

    public Transform CurrentMuzzleTransform => currentMuzzleTransform;

    public string AmmoDisplayText
    {
        get
        {
            WeaponDefinition weapon = CurrentWeapon;
            if (weapon == null)
            {
                return "N/A";
            }

            return weapon.InfiniteAmmo ? "∞" : $"{CurrentAmmoInMagazine}/{weapon.MagazineSize}";
        }
    }

    public override void OnNetworkSpawn()
    {
        nextServerFireTime = 0d;
        nextLocalFireTime = 0d;

        if (playerHealth == null)
        {
            playerHealth = GetComponent<PlayerHealth>();
        }

        if (playerHealth != null)
        {
            playerHealth.DeadStateChanged += OnDeadStateChanged;
        }

        if (IsServer)
        {
            InitializeCurrentWeaponState();
        }

        currentWeaponIndex.OnValueChanged += OnCurrentWeaponIndexChanged;
        currentAmmoInMagazine.OnValueChanged += OnCurrentAmmoInMagazineChanged;
        isReloading.OnValueChanged += OnReloadStateChanged;

        RefreshEquippedWeaponModel();
    }

    public override void OnNetworkDespawn()
    {
        if (playerHealth != null)
        {
            playerHealth.DeadStateChanged -= OnDeadStateChanged;
        }

        currentWeaponIndex.OnValueChanged -= OnCurrentWeaponIndexChanged;
        currentAmmoInMagazine.OnValueChanged -= OnCurrentAmmoInMagazineChanged;
        isReloading.OnValueChanged -= OnReloadStateChanged;
        ClearEquippedWeaponModel();
    }

    private void Update()
    {
        if (!IsServer || NetworkManager == null)
        {
            return;
        }

        TryCompleteReloadServer(NetworkManager.ServerTime.Time);
    }

    public bool CanFireServer(double serverTime)
    {
        WeaponDefinition weapon = CurrentWeapon;
        if (weapon == null || serverTime < nextServerFireTime || isReloading.Value)
        {
            return false;
        }

        return weapon.InfiniteAmmo || GetStoredAmmoInMagazineServer(CurrentWeaponIndex) > 0;
    }

    public bool CanFireLocal(double localTime)
    {
        WeaponDefinition weapon = CurrentWeapon;
        if (weapon == null || localTime < nextLocalFireTime || isReloading.Value)
        {
            return false;
        }

        return weapon.InfiniteAmmo || currentAmmoInMagazine.Value > 0;
    }

    public void MarkServerShotFired(double serverTime)
    {
        WeaponDefinition weapon = CurrentWeapon;
        if (weapon == null)
        {
            return;
        }

        if (!weapon.InfiniteAmmo)
        {
            int weaponIndex = CurrentWeaponIndex;
            int remainingAmmo = Mathf.Max(0, GetStoredAmmoInMagazineServer(weaponIndex) - 1);
            SetStoredAmmoInMagazineServer(weaponIndex, remainingAmmo);
        }

        nextServerFireTime = serverTime + weapon.FireInterval;
    }

    public void MarkLocalShotFired(double localTime)
    {
        WeaponDefinition weapon = CurrentWeapon;
        if (weapon == null)
        {
            return;
        }

        nextLocalFireTime = localTime + weapon.FireInterval;
    }

    public bool TryStartReload()
    {
        if (!IsOwner || !CanReloadLocal())
        {
            return false;
        }

        RequestReloadServerRpc();
        return true;
    }

    public bool CanReloadLocal()
    {
        WeaponDefinition weapon = CurrentWeapon;
        return weapon != null
            && !weapon.InfiniteAmmo
            && !isReloading.Value
            && CurrentAmmoInMagazine < weapon.MagazineSize;
    }

    public bool CanReloadServer()
    {
        WeaponDefinition weapon = CurrentWeapon;
        return weapon != null
            && !weapon.InfiniteAmmo
            && !isReloading.Value
            && GetStoredAmmoInMagazineServer(CurrentWeaponIndex) < weapon.MagazineSize;
    }

    public bool StartReloadServer(double serverTime)
    {
        if (!IsServer || !CanReloadServer())
        {
            return false;
        }

        WeaponDefinition weapon = CurrentWeapon;
        isReloading.Value = true;
        reloadCompleteServerTime.Value = serverTime + weapon.ReloadTime;
        return true;
    }

    public bool TryCompleteReloadServer(double serverTime)
    {
        if (!IsServer || !isReloading.Value || serverTime < reloadCompleteServerTime.Value)
        {
            return false;
        }

        WeaponDefinition weapon = CurrentWeapon;
        SetStoredAmmoInMagazineServer(CurrentWeaponIndex, weapon != null ? weapon.MagazineSize : 0);
        isReloading.Value = false;
        reloadCompleteServerTime.Value = 0d;
        return true;
    }

    public bool TrySwitchWeapon(int slotIndex)
    {
        if (!IsOwner || !CanSwitchToWeaponLocal(slotIndex))
        {
            return false;
        }

        RequestSwitchWeaponServerRpc(slotIndex);
        return true;
    }

    public bool CanSwitchToWeaponLocal(int slotIndex)
    {
        return IsValidWeaponIndex(slotIndex)
            && slotIndex != CurrentWeaponIndex
            && GetWeaponAt(slotIndex) != null;
    }

    public bool TrySwitchWeaponServer(int slotIndex)
    {
        if (!IsServer || !IsValidWeaponIndex(slotIndex) || GetWeaponAt(slotIndex) == null)
        {
            return false;
        }

        StoreCurrentAmmoInMagazineServer();
        currentWeaponIndex.Value = slotIndex;
        isReloading.Value = false;
        reloadCompleteServerTime.Value = 0d;
        currentAmmoInMagazine.Value = GetStoredAmmoInMagazineServer(slotIndex);
        nextServerFireTime = NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;
        return true;
    }

    public float GetReloadRemaining(double serverTime)
    {
        return isReloading.Value ? Mathf.Max(0f, (float)(reloadCompleteServerTime.Value - serverTime)) : 0f;
    }

    public float GetReloadProgress(double serverTime)
    {
        WeaponDefinition weapon = CurrentWeapon;
        if (weapon == null)
        {
            return 0f;
        }

        if (!isReloading.Value)
        {
            return 1f;
        }

        float reloadTime = Mathf.Max(0.01f, weapon.ReloadTime);
        float remaining = GetReloadRemaining(serverTime);
        return Mathf.Clamp01(1f - remaining / reloadTime);
    }

    public float GetLocalCooldownRemaining(double localTime)
    {
        WeaponDefinition weapon = CurrentWeapon;
        if (weapon == null)
        {
            return 0f;
        }

        return Mathf.Max(0f, (float)(nextLocalFireTime - localTime));
    }

    public float GetServerCooldownRemaining(double serverTime)
    {
        WeaponDefinition weapon = CurrentWeapon;
        if (weapon == null)
        {
            return 0f;
        }

        return Mathf.Max(0f, (float)(nextServerFireTime - serverTime));
    }

    private void InitializeCurrentWeaponState()
    {
        EnsureServerAmmoState();
        int safeIndex = IsValidWeaponIndex(currentWeaponIndex.Value) ? currentWeaponIndex.Value : 0;
        currentWeaponIndex.Value = safeIndex;
        currentAmmoInMagazine.Value = GetStoredAmmoInMagazineServer(safeIndex);
        isReloading.Value = false;
        reloadCompleteServerTime.Value = 0d;
    }

    private void EnsureServerAmmoState()
    {
        if (!IsServer)
        {
            return;
        }

        int slotCount = WeaponSlotCount;
        if (slotCount <= 0)
        {
            serverAmmoInMagazineBySlot = Array.Empty<int>();
            return;
        }

        if (serverAmmoInMagazineBySlot != null && serverAmmoInMagazineBySlot.Length == slotCount)
        {
            return;
        }

        int[] previousAmmoBySlot = serverAmmoInMagazineBySlot;
        serverAmmoInMagazineBySlot = new int[slotCount];

        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            WeaponDefinition weapon = GetWeaponAt(slotIndex);
            int magazineSize = GetMagazineSize(weapon);

            if (previousAmmoBySlot != null && slotIndex < previousAmmoBySlot.Length)
            {
                serverAmmoInMagazineBySlot[slotIndex] = Mathf.Clamp(previousAmmoBySlot[slotIndex], 0, magazineSize);
            }
            else
            {
                serverAmmoInMagazineBySlot[slotIndex] = magazineSize;
            }
        }
    }

    private int GetStoredAmmoInMagazineServer(int slotIndex)
    {
        if (!IsServer || !IsValidWeaponIndex(slotIndex))
        {
            return 0;
        }

        EnsureServerAmmoState();
        WeaponDefinition weapon = GetWeaponAt(slotIndex);
        return Mathf.Clamp(serverAmmoInMagazineBySlot[slotIndex], 0, GetMagazineSize(weapon));
    }

    private void SetStoredAmmoInMagazineServer(int slotIndex, int ammo)
    {
        if (!IsServer || !IsValidWeaponIndex(slotIndex))
        {
            return;
        }

        EnsureServerAmmoState();
        WeaponDefinition weapon = GetWeaponAt(slotIndex);
        int clampedAmmo = Mathf.Clamp(ammo, 0, GetMagazineSize(weapon));
        serverAmmoInMagazineBySlot[slotIndex] = clampedAmmo;

        if (slotIndex == CurrentWeaponIndex)
        {
            currentAmmoInMagazine.Value = clampedAmmo;
        }
    }

    private void StoreCurrentAmmoInMagazineServer()
    {
        if (!IsServer || !IsValidWeaponIndex(CurrentWeaponIndex))
        {
            return;
        }

        SetStoredAmmoInMagazineServer(CurrentWeaponIndex, currentAmmoInMagazine.Value);
    }

    private WeaponDefinition GetWeaponAt(int slotIndex)
    {
        if (weaponSlots != null && weaponSlots.Length > 0)
        {
            return slotIndex >= 0 && slotIndex < weaponSlots.Length ? weaponSlots[slotIndex] : null;
        }

        return slotIndex == 0 ? currentWeapon : null;
    }

    private bool IsValidWeaponIndex(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < WeaponSlotCount;
    }

    private static int GetMagazineSize(WeaponDefinition weapon)
    {
        return weapon != null ? Mathf.Max(1, weapon.MagazineSize) : 0;
    }

    private void OnCurrentWeaponIndexChanged(int previousValue, int newValue)
    {
        WeaponIndexChanged?.Invoke(previousValue, newValue);
        RefreshEquippedWeaponModel();
        TryPlaySwitchAudio(CurrentWeapon);
    }

    private void OnCurrentAmmoInMagazineChanged(int previousValue, int newValue)
    {
        AmmoInMagazineChanged?.Invoke(previousValue, newValue);
    }

    private void OnReloadStateChanged(bool previousValue, bool newValue)
    {
        ReloadStateChanged?.Invoke(previousValue, newValue);
    }

    private void RefreshEquippedWeaponModel()
    {
        WeaponDefinition weapon = CurrentWeapon;
        GameObject modelPrefab = weapon != null ? weapon.WeaponModelPrefab : null;
        if (modelPrefab == null)
        {
            currentMuzzleTransform = null;
            if (clearModelWhenWeaponHasNoPrefab)
            {
                ClearEquippedWeaponModel();
            }

            return;
        }

        Transform socket = GetWeaponModelSocket();
        if (socket == null)
        {
            Debug.LogWarning(
                $"[{nameof(PlayerWeaponController)}] Could not find a weapon model socket for {name}. " +
                "Assign Weapon Model Socket on the player prefab.",
                this);
            ClearEquippedWeaponModel();
            return;
        }

        if (spawnedWeaponModel != null && spawnedWeaponModelPrefab != modelPrefab)
        {
            ClearEquippedWeaponModel();
        }

        if (spawnedWeaponModel == null)
        {
            spawnedWeaponModel = Instantiate(modelPrefab, socket);
            spawnedWeaponModel.name = $"{modelPrefab.name}_Equipped";
            spawnedWeaponModelPrefab = modelPrefab;
            if (disableSpawnedModelColliders)
            {
                DisableModelColliders(spawnedWeaponModel);
            }
        }
        else if (spawnedWeaponModel.transform.parent != socket)
        {
            spawnedWeaponModel.transform.SetParent(socket, false);
        }

        ApplyWeaponModelTransform(spawnedWeaponModel.transform, weapon);
        currentMuzzleTransform = FindMuzzleTransform(spawnedWeaponModel.transform, weapon);
        ApplyWeaponModelVisibility();
    }

    private Transform GetWeaponModelSocket()
    {
        if (weaponModelSocket != null)
        {
            return weaponModelSocket;
        }

        if (!autoFindWeaponModelSocket)
        {
            return transform;
        }

        weaponModelSocket = FindWeaponModelSocketByName();
        return weaponModelSocket != null ? weaponModelSocket : transform;
    }

    private Transform FindWeaponModelSocketByName()
    {
        if (weaponModelSocketNameHints == null || weaponModelSocketNameHints.Length == 0)
        {
            return null;
        }

        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int hintIndex = 0; hintIndex < weaponModelSocketNameHints.Length; hintIndex++)
        {
            string hint = weaponModelSocketNameHints[hintIndex];
            if (string.IsNullOrWhiteSpace(hint))
            {
                continue;
            }

            for (int childIndex = 0; childIndex < children.Length; childIndex++)
            {
                Transform child = children[childIndex];
                if (child != null && child.name == hint)
                {
                    return child;
                }
            }
        }

        return null;
    }

    private static void ApplyWeaponModelTransform(Transform modelTransform, WeaponDefinition weapon)
    {
        if (modelTransform == null || weapon == null)
        {
            return;
        }

        modelTransform.localPosition = weapon.ModelLocalPosition;
        modelTransform.localRotation = Quaternion.Euler(weapon.ModelLocalEulerAngles);
        modelTransform.localScale = weapon.ModelLocalScale;
    }

    private Transform FindMuzzleTransform(Transform modelRoot, WeaponDefinition weapon)
    {
        if (modelRoot == null || weapon == null || string.IsNullOrWhiteSpace(weapon.MuzzleTransformName))
        {
            return null;
        }

        Transform muzzle = FindChildTransformByName(modelRoot, weapon.MuzzleTransformName);
        if (muzzle == null)
        {
            Debug.LogWarning(
                $"[{nameof(PlayerWeaponController)}] Could not find muzzle transform '{weapon.MuzzleTransformName}' " +
                $"under weapon model '{modelRoot.name}' for weapon '{weapon.WeaponName}'. Falling back to PlayerCombatController Fire Origin.",
                this);
        }

        return muzzle;
    }

    private static Transform FindChildTransformByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
        {
            return null;
        }

        if (root.name == targetName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindChildTransformByName(root.GetChild(i), targetName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static void DisableModelColliders(GameObject model)
    {
        if (model == null)
        {
            return;
        }

        Collider[] colliders = model.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        Rigidbody[] rigidbodies = model.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            rigidbodies[i].isKinematic = true;
            rigidbodies[i].detectCollisions = false;
        }
    }

    private void ClearEquippedWeaponModel()
    {
        if (spawnedWeaponModel != null)
        {
            Destroy(spawnedWeaponModel);
            spawnedWeaponModel = null;
            spawnedWeaponModelPrefab = null;
            currentMuzzleTransform = null;
        }
    }

    private void OnDeadStateChanged(bool dead)
    {
        ApplyWeaponModelVisibility();
    }

    private void ApplyWeaponModelVisibility()
    {
        if (spawnedWeaponModel == null)
        {
            return;
        }

        if (playerHealth == null)
        {
            playerHealth = GetComponent<PlayerHealth>();
        }

        bool shouldShow = playerHealth == null || !playerHealth.IsDead;
        spawnedWeaponModel.SetActive(shouldShow);
    }

    private void TryPlaySwitchAudio(WeaponDefinition weapon)
    {
        if (weapon == null || weapon.SwitchAudioClip == null || switchAudioVolume <= 0f)
        {
            return;
        }

        Vector3 audioPosition = currentMuzzleTransform != null
            ? currentMuzzleTransform.position
            : GetWeaponModelSocket().position;

        AudioSource.PlayClipAtPoint(weapon.SwitchAudioClip, audioPosition, switchAudioVolume);
    }

    [ServerRpc]
    private void RequestReloadServerRpc()
    {
        if (NetworkManager == null)
        {
            return;
        }

        StartReloadServer(NetworkManager.ServerTime.Time);
    }

    [ServerRpc]
    private void RequestSwitchWeaponServerRpc(int slotIndex)
    {
        TrySwitchWeaponServer(slotIndex);
    }
}

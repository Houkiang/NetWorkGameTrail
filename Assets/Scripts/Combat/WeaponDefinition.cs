using UnityEngine;

[CreateAssetMenu(fileName = "WeaponDefinition", menuName = "Network Game/Combat/Weapon Definition")]
public class WeaponDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField]
    [Tooltip("武器唯一标识。用于以后保存、切枪、UI 或配置表查找；建议英文小写且不要重复。")]
    private string weaponId = "test_rifle";

    [SerializeField]
    [Tooltip("武器显示名称。主要给 Inspector、调试面板和未来 UI 使用。")]
    private string weaponName = "Test Rifle";

    [Header("Ballistics")]
    [SerializeField]
    [Tooltip("基础伤害。命中玩家时会再经过命中盒倍率修正，例如爆头、身体、四肢倍率。")]
    private int damage = 25;

    [SerializeField]
    [Tooltip("最大射程，单位为 Unity 米。射线超过该距离后视为未命中。")]
    private float range = 80f;

    [SerializeField]
    [Tooltip("每秒最多开火次数。例如 5 表示每秒 5 发，等价于 0.2 秒一发。")]
    private float fireRate = 5f;

    [SerializeField]
    [Tooltip("射击可命中的物理层。环境、玩家命中盒等需要在这个 LayerMask 内才会被检测。")]
    private LayerMask hitMask = Physics.DefaultRaycastLayers;

    [SerializeField]
    [Min(0f)]
    [Tooltip("子弹散布角度，单位为度。0 表示完全精准；数值越大，服务端实际射线越可能偏离准星方向。")]
    private float spreadAngle = 0f;

    [SerializeField]
    [Min(0.001f)]
    [Tooltip("枪口重叠检测半径。用于处理枪口已经伸进墙体或碰撞体时，立即判定命中，避免穿墙射击。")]
    private float muzzleOverlapRadius = 0.05f;

    [Header("Ammo")]
    [SerializeField]
    [Tooltip("是否无限弹药。勾选后不会消耗弹匣子弹，也不会强制换弹；适合当前测试武器或无限子弹模式。")]
    private bool infiniteAmmo = true;

    [SerializeField]
    [Min(1)]
    [Tooltip("弹匣容量。关闭无限弹药后，每次换弹会把当前弹匣恢复到这个数量。")]
    private int magazineSize = 30;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("换弹耗时，单位为秒。服务器会在该时间结束后补满当前弹匣。")]
    private float reloadTime = 1.8f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("换弹动画原始时长，单位为秒。代码会用 原始动画时长 / 换弹耗时 计算动画速度倍率，让动画播放时长匹配 Reload Time。")]
    private float reloadAnimationDuration = 1.8f;

    [Header("Handling")]
    [SerializeField]
    [Tooltip("是否自动武器。勾选时按住开火键会连续射击；不勾选时每按下一次只打一发。")]
    private bool automatic = true;

    [SerializeField]
    [Min(0f)]
    [Tooltip("垂直后坐力预留值，单位为度。当前只作为配置和调试显示，后续接入相机/准星回弹时使用。")]
    private float recoilPitch = 0f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("水平后坐力预留值，单位为度。当前只作为配置和调试显示，后续接入随机左右偏移时使用。")]
    private float recoilYaw = 0f;

    [Header("Model")]
    [SerializeField]
    [Tooltip("武器模型预制体。切换到该武器时，会在玩家的 Weapon Model Socket 下生成这个模型。")]
    private GameObject weaponModelPrefab;

    [SerializeField]
    [Tooltip("武器模型挂到 Socket 后的本地位置偏移。用于逐把武器微调握持位置。")]
    private Vector3 modelLocalPosition = Vector3.zero;

    [SerializeField]
    [Tooltip("武器模型挂到 Socket 后的本地欧拉角。用于逐把武器微调朝向。")]
    private Vector3 modelLocalEulerAngles = Vector3.zero;

    [SerializeField]
    [Tooltip("武器模型挂到 Socket 后的本地缩放。通常保持 1,1,1；资源比例不一致时可单独调整。")]
    private Vector3 modelLocalScale = Vector3.one;

    [SerializeField]
    [Tooltip("武器模型内的枪口节点名称。切换到该武器后，开火、枪口火光和 Tracer 会优先使用这个节点。")]
    private string muzzleTransformName = "MuzzlePosition";

    [Header("Presentation")]
    [SerializeField]
    [Tooltip("枪口火光预制体。开火时生成在 FireOrigin/枪口位置，并随枪口节点移动。")]
    private GameObject muzzleFlashPrefab;

    [SerializeField]
    [Tooltip("命中环境时播放的特效预制体，例如墙面火花、尘土、碎屑。")]
    private GameObject impactPrefab;

    [SerializeField]
    [Tooltip("命中玩家时播放的特效预制体，例如血雾或角色受击火花。为空时会回退使用环境命中特效。")]
    private GameObject playerImpactPrefab;

    [SerializeField]
    [Tooltip("弹道轨迹预制体。用于显示从枪口到命中点/终点的可视化路径；为空时只使用调试线。")]
    private GameObject tracerPrefab;

    [SerializeField]
    [Tooltip("开火音效。开火表现播放时会在枪口位置播放该音频。")]
    private AudioClip fireAudioClip;

    [SerializeField]
    [Tooltip("换枪音效。切换到该武器时播放，例如抬枪、拔枪或武器装备声音。")]
    private AudioClip switchAudioClip;

    [SerializeField]
    [Tooltip("换弹开始音效。进入换弹状态时播放，例如取弹匣、拉枪机的开始声音。")]
    private AudioClip reloadStartAudioClip;

    [SerializeField]
    [Tooltip("换弹完成音效。弹匣补满、换弹结束时播放，例如插入弹匣或上膛声音。")]
    private AudioClip reloadCompleteAudioClip;

    public string WeaponId => weaponId;

    public string WeaponName => weaponName;

    public int Damage => damage;

    public float Range => range;

    public float FireRate => fireRate;

    public float FireInterval => fireRate > 0f ? 1f / fireRate : float.PositiveInfinity;

    public LayerMask HitMask => hitMask;

    public float SpreadAngle => spreadAngle;

    public float MuzzleOverlapRadius => muzzleOverlapRadius;

    public bool InfiniteAmmo => infiniteAmmo;

    public int MagazineSize => magazineSize;

    public float ReloadTime => reloadTime;

    public float ReloadAnimationDuration => reloadAnimationDuration;

    public bool Automatic => automatic;

    public float RecoilPitch => recoilPitch;

    public float RecoilYaw => recoilYaw;

    public GameObject WeaponModelPrefab => weaponModelPrefab;

    public Vector3 ModelLocalPosition => modelLocalPosition;

    public Vector3 ModelLocalEulerAngles => modelLocalEulerAngles;

    public Vector3 ModelLocalScale => modelLocalScale;

    public string MuzzleTransformName => muzzleTransformName;

    public GameObject MuzzleFlashPrefab => muzzleFlashPrefab;

    public GameObject ImpactPrefab => impactPrefab;

    public GameObject PlayerImpactPrefab => playerImpactPrefab != null ? playerImpactPrefab : impactPrefab;

    public GameObject TracerPrefab => tracerPrefab;

    public AudioClip FireAudioClip => fireAudioClip;

    public AudioClip SwitchAudioClip => switchAudioClip;

    public AudioClip ReloadStartAudioClip => reloadStartAudioClip;

    public AudioClip ReloadCompleteAudioClip => reloadCompleteAudioClip;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(weaponId))
        {
            weaponId = name;
        }

        if (string.IsNullOrWhiteSpace(weaponName))
        {
            weaponName = weaponId;
        }

        damage = Mathf.Max(0, damage);
        range = Mathf.Max(0.1f, range);
        fireRate = Mathf.Max(0.01f, fireRate);
        spreadAngle = Mathf.Clamp(spreadAngle, 0f, 45f);
        muzzleOverlapRadius = Mathf.Max(0.001f, muzzleOverlapRadius);
        magazineSize = Mathf.Max(1, magazineSize);
        reloadTime = Mathf.Max(0.01f, reloadTime);
        reloadAnimationDuration = Mathf.Max(0.01f, reloadAnimationDuration);
        recoilPitch = Mathf.Max(0f, recoilPitch);
        recoilYaw = Mathf.Max(0f, recoilYaw);
        if (modelLocalScale == Vector3.zero)
        {
            modelLocalScale = Vector3.one;
        }

        if (string.IsNullOrWhiteSpace(muzzleTransformName))
        {
            muzzleTransformName = "MuzzlePosition";
        }
    }
}

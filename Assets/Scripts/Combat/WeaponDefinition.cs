using UnityEngine;

[CreateAssetMenu(fileName = "WeaponDefinition", menuName = "Network Game/Combat/Weapon Definition")]
public class WeaponDefinition : ScriptableObject
{
    [SerializeField]
    private string weaponName = "Test Rifle";

    [SerializeField]
    private int damage = 25;

    [SerializeField]
    private float range = 80f;

    [SerializeField]
    private float fireRate = 5f;

    [SerializeField]
    private LayerMask hitMask = Physics.DefaultRaycastLayers;

    [Header("Presentation")]
    [SerializeField]
    private GameObject muzzleFlashPrefab;

    [SerializeField]
    private GameObject impactPrefab;

    [SerializeField]
    private GameObject playerImpactPrefab;

    [SerializeField]
    private GameObject tracerPrefab;

    [SerializeField]
    private AudioClip fireAudioClip;

    public string WeaponName => weaponName;

    public int Damage => damage;

    public float Range => range;

    public float FireRate => fireRate;

    public float FireInterval => fireRate > 0f ? 1f / fireRate : float.PositiveInfinity;

    public LayerMask HitMask => hitMask;

    public GameObject MuzzleFlashPrefab => muzzleFlashPrefab;

    public GameObject ImpactPrefab => impactPrefab;

    public GameObject PlayerImpactPrefab => playerImpactPrefab != null ? playerImpactPrefab : impactPrefab;

    public GameObject TracerPrefab => tracerPrefab;

    public AudioClip FireAudioClip => fireAudioClip;

    private void OnValidate()
    {
        damage = Mathf.Max(0, damage);
        range = Mathf.Max(0.1f, range);
        fireRate = Mathf.Max(0.01f, fireRate);
    }
}

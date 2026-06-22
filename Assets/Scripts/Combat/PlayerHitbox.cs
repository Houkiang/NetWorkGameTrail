using UnityEngine;

[DisallowMultipleComponent]
public class PlayerHitbox : MonoBehaviour
{
    [SerializeField]
    private PlayerHealth ownerHealth;

    [SerializeField]
    private float damageMultiplier = 1f;

    private Collider[] cachedColliders;

    public PlayerHealth OwnerHealth => ownerHealth;

    public float DamageMultiplier => damageMultiplier;

    private void Awake()
    {
        if (ownerHealth == null)
        {
            ownerHealth = GetComponentInParent<PlayerHealth>();
        }

        CacheColliders();
    }

    public bool TryGetOwnerHealth(out PlayerHealth health)
    {
        if (ownerHealth == null)
        {
            ownerHealth = GetComponentInParent<PlayerHealth>();
        }

        health = ownerHealth;
        return health != null;
    }

    public int ApplyDamageMultiplier(int baseDamage)
    {
        return Mathf.Max(0, Mathf.RoundToInt(baseDamage * damageMultiplier));
    }

    public void SetHitboxEnabled(bool enabled)
    {
        CacheColliders();
        if (cachedColliders == null)
        {
            return;
        }

        for (int i = 0; i < cachedColliders.Length; i++)
        {
            if (cachedColliders[i] != null)
            {
                cachedColliders[i].enabled = enabled;
            }
        }
    }

    private void CacheColliders()
    {
        if (cachedColliders == null || cachedColliders.Length == 0)
        {
            cachedColliders = GetComponents<Collider>();
        }
    }

    private void OnValidate()
    {
        damageMultiplier = Mathf.Max(0f, damageMultiplier);
    }
}

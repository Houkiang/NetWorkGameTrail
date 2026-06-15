using UnityEngine;

[DisallowMultipleComponent]
public class PlayerHitbox : MonoBehaviour
{
    [SerializeField]
    private PlayerHealth ownerHealth;

    [SerializeField]
    private float damageMultiplier = 1f;

    public PlayerHealth OwnerHealth => ownerHealth;

    public float DamageMultiplier => damageMultiplier;

    private void Awake()
    {
        if (ownerHealth == null)
        {
            ownerHealth = GetComponentInParent<PlayerHealth>();
        }
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

    private void OnValidate()
    {
        damageMultiplier = Mathf.Max(0f, damageMultiplier);
    }
}

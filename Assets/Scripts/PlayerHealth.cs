using UnityEngine;

[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour
{
    [Min(1)] public int maxHealth = 100;
    [SerializeField] private int currentHealth = 100;
    [SerializeField] private bool isDead;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => isDead;
    public float NormalizedHealth => maxHealth > 0 ? currentHealth / (float)maxHealth : 0f;

    private void Awake()
    {
        ResetHealth();
    }

    public void ResetHealth()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = maxHealth;
        isDead = false;
    }

    public bool TakeDamage(int damage, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (isDead || damage <= 0)
        {
            return false;
        }

        currentHealth = Mathf.Max(0, currentHealth - damage);
        BloodSplatterEffect.Spawn(hitPoint, hitNormal, 0.9f);

        CombatHUD hud = GetComponent<CombatHUD>();
        if (hud != null)
        {
            hud.FlashDamage();
        }

        Debug.Log($"[Combat] Player took {damage} damage. HP={currentHealth}/{maxHealth}.", this);
        if (currentHealth == 0)
        {
            Die();
        }
        return true;
    }

    private void Die()
    {
        isDead = true;

        FPSControllerLPFP.FpsControllerLPFP movement = GetComponent<FPSControllerLPFP.FpsControllerLPFP>();
        if (movement != null)
        {
            movement.enabled = false;
        }

        foreach (AutomaticGunScriptLPFP weapon in GetComponentsInChildren<AutomaticGunScriptLPFP>(true))
        {
            weapon.enabled = false;
        }
        foreach (HandgunScriptLPFP weapon in GetComponentsInChildren<HandgunScriptLPFP>(true))
        {
            weapon.enabled = false;
        }

        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.constraints = RigidbodyConstraints.FreezeAll;
        }

        AudioSource footsteps = GetComponent<AudioSource>();
        if (footsteps != null)
        {
            footsteps.Stop();
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Debug.Log("[Combat] Player defeated.", this);
    }
}

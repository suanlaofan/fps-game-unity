using UnityEngine;

[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour
{
    [Min(1)] public int maxHealth = 100;
    [SerializeField] private int currentHealth = 100;
    [SerializeField] private bool isDead;
    [Header("Damage Response")]
    [Tooltip("Keeps locomotion and time scale unchanged when the player is hit.")]
    public bool disableHitStun = true;

    private float nextDamageTime;
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
        nextDamageTime = 0;
    }

    public bool TakeDamage(int damage, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (isDead || damage <= 0 || Time.time < nextDamageTime)
        {
            return false;
        }

        nextDamageTime = Time.time + 0.5f;
        currentHealth = Mathf.Max(0, currentHealth - damage);
        BloodSplatterEffect.Spawn(hitPoint, hitNormal, 0.9f);

        CombatHUD hud = GetComponent<CombatHUD>();
        if (hud != null)
        {
            hud.FlashDamage();
        }

        if (disableHitStun && !isDead)
        {
            // Damage is feedback only. Do not pause, zero velocity, or leave
            // the XR locomotion body constrained after a melee hit.
            Time.timeScale = 1f;
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                body.constraints = RigidbodyConstraints.FreezeRotation;
            }
            Debug.Log("[PICO-FRESH] PICO_FRESH_PLAYER_DAMAGE_NO_STUN hp=" +
                      currentHealth + "/" + maxHealth + " locomotion=preserved.", this);
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
        if (body != null && !body.isKinematic)
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

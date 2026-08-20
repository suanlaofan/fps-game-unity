using UnityEngine;

[DisallowMultipleComponent]
public class CombatHUD : MonoBehaviour
{
    public PlayerHealth playerHealth;
    public EnemyController enemy;

    [Header("Legacy HUD Visibility")]
    [Tooltip("Keeps the original OnGUI health bars available when the Level 0 UI flow is not installed.")]
    public bool showBars = true;
    [Tooltip("Keeps the original terminal-state labels available when the Level 0 UI flow is not installed.")]
    public bool showTerminalState = true;

    private float damageFlash;
    private GUIStyle labelStyle;
    private GUIStyle stateStyle;

    public void FlashDamage()
    {
        damageFlash = 1f;
    }

    private void Awake()
    {
        if (playerHealth == null)
        {
            playerHealth = GetComponent<PlayerHealth>();
        }
    }

    private void Update()
    {
        damageFlash = Mathf.MoveTowards(damageFlash, 0f, Time.unscaledDeltaTime * 2.8f);
    }

    private void OnGUI()
    {
        EnsureStyles();

        if (showBars)
        {
            float barWidth = Mathf.Min(280f, Screen.width - 48f);
            DrawHealthBar(new Rect(24f, 24f, barWidth, 22f), "PLAYER", playerHealth != null ? playerHealth.CurrentHealth : 0,
                playerHealth != null ? playerHealth.MaxHealth : 100, new Color(0.12f, 0.78f, 0.3f));
            DrawHealthBar(new Rect(24f, 58f, barWidth, 22f), "ENEMY", enemy != null ? enemy.CurrentHealth : 0,
                enemy != null ? enemy.MaxHealth : 500, new Color(0.82f, 0.08f, 0.06f));
        }

        if (damageFlash > 0f)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.8f, 0f, 0f, damageFlash * 0.2f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        if (!showTerminalState)
        {
            return;
        }

        if (playerHealth != null && playerHealth.IsDead)
        {
            GUI.Label(new Rect(0f, Screen.height * 0.42f, Screen.width, 80f), "YOU DIED", stateStyle);
        }
        else if (enemy != null && enemy.IsDead)
        {
            GUI.Label(new Rect(0f, Screen.height * 0.42f, Screen.width, 80f), "ENEMY DEFEATED", stateStyle);
        }
    }

    private void DrawHealthBar(Rect rect, string label, int current, int maximum, Color fillColor)
    {
        Color previous = GUI.color;
        GUI.color = new Color(0.02f, 0.02f, 0.02f, 0.82f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        float normalized = maximum > 0 ? Mathf.Clamp01(current / (float)maximum) : 0f;
        Rect fillRect = new Rect(rect.x + 2f, rect.y + 2f, (rect.width - 4f) * normalized, rect.height - 4f);
        GUI.color = fillColor;
        GUI.DrawTexture(fillRect, Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUI.Label(rect, $"{label}  {current} / {maximum}", labelStyle);
        GUI.color = previous;
    }

    private void EnsureStyles()
    {
        if (labelStyle != null)
        {
            return;
        }

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        stateStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 34,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.95f, 0.08f, 0.06f) }
        };
    }
}

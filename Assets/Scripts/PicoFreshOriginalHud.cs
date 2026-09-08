using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// XR-compatible presentation of the original gameplay blood bars. The
/// authored Player Canvas remains the source for ammo, weapon name and
/// crosshair; this small layer replaces only the desktop OnGUI bars that are
/// not reliably composited by the PICO stereo target.
/// </summary>
[DefaultExecutionOrder(-1100)]
[DisallowMultipleComponent]
public sealed class PicoFreshOriginalHud : MonoBehaviour
{
    private const float CanvasWidth = 800f;
    private const float CanvasHeight = 600f;
    private const float BarWidth = 300f;
    private const float BarHeight = 24f;
    private const float BarLeft = 24f;
    // Player Canvas is configured with a bottom-left pivot in the XR copy.
    // Keep the combat readout in one compact lower-left stack above ammo.
    private const float PlayerBarBottom = 146f;
    private const float EnemyBarBottom = 112f;

    private Camera xrCamera;
    private Canvas hudCanvas;
    private GameObject hudRoot;
    private Image playerFill;
    private Image enemyFill;
    private RectTransform playerFillRect;
    private RectTransform enemyFillRect;
    private Text playerLabel;
    private Text enemyLabel;
    private PlayerHealth playerHealth;
    private EnemyController enemy;
    private Level0GameFlow flow;
    private float nextDiagnosticTime;
    private bool readyLogged;
    private int lastPlayerHealth = int.MinValue;
    private int lastEnemyHealth = int.MinValue;

    public void Configure(Camera camera, Canvas canvas)
    {
        xrCamera = camera;
        hudCanvas = canvas;
        EnsureHud();
    }

    private void Start()
    {
        EnsureReferences();
        EnsureHud();
    }

    private void Update()
    {
        if (!PicoFreshRuntime.IsPicoXrActive || hudCanvas == null)
        {
            return;
        }

        EnsureReferences();
        EnsureHud();

        bool playing = flow == null || flow.CurrentState == Level0GameFlow.GameState.Playing;
        if (hudRoot != null && hudRoot.activeSelf != playing)
        {
            hudRoot.SetActive(playing);
        }

        if (!playing || hudRoot == null)
        {
            return;
        }

        float playerRatio = playerHealth != null ? playerHealth.NormalizedHealth : 0f;
        float enemyRatio = enemy != null && enemy.MaxHealth > 0
            ? Mathf.Clamp01(enemy.CurrentHealth / (float)enemy.MaxHealth)
            : 0f;
        SetFill(playerFill, playerFillRect, playerRatio);
        SetFill(enemyFill, enemyFillRect, enemyRatio);
        if (playerLabel != null && playerHealth != null)
        {
            playerLabel.text = $"PLAYER  {playerHealth.CurrentHealth} / {playerHealth.MaxHealth}";
        }
        if (enemyLabel != null && enemy != null)
        {
            enemyLabel.text = $"ENEMY  {enemy.CurrentHealth} / {enemy.MaxHealth}";
        }

        if (!readyLogged)
        {
            readyLogged = true;
            Debug.Log("[PICO-FRESH] PICO_FRESH_ORIGINAL_HUD_READY mode=WorldSpace canvas='" +
                      hudCanvas.name + "' bloodBars=true ammoSource=PlayerCanvas " +
                      "distance=0.90 scale=0.00072.", this);
        }
        if (Time.unscaledTime >= nextDiagnosticTime)
        {
            nextDiagnosticTime = Time.unscaledTime + 1f;
            Debug.Log("[PICO-FRESH] PICO_FRESH_ORIGINAL_HUD_VALUES player=" +
                      (playerHealth != null ? playerHealth.CurrentHealth : -1) + "/" +
                      (playerHealth != null ? playerHealth.MaxHealth : -1) +
                      " enemy=" + (enemy != null ? enemy.CurrentHealth : -1) + "/" +
                      (enemy != null ? enemy.MaxHealth : -1) + ".", this);
        }

        int currentPlayerHealth = playerHealth != null ? playerHealth.CurrentHealth : -1;
        int currentEnemyHealth = enemy != null ? enemy.CurrentHealth : -1;
        if (currentPlayerHealth != lastPlayerHealth || currentEnemyHealth != lastEnemyHealth)
        {
            lastPlayerHealth = currentPlayerHealth;
            lastEnemyHealth = currentEnemyHealth;
            Debug.Log("[PICO-FRESH] PICO_FRESH_ORIGINAL_HUD_CHANGED player=" +
                      currentPlayerHealth + " enemy=" + currentEnemyHealth +
                      " layout=lower-left fill=live.", this);
        }
    }

    private void EnsureReferences()
    {
        if (flow == null)
        {
            flow = FindFirstObjectByType<Level0GameFlow>(FindObjectsInactive.Include);
        }

        if (flow != null)
        {
            if (flow.playerHealth != null)
            {
                playerHealth = flow.playerHealth;
            }
            if (flow.enemy != null)
            {
                enemy = flow.enemy;
            }
        }

        if (playerHealth == null)
        {
            playerHealth = FindFirstObjectByType<PlayerHealth>(FindObjectsInactive.Include);
        }
        if (enemy == null)
        {
            enemy = FindFirstObjectByType<EnemyController>(FindObjectsInactive.Include);
        }
    }

    private void EnsureHud()
    {
        if (hudCanvas == null)
        {
            return;
        }

        if (hudRoot == null)
        {
            Transform existing = hudCanvas.transform.Find("PICO Original Blood Bars");
            hudRoot = existing != null ? existing.gameObject : null;
        }
        if (hudRoot == null)
        {
            hudRoot = new GameObject("PICO Original Blood Bars", typeof(RectTransform));
            hudRoot.transform.SetParent(hudCanvas.transform, false);
        }
        hudRoot.transform.SetAsLastSibling();

        RectTransform rootRect = hudRoot.transform as RectTransform;
        if (rootRect != null)
        {
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.zero;
            rootRect.pivot = Vector2.zero;
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
            rootRect.localScale = Vector3.one;
        }

        if (playerFill != null && enemyFill != null && playerLabel != null && enemyLabel != null)
        {
            return;
        }

        CreateBar("Player Blood Bar", new Vector2(BarLeft, PlayerBarBottom),
                  new Color(0.12f, 0.78f, 0.30f), out playerFill, out playerFillRect, out playerLabel);
        CreateBar("Enemy Blood Bar", new Vector2(BarLeft, EnemyBarBottom),
                  new Color(0.82f, 0.08f, 0.06f), out enemyFill, out enemyFillRect, out enemyLabel);
    }

    private void CreateBar(string name, Vector2 position, Color fillColor,
                           out Image fill, out RectTransform fillRect, out Text label)
    {
        GameObject trackObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        trackObject.transform.SetParent(hudRoot.transform, false);
        RectTransform trackRect = trackObject.transform as RectTransform;
        trackRect.anchorMin = Vector2.zero;
        trackRect.anchorMax = Vector2.zero;
        trackRect.pivot = Vector2.zero;
        trackRect.anchoredPosition = position;
        trackRect.sizeDelta = new Vector2(BarWidth, BarHeight);

        Image track = trackObject.GetComponent<Image>();
        track.color = new Color(0.02f, 0.02f, 0.02f, 0.88f);
        track.raycastTarget = false;

        GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillObject.transform.SetParent(trackObject.transform, false);
        fillRect = fillObject.transform as RectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.zero;
        fillRect.pivot = Vector2.zero;
        fillRect.anchoredPosition = new Vector2(2f, 2f);
        fillRect.sizeDelta = new Vector2(BarWidth - 4f, BarHeight - 4f);
        fill = fillObject.GetComponent<Image>();
        fill.color = fillColor;
        // Width is driven directly from live health. This avoids relying on
        // a dynamically-created filled-image mesh in the stereo compositor.
        fill.type = Image.Type.Simple;
        fill.raycastTarget = false;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.transform.SetParent(trackObject.transform, false);
        RectTransform labelRect = labelObject.transform as RectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.zero;
        labelRect.pivot = Vector2.zero;
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.sizeDelta = new Vector2(BarWidth, BarHeight);
        label = labelObject.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = 15;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    private static void SetFill(Image image, RectTransform fillRect, float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        if (image != null)
        {
            image.fillAmount = ratio;
            image.SetVerticesDirty();
        }
        if (fillRect != null)
        {
            fillRect.sizeDelta = new Vector2((BarWidth - 4f) * ratio, BarHeight - 4f);
        }
    }
}

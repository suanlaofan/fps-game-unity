using FPSControllerLPFP;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Owns the Level 0 title, gameplay and result-screen flow. The visual layout is
/// authored by Level0FigmaUiSetup so it remains editable in the Unity scene.
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public class Level0GameFlow : MonoBehaviour
{
    public enum GameState
    {
        Menu,
        Playing,
        Victory,
        GameOver
    }

    private static bool startPlayingAfterSceneReload;

    [Header("Scene References")]
    public PlayerHealth playerHealth;
    public EnemyController enemy;
    public HorrorBgmPlayer bgmPlayer;
    public CombatHUD combatHud;
    public FpsControllerLPFP playerController;
    public GameObject legacyPlayerCanvas;

    [Header("Figma UI States")]
    public GameObject menuScreen;
    public GameObject gameplayHud;
    public GameObject victoryScreen;
    public GameObject gameOverScreen;

    [Header("Figma UI Buttons")]
    public Button startGameButton;
    public Button exitButton;
    public Button victoryContinueButton;
    public Button victoryMainMenuButton;
    public Button gameOverContinueButton;
    public Button gameOverMainMenuButton;

    [Header("Gameplay HUD")]
    public Image playerHealthFill;
    public Text playerHealthText;
    public Image enemyHealthFill;
    public Text enemyHealthText;
    public Text ammoText;
    public Text weaponNameText;

    private AutomaticGunScriptLPFP[] automaticWeapons;
    private HandgunScriptLPFP[] handgunWeapons;
    private Rigidbody playerBody;
    private Animator[] enemyAnimators;
    private bool initialized;
    private GameState state;

    public GameState CurrentState => state;

    private void Awake()
    {
        ResolveReferences();
        CacheGameplayComponents();
        ConfigureButtons();

        if (combatHud != null)
        {
            // The original in-game presentation is used while playing.  Result
            // screens are exact Figma images, so never let terminal labels
            // paint over them.
            combatHud.showBars = false;
            combatHud.showTerminalState = false;
        }

        if (legacyPlayerCanvas != null)
        {
            legacyPlayerCanvas.SetActive(false);
        }

        bool continueDirectly = startPlayingAfterSceneReload;
        startPlayingAfterSceneReload = false;
        // Disable the roots before any of their normal Awake/Start messages run.
        // Start then restores them after their own initialization has completed.
        SetEnemyRootActive(false);
        SetBgmRootActive(false);
        initialized = true;
        EnterMenu();
        if (continueDirectly)
        {
            StartCoroutine(BeginPlayingNextFrame());
        }
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        switch (state)
        {
            case GameState.Playing:
                UpdateGameplayHud();
                if (playerHealth != null && playerHealth.IsDead)
                {
                    EnterGameOver();
                }
                else if (enemy != null && enemy.IsDead)
                {
                    EnterVictory();
                }
                break;

            case GameState.Menu:
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                {
                    StartGame();
                }
                else if (Input.GetKeyDown(KeyCode.Escape))
                {
                    ExitGame();
                }
                break;

            case GameState.Victory:
            case GameState.GameOver:
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                {
                    ContinueGame();
                }
                else if (Input.GetKeyDown(KeyCode.Escape))
                {
                    ReturnToMainMenu();
                }
                break;
        }
    }

    public void StartGame()
    {
        if (state == GameState.Playing)
        {
            return;
        }

        EnterPlaying();
    }

    private System.Collections.IEnumerator BeginPlayingNextFrame()
    {
        yield return null;
        EnterPlaying();
    }

    public void ContinueGame()
    {
        startPlayingAfterSceneReload = true;
        ReloadCurrentScene();
    }

    public void ReturnToMainMenu()
    {
        startPlayingAfterSceneReload = false;
        ReloadCurrentScene();
    }

    public void ExitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void EnterMenu()
    {
        state = GameState.Menu;
        Time.timeScale = 0f;
        SetPlayerGameplayEnabled(false);
        SetEnemyRootActive(false);
        SetBgmRootActive(false);
        ShowOnly(menuScreen);
        SetMenuCursor();
        Debug.Log("[Level0UI] State=Menu. START GAME begins the Level 0 run.", this);
    }

    private void EnterPlaying()
    {
        state = GameState.Playing;
        Time.timeScale = 1f;
        SetEnemyRootActive(true);
        SetBgmRootActive(true);
        SetPlayerGameplayEnabled(true);
        // Keep the supplied Figma menu/result screens intact, but restore the
        // project's original gameplay HUD instead of the later hand-authored
        // Gameplay HUD canvas.
        ShowOnly(null);
        SetGameplayCursor();
        UpdateGameplayHud();
        Debug.Log("[Level0UI] State=Playing.", this);
    }

    private void EnterVictory()
    {
        if (state != GameState.Playing)
        {
            return;
        }

        state = GameState.Victory;
        SetPlayerGameplayEnabled(false);
        SetBgmRootActive(false);
        SetEnemyRootActive(false);
        Time.timeScale = 0f;
        ShowOnly(victoryScreen);
        SetMenuCursor();
        Debug.Log("[Level0UI] State=Victory.", this);
    }

    private void EnterGameOver()
    {
        if (state != GameState.Playing)
        {
            return;
        }

        state = GameState.GameOver;
        SetPlayerGameplayEnabled(false);
        FreezeEnemyForResultScreen();
        SetBgmRootActive(false);
        Time.timeScale = 0f;
        ShowOnly(gameOverScreen);
        SetMenuCursor();
        Debug.Log("[Level0UI] State=GameOver.", this);
    }

    private void ResolveReferences()
    {
        if (playerHealth == null)
        {
            playerHealth = FindUniqueComponent<PlayerHealth>();
        }

        if (enemy == null)
        {
            EnemyController[] enemies = FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (EnemyController candidate in enemies)
            {
                if (candidate != null && candidate.name == "diren")
                {
                    enemy = candidate;
                    break;
                }
            }

            if (enemy == null && enemies.Length == 1)
            {
                enemy = enemies[0];
            }
        }

        if (bgmPlayer == null)
        {
            bgmPlayer = FindUniqueComponent<HorrorBgmPlayer>();
        }

        if (combatHud == null && playerHealth != null)
        {
            combatHud = playerHealth.GetComponent<CombatHUD>();
        }

        if (playerController == null && playerHealth != null)
        {
            playerController = playerHealth.GetComponent<FpsControllerLPFP>();
        }

        if (legacyPlayerCanvas == null && playerHealth != null)
        {
            Transform legacyCanvas = FindDescendantByName(playerHealth.transform, "Player Canvas");
            legacyPlayerCanvas = legacyCanvas != null ? legacyCanvas.gameObject : null;
        }
    }

    private void CacheGameplayComponents()
    {
        if (playerHealth != null)
        {
            automaticWeapons = playerHealth.GetComponentsInChildren<AutomaticGunScriptLPFP>(true);
            handgunWeapons = playerHealth.GetComponentsInChildren<HandgunScriptLPFP>(true);
            playerBody = playerHealth.GetComponent<Rigidbody>();
        }

        if (enemy != null)
        {
            enemyAnimators = enemy.GetComponentsInChildren<Animator>(true);
        }
    }

    private void ConfigureButtons()
    {
        if (startGameButton != null)
        {
            startGameButton.onClick.AddListener(StartGame);
        }
        if (exitButton != null)
        {
            exitButton.onClick.AddListener(ExitGame);
        }
        if (victoryContinueButton != null)
        {
            victoryContinueButton.onClick.AddListener(ContinueGame);
        }
        if (victoryMainMenuButton != null)
        {
            victoryMainMenuButton.onClick.AddListener(ReturnToMainMenu);
        }
        if (gameOverContinueButton != null)
        {
            gameOverContinueButton.onClick.AddListener(ContinueGame);
        }
        if (gameOverMainMenuButton != null)
        {
            gameOverMainMenuButton.onClick.AddListener(ReturnToMainMenu);
        }
    }

    private void SetPlayerGameplayEnabled(bool enabled)
    {
        if (playerController != null && (enabled || playerHealth == null || !playerHealth.IsDead))
        {
            playerController.enabled = enabled;
        }

        SetComponentsEnabled(automaticWeapons, enabled);
        SetComponentsEnabled(handgunWeapons, enabled);

        if (combatHud != null)
        {
            // Show the pre-existing health bars only during a live run.  This
            // leaves the Figma menu, win and lose images completely untouched.
            combatHud.showBars = enabled;
            combatHud.showTerminalState = false;
        }

        if (legacyPlayerCanvas != null)
        {
            // Restore the original weapon UI (crosshair/ammo presentation)
            // during gameplay, and hide it behind every Figma full-page view.
            legacyPlayerCanvas.SetActive(enabled);
        }

        if (!enabled && playerBody != null)
        {
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            if (playerHealth == null || !playerHealth.IsDead)
            {
                playerBody.constraints = RigidbodyConstraints.FreezeAll;
            }
        }
        else if (enabled && playerBody != null && (playerHealth == null || !playerHealth.IsDead))
        {
            playerBody.constraints = RigidbodyConstraints.FreezeRotation;
        }
    }

    private void SetEnemyRootActive(bool active)
    {
        if (enemy == null)
        {
            return;
        }

        GameObject root = enemy.gameObject;
        if (root.activeSelf != active)
        {
            root.SetActive(active);
        }

        if (active)
        {
            enemy.enabled = true;
            if (enemy.horrorAudio != null)
            {
                enemy.horrorAudio.enabled = true;
            }
            RestoreEnemyAnimation();
        }
    }

    private void SetBgmRootActive(bool active)
    {
        if (bgmPlayer == null)
        {
            return;
        }

        GameObject root = bgmPlayer.gameObject;
        if (root.activeSelf != active)
        {
            root.SetActive(active);
        }

        if (active && bgmPlayer.isActiveAndEnabled && !bgmPlayer.IsPlaying)
        {
            // This is a harmless no-op before Start, and also covers a manually
            // re-enabled BGM object during editor testing.
            bgmPlayer.StartPlayback();
        }
    }

    private void FreezeEnemyForResultScreen()
    {
        if (enemy == null || !enemy.gameObject.activeInHierarchy)
        {
            return;
        }

        NavMeshAgent agent = enemy.agent != null ? enemy.agent : enemy.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        enemy.enabled = false;
        if (enemy.horrorAudio != null)
        {
            enemy.horrorAudio.enabled = false;
        }

        if (enemyAnimators != null)
        {
            foreach (Animator animator in enemyAnimators)
            {
                if (animator != null)
                {
                    animator.speed = 0f;
                }
            }
        }
    }

    private void RestoreEnemyAnimation()
    {
        if (enemyAnimators == null)
        {
            enemyAnimators = enemy.GetComponentsInChildren<Animator>(true);
        }

        foreach (Animator animator in enemyAnimators)
        {
            if (animator != null)
            {
                animator.speed = 1f;
            }
        }
    }

    private void UpdateGameplayHud()
    {
        if (playerHealth != null)
        {
            float health = playerHealth.NormalizedHealth;
            if (playerHealthFill != null)
            {
                playerHealthFill.fillAmount = health;
            }
            if (playerHealthText != null)
            {
                playerHealthText.text = $"{playerHealth.CurrentHealth:000} / {playerHealth.MaxHealth:000}";
            }
        }

        if (enemy != null)
        {
            float health = enemy.MaxHealth > 0 ? Mathf.Clamp01(enemy.CurrentHealth / (float)enemy.MaxHealth) : 0f;
            if (enemyHealthFill != null)
            {
                enemyHealthFill.fillAmount = health;
            }
            if (enemyHealthText != null)
            {
                enemyHealthText.text = $"ENTITY // {enemy.CurrentHealth:000} / {enemy.MaxHealth:000}";
            }
        }

        AutomaticGunScriptLPFP weapon = GetActiveAutomaticWeapon();
        if (weapon != null)
        {
            if (ammoText != null)
            {
                ammoText.text = $"{weapon.CurrentAmmo:00} / {weapon.MaxAmmo:00}";
            }
            if (weaponNameText != null)
            {
                weaponNameText.text = weapon.DisplayWeaponName.ToUpperInvariant();
            }
        }
        else
        {
            if (ammoText != null)
            {
                ammoText.text = "-- / --";
            }
            if (weaponNameText != null)
            {
                weaponNameText.text = "WEAPON OFFLINE";
            }
        }
    }

    private AutomaticGunScriptLPFP GetActiveAutomaticWeapon()
    {
        if (automaticWeapons == null)
        {
            return null;
        }

        foreach (AutomaticGunScriptLPFP weapon in automaticWeapons)
        {
            if (weapon != null && weapon.gameObject.activeInHierarchy)
            {
                return weapon;
            }
        }
        return automaticWeapons.Length > 0 ? automaticWeapons[0] : null;
    }

    private void ShowOnly(GameObject target)
    {
        SetActive(menuScreen, target == menuScreen);
        SetActive(gameplayHud, target == gameplayHud);
        SetActive(victoryScreen, target == victoryScreen);
        SetActive(gameOverScreen, target == gameOverScreen);
    }

    private void ReloadCurrentScene()
    {
        Time.timeScale = 1f;
        Scene currentScene = SceneManager.GetActiveScene();
        if (currentScene.buildIndex >= 0)
        {
            SceneManager.LoadScene(currentScene.buildIndex);
        }
        else
        {
            SceneManager.LoadScene(currentScene.name);
        }
    }

    private static void SetComponentsEnabled(Behaviour[] components, bool enabled)
    {
        if (components == null)
        {
            return;
        }

        foreach (Behaviour component in components)
        {
            if (component != null)
            {
                component.enabled = enabled;
            }
        }
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private static void SetMenuCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private static void SetGameplayCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private static T FindUniqueComponent<T>() where T : Component
    {
        T[] found = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return found.Length > 0 ? found[0] : null;
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null)
        {
            return null;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == targetName)
            {
                return child;
            }
        }
        return null;
    }
}

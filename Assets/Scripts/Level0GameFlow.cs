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
    [Header("Opening preparation")]
    public float preparationSeconds = 8f;
    public float minimumEnemyDistance = 18f;
    private float enemyReleaseAt;
    private Coroutine preparation;
    public float PreparationRemaining => state == GameState.Playing ? Mathf.Max(0, enemyReleaseAt-Time.time) : 0;
    public float EnemySpawnDistance { get; private set; }

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

    private static bool DesktopKeyDown(KeyCode key)
    {
        if (!Application.isEditor) return false;
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return false;
        if (key == KeyCode.Return) return keyboard.enterKey.wasPressedThisFrame;
        if (key == KeyCode.Space) return keyboard.spaceKey.wasPressedThisFrame;
        if (key == KeyCode.Escape) return keyboard.escapeKey.wasPressedThisFrame;
        return false;
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
                if (DesktopKeyDown(KeyCode.Return) || DesktopKeyDown(KeyCode.Space) ||
                    PicoFreshRuntime.MenuPressedThisFrame)
                {
                    StartGame();
                }
                else if (DesktopKeyDown(KeyCode.Escape))
                {
                    ExitGame();
                }
                break;

            case GameState.Victory:
            case GameState.GameOver:
                if (DesktopKeyDown(KeyCode.Return) || DesktopKeyDown(KeyCode.Space) ||
                    PicoFreshRuntime.MenuPressedThisFrame)
                {
                    ContinueGame();
                }
                else if (DesktopKeyDown(KeyCode.Escape))
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
        SetEnemyRootActive(false);
        PositionEnemyForNewRun();
        enemyReleaseAt = Time.time + preparationSeconds;
        if (preparation != null) StopCoroutine(preparation);
        preparation = StartCoroutine(ReleaseEnemyAfterPreparation());
        SetBgmRootActive(true);
        SetPlayerGameplayEnabled(true);
        // The authored Figma pages are reserved for menu and result states.
        // During a run the original project HUD (CombatHUD + Player Canvas)
        // supplies the health bars, ammo and weapon readout.
        ShowOnly(null);
        SetGameplayCursor();
        UpdateGameplayHud();
        Debug.Log("[Level0UI] State=Playing hud=front-world preparation="+preparationSeconds+" enemyDistance="+EnemySpawnDistance, this);
    }

    private System.Collections.IEnumerator ReleaseEnemyAfterPreparation()
    {
        while (state == GameState.Playing && Time.time < enemyReleaseAt) yield return null;
        if (state != GameState.Playing) yield break;
        // The player can sprint to the planned spawn during preparation.
        // Recheck at release time and keep waiting if no safe reachable point exists.
        while (!PositionEnemyForNewRun())
        {
            enemyReleaseAt = Time.time + 1f;
            yield return new WaitForSeconds(1f);
            if (state != GameState.Playing) yield break;
        }
        SetEnemyRootActive(true);
        Debug.Log("LEVEL0_ENCOUNTER_BEGIN distance="+Vector3.Distance(enemy.transform.position,playerHealth.transform.position));
        preparation = null;
    }

    private bool PositionEnemyForNewRun()
    {
        if (!enemy || !playerHealth) return false;
        Vector3 origin = playerHealth.transform.position;
        var agent = enemy.GetComponent<NavMeshAgent>();
        int areas = agent ? agent.areaMask : NavMesh.AllAreas;
        if (!NavMesh.SamplePosition(origin,out NavMeshHit playerNav,3f,areas))
        { Debug.LogError("LEVEL0_ENCOUNTER_PLAYER_NAVMESH_MISSING"); return false; }
        var path = new NavMeshPath();
        Vector3 selected = enemy.transform.position;
        float bestScore = float.NegativeInfinity;
        for (int ring=0;ring<4;ring++)
        for (int step=0;step<16;step++)
        {
            float angle=step*22.5f*Mathf.Deg2Rad;
            Vector3 target=origin+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*(minimumEnemyDistance+ring*4);
            if(!NavMesh.SamplePosition(target,out NavMeshHit hit,2f,areas))continue;
            float distance=Vector3.Distance(Vector3.ProjectOnPlane(hit.position-origin,Vector3.up),Vector3.zero);
            if(distance<minimumEnemyDistance||!NavMesh.CalculatePath(hit.position,playerNav.position,areas,path)||path.status!=NavMeshPathStatus.PathComplete)continue;
            bool hidden=Physics.Linecast(origin+Vector3.up*1.4f,hit.position+Vector3.up*1.4f,1<<12,QueryTriggerInteraction.Ignore);
            float score=(hidden?100:0)-Mathf.Abs(distance-(minimumEnemyDistance+4));
            if(score>bestScore){bestScore=score;selected=hit.position;}
        }
        if(float.IsNegativeInfinity(bestScore))
        { Debug.LogError("LEVEL0_ENCOUNTER_NO_DISTANT_SPAWN minimum="+minimumEnemyDistance); return false; }
        enemy.transform.position=selected;
        EnemySpawnDistance=Vector3.Distance(Vector3.ProjectOnPlane(selected-origin,Vector3.up),Vector3.zero);
        Debug.Log("LEVEL0_ENCOUNTER_PREPARED seconds="+preparationSeconds+" distance="+EnemySpawnDistance+" navmesh=complete");
        return true;
    }

    private void EnterVictory()
    {
        if (state != GameState.Playing)
        {
            return;
        }

        state = GameState.Victory;
        if (PicoFreshRuntime.Instance) PicoFreshRuntime.Instance.SetPaused(false);
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
        if (PicoFreshRuntime.Instance) PicoFreshRuntime.Instance.SetPaused(false);
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

        // PICO mounts the visible weapon subtree below XR Camera before this
        // flow initializes, so discover the same authored weapon components in
        // the loaded scene if they are no longer children of the physics root.
        if (automaticWeapons == null || automaticWeapons.Length == 0)
        {
            automaticWeapons = FindObjectsByType<AutomaticGunScriptLPFP>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
        if (handgunWeapons == null || handgunWeapons.Length == 0)
        {
            handgunWeapons = FindObjectsByType<HandgunScriptLPFP>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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
            playerController.enabled = enabled && !PicoFreshRuntime.IsPicoXrActive;
        }

        SetComponentsEnabled(automaticWeapons, enabled && !PicoFreshRuntime.IsPicoXrActive);
        SetComponentsEnabled(handgunWeapons, enabled && !PicoFreshRuntime.IsPicoXrActive);
        if (PicoFreshRuntime.IsPicoXrActive)
        {
            PicoFreshRuntime.SetViewModelVisible(enabled);
        }

        if (combatHud != null)
        {
            // PICO uses the XR-compatible original blood-bar layer. Keep the
            // legacy OnGUI bars for desktop, but do not rely on IMGUI inside
            // the stereo compositor.
            combatHud.showBars = enabled && !PicoFreshRuntime.IsPicoXrActive;
            combatHud.showTerminalState = false;
        }

        if (legacyPlayerCanvas != null)
        {
            // The authored Player Canvas contains the original ammo count,
            // weapon name and crosshair. PicoFreshRuntime routes and scales it
            // for the XR camera before this state is entered.
            legacyPlayerCanvas.SetActive(enabled && !PicoFreshRuntime.IsPicoXrActive);
        }

        Debug.Log("[Level0UI] ORIGINAL_GAMEPLAY_HUD enabled=" + enabled +
                  " bloodBars=" + (combatHud != null && combatHud.showBars) +
                  " ammoCanvas=" + (legacyPlayerCanvas != null && legacyPlayerCanvas.activeInHierarchy) +
                  " xrBloodBars=" + (PicoFreshRuntime.IsPicoXrActive && enabled) +
                  " figmaGameplay=" + (gameplayHud != null && gameplayHud.activeInHierarchy) + ".", this);

        if (!enabled && playerBody != null && !playerBody.isKinematic)
        {
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            if (playerHealth == null || !playerHealth.IsDead)
            {
                playerBody.constraints = RigidbodyConstraints.FreezeAll;
            }
        }
        else if (enabled && playerBody != null && !playerBody.isKinematic && (playerHealth == null || !playerHealth.IsDead))
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
        if (PicoFreshRuntime.IsPicoXrActive) return;
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
        if (PicoFreshRuntime.IsPicoXrActive && !Application.isEditor)
        {
            // Re-enter the lightweight XR bootstrap so the runtime loader and
            // AssetBundle lifecycle are rebuilt before jogo is loaded again.
            SceneManager.LoadScene(0, LoadSceneMode.Single);
            return;
        }

        Scene currentScene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(currentScene.path, new LoadSceneParameters(LoadSceneMode.Single));
        return;
#else
        if (currentScene.buildIndex >= 0)
        {
            SceneManager.LoadScene(currentScene.buildIndex);
        }
        else
        {
            SceneManager.LoadScene(currentScene.name);
        }
#endif
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

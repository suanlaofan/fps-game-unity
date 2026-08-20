#if UNITY_EDITOR
using System.Collections.Generic;
using FPSControllerLPFP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the editable Unity implementation of the Level 0 Figma screens:
/// title, in-game HUD, victory and game-over. It intentionally uses the
/// project's existing 3D scene as the background, matching the Figma concept.
/// </summary>
public static class Level0FigmaUiSetup
{
    private const string ScenePath = "Assets/Scenes/jogo.unity";
    private const string RootName = "Level 0 UI";
    private const string EnemyName = "diren";
    private const string LegacyPlayerCanvasName = "Player Canvas";

    private static readonly Color WarmGold = new Color(0.96f, 0.70f, 0.12f, 1f);
    private static readonly Color WarmGoldMuted = new Color(0.73f, 0.50f, 0.07f, 1f);
    private static readonly Color Cream = new Color(0.94f, 0.91f, 0.83f, 1f);
    private static readonly Color Threat = new Color(0.82f, 0.10f, 0.06f, 1f);

    [MenuItem("Tools/FPS Game/Apply Figma Level 0 UI")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Level0UISetup] Exit Play Mode before applying the Figma UI.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!IsTargetScene(scene))
        {
            Debug.LogError($"[Level0UISetup] Open '{ScenePath}' before running this command.");
            return;
        }

        ApplyToOpenScene(scene);
    }

    /// <summary>Batch-mode entry point: opens the target scene before applying.</summary>
    public static void ApplyFromBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Level0UISetup] Batch apply cannot run in Play Mode.");
            return;
        }

        Scene scene = OpenTargetScene();
        if (!IsTargetScene(scene))
        {
            return;
        }

        ApplyToOpenScene(scene);
    }

    private static void ApplyToOpenScene(Scene scene)
    {

        PlayerHealth player = FindSingleComponent<PlayerHealth>(scene, "PlayerHealth");
        EnemyController enemy = FindEnemy(scene);
        HorrorBgmPlayer bgm = FindSingleComponent<HorrorBgmPlayer>(scene, "HorrorBgmPlayer");
        if (player == null || enemy == null || bgm == null)
        {
            return;
        }

        EnsureEventSystem(scene);

        GameObject root = FindObject(scene, RootName);
        if (root == null)
        {
            root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(root, "Create Level 0 UI");
            SceneManager.MoveGameObjectToScene(root, scene);
        }

        Canvas canvas = GetOrAddComponent<Canvas>(root);
        CanvasScaler scaler = GetOrAddComponent<CanvasScaler>(root);
        GetOrAddComponent<GraphicRaycaster>(root);
        Level0GameFlow flow = GetOrAddComponent<Level0GameFlow>(root);
        ConfigureCanvas(canvas, scaler);
        ClearChildren(root.transform);

        GameObject menuScreen = CreateMenuScreen(root.transform, out Button startButton, out Button exitButton);
        GameObject gameplayHud = CreateGameplayHud(root.transform, out Image playerFill, out Text playerText,
            out Image enemyFill, out Text enemyText, out Text ammoText, out Text weaponNameText);
        GameObject victoryScreen = CreateResultScreen(root.transform, "Victory Screen", "VICTORY", "ESCAPE SUCCESSFUL", false,
            out Button victoryContinue, out Button victoryMainMenu);
        GameObject gameOverScreen = CreateResultScreen(root.transform, "Game Over Screen", "GAME OVER", "ESCAPE FAILED", true,
            out Button gameOverContinue, out Button gameOverMainMenu);

        Undo.RecordObject(flow, "Bind Level 0 UI flow");
        flow.playerHealth = player;
        flow.enemy = enemy;
        flow.bgmPlayer = bgm;
        flow.combatHud = player.GetComponent<CombatHUD>();
        flow.playerController = player.GetComponent<FpsControllerLPFP>();
        Transform legacyCanvas = FindDescendantByName(player.transform, LegacyPlayerCanvasName);
        flow.legacyPlayerCanvas = legacyCanvas != null ? legacyCanvas.gameObject : null;
        flow.menuScreen = menuScreen;
        flow.gameplayHud = gameplayHud;
        flow.victoryScreen = victoryScreen;
        flow.gameOverScreen = gameOverScreen;
        flow.startGameButton = startButton;
        flow.exitButton = exitButton;
        flow.victoryContinueButton = victoryContinue;
        flow.victoryMainMenuButton = victoryMainMenu;
        flow.gameOverContinueButton = gameOverContinue;
        flow.gameOverMainMenuButton = gameOverMainMenu;
        flow.playerHealthFill = playerFill;
        flow.playerHealthText = playerText;
        flow.enemyHealthFill = enemyFill;
        flow.enemyHealthText = enemyText;
        flow.ammoText = ammoText;
        flow.weaponNameText = weaponNameText;
        EditorUtility.SetDirty(flow);

        if (flow.combatHud != null)
        {
            Undo.RecordObject(flow.combatHud, "Hide legacy combat HUD text");
            flow.combatHud.showBars = false;
            flow.combatHud.showTerminalState = false;
            EditorUtility.SetDirty(flow.combatHud);
        }

        if (flow.legacyPlayerCanvas != null && flow.legacyPlayerCanvas.activeSelf)
        {
            Undo.RecordObject(flow.legacyPlayerCanvas, "Hide legacy weapon HUD");
            flow.legacyPlayerCanvas.SetActive(false);
        }

        AddSceneToBuildSettings();
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene))
        {
            Debug.LogError($"[Level0UISetup] Failed to save '{scene.path}'.");
            return;
        }

        Selection.activeGameObject = root;
        Debug.Log($"[Level0UISetup] SUCCESS: created editable Figma-inspired UI screens under '{RootName}', " +
                  "bound Menu/Playing/Victory/GameOver flow, hid legacy HUD layers, and added jogo to Build Settings.");
    }

    [MenuItem("Tools/FPS Game/Validate Figma Level 0 UI")]
    public static void Validate()
    {
        Scene scene = SceneManager.GetActiveScene();
        ValidateOpenScene(scene);
    }

    /// <summary>Batch-mode entry point: opens the target scene before validating.</summary>
    public static void ValidateFromBatch()
    {
        Scene scene = OpenTargetScene();
        if (!IsTargetScene(scene))
        {
            return;
        }

        ValidateOpenScene(scene);
    }

    private static void ValidateOpenScene(Scene scene)
    {
        GameObject root = FindObject(scene, RootName);
        Level0GameFlow flow = root != null ? root.GetComponent<Level0GameFlow>() : null;
        bool sceneInBuild = false;
        foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
        {
            if (buildScene.path == ScenePath && buildScene.enabled)
            {
                sceneInBuild = true;
                break;
            }
        }

        bool valid = flow != null && flow.playerHealth != null && flow.enemy != null && flow.bgmPlayer != null &&
                     flow.menuScreen != null && flow.gameplayHud != null && flow.victoryScreen != null &&
                     flow.gameOverScreen != null && flow.startGameButton != null && flow.exitButton != null &&
                     flow.victoryContinueButton != null && flow.victoryMainMenuButton != null &&
                     flow.gameOverContinueButton != null && flow.gameOverMainMenuButton != null && sceneInBuild;

        if (!valid)
        {
            Debug.LogError($"[Level0UIValidate] FAILED root={(root != null)}, flow={(flow != null)}, " +
                           $"player={(flow != null && flow.playerHealth != null)}, enemy={(flow != null && flow.enemy != null)}, " +
                           $"bgm={(flow != null && flow.bgmPlayer != null)}, screens={(flow != null && flow.menuScreen != null && flow.gameplayHud != null && flow.victoryScreen != null && flow.gameOverScreen != null)}, " +
                           $"buttons={(flow != null && flow.startGameButton != null && flow.exitButton != null && flow.victoryContinueButton != null && flow.victoryMainMenuButton != null && flow.gameOverContinueButton != null && flow.gameOverMainMenuButton != null)}, buildScene={sceneInBuild}.");
            return;
        }

        Canvas canvas = root.GetComponent<Canvas>();
        Debug.Log($"[Level0UIValidate] SUCCESS canvas={(canvas != null ? canvas.renderMode.ToString() : "<none>")}, " +
                  $"screens=4, playerHP={flow.playerHealth.MaxHealth}, enemyHP={flow.enemy.MaxHealth}, " +
                  $"legacyCanvasHidden={(flow.legacyPlayerCanvas == null || !flow.legacyPlayerCanvas.activeSelf)}, " +
                  $"legacyBarsHidden={(flow.combatHud != null && !flow.combatHud.showBars && !flow.combatHud.showTerminalState)}, " +
                  $"buildScene={sceneInBuild}.");
    }

    private static GameObject CreateMenuScreen(Transform parent, out Button startButton, out Button exitButton)
    {
        GameObject screen = CreateScreen(parent, "Menu Screen");
        AddFullImage(screen.transform, "Menu Darken", new Color(0f, 0f, 0f, 0.40f));
        AddLeftVeil(screen.transform, "Menu Left Veil", 0.63f);

        Text eyebrow = AddText(screen.transform, "Eyebrow", "BACKROOMS // ENTRY PROTOCOL", 15, WarmGoldMuted, TextAnchor.MiddleLeft);
        SetRect(eyebrow.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(202f, -222f), new Vector2(420f, 28f));

        Text title = AddText(screen.transform, "Title", "LEVEL 0", 98, Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
        SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(196f, -342f), new Vector2(660f, 126f));
        AddLine(screen.transform, "Title Rule", new Vector2(200f, -365f), new Vector2(522f, 2f), WarmGoldMuted);
        AddText(screen.transform, "Instruction", "DO NOT LOOK BACK.", 16, new Color(0.80f, 0.73f, 0.56f, 0.92f), TextAnchor.MiddleLeft)
            .rectTransform.SetParent(screen.transform, false);
        RectTransform instruction = screen.transform.Find("Instruction").GetComponent<RectTransform>();
        SetRect(instruction, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(202f, -406f), new Vector2(420f, 30f));

        startButton = AddGoldButton(screen.transform, "START GAME", new Vector2(202f, -493f), true);
        startButton.gameObject.name = "START GAME";
        exitButton = AddGoldButton(screen.transform, "EXIT", new Vector2(202f, -583f), false);
        exitButton.gameObject.name = "EXIT";

        AddText(screen.transform, "Footer", "WASD MOVE  •  MOUSE LOOK  •  LMB FIRE  •  R RELOAD", 13,
            new Color(0.72f, 0.64f, 0.44f, 0.72f), TextAnchor.MiddleLeft).rectTransform
            .SetParent(screen.transform, false);
        RectTransform footer = screen.transform.Find("Footer").GetComponent<RectTransform>();
        SetRect(footer, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(204f, 74f), new Vector2(600f, 30f));
        return screen;
    }

    private static GameObject CreateResultScreen(Transform parent, string screenName, string titleText, string subtitleText,
        bool threat, out Button continueButton, out Button mainMenuButton)
    {
        GameObject screen = CreateScreen(parent, screenName);
        AddFullImage(screen.transform, "Result Darken", new Color(0f, 0f, 0f, threat ? 0.53f : 0.43f));
        AddLeftVeil(screen.transform, "Result Left Veil", 0.68f);
        if (threat)
        {
            Image alertBar = AddImage(screen.transform, "Threat Accent", new Color(0.64f, 0.055f, 0.025f, 0.74f));
            SetRect(alertBar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(196f, -371f), new Vector2(522f, 3f));
        }

        Text eyebrow = AddText(screen.transform, "Eyebrow", threat ? "VITALS LOST // SIGNAL TERMINATED" : "EXIT FOUND // SIGNAL STABLE",
            15, threat ? new Color(0.86f, 0.25f, 0.13f, 1f) : WarmGoldMuted, TextAnchor.MiddleLeft);
        SetRect(eyebrow.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(202f, -222f), new Vector2(500f, 28f));

        Text title = AddText(screen.transform, "Title", titleText, titleText == "GAME OVER" ? 84 : 96, Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
        SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(196f, -342f), new Vector2(770f, 126f));
        AddLine(screen.transform, "Title Rule", new Vector2(200f, -365f), new Vector2(522f, 2f), threat ? Threat : WarmGoldMuted);

        Text subtitle = AddText(screen.transform, "Subtitle", subtitleText, 15, threat ? new Color(0.90f, 0.36f, 0.18f, 1f) : WarmGold,
            TextAnchor.MiddleLeft);
        SetRect(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(202f, -410f), new Vector2(480f, 30f));

        continueButton = AddGoldButton(screen.transform, "CONTINUE", new Vector2(202f, -494f), true);
        continueButton.gameObject.name = "CONTINUE";
        mainMenuButton = AddGoldButton(screen.transform, "MAIN MENU", new Vector2(202f, -584f), false);
        mainMenuButton.gameObject.name = "MAIN MENU";

        Text hint = AddText(screen.transform, "Hint", "[ ENTER ] CONTINUE     [ ESC ] MAIN MENU", 13,
            new Color(0.74f, 0.67f, 0.48f, 0.75f), TextAnchor.MiddleLeft);
        SetRect(hint.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(204f, 74f), new Vector2(560f, 30f));
        return screen;
    }

    private static GameObject CreateGameplayHud(Transform parent, out Image playerFill, out Text playerText, out Image enemyFill,
        out Text enemyText, out Text ammoText, out Text weaponNameText)
    {
        GameObject screen = CreateScreen(parent, "Gameplay HUD");
        screen.GetComponent<RectTransform>().SetAsLastSibling();

        Image topShade = AddImage(screen.transform, "Top Shade", new Color(0f, 0f, 0f, 0.43f));
        SetStretchTop(topShade.rectTransform, 120f);

        Text location = AddText(screen.transform, "Location", "LEVEL 0  /  HALLWAY SECTOR", 15, WarmGold, TextAnchor.MiddleLeft);
        SetRect(location.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(52f, -58f), new Vector2(420f, 30f));
        AddLine(screen.transform, "Location Rule", new Vector2(52f, -86f), new Vector2(286f, 1f), WarmGoldMuted);

        Text objective = AddText(screen.transform, "Objective", "OBJECTIVE  //  SURVIVE", 14, Cream, TextAnchor.MiddleRight);
        SetRect(objective.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-432f, -58f), new Vector2(380f, 30f));

        GameObject playerPanel = AddPanel(screen.transform, "Player Vitals", new Vector2(48f, 66f), new Vector2(400f, 110f));
        Text playerLabel = AddText(playerPanel.transform, "Label", "PLAYER VITALS", 14, WarmGold, TextAnchor.MiddleLeft, FontStyle.Bold);
        SetRect(playerLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -35f), new Vector2(180f, 24f));
        playerText = AddText(playerPanel.transform, "Value", "100 / 100", 24, Cream, TextAnchor.MiddleRight, FontStyle.Bold);
        SetRect(playerText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-178f, -40f), new Vector2(154f, 32f));
        playerFill = AddProgressBar(playerPanel.transform, "Health Fill", new Vector2(22f, 22f), new Vector2(356f, 12f), new Color(0.91f, 0.66f, 0.12f, 1f));

        GameObject enemyPanel = AddPanel(screen.transform, "Entity Vitals", new Vector2(760f, 66f), new Vector2(400f, 84f));
        RectTransform enemyRect = enemyPanel.GetComponent<RectTransform>();
        enemyRect.anchorMin = new Vector2(0.5f, 0f);
        enemyRect.anchorMax = new Vector2(0.5f, 0f);
        enemyRect.pivot = new Vector2(0.5f, 0f);
        enemyRect.anchoredPosition = new Vector2(0f, 66f);
        Text enemyLabel = AddText(enemyPanel.transform, "Label", "HOSTILE ENTITY", 13, new Color(0.96f, 0.35f, 0.23f, 1f), TextAnchor.MiddleLeft, FontStyle.Bold);
        SetRect(enemyLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -31f), new Vector2(176f, 24f));
        enemyText = AddText(enemyPanel.transform, "Value", "ENTITY // 500 / 500", 13, Cream, TextAnchor.MiddleRight, FontStyle.Bold);
        SetRect(enemyText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-226f, -31f), new Vector2(204f, 24f));
        enemyFill = AddProgressBar(enemyPanel.transform, "Entity Health Fill", new Vector2(22f, 17f), new Vector2(356f, 9f), new Color(0.80f, 0.08f, 0.04f, 1f));

        GameObject weaponPanel = AddPanel(screen.transform, "Weapon Status", new Vector2(-48f, 66f), new Vector2(318f, 110f));
        RectTransform weaponRect = weaponPanel.GetComponent<RectTransform>();
        weaponRect.anchorMin = new Vector2(1f, 0f);
        weaponRect.anchorMax = new Vector2(1f, 0f);
        weaponRect.pivot = new Vector2(1f, 0f);
        weaponRect.anchoredPosition = new Vector2(-48f, 66f);
        weaponNameText = AddText(weaponPanel.transform, "Weapon", "ASSAULT RIFLE", 13, WarmGold, TextAnchor.MiddleLeft, FontStyle.Bold);
        SetRect(weaponNameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -35f), new Vector2(210f, 24f));
        ammoText = AddText(weaponPanel.transform, "Ammo", "30 / 30", 32, Cream, TextAnchor.MiddleRight, FontStyle.Bold);
        SetRect(ammoText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-176f, 24f), new Vector2(154f, 48f));
        AddText(weaponPanel.transform, "Ammo Label", "AMMO", 11, WarmGoldMuted, TextAnchor.MiddleLeft)
            .rectTransform.SetParent(weaponPanel.transform, false);
        RectTransform ammoLabel = weaponPanel.transform.Find("Ammo Label").GetComponent<RectTransform>();
        SetRect(ammoLabel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(22f, 27f), new Vector2(70f, 20f));

        CreateReticle(screen.transform);
        return screen;
    }

    private static GameObject CreateScreen(Transform parent, string name)
    {
        GameObject screen = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(screen, $"Create {name}");
        screen.transform.SetParent(parent, false);
        SetStretch(screen.GetComponent<RectTransform>());
        return screen;
    }

    private static void ConfigureCanvas(Canvas canvas, CanvasScaler scaler)
    {
        Undo.RecordObject(canvas, "Configure Level 0 UI canvas");
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;
        EditorUtility.SetDirty(canvas);

        Undo.RecordObject(scaler, "Configure Level 0 UI scaler");
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        EditorUtility.SetDirty(scaler);
    }

    private static void EnsureEventSystem(Scene scene)
    {
        EventSystem[] systems = Resources.FindObjectsOfTypeAll<EventSystem>();
        foreach (EventSystem system in systems)
        {
            if (system != null && system.gameObject.scene == scene)
            {
                return;
            }
        }

        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");
        SceneManager.MoveGameObjectToScene(eventSystem, scene);
    }

    private static Image AddFullImage(Transform parent, string name, Color color)
    {
        Image image = AddImage(parent, name, color);
        SetStretch(image.rectTransform);
        return image;
    }

    private static void AddLeftVeil(Transform parent, string name, float alpha)
    {
        Image veil = AddImage(parent, name, new Color(0f, 0f, 0f, alpha));
        SetRect(veil.rectTransform, new Vector2(0f, 0f), new Vector2(0.62f, 1f), Vector2.zero, Vector2.zero);
        Image rim = AddImage(parent, name + " Rim", new Color(WarmGold.r, WarmGold.g, WarmGold.b, 0.18f));
        SetRect(rim.rectTransform, new Vector2(0.62f, 0f), new Vector2(0.62f, 1f), new Vector2(-1f, 0f), new Vector2(1f, 0f));
    }

    private static GameObject AddPanel(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
    {
        Image panelImage = AddImage(parent, name, new Color(0.012f, 0.010f, 0.005f, 0.71f));
        RectTransform rect = panelImage.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        Outline outline = panelImage.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(WarmGoldMuted.r, WarmGoldMuted.g, WarmGoldMuted.b, 0.56f);
        outline.effectDistance = new Vector2(1f, -1f);
        return panelImage.gameObject;
    }

    private static Image AddProgressBar(Transform parent, string name, Vector2 position, Vector2 size, Color fillColor)
    {
        Image back = AddImage(parent, name + " Track", new Color(0f, 0f, 0f, 0.86f));
        SetRect(back.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), position, size);
        Outline outline = back.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(WarmGoldMuted.r, WarmGoldMuted.g, WarmGoldMuted.b, 0.68f);
        outline.effectDistance = new Vector2(1f, -1f);

        Image fill = AddImage(back.transform, name, fillColor);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.fillAmount = 1f;
        SetStretch(fill.rectTransform, 2f);
        return fill;
    }

    private static void CreateReticle(Transform parent)
    {
        Image center = AddImage(parent, "Reticle Center", new Color(Cream.r, Cream.g, Cream.b, 0.92f));
        SetRect(center.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 4f));
        AddReticleLine(parent, "Reticle Top", new Vector2(0f, 13f), new Vector2(2f, 11f));
        AddReticleLine(parent, "Reticle Bottom", new Vector2(0f, -13f), new Vector2(2f, 11f));
        AddReticleLine(parent, "Reticle Left", new Vector2(-13f, 0f), new Vector2(11f, 2f));
        AddReticleLine(parent, "Reticle Right", new Vector2(13f, 0f), new Vector2(11f, 2f));
    }

    private static void AddReticleLine(Transform parent, string name, Vector2 position, Vector2 size)
    {
        Image line = AddImage(parent, name, new Color(WarmGold.r, WarmGold.g, WarmGold.b, 0.90f));
        SetRect(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
    }

    private static Button AddGoldButton(Transform parent, string label, Vector2 anchoredPosition, bool primary)
    {
        Image image = AddImage(parent, label + " Button", primary
            ? new Color(0.40f, 0.26f, 0.025f, 0.78f)
            : new Color(0.025f, 0.019f, 0.006f, 0.48f));
        SetRect(image.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), anchoredPosition, new Vector2(344f, 64f));
        Outline outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = primary ? WarmGold : new Color(WarmGold.r, WarmGold.g, WarmGold.b, 0.72f);
        outline.effectDistance = new Vector2(1f, -1f);

        Button button = image.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.20f, 1.12f, 0.76f, 1f);
        colors.pressedColor = new Color(0.78f, 0.67f, 0.26f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = 0.10f;
        button.colors = colors;

        Text text = AddText(image.transform, "Label", label, 16, WarmGold, TextAnchor.MiddleCenter, FontStyle.Bold);
        SetStretch(text.rectTransform);
        return button;
    }

    private static Image AddImage(Transform parent, string name, Color color)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Undo.RegisterCreatedObjectUndo(gameObject, $"Create {name}");
        gameObject.transform.SetParent(parent, false);
        Image image = gameObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Text AddText(Transform parent, string name, string content, int fontSize, Color color, TextAnchor alignment,
        FontStyle style = FontStyle.Normal)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        Undo.RegisterCreatedObjectUndo(gameObject, $"Create {name}");
        gameObject.transform.SetParent(parent, false);
        Text text = gameObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        Shadow shadow = gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.84f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    private static void AddLine(Transform parent, string name, Vector2 topLeftPosition, Vector2 size, Color color)
    {
        Image line = AddImage(parent, name, color);
        SetRect(line.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), topLeftPosition, size);
    }

    private static void SetStretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void SetStretchTop(RectTransform rect, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(0f, -height);
        rect.offsetMax = Vector2.zero;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0f, 1f);
        if (anchorMin == anchorMax && anchorMin.x == 1f && anchorMin.y == 1f)
        {
            rect.pivot = new Vector2(1f, 1f);
        }
        else if (anchorMin == anchorMax && anchorMin.x == 1f && anchorMin.y == 0f)
        {
            rect.pivot = new Vector2(1f, 0f);
        }
        else if (anchorMin == anchorMax && anchorMin == new Vector2(0.5f, 0.5f))
        {
            rect.pivot = new Vector2(0.5f, 0.5f);
        }
        else if (anchorMin == anchorMax && anchorMin.y == 0f)
        {
            rect.pivot = new Vector2(0f, 0f);
        }
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private static void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
        }
    }

    private static void AddSceneToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool found = false;
        for (int i = 0; i < scenes.Count; i++)
        {
            if (scenes[i].path == ScenePath)
            {
                scenes[i] = new EditorBuildSettingsScene(ScenePath, true);
                found = true;
                break;
            }
        }

        if (!found)
        {
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static Scene OpenTargetScene()
    {
        if (!System.IO.File.Exists(ScenePath))
        {
            Debug.LogError($"[Level0UISetup] Scene not found: '{ScenePath}'.");
            return default;
        }

        if (IsTargetScene(SceneManager.GetActiveScene()))
        {
            return SceneManager.GetActiveScene();
        }

        return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    private static bool IsTargetScene(Scene scene)
    {
        return scene.IsValid() && scene.isLoaded && scene.path == ScenePath;
    }

    private static EnemyController FindEnemy(Scene scene)
    {
        EnemyController[] all = Resources.FindObjectsOfTypeAll<EnemyController>();
        EnemyController fallback = null;
        foreach (EnemyController candidate in all)
        {
            if (candidate == null || candidate.gameObject.scene != scene)
            {
                continue;
            }
            if (candidate.name == EnemyName)
            {
                return candidate;
            }
            fallback = fallback ?? candidate;
        }

        if (fallback == null)
        {
            Debug.LogError($"[Level0UISetup] Could not find '{EnemyName}' EnemyController in '{scene.path}'.");
        }
        return fallback;
    }

    private static T FindSingleComponent<T>(Scene scene, string typeName) where T : Component
    {
        T[] all = Resources.FindObjectsOfTypeAll<T>();
        T result = null;
        int count = 0;
        foreach (T candidate in all)
        {
            if (candidate == null || candidate.gameObject.scene != scene)
            {
                continue;
            }
            result = candidate;
            count++;
        }

        if (count != 1)
        {
            Debug.LogError($"[Level0UISetup] Expected exactly one {typeName} in '{scene.path}', found {count}.");
            return null;
        }
        return result;
    }

    private static GameObject FindObject(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == objectName)
                {
                    return candidate.gameObject;
                }
            }
        }
        return null;
    }

    private static Transform FindDescendantByName(Transform root, string objectName)
    {
        if (root == null)
        {
            return null;
        }
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == objectName)
            {
                return candidate;
            }
        }
        return null;
    }

    private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(gameObject);
    }
}
#endif

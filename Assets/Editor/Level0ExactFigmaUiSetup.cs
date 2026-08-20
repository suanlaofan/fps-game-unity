#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Installs the six complete Level 0 Figma exports as the actual Unity page
/// visuals. The three page pairs are switched only by their two transparent
/// buttons, preserving the supplied Figma pixels and their focus states.
/// </summary>
public static class Level0ExactFigmaUiSetup
{
    private const string ScenePath = "Assets/Scenes/jogo.unity";
    private const string RootName = "Level 0 UI";
    private const string ScreenImageName = "Exact Figma Background";

    // The Figma archive's unsuffixed start export has START GAME highlighted;
    // its -1 export has EXIT highlighted. The normalized asset filenames were
    // preserved from the first import, so map them by verified visual state.
    private const string StartFirstPath = "Assets/FigmaLevel0/Figma_Level0_Start_Exit.png";
    private const string StartSecondPath = "Assets/FigmaLevel0/Figma_Level0_Start_Play.png";
    private const string VictoryFirstPath = "Assets/FigmaLevel0/Figma_Level0_Victory_Continue.png";
    private const string VictorySecondPath = "Assets/FigmaLevel0/Figma_Level0_Victory_Menu.png";
    private const string GameOverFirstPath = "Assets/FigmaLevel0/Figma_Level0_GameOver_Continue.png";
    private const string GameOverSecondPath = "Assets/FigmaLevel0/Figma_Level0_GameOver_Menu.png";

    // Positions measured directly from the exported 1920 x 1080 Figma images.
    // They intentionally cover only the two Figma buttons, not decorative text.
    private static readonly Rect StartFirstHitArea = new Rect(134f, 539f, 544f, 102f);
    private static readonly Rect StartSecondHitArea = new Rect(134f, 690f, 544f, 102f);
    private static readonly Rect ResultFirstHitArea = new Rect(237f, 539f, 539f, 102f);
    private static readonly Rect ResultSecondHitArea = new Rect(238f, 690f, 538f, 102f);

    [MenuItem("Tools/FPS Game/Apply Exact Figma Level 0 UI")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[ExactFigmaUI] Exit Play Mode before applying the UI.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!IsTargetScene(scene))
        {
            Debug.LogError($"[ExactFigmaUI] Open '{ScenePath}' before applying.");
            return;
        }

        ApplyToScene(scene);
    }

    /// <summary>Batch-mode entry point for repeatable validation.</summary>
    public static void ApplyFromBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[ExactFigmaUI] Cannot run in Play Mode.");
            return;
        }

        Scene scene = IsTargetScene(SceneManager.GetActiveScene())
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ApplyToScene(scene);
    }

    [MenuItem("Tools/FPS Game/Validate Exact Figma Level 0 UI")]
    public static void Validate()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject root = FindRoot(scene, RootName);
        Level0GameFlow flow = root != null ? root.GetComponent<Level0GameFlow>() : null;
        bool valid = flow != null && ValidateScreen(flow.menuScreen, flow.startGameButton, flow.exitButton,
                         StartFirstPath, StartSecondPath) &&
                     ValidateScreen(flow.victoryScreen, flow.victoryContinueButton, flow.victoryMainMenuButton,
                         VictoryFirstPath, VictorySecondPath) &&
                     ValidateScreen(flow.gameOverScreen, flow.gameOverContinueButton, flow.gameOverMainMenuButton,
                         GameOverFirstPath, GameOverSecondPath);

        if (valid)
        {
            Debug.Log("[ExactFigmaUI] SUCCESS: all six full 1920x1080 Figma page exports are assigned to their matching two-button screens.");
        }
        else
        {
            Debug.LogError("[ExactFigmaUI] FAILED: expected exact Figma backgrounds, controllers, sprites, and transparent hit regions were not all found.");
        }
    }

    private static void ApplyToScene(Scene scene)
    {
        if (!IsTargetScene(scene))
        {
            Debug.LogError($"[ExactFigmaUI] Scene not found: '{ScenePath}'.");
            return;
        }

        GameObject root = FindRoot(scene, RootName);
        Level0GameFlow flow = root != null ? root.GetComponent<Level0GameFlow>() : null;
        if (flow == null || flow.menuScreen == null || flow.victoryScreen == null || flow.gameOverScreen == null)
        {
            Debug.LogError("[ExactFigmaUI] Existing Level 0 UI flow and all three result/menu screen references are required.");
            return;
        }

        Sprite startFirst = LoadSprite(StartFirstPath);
        Sprite startSecond = LoadSprite(StartSecondPath);
        Sprite victoryFirst = LoadSprite(VictoryFirstPath);
        Sprite victorySecond = LoadSprite(VictorySecondPath);
        Sprite gameOverFirst = LoadSprite(GameOverFirstPath);
        Sprite gameOverSecond = LoadSprite(GameOverSecondPath);
        if (startFirst == null || startSecond == null || victoryFirst == null || victorySecond == null || gameOverFirst == null || gameOverSecond == null)
        {
            return;
        }

        ApplyScreen(flow.menuScreen, flow.startGameButton, flow.exitButton, startFirst, startSecond, StartFirstHitArea, StartSecondHitArea);
        ApplyScreen(flow.victoryScreen, flow.victoryContinueButton, flow.victoryMainMenuButton, victoryFirst, victorySecond, ResultFirstHitArea, ResultSecondHitArea);
        ApplyScreen(flow.gameOverScreen, flow.gameOverContinueButton, flow.gameOverMainMenuButton, gameOverFirst, gameOverSecond, ResultFirstHitArea, ResultSecondHitArea);

        // Gameplay has no dedicated Figma frame among the supplied six pages.
        // Keep the pre-existing live HUD exactly as-is instead of inventing it.
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene))
        {
            Debug.LogError($"[ExactFigmaUI] Could not save '{ScenePath}'.");
            return;
        }

        Selection.activeGameObject = root;
        Debug.Log("[ExactFigmaUI] SUCCESS: installed all six Figma images as full-page backgrounds with only their corresponding transparent button hit regions in Unity.");
    }

    private static void ApplyScreen(GameObject screen, Button firstButton, Button secondButton, Sprite firstPage, Sprite secondPage,
        Rect firstHitArea, Rect secondHitArea)
    {
        if (screen == null || firstButton == null || secondButton == null)
        {
            Debug.LogError("[ExactFigmaUI] A screen is missing one of its two required buttons.");
            return;
        }

        Image pageImage = GetOrCreateBackground(screen.transform);
        pageImage.sprite = firstPage;
        pageImage.color = Color.white;
        pageImage.raycastTarget = false;
        pageImage.preserveAspect = false;

        FigmaTwoButtonScreen controller = screen.GetComponent<FigmaTwoButtonScreen>();
        if (controller == null)
        {
            controller = Undo.AddComponent<FigmaTwoButtonScreen>(screen);
        }
        controller.pageImage = pageImage;
        controller.firstButtonPage = firstPage;
        controller.secondButtonPage = secondPage;
        controller.firstButton = firstButton;
        controller.secondButton = secondButton;
        EditorUtility.SetDirty(controller);

        // The source screen is now an exact flat Figma page; hide all legacy
        // hand-authored display objects without destroying their flow bindings.
        foreach (Transform child in screen.transform)
        {
            if (child == pageImage.transform || child == firstButton.transform || child == secondButton.transform)
            {
                continue;
            }
            child.gameObject.SetActive(false);
        }

        ConfigureHitButton(firstButton, firstHitArea, controller, true);
        ConfigureHitButton(secondButton, secondHitArea, controller, false);
        pageImage.transform.SetAsFirstSibling();
        firstButton.transform.SetAsLastSibling();
        secondButton.transform.SetAsLastSibling();
    }

    private static void ConfigureHitButton(Button button, Rect area, FigmaTwoButtonScreen controller, bool first)
    {
        button.gameObject.SetActive(true);
        button.transition = Selectable.Transition.None;
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(area.x, -area.y);
        rect.sizeDelta = new Vector2(area.width, area.height);

        Image image = button.targetGraphic as Image;
        if (image != null)
        {
            image.sprite = null;
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = true;
        }

        foreach (Graphic graphic in button.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic != image)
            {
                graphic.raycastTarget = false;
                graphic.color = new Color(graphic.color.r, graphic.color.g, graphic.color.b, 0f);
            }
        }

        foreach (Behaviour behaviour in button.GetComponents<Behaviour>())
        {
            if (behaviour is Outline || behaviour is Shadow)
            {
                behaviour.enabled = false;
            }
        }

        FigmaScreenPointerEvents events = button.GetComponent<FigmaScreenPointerEvents>();
        if (events == null)
        {
            events = Undo.AddComponent<FigmaScreenPointerEvents>(button.gameObject);
        }
        events.owner = controller;
        events.showFirstPage = first;
        EditorUtility.SetDirty(button);
        EditorUtility.SetDirty(events);
    }

    private static Image GetOrCreateBackground(Transform screen)
    {
        Transform found = screen.Find(ScreenImageName);
        if (found != null)
        {
            Image existing = found.GetComponent<Image>();
            if (existing != null)
            {
                found.gameObject.SetActive(true);
                SetStretch(found.GetComponent<RectTransform>());
                return existing;
            }
        }

        GameObject background = new GameObject(ScreenImageName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Undo.RegisterCreatedObjectUndo(background, "Create exact Figma background");
        background.transform.SetParent(screen, false);
        Image image = background.GetComponent<Image>();
        image.raycastTarget = false;
        SetStretch(background.GetComponent<RectTransform>());
        return image;
    }

    private static bool ValidateScreen(GameObject screen, Button first, Button second, string firstPath, string secondPath)
    {
        if (screen == null || first == null || second == null)
        {
            return false;
        }

        FigmaTwoButtonScreen controller = screen.GetComponent<FigmaTwoButtonScreen>();
        Image image = controller != null ? controller.pageImage : null;
        return controller != null && image != null && image.raycastTarget == false &&
               controller.firstButton == first && controller.secondButton == second &&
               controller.firstButtonPage == LoadSprite(firstPath) && controller.secondButtonPage == LoadSprite(secondPath) &&
               IsTransparentInteractiveButton(first) && IsTransparentInteractiveButton(second);
    }

    private static bool IsTransparentInteractiveButton(Button button)
    {
        Image image = button != null ? button.targetGraphic as Image : null;
        return button != null && button.transition == Selectable.Transition.None && image != null &&
               image.raycastTarget && image.color.a <= 0.001f && button.GetComponent<FigmaScreenPointerEvents>() != null;
    }

    private static Sprite LoadSprite(string assetPath)
    {
        if (!File.Exists(assetPath))
        {
            Debug.LogError($"[ExactFigmaUI] Missing Figma export: '{assetPath}'.");
            return null;
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sprite == null)
        {
            Debug.LogError($"[ExactFigmaUI] '{assetPath}' must be imported as Sprite (2D and UI).");
        }
        return sprite;
    }

    private static GameObject FindRoot(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == objectName)
            {
                return root;
            }
        }
        return null;
    }

    private static bool IsTargetScene(Scene scene)
    {
        return scene.IsValid() && scene.isLoaded && scene.path == ScenePath;
    }

    private static void SetStretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }
}
#endif

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ShitiEnemyReplacementSetup
{
    private const string ModelPath = "Assets/shiti/tripo_convert_811de8ba-2a26-4b21-ac9a-8bc8f32bfab7.fbx";
    private const string TargetName = "diren";
    private const string VisualRootName = "ShitiEnemyModel";
    private const string ControllerPath = "Assets/shiti/ShitiEnemy.controller";
    private const string MotionParameter = "MotionState";
    private const float VisualYaw = 90f;

    private const string InspectMenuPath = "Tools/FPS Game/Inspect Shiti Enemy Asset";
    private const string ReplaceMenuPath = "Tools/FPS Game/Replace Diren With Shiti";

    [MenuItem(InspectMenuPath)]
    public static void InspectAsset()
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
        if (assets == null || assets.Length == 0)
        {
            Debug.LogError($"[ShitiInspect] No imported assets found at '{ModelPath}'.");
            return;
        }

        foreach (UnityEngine.Object asset in assets)
        {
            if (asset is AnimationClip clip)
            {
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                string samplePath = bindings.Length > 0 ? bindings[0].path : "<none>";
                Debug.Log($"[ShitiInspect] AnimationClip name='{clip.name}', length={clip.length:F2}s, frames={clip.frameRate * clip.length:F0}, human={clip.humanMotion}, loop={clip.isLooping}, bindings={bindings.Length}, samplePath='{samplePath}'.");
            }
            else if (asset is Avatar avatar)
            {
                Debug.Log($"[ShitiInspect] Avatar name='{avatar.name}', valid={avatar.isValid}, human={avatar.isHuman}.");
            }
            else if (asset is GameObject gameObject)
            {
                Debug.Log($"[ShitiInspect] GameObject name='{gameObject.name}', children={gameObject.GetComponentsInChildren<Transform>(true).Length}, renderers={gameObject.GetComponentsInChildren<Renderer>(true).Length}, animators={gameObject.GetComponentsInChildren<Animator>(true).Length}.");
            }
            else
            {
                Debug.Log($"[ShitiInspect] {asset.GetType().Name} name='{asset.name}'.");
            }
        }

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"[ShitiInspect] The FBX root GameObject could not be loaded from '{ModelPath}'.");
            return;
        }

        GameObject preview = UnityEngine.Object.Instantiate(model);
        preview.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            Bounds bounds = CalculateBounds(preview);
            Debug.Log($"[ShitiInspect] Preview bounds size={bounds.size}, center={bounds.center}.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(preview);
        }
    }

    [MenuItem(ReplaceMenuPath)]
    public static void ReplaceEnemyVisual()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[ShitiReplace] No loaded scene is active.");
            return;
        }

        GameObject target = FindSingleObject(scene, TargetName);
        if (target == null)
        {
            return;
        }

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"[ShitiReplace] The model could not be loaded from '{ModelPath}'.");
            return;
        }

        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[ShitiReplace] No ModelImporter found at '{ModelPath}'.");
            return;
        }

        ConfigureLoopingClips(importer);
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        Dictionary<string, AnimationClip> clips = LoadPrimaryClips();
        string[] requiredClips = { "look_around", "walk", "run", "box_02" };
        foreach (string required in requiredClips)
        {
            if (!clips.ContainsKey(required))
            {
                Debug.LogError($"[ShitiReplace] Required animation clip 'preset:biped:{required}' was not imported.");
                return;
            }
        }

        AnimatorController controller = BuildController(clips);

        Transform existingVisual = target.transform.Find(VisualRootName);
        if (existingVisual != null)
        {
            Undo.DestroyObjectImmediate(existingVisual.gameObject);
        }

        foreach (Transform child in GetDirectChildren(target.transform))
        {
            Undo.RecordObject(child.gameObject, "Hide original diren model");
            child.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
        }

        Animator originalAnimator = target.GetComponent<Animator>();
        if (originalAnimator != null)
        {
            Undo.RecordObject(originalAnimator, "Disable original diren animator");
            originalAnimator.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(originalAnimator);
        }

        GameObject visual = PrefabUtility.InstantiatePrefab(model, scene) as GameObject;
        if (visual == null)
        {
            visual = UnityEngine.Object.Instantiate(model);
            SceneManager.MoveGameObjectToScene(visual, scene);
        }

        Undo.RegisterCreatedObjectUndo(visual, "Add Shiti enemy model");
        visual.name = VisualRootName;
        visual.transform.SetParent(target.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.Euler(0f, VisualYaw, 0f);
        visual.transform.localScale = Vector3.one;

        Animator visualAnimator = visual.GetComponent<Animator>();
        if (visualAnimator == null)
        {
            visualAnimator = Undo.AddComponent<Animator>(visual);
        }

        visualAnimator.runtimeAnimatorController = controller;
        visualAnimator.applyRootMotion = false;
        visualAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        EditorUtility.SetDirty(visualAnimator);

        AlignVisualToCollider(target, visual);

        EnemyController enemyController = target.GetComponent<EnemyController>();
        if (enemyController != null)
        {
            Undo.RecordObject(enemyController, "Bind Shiti animator to enemy controller");
            enemyController.animator = visualAnimator;
            PrefabUtility.RecordPrefabInstancePropertyModifications(enemyController);
            EditorUtility.SetDirty(enemyController);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene))
        {
            Debug.LogError($"[ShitiReplace] Failed to save scene '{scene.path}'.");
            return;
        }

        Selection.activeGameObject = target;
        EditorGUIUtility.PingObject(target);
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log($"[ShitiReplace] SUCCESS: Replaced the visible model on '{TargetName}' with '{ModelPath}', created '{ControllerPath}' from {clips.Count} Generic clips, bound state-driven animations, and saved '{scene.path}'.");
    }

    private static void ConfigureLoopingClips(ModelImporter importer)
    {
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }

        bool changed = false;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            string shortName = GetShortClipName(clip.name);
            bool shouldLoop = shortName == "look_around" || shortName == "walk" || shortName == "run" || shortName == "box_02";
            if (clip.loopTime != shouldLoop)
            {
                clip.loopTime = shouldLoop;
                changed = true;
            }

            bool shouldLoopPose = shortName == "walk" || shortName == "run";
            if (clip.loopPose != shouldLoopPose)
            {
                clip.loopPose = shouldLoopPose;
                changed = true;
            }
        }

        if (changed)
        {
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
    }

    private static Dictionary<string, AnimationClip> LoadPrimaryClips()
    {
        var result = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            AnimationClip clip = asset as AnimationClip;
            if (clip == null || !clip.name.StartsWith("preset:biped:", StringComparison.Ordinal))
            {
                continue;
            }

            string shortName = GetShortClipName(clip.name);
            if (!result.ContainsKey(shortName))
            {
                result.Add(shortName, clip);
            }
        }

        return result;
    }

    private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        if (controller == null)
        {
            throw new InvalidOperationException($"Could not create AnimatorController at '{ControllerPath}'.");
        }

        controller.AddParameter(MotionParameter, AnimatorControllerParameterType.Int);
        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

        AnimatorState idle = stateMachine.AddState("Look Around");
        idle.motion = clips["look_around"];
        AnimatorState walk = stateMachine.AddState("Walk");
        walk.motion = clips["walk"];
        AnimatorState run = stateMachine.AddState("Run");
        run.motion = clips["run"];
        AnimatorState attack = stateMachine.AddState("Attack");
        attack.motion = clips["box_02"];

        AddMotionTransition(stateMachine, idle, 0);
        AddMotionTransition(stateMachine, walk, 1);
        AddMotionTransition(stateMachine, run, 2);
        AddMotionTransition(stateMachine, attack, 3);

        if (clips.TryGetValue("hit_to_body_01", out AnimationClip hitClip))
        {
            AnimatorState hit = stateMachine.AddState("Hit");
            hit.motion = hitClip;
            AddMotionTransition(stateMachine, hit, 4);
        }

        if (clips.TryGetValue("defeat_03", out AnimationClip defeatClip))
        {
            AnimatorState defeat = stateMachine.AddState("Defeat");
            defeat.motion = defeatClip;
            AddMotionTransition(stateMachine, defeat, 5);
        }

        stateMachine.defaultState = idle;
        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].defaultWeight = 1.0f;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static void AddMotionTransition(AnimatorStateMachine stateMachine, AnimatorState destination, int state)
    {
        AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(destination);
        transition.AddCondition(AnimatorConditionMode.Equals, state, MotionParameter);
        transition.hasExitTime = false;
        transition.duration = 0.12f;
        transition.canTransitionToSelf = false;
    }

    private static void AlignVisualToCollider(GameObject target, GameObject visual)
    {
        Bounds bounds = CalculateBounds(visual);
        if (bounds.size.y < 0.001f)
        {
            Debug.LogWarning("[ShitiReplace] The imported model has no measurable renderer bounds; leaving its scale unchanged.");
            return;
        }

        CapsuleCollider collider = target.GetComponent<CapsuleCollider>();
        float targetHeight = collider != null ? collider.height * Mathf.Abs(target.transform.lossyScale.y) : 2.0f;
        float scale = targetHeight / bounds.size.y;
        visual.transform.localScale = Vector3.one * scale;
        bounds = CalculateBounds(visual);

        Vector3 targetCenter = collider != null
            ? target.transform.TransformPoint(collider.center)
            : target.transform.position + Vector3.up * (targetHeight * 0.5f);
        visual.transform.position += targetCenter - bounds.center;
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position, Vector3.zero);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private static GameObject FindSingleObject(Scene scene, string objectName)
    {
        GameObject match = null;
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name != objectName)
                {
                    continue;
                }

                match = transform.gameObject;
                count++;
            }
        }

        if (count != 1)
        {
            Debug.LogError($"[ShitiReplace] Expected exactly one object named '{objectName}' in '{scene.path}', found {count}.");
            return null;
        }

        return match;
    }

    private static List<Transform> GetDirectChildren(Transform parent)
    {
        var children = new List<Transform>();
        for (int i = 0; i < parent.childCount; i++)
        {
            children.Add(parent.GetChild(i));
        }

        return children;
    }

    private static string GetShortClipName(string clipName)
    {
        int separator = clipName.LastIndexOf(':');
        return separator >= 0 ? clipName.Substring(separator + 1) : clipName;
    }
}

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scene preparation for the existing PICO bootstrap and gameplay bundle.</summary>
public static class Level0PerformanceSetup
{
    private const string GameplayPath = "Assets/Scenes/jogo.unity";
    private const string BootstrapPath = "Assets/Scenes/PicoFreshBootstrap.unity";

    /// <summary>
    /// Call after Level0VrSetup.Apply, while jogo is loaded. Preserves the authored
    /// lights and materials, but prevents the four fill lights from forcing extra
    /// per-pixel ForwardAdd passes on the environment.
    /// </summary>
    public static int ApplyLighting()
    {
        RequireEditMode();
        Scene gameplay = SceneManager.GetSceneByPath(GameplayPath);
        if (!gameplay.IsValid() || !gameplay.isLoaded)
            throw new InvalidOperationException("Load jogo before applying Level0 lighting.");

        Light[] fills = gameplay.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Light>(true))
            .Where(light => light.type == LightType.Spot &&
                light.name.StartsWith("Fill ", StringComparison.Ordinal)).ToArray();
        if (fills.Length != 4)
            throw new InvalidOperationException("Expected four authored Level0 fill lights; found " + fills.Length + ".");

        foreach (Light fill in fills)
        {
            fill.renderMode = LightRenderMode.ForceVertex;
            EditorUtility.SetDirty(fill);
        }
        EditorSceneManager.MarkSceneDirty(gameplay);
        if (!EditorSceneManager.SaveScene(gameplay))
            throw new InvalidOperationException("Could not save Level0 fill light settings.");
        if (fills.Any(fill => fill.renderMode != LightRenderMode.ForceVertex))
            throw new InvalidOperationException("Level0 fill light validation failed.");

        Directory.CreateDirectory("Evidence");
        string report = "LEVEL0_LIGHTING_PASS fill_lights=" + fills.Length +
            " render_mode=ForceVertex authored_color_intensity_transform=preserved\n";
        File.WriteAllText("Evidence/lighting-setup.txt", report);
        Debug.Log(report);
        return fills.Length;
    }

    /// <summary>
    /// Call after CreateBootstrapScene and before building either the scene bundle
    /// or APK. Both runtime scenes must reference the same baked occlusion asset.
    /// Leaves both scenes open and jogo active; does not recreate either scene.
    /// </summary>
    public static void BakeSharedOcclusion()
    {
        RequireEditMode();
        if (!File.Exists(BootstrapPath) || !File.Exists(GameplayPath))
            throw new InvalidOperationException("Create bootstrap and gameplay scenes before the shared bake.");

        // Preserve pending changes in these two generated scenes. Do not discard
        // unsaved work in another open scene when opening the bake scene set.
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene current = SceneManager.GetSceneAt(index);
            if (!current.isLoaded || !current.isDirty) continue;
            if (current.path != BootstrapPath && current.path != GameplayPath)
                throw new InvalidOperationException("Save unrelated scene before the shared bake: " + current.path);
            if (!EditorSceneManager.SaveScene(current))
                throw new InvalidOperationException("Could not save scene before the shared bake: " + current.path);
        }

        Scene bootstrap = EditorSceneManager.OpenScene(BootstrapPath, OpenSceneMode.Single);
        Scene gameplay = EditorSceneManager.OpenScene(GameplayPath, OpenSceneMode.Additive);
        if (SceneManager.GetActiveScene() != bootstrap && !SceneManager.SetActiveScene(bootstrap))
            throw new InvalidOperationException("Could not make bootstrap the first occlusion scene.");

        Renderer[] expectedRenderers = gameplay.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy &&
                (GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.OccludeeStatic) != 0)
            .ToArray();
        if (expectedRenderers.Length == 0)
            throw new InvalidOperationException("Gameplay has no active static occlusion targets.");

        // Compute finishes the optimizer, but Unity 6 can publish the imported
        // native data and update umbraDataSize on a subsequent editor update.
        // Validate the saved PVS and scene/renderer mappings, rather than using
        // that briefly stale editor cache as the sole success condition.
        if (!StaticOcclusionCulling.Compute())
            throw new InvalidOperationException("Unity rejected or failed the shared occlusion bake.");
        if (StaticOcclusionCulling.isRunning)
            throw new InvalidOperationException("Shared occlusion bake has not completed.");

        string generatedPath = Path.ChangeExtension(BootstrapPath, null) + "/OcclusionCullingData.asset";
        if (!File.Exists(generatedPath))
            throw new InvalidOperationException("Shared bake did not write its native data asset: " + generatedPath);
        AssetDatabase.ImportAsset(generatedPath,
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        EditorSceneManager.MarkSceneDirty(bootstrap);
        EditorSceneManager.MarkSceneDirty(gameplay);
        if (!EditorSceneManager.SaveScene(bootstrap) || !EditorSceneManager.SaveScene(gameplay))
            throw new InvalidOperationException("Could not save both shared-occlusion scenes.");
        AssetDatabase.SaveAssets();

        string bootstrapGuid = ReadOcclusionGuid(BootstrapPath);
        string gameplayGuid = ReadOcclusionGuid(GameplayPath);
        if (bootstrapGuid != gameplayGuid)
            throw new InvalidOperationException("Bootstrap and gameplay do not reference the same occlusion data.");
        string assetPath = AssetDatabase.GUIDToAssetPath(bootstrapGuid);
        UnityEngine.Object dataAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (!dataAsset || assetPath != generatedPath ||
            !File.Exists(assetPath) || new FileInfo(assetPath).Length == 0)
            throw new InvalidOperationException("Shared occlusion reference has no saved data asset: " + assetPath);

        // OcclusionCullingData is a native-only class in this Unity version;
        // AssetDatabase returns UnityEngine.Object, so GetType().Name is not a
        // valid type check. Check the actual class ID, payload, and target IDs.
        string dataText = File.ReadAllText(assetPath);
        Match pvs = Regex.Match(dataText, @"(?m)^  m_PVSData: ([a-fA-F0-9]+)\s*$");
        if (!Regex.IsMatch(dataText, @"(?m)^--- !u!363 ") || !pvs.Success ||
            pvs.Groups[1].Length < 32 || pvs.Groups[1].Length % 2 != 0)
            throw new InvalidOperationException("Shared bake asset has no valid native PVS payload.");
        int dataBytes = pvs.Groups[1].Length / 2;
        string[] mappedScenes = Regex.Matches(dataText, @"(?m)^    scene: ([a-fA-F0-9]{32})\s*$")
            .Cast<Match>().Select(match => match.Groups[1].Value.ToLowerInvariant()).ToArray();
        string[] expectedScenes = { ReadSceneGuid(BootstrapPath), ReadSceneGuid(GameplayPath) };
        if (mappedScenes.Length != 2 || expectedScenes.Any(guid => !mappedScenes.Contains(guid)))
            throw new InvalidOperationException("Shared PVS does not map both saved scenes.");
        ulong[] mappedRenderers = Regex.Matches(dataText, @"(?m)^  - targetObject: (\d+)\s*$")
            .Cast<Match>().Select(match => ulong.Parse(match.Groups[1].Value)).OrderBy(id => id).ToArray();
        ulong[] expectedIds = expectedRenderers.Select(renderer =>
            GlobalObjectId.GetGlobalObjectIdSlow(renderer).targetObjectId).OrderBy(id => id).ToArray();
        if (!mappedRenderers.SequenceEqual(expectedIds))
            throw new InvalidOperationException("Shared PVS renderer IDs do not match the current static geometry: baked=" +
                mappedRenderers.Length + " current=" + expectedIds.Length);

        if (SceneManager.GetActiveScene() != gameplay && !SceneManager.SetActiveScene(gameplay))
            throw new InvalidOperationException("Could not restore jogo as the active scene after the shared bake.");
        Directory.CreateDirectory("Evidence");
        string report = "LEVEL0_SHARED_OCCLUSION_PASS bytes=" + dataBytes +
            " guid=" + gameplayGuid + " asset=" + assetPath +
            " bootstrap_ref=matched gameplay_ref=matched scene_mappings=" + mappedScenes.Length +
            " renderer_ids_matched=" + mappedRenderers.Length +
            " editor_cached_umbra_bytes=" + StaticOcclusionCulling.umbraDataSize + " active_scene=jogo\n";
        File.WriteAllText("Evidence/shared-occlusion-setup.txt", report);
        Debug.Log(report);
    }

    private static string ReadOcclusionGuid(string scenePath)
    {
        string sceneText = File.ReadAllText(scenePath);
        Match match = Regex.Match(sceneText,
            @"m_OcclusionCullingData:\s*\{[^}]*\bguid:\s*([a-fA-F0-9]{32})");
        if (!match.Success || match.Groups[1].Value == new string('0', 32))
            throw new InvalidOperationException("Scene has no serialized occlusion data GUID: " + scenePath);
        return match.Groups[1].Value.ToLowerInvariant();
    }

    private static string ReadSceneGuid(string scenePath)
    {
        Match match = Regex.Match(File.ReadAllText(scenePath), @"(?m)^  m_SceneGUID: ([a-fA-F0-9]{32})\s*$");
        if (!match.Success || match.Groups[1].Value == new string('0', 32))
            throw new InvalidOperationException("Scene has no baked scene GUID: " + scenePath);
        return match.Groups[1].Value.ToLowerInvariant();
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before Level0 performance setup.");
        if (StaticOcclusionCulling.isRunning)
            throw new InvalidOperationException("Wait for the current occlusion bake to finish.");
    }
}
#endif

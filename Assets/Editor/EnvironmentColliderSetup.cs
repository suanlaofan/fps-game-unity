using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EnvironmentColliderSetup
{
    private const string MenuPath = "Tools/FPS Game/Make Jogo Environment Solid";

    private static readonly string[] EnvironmentModelPaths =
    {
        "Assets/fpsgamemoxing/tripo_convert_0f843152-b27e-4a63-9772-ef424d69963c/tripo_convert_0f843152-b27e-4a63-9772-ef424d69963c.fbx",
        "Assets/fpsgamemoxing/tripo_convert_1ac10d12-aab2-40c7-8f70-2f44cc976a35/tripo_convert_1ac10d12-aab2-40c7-8f70-2f44cc976a35.fbx",
        "Assets/fpsgamemoxing/tripo_convert_31252e50-3c53-479c-9124-9539a80f4aa9/tripo_convert_31252e50-3c53-479c-9124-9539a80f4aa9.fbx",
        "Assets/fpsgamemoxing/tripo_convert_77338fa8-9fee-4a92-887d-5fb4ba761bfa/tripo_convert_77338fa8-9fee-4a92-887d-5fb4ba761bfa.fbx",
        "Assets/fpsgamemoxing/tripo_convert_8cfdc8ba-15cd-44a3-aacd-7c4061754d3e/tripo_convert_8cfdc8ba-15cd-44a3-aacd-7c4061754d3e.fbx",
        "Assets/fpsgamemoxing/tripo_convert_c01b0f2b-e269-482b-a153-0faeb754bfa2/tripo_convert_c01b0f2b-e269-482b-a153-0faeb754bfa2.fbx"
    };

    [MenuItem(MenuPath)]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || !scene.path.EndsWith("Assets/Scenes/jogo.unity", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError("[EnvironmentColliders] Open Assets/Scenes/jogo.unity before running this command.");
            return;
        }

        int changedImporters = 0;
        foreach (string assetPath in EnvironmentModelPaths)
        {
            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[EnvironmentColliders] Missing ModelImporter: {assetPath}");
                continue;
            }

            if (importer.addCollider)
            {
                continue;
            }

            importer.addCollider = true;
            importer.SaveAndReimport();
            changedImporters++;
        }

        int staticMeshColliders = 0;
        int dynamicMeshColliders = 0;
        foreach (MeshCollider collider in UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include))
        {
            if (collider == null || !collider.gameObject.scene.IsValid() || collider.gameObject.scene != scene)
            {
                continue;
            }

            if (collider.attachedRigidbody == null)
            {
                staticMeshColliders++;
            }
            else
            {
                dynamicMeshColliders++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            Debug.LogError($"[EnvironmentColliders] Failed to save '{scene.path}'.");
            return;
        }

        Debug.Log($"[EnvironmentColliders] SUCCESS: Enabled collision generation for {changedImporters} environment FBX importers. Scene now contains {staticMeshColliders} static MeshCollider(s), {dynamicMeshColliders} dynamic MeshCollider(s), and no Rigidbody was added to environment models.");
    }
}

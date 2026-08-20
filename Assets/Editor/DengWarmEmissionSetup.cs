using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DengWarmEmissionSetup
{
    private const string MenuPath = "Tools/FPS Game/Apply Warm Emission To Deng";
    private const string TargetName = "deng";
    private const string MaterialFolder = "Assets/Materials/DengWarmEmission";

    private static readonly Color WarmYellow = new Color(1.0f, 0.64f, 0.22f, 1.0f);
    private const float EmissionIntensity = 3.0f;

    [MenuItem(MenuPath)]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[DengWarmEmission] No loaded scene is active.");
            return;
        }

        List<GameObject> matches = FindObjectsNamed(scene, TargetName);
        if (matches.Count != 1)
        {
            Debug.LogError($"[DengWarmEmission] Expected exactly one object named '{TargetName}' in '{scene.path}', found {matches.Count}.");
            return;
        }

        GameObject target = matches[0];
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogError($"[DengWarmEmission] '{TargetName}' has no Renderer components.");
            return;
        }

        EnsureFolder(MaterialFolder);

        var materialCopies = new Dictionary<Material, Material>();
        int materialIndex = 0;

        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;

            for (int slot = 0; slot < materials.Length; slot++)
            {
                Material source = materials[slot];
                if (source == null)
                {
                    continue;
                }

                if (!materialCopies.TryGetValue(source, out Material emissive))
                {
                    string sourcePath = AssetDatabase.GetAssetPath(source);
                    if (sourcePath.StartsWith(MaterialFolder + "/", StringComparison.Ordinal))
                    {
                        emissive = source;
                    }
                    else
                    {
                        string assetPath = $"{MaterialFolder}/DengWarmEmission_{materialIndex:00}.mat";
                        emissive = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                        if (emissive == null)
                        {
                            emissive = new Material(source)
                            {
                                name = $"DengWarmEmission_{materialIndex:00}"
                            };
                            AssetDatabase.CreateAsset(emissive, assetPath);
                        }
                    }

                    ConfigureEmission(emissive);
                    EditorUtility.SetDirty(emissive);
                    materialCopies.Add(source, emissive);
                    materialIndex++;
                }

                if (materials[slot] != emissive)
                {
                    materials[slot] = emissive;
                    changed = true;
                }
            }

            if (changed)
            {
                Undo.RecordObject(renderer, "Apply warm emission to deng");
                renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
            }
        }

        Selection.activeGameObject = target;
        EditorGUIUtility.PingObject(target);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();

        if (!EditorSceneManager.SaveScene(scene))
        {
            Debug.LogError($"[DengWarmEmission] Failed to save scene '{scene.path}'.");
            return;
        }

        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log($"[DengWarmEmission] SUCCESS: Applied warm-yellow emission to '{TargetName}' using {materialCopies.Count} material asset(s) across {renderers.Length} renderer(s), then saved '{scene.path}'.");
    }

    private static void ConfigureEmission(Material material)
    {
        if (!material.HasProperty("_EmissionColor"))
        {
            Shader standard = Shader.Find("Standard");
            if (standard == null)
            {
                throw new InvalidOperationException("The Standard shader could not be found.");
            }

            material.shader = standard;
        }

        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", WarmYellow * EmissionIntensity);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
    }

    private static List<GameObject> FindObjectsNamed(Scene scene, string objectName)
    {
        var matches = new List<GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == objectName)
                {
                    matches.Add(transform.gameObject);
                }
            }
        }

        return matches;
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
}

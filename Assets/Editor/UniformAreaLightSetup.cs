using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class UniformAreaLightSetup
{
    private const string MenuPath = "Tools/FPS Game/Build Uniform Area Fill";
    private const string TargetName = "Area Light";
    private const string FillRootName = "Uniform Area Fill";

    private const float FillIntensity = 2.5f;
    private const float FillRange = 35.0f;
    private const float OuterAngle = 170.0f;
    private const float InnerAngle = 130.0f;

    [MenuItem(MenuPath)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[UniformAreaFill] No loaded scene is active.");
            return;
        }

        List<GameObject> matches = FindObjectsNamed(scene, TargetName);
        if (matches.Count != 1)
        {
            Debug.LogError($"[UniformAreaFill] Expected one object named '{TargetName}' in '{scene.path}', found {matches.Count}.");
            return;
        }

        GameObject target = matches[0];
        Light source = target.GetComponent<Light>();
        if (source == null)
        {
            Debug.LogError($"[UniformAreaFill] '{TargetName}' has no Light component.");
            return;
        }

        Undo.RecordObject(target.transform, "Orient uniform area fill");
        target.transform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);

        Vector2 areaSize = source.areaSize;
        if (areaSize.x < 1.0f || areaSize.y < 1.0f)
        {
            areaSize = new Vector2(81.21579f, 69.1099f);
        }

        Undo.RecordObject(source, "Disable center area light");
        source.type = LightType.Rectangle;
        source.lightmapBakeType = LightmapBakeType.Baked;
        source.areaSize = areaSize;
        source.shadows = LightShadows.None;
        source.enabled = false;

        Transform existingFill = target.transform.Find(FillRootName);
        if (existingFill != null)
        {
            Undo.DestroyObjectImmediate(existingFill.gameObject);
        }

        var fillRoot = new GameObject(FillRootName);
        Undo.RegisterCreatedObjectUndo(fillRoot, "Create uniform area fill");
        fillRoot.transform.SetParent(target.transform, false);

        float offsetX = areaSize.x * 0.25f;
        float offsetY = areaSize.y * 0.25f;
        Vector3[] positions =
        {
            new Vector3(-offsetX, -offsetY, 0.1f),
            new Vector3(offsetX, -offsetY, 0.1f),
            new Vector3(-offsetX, offsetY, 0.1f),
            new Vector3(offsetX, offsetY, 0.1f)
        };

        for (int i = 0; i < positions.Length; i++)
        {
            var fillObject = new GameObject($"Fill {i + 1:00}");
            Undo.RegisterCreatedObjectUndo(fillObject, "Create uniform fill light");
            fillObject.transform.SetParent(fillRoot.transform, false);
            fillObject.transform.localPosition = positions[i];
            fillObject.transform.localRotation = Quaternion.identity;

            Light fill = Undo.AddComponent<Light>(fillObject);
            fill.type = LightType.Spot;
            fill.lightmapBakeType = LightmapBakeType.Realtime;
            fill.color = source.color;
            fill.intensity = FillIntensity;
            fill.range = FillRange;
            fill.spotAngle = OuterAngle;
            fill.innerSpotAngle = InnerAngle;
            fill.shadows = LightShadows.None;
            fill.renderMode = LightRenderMode.ForcePixel;
            fill.cullingMask = source.cullingMask;
            fill.bounceIntensity = 0.0f;
        }

        Selection.activeGameObject = target;
        EditorGUIUtility.PingObject(target);
        EditorSceneManager.MarkSceneDirty(scene);

        if (!EditorSceneManager.SaveScene(scene))
        {
            Debug.LogError($"[UniformAreaFill] Failed to save scene '{scene.path}'.");
            return;
        }

        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log($"[UniformAreaFill] SUCCESS: Replaced the center hotspot with four overlapping warm fill lights across {areaSize.x:F1} x {areaSize.y:F1}, then saved '{scene.path}'.");
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
}

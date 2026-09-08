#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ByteDance.PICO.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

/// <summary>
/// Fresh, self-contained PICO XR setup and build entry for this copy.
/// Camera aspect is intentionally never assigned; PICO/XR owns it.
/// </summary>
public static class PicoFreshBuild
{
    private const string ScenePath = "Assets/Scenes/jogo.unity";
    private const string BootstrapScenePath = "Assets/Scenes/PicoFreshBootstrap.unity";
    private const string StreamingAssetsPath = "Assets/StreamingAssets";
    private const string BundleName = "fpsgame-jogo-fresh.bundle";
    private const string GeneratedPath = "Assets/PicoFresh/Generated";
    private const string PxrSettingsKey = "ByteDance.PICO.XR.Settings";
    private const string XrLoaderSettingsKey = "com.unity.xr.management.loader_settings";
    private const string PicoLoaderType = "ByteDance.PICO.XR.PXR_Loader";
    private const string PackageVersion = "0.13.1";
    private const string PackageId = "com.suanlaofan.fpsgame.picofresh";
    private const string OutputApk = "Builds/PICO/Level0-PICO-VR-v1.2.4.apk";

    [MenuItem("Tools/FPS Game/PICO Fresh/Configure And Build")] 
    public static void ConfigureAndBuild()
    {
        ConfigureProject();
        ConfigureJogoScene();
        Level0VrSetup.Apply();
        Level0PerformanceSetup.ApplyLighting();
        CreateBootstrapScene();
        Level0PerformanceSetup.BakeSharedOcclusion();
        BuildGameplayBundle();
        BuildApk();
    }

    [MenuItem("Tools/FPS Game/PICO Fresh/Configure Project")]
    public static void ConfigureProject()
    {
        EnsurePicoPackage();
        SwitchToAndroid();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
        PlayerSettings.bundleVersion = "1.2.4-vr-release";
        PlayerSettings.productName = "Level0";
        Level0LauncherIconSetup.Apply();
        PlayerSettings.Android.bundleVersionCode = 16;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        PlayerSettings.Android.useCustomKeystore = false;
        // The gameplay scene is loaded from an AssetBundle after bootstrap.
        // Keep Unity component types available for that runtime-loaded scene.
        PlayerSettings.stripEngineCode = false;
        PlayerSettings.enableFrameTimingStats = true;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
        EditorUserBuildSettings.buildAppBundle = false;
        SetSerializedInt(GetPlayerSettingsSerializedObject(), "activeInputHandler", 1);
        AddAndroidDefine("ENABLE_PICO_XR_SDK");
        ConfigureXrLoader();
        ConfigurePicoSettings();
        EnsurePicoPlatformAsset();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateProject();
        Debug.Log("[PICO-FRESH] CONFIGURE_SUCCESS sdk=" + PackageVersion +
                  " appMode=XR stereo=Multiview fovOverride=off openMRC=off aspect=XR_MANAGED.");
    }

    [MenuItem("Tools/FPS Game/PICO Fresh/Validate Project")]
    public static void ValidateProject()
    {
        EnsurePicoPackage();
        List<string> failures = new List<string>();
        if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel29) failures.Add("minSdk");
        if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0) failures.Add("arm64");
        if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP) failures.Add("il2cpp");
        if (!PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Contains(GraphicsDeviceType.Vulkan)) failures.Add("vulkan");
        if (!HasAndroidDefine("ENABLE_PICO_XR_SDK")) failures.Add("pico-define");
        if (!HasPicoLoader()) failures.Add("PXR_Loader");
        if (!EditorBuildSettings.TryGetConfigObject(PxrSettingsKey, out UnityEngine.Object settings) || settings == null) failures.Add("PXR_Settings");
        if (failures.Count != 0) throw new InvalidOperationException("[PICO-FRESH] VALIDATE_FAILED " + string.Join(",", failures));
        Debug.Log("[PICO-FRESH] VALIDATE_SUCCESS loader=PXR_Loader settings=PXR_Settings.");
    }

    public static void ConfigureJogoScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Camera[] existingCameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Camera camera in existingCameras)
        {
            camera.enabled = false;
            camera.tag = "Untagged";
            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
        }

        Transform player = FindPlayer();
        Transform origin = FindOrCreateRoot("PICO XR Origin");
        origin.position = player != null ? new Vector3(player.position.x, 0f, player.position.z) : Vector3.zero;
        origin.rotation = Quaternion.identity;
        origin.localScale = Vector3.one;
        XROrigin xrOrigin = GetOrAdd<XROrigin>(origin.gameObject);
        PXR_Manager manager = GetOrAdd<PXR_Manager>(origin.gameObject);
        manager.useRecommendedAntiAliasingLevel = false;
        SetSerializedBool(new SerializedObject(manager), "openMRC", false);

        Transform offset = FindOrCreateChild(origin, "Camera Offset");
        offset.localPosition = Vector3.zero;
        offset.localRotation = Quaternion.identity;
        Transform cameraTransform = FindOrCreateChild(offset, "XR Camera");
        cameraTransform.localPosition = Vector3.zero;
        cameraTransform.localRotation = Quaternion.identity;
        cameraTransform.tag = "MainCamera";
        Camera xrCamera = GetOrAdd<Camera>(cameraTransform.gameObject);
        xrCamera.enabled = true;
        xrCamera.stereoTargetEye = StereoTargetEyeMask.Both;
        xrCamera.fieldOfView = 92f;
        xrCamera.nearClipPlane = 0.03f;
        xrCamera.farClipPlane = 1000f;
        xrCamera.clearFlags = CameraClearFlags.Skybox;
        // Do not assign Camera.aspect. XR/PICO supplies the per-eye viewport.
        GetOrAdd<AudioListener>(cameraTransform.gameObject).enabled = true;
        TrackedPoseDriver pose = GetOrAdd<TrackedPoseDriver>(cameraTransform.gameObject);
        pose.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        pose.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        pose.ignoreTrackingState = false;
        pose.positionInput = DirectAction("PICO HMD Position", "<PXR_HMD>/centerEyePosition", "Vector3");
        pose.rotationInput = DirectAction("PICO HMD Rotation", "<PXR_HMD>/centerEyeRotation", "Quaternion");
        pose.trackingStateInput = DirectAction("PICO HMD Tracking State", "<PXR_HMD>/trackingState", "Integer");
        xrOrigin.Camera = xrCamera;
        xrOrigin.CameraFloorOffsetObject = offset.gameObject;
        xrOrigin.CameraYOffset = 0f;
        xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        BoxCollider floor = EnsureGroundSafety(player);
        if (player == null) throw new InvalidOperationException("[PICO-FRESH] SCENE_VALIDATE_FAILED player-root-missing.");
        if (UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
            throw new InvalidOperationException("[PICO-FRESH] SCENE_VALIDATE_FAILED enemy-missing.");
        if (UnityEngine.Object.FindObjectsByType<Level0GameFlow>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 1)
            throw new InvalidOperationException("[PICO-FRESH] SCENE_VALIDATE_FAILED level0-flow-count.");
        // Keep the menu scene from constructing an agent before its bundled
        // NavMeshSurface has registered data; Level0GameFlow enables it on start.
        foreach (EnemyController enemy in UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (enemy != null)
            {
                enemy.gameObject.SetActive(false);
            }
        }
        PicoFreshRuntime runtime = GetOrAdd<PicoFreshRuntime>(origin.gameObject);
        runtime.Configure(xrCamera, floor);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        ValidateJogoScene();
        Debug.Log("[PICO-FRESH] SCENE_SUCCESS hierarchy=PICO-XR-Origin/Camera-Offset/XR-Camera fov=92 near=0.03 far=1000 aspect=XR_MANAGED legacyMainCameras=untagged-disabled.");
    }

    public static void ValidateJogoScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform[] origins = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => t != null && t.name == "PICO XR Origin" && t.gameObject.scene == scene).ToArray();
        Camera[] xrCameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(c => c != null && c.name == "XR Camera" && c.gameObject.scene == scene).ToArray();
        Camera[] mainCameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(c => c != null && c.CompareTag("MainCamera") && c.gameObject.scene == scene).ToArray();
        if (origins.Length != 1 || xrCameras.Length != 1 || mainCameras.Length != 1 || mainCameras[0] != xrCameras[0])
            throw new InvalidOperationException("[PICO-FRESH] SCENE_VALIDATE_FAILED origins=" + origins.Length + " xrCameras=" + xrCameras.Length + " mainCameras=" + mainCameras.Length);
        Camera camera = xrCameras[0];
        if (Mathf.Abs(camera.fieldOfView - 92f) > 0.001f || Mathf.Abs(camera.nearClipPlane - 0.03f) > 0.0001f || Mathf.Abs(camera.farClipPlane - 1000f) > 0.001f)
            throw new InvalidOperationException("[PICO-FRESH] SCENE_VALIDATE_FAILED camera-values.");
        if (File.ReadAllText(ScenePath).Contains("m_Aspect:")) throw new InvalidOperationException("[PICO-FRESH] SCENE_VALIDATE_FAILED serialized-aspect-lock.");
        if (UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
            throw new InvalidOperationException("[PICO-FRESH] SCENE_VALIDATE_FAILED enemy-missing.");
        if (UnityEngine.Object.FindObjectsByType<AutomaticGunScriptLPFP>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
            throw new InvalidOperationException("[PICO-FRESH] SCENE_VALIDATE_FAILED automatic-weapon-missing.");
        if (UnityEngine.Object.FindObjectsByType<Level0GameFlow>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 1)
            throw new InvalidOperationException("[PICO-FRESH] SCENE_VALIDATE_FAILED level0-flow-count.");
        Debug.Log("[PICO-FRESH] SCENE_VALIDATE_SUCCESS xrOrigin=1 xrCamera=1 mainCamera=XR-Camera fov=92 near=0.03 far=1000 aspect=0-by-XR enemy=present weapon=present hudFlow=present.");
    }

    public static void CreateBootstrapScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject originObject = new GameObject("PICO XR Bootstrap Origin");
        XROrigin origin = originObject.AddComponent<XROrigin>();
        PXR_Manager manager = originObject.AddComponent<PXR_Manager>();
        manager.useRecommendedAntiAliasingLevel = false;
        SetSerializedBool(new SerializedObject(manager), "openMRC", false);

        Transform offset = new GameObject("Camera Offset").transform;
        offset.SetParent(originObject.transform, false);
        Transform cameraTransform = new GameObject("XR Camera").transform;
        cameraTransform.SetParent(offset, false);
        cameraTransform.tag = "MainCamera";
        Camera camera = cameraTransform.gameObject.AddComponent<Camera>();
        camera.enabled = true;
        camera.stereoTargetEye = StereoTargetEyeMask.Both;
        camera.fieldOfView = 92f;
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 1000f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.01f, 0.015f, 0.025f, 1f);
        cameraTransform.gameObject.AddComponent<AudioListener>();
        TrackedPoseDriver pose = cameraTransform.gameObject.AddComponent<TrackedPoseDriver>();
        pose.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        pose.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        pose.positionInput = DirectAction("PICO HMD Position", "<PXR_HMD>/centerEyePosition", "Vector3");
        pose.rotationInput = DirectAction("PICO HMD Rotation", "<PXR_HMD>/centerEyeRotation", "Quaternion");
        pose.trackingStateInput = DirectAction("PICO HMD Tracking State", "<PXR_HMD>/trackingState", "Integer");
        origin.Camera = camera;
        origin.CameraFloorOffsetObject = offset.gameObject;
        origin.CameraYOffset = 0f;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        GameObject loaderObject = new GameObject("PICO Fresh Bootstrap Loader");
        PicoFreshBootstrapLoader loader = loaderObject.AddComponent<PicoFreshBootstrapLoader>();
        loader.Configure(originObject, BundleName, "jogo");
        EditorSceneManager.SaveScene(scene, BootstrapScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PICO-FRESH] BOOTSTRAP_SUCCESS scene=" + BootstrapScenePath + " camera=fov92/near0.03/far1000.");
    }

    public static void BuildGameplayBundle()
    {
        Directory.CreateDirectory(StreamingAssetsPath);
        string[] oldFiles = Directory.GetFiles(StreamingAssetsPath);
        foreach (string file in oldFiles) File.Delete(file);
        AssetBundleBuild[] builds =
        {
            new AssetBundleBuild { assetBundleName = BundleName, assetNames = new[] { ScenePath } }
        };
        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(StreamingAssetsPath, builds,
            BuildAssetBundleOptions.UncompressedAssetBundle, BuildTarget.Android);
        string bundlePath = Path.Combine(StreamingAssetsPath, BundleName);
        if (manifest == null || !File.Exists(bundlePath)) throw new InvalidOperationException("[PICO-FRESH] BUNDLE_FAILED path=" + bundlePath);
        Debug.Log("[PICO-FRESH] BUNDLE_SUCCESS path=" + bundlePath + " bytes=" + new FileInfo(bundlePath).Length + " scene=" + ScenePath + ".");
    }

    public static void BuildApk()
    {
        ConfigureProject();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootstrapScenePath, true) };
        Scene bootstrap = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputPath = Path.Combine(projectRoot, OutputApk);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { BootstrapScenePath },
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.CompressWithLz4HC
        });
        if (report.summary.result != BuildResult.Succeeded || !File.Exists(outputPath))
            throw new InvalidOperationException("[PICO-FRESH] APK_FAILED result=" + report.summary.result);
        string receipt = Path.Combine(projectRoot, "Builds/PICO/Level0-PICO-VR-build-receipt.txt");
        File.WriteAllText(receipt, "PICO_FRESH_BUILD_PASS\n" +
            "sourceBaseline=FPSGameUnity-PICO-Fresh-20260823-local-copy\n" +
            "version=1.2.4-vr-release code=16 input=InputSystem\n" +
            "picoPackage=" + PackageVersion + "\n" +
            "scene=jogo\nbootstrap=" + BootstrapScenePath + "\n" +
            "bundle=" + BundleName + "\n" +
            "camera=XR Camera verticalFOV=92 near=0.03 far=1000 aspect=XR_MANAGED\n" +
            "stereo=Multiview appMode=XR fovOverride=off openMRC=off\n" +
            "gameplay=navmesh-enemy controller-weapon visual-recoil start-gameplay-victory-gameover\n" +
            "ui=fullscreen-black-menu front-world-hud\n" +
            "weapon=right-controller muzzle-hitscan ammo24 damage15 reload1.8\n" +
            "combat=player-hit-no-stun enemy-wall-phasing-disabled navmesh-collision\n" +
            "uiInteraction=right-controller-origin-and-direction world-button-rectangles geometry-occlusion trigger-release-click\n" +
            "apkBytes=" + new FileInfo(outputPath).Length + "\n");
        Debug.Log("[PICO-FRESH] APK_SUCCESS path=" + outputPath + " bytes=" + new FileInfo(outputPath).Length + " scene=bootstrap bundle=jogo.");
    }

    private static void ConfigureXrLoader()
    {
        Type perTargetType = RequireType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget");
        Type generalType = RequireType("UnityEngine.XR.Management.XRGeneralSettings");
        Type managerType = RequireType("UnityEngine.XR.Management.XRManagerSettings");
        Type metadataType = RequireType("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore");
        UnityEngine.Object perTarget = FindAsset(perTargetType) ?? CreateAsset(perTargetType, GeneratedPath + "/XRGeneralSettingsPerBuildTarget.asset");
        MethodInfo get = perTargetType.GetMethod("SettingsForBuildTarget", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo set = perTargetType.GetMethod("SetSettingsForBuildTarget", BindingFlags.Public | BindingFlags.Instance);
        UnityEngine.Object general = get.Invoke(perTarget, new object[] { BuildTargetGroup.Android }) as UnityEngine.Object;
        if (general == null)
        {
            general = ScriptableObject.CreateInstance(generalType);
            general.name = "PICO Fresh XR General Settings";
            AssetDatabase.AddObjectToAsset(general, perTarget);
            set.Invoke(perTarget, new object[] { BuildTargetGroup.Android, general });
        }
        PropertyInfo managerProperty = generalType.GetProperty("Manager", BindingFlags.Public | BindingFlags.Instance);
        UnityEngine.Object manager = managerProperty.GetValue(general) as UnityEngine.Object;
        if (manager == null)
        {
            manager = ScriptableObject.CreateInstance(managerType);
            manager.name = "PICO Fresh XR Manager";
            AssetDatabase.AddObjectToAsset(manager, perTarget);
            managerProperty.SetValue(general, manager);
        }
        SetProperty(managerType, manager, "automaticLoading", true);
        SetProperty(managerType, manager, "automaticRunning", true);
        SetProperty(generalType, general, "InitManagerOnStart", true);
        MethodInfo activeLoaders = managerType.GetProperty("activeLoaders", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
        if (activeLoaders?.Invoke(manager, null) is IEnumerable current)
        {
            MethodInfo remove = metadataType.GetMethods(BindingFlags.Public | BindingFlags.Static).First(m => m.Name == "RemoveLoader" && m.GetParameters().Length == 3);
            foreach (object loader in current.Cast<object>().Where(x => x != null).ToArray())
            {
                if (loader.GetType().FullName != PicoLoaderType) remove.Invoke(null, new object[] { manager, loader.GetType().FullName, BuildTargetGroup.Android });
            }
        }
        if (!ManagerHasPicoLoader(managerType, manager))
        {
            MethodInfo assign = metadataType.GetMethods(BindingFlags.Public | BindingFlags.Static).First(m => m.Name == "AssignLoader" && m.GetParameters().Length == 3);
            bool assigned = (bool)assign.Invoke(null, new object[] { manager, PicoLoaderType, BuildTargetGroup.Android });
            if (!assigned && !ManagerHasPicoLoader(managerType, manager)) throw new InvalidOperationException("[PICO-FRESH] PXR_Loader assignment failed.");
        }
        EditorBuildSettings.AddConfigObject(XrLoaderSettingsKey, perTarget, true);
        EditorUtility.SetDirty(perTarget);
        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(manager);
    }

    private static void ConfigurePicoSettings()
    {
        Type settingsType = RequireType("ByteDance.PICO.XR.PXR_Settings");
        UnityEngine.Object settings = FindAsset(settingsType) ?? CreateAsset(settingsType, GeneratedPath + "/PXR_Settings.asset");
        SerializedObject serialized = new SerializedObject(settings);
        SetSerializedInt(serialized, "appMode", 0);
        SetSerializedInt(serialized, "stereoRenderingModeAndroid", 1);
        SetSerializedBool(serialized, "optimizeBufferDiscards", true);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorBuildSettings.AddConfigObject(PxrSettingsKey, settings, true);

        Type projectType = RequireType("ByteDance.PICO.XR.PXR_ProjectSetting");
        UnityEngine.Object project = FindAsset(projectType) ?? CreateAsset(projectType, "Assets/Resources/PXR_ProjectSetting.asset");
        SerializedObject projectSerialized = new SerializedObject(project);
        SetSerializedBool(projectSerialized, "openMRC", false);
        SetSerializedBoolIfPresent(projectSerialized, "portalInited", true);
        SetSerializedIntIfPresent(projectSerialized, "portalFirstSelected", 1);
        SetSerializedBoolIfPresent(projectSerialized, "isSpatialAdapter", false);
        projectSerialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(project);
    }

    private static void EnsurePicoPlatformAsset()
    {
        Type platformType = FindType("ByteDance.PICO.Platform.Framework.PXR_PlatformSetting");
        if (platformType == null || FindAsset(platformType) != null) return;
        CreateAsset(platformType, "Assets/Resources/PXR_PlatformSetting.asset");
    }

    private static BoxCollider EnsureGroundSafety(Transform player)
    {
        GameObject floor = GameObject.Find("PICO Fresh XR Ground Safety") ?? new GameObject("PICO Fresh XR Ground Safety");
        Vector3 center = player != null ? new Vector3(player.position.x, 0.02f, player.position.z) : Vector3.zero;
        floor.transform.position = new Vector3(center.x, center.y - 0.12f, center.z);
        floor.transform.rotation = Quaternion.identity;
        floor.transform.localScale = Vector3.one;
        BoxCollider collider = GetOrAdd<BoxCollider>(floor);
        collider.center = Vector3.zero;
        collider.size = new Vector3(96f, 0.24f, 96f);
        collider.isTrigger = false;
        collider.enabled = true;
        return collider;
    }

    private static Transform FindPlayer()
    {
        return UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(t => t != null && t.name == "Assault_Rifle_01_FPSController");
    }

    private static Transform FindOrCreateRoot(string name)
    {
        GameObject existing = GameObject.Find(name);
        return existing != null ? existing.transform : new GameObject(name).transform;
    }

    private static Transform FindOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;
        GameObject created = new GameObject(name);
        created.transform.SetParent(parent, false);
        return created.transform;
    }

    private static InputActionProperty DirectAction(string name, string binding, string type)
    {
        return new InputActionProperty(new InputAction(name, InputActionType.Value, binding, expectedControlType: type));
    }

    private static T GetOrAdd<T>(GameObject objectToChange) where T : Component
    {
        T component = objectToChange.GetComponent<T>();
        return component != null ? component : objectToChange.AddComponent<T>();
    }

    private static void SwitchToAndroid()
    {
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            throw new InvalidOperationException("[PICO-FRESH] Android target switch failed.");
    }

    private static void EnsurePicoPackage()
    {
        string path = "Packages/com.bytedance.pico.xr/package.json";
        if (!File.Exists(path) || !File.ReadAllText(path).Contains("\"version\": \"" + PackageVersion + "\""))
            throw new FileNotFoundException("[PICO-FRESH] PICO XR 0.13.1 embedded package missing.", path);
    }

    private static void AddAndroidDefine(string define)
    {
        HashSet<string> values = new HashSet<string>(PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android).Split(';').Where(s => !string.IsNullOrEmpty(s)), StringComparer.Ordinal);
        if (values.Add(define)) PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, string.Join(";", values.OrderBy(s => s, StringComparer.Ordinal)));
    }

    private static bool HasAndroidDefine(string define) => PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android).Split(';').Contains(define, StringComparer.Ordinal);
    private static Type RequireType(string name) => FindType(name) ?? throw new TypeLoadException("[PICO-FRESH] missing type " + name);
    private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
    private static UnityEngine.Object FindAsset(Type type) => AssetDatabase.FindAssets("t:" + type.Name).Select(g => AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(g), type)).FirstOrDefault(x => x != null);
    private static UnityEngine.Object CreateAsset(Type type, string path)
    {
        EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
        ScriptableObject asset = ScriptableObject.CreateInstance(type) as ScriptableObject;
        asset.name = type.Name;
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }
    private static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
        string[] pieces = folder.Split('/');
        string current = pieces[0];
        for (int i = 1; i < pieces.Length; i++)
        {
            string next = current + "/" + pieces[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, pieces[i]);
            current = next;
        }
    }
    private static void SetProperty(Type type, UnityEngine.Object target, string name, bool value) => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.SetValue(target, value);
    private static void SetSerializedInt(SerializedObject serialized, string name, int value) { SerializedProperty property = serialized.FindProperty(name) ?? throw new MissingMemberException(name); property.intValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }
    private static void SetSerializedBool(SerializedObject serialized, string name, bool value) { SerializedProperty property = serialized.FindProperty(name) ?? throw new MissingMemberException(name); property.boolValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }
    private static void SetSerializedIntIfPresent(SerializedObject serialized, string name, int value) { SerializedProperty property = serialized.FindProperty(name); if (property != null) property.intValue = value; }
    private static void SetSerializedBoolIfPresent(SerializedObject serialized, string name, bool value) { SerializedProperty property = serialized.FindProperty(name); if (property != null) property.boolValue = value; }
    private static bool ManagerHasPicoLoader(Type type, UnityEngine.Object manager) => type.GetProperty("activeLoaders", BindingFlags.Public | BindingFlags.Instance)?.GetValue(manager) is IEnumerable loaders && loaders.Cast<object>().Any(x => x != null && x.GetType().FullName == PicoLoaderType);
    private static bool HasPicoLoader()
    {
        Type perTargetType = FindType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget");
        Type managerType = FindType("UnityEngine.XR.Management.XRManagerSettings");
        if (perTargetType == null || managerType == null) return false;
        UnityEngine.Object perTarget = FindAsset(perTargetType);
        MethodInfo get = perTargetType.GetMethod("SettingsForBuildTarget", BindingFlags.Public | BindingFlags.Instance);
        UnityEngine.Object general = perTarget != null && get != null ? get.Invoke(perTarget, new object[] { BuildTargetGroup.Android }) as UnityEngine.Object : null;
        UnityEngine.Object manager = general?.GetType().GetProperty("Manager", BindingFlags.Public | BindingFlags.Instance)?.GetValue(general) as UnityEngine.Object;
        return manager != null && ManagerHasPicoLoader(managerType, manager);
    }
    private static SerializedObject GetPlayerSettingsSerializedObject()
    {
        MethodInfo getter = typeof(PlayerSettings).GetMethod("GetSerializedObject", BindingFlags.NonPublic | BindingFlags.Static);
        if (getter?.Invoke(null, null) is SerializedObject serialized) return serialized;
        throw new InvalidOperationException("[PICO-FRESH] PlayerSettings serialized object unavailable.");
    }
}
#endif

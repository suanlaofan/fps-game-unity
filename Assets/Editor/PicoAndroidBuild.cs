#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Build.Profile;
using UnityEngine;

/// <summary>
/// Reproducible release build entry point for the PICO/Android smoke package.
/// It deliberately only owns Android player settings and Build Settings scenes;
/// no gameplay scene content is changed by this utility.
/// </summary>
public static class PicoAndroidBuild
{
    public const string PackageId = "com.suanlaofan.fpsgame.pico";
    public const string RelativeApkPath = "Builds/PICO/FPSGame-PICO.apk";

    [MenuItem("Tools/FPS Game/Build PICO Android APK")]
    public static void BuildForPico()
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled && !string.IsNullOrEmpty(scene.path))
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            throw new BuildFailedException("[PicoBuild] No enabled Build Settings scenes were found.");
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputPath = Path.Combine(projectRoot, RelativeApkPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        // Ensure the Android platform profile is active before changing its
        // platform-scoped settings.  Unity 6 may otherwise retain a desktop
        // profile even when the command line target is Android.
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
        {
            throw new BuildFailedException("[PicoBuild] Could not switch the active build target to Android.");
        }
        BuildProfile.SetActiveBuildProfile(null);

        // PICO Emulator 0.13 is an arm64 Android target.  Keep the project on
        // its established Mono backend for the fastest, lowest-risk package.
        EditorUserBuildSettings.buildAppBundle = false;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
        PlayerSettings.bundleVersion = "1.0.0";
        PlayerSettings.Android.bundleVersionCode = Math.Max(1, PlayerSettings.Android.bundleVersionCode);
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.useCustomKeystore = false;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        AssetDatabase.SaveAssets();

        if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0)
        {
            throw new BuildFailedException("[PicoBuild] ARM64 was not applied to the active Android Build Profile.");
        }

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        });

        if (report.summary.result != BuildResult.Succeeded || !File.Exists(outputPath))
        {
            throw new BuildFailedException($"[PicoBuild] Android build failed: {report.summary.result}; output='{outputPath}'.");
        }

        Debug.Log($"[PicoBuild] SUCCESS package={PackageId}, scenes={string.Join(", ", scenes)}, apk='{outputPath}', bytes={new FileInfo(outputPath).Length}.");
    }
}
#endif

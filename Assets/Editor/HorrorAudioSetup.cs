using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HorrorAudioSetup
{
    private const string ScenePath = "Assets/Scenes/jogo.unity";
    private const string EnemyName = "diren";
    private const string BgmObjectName = "Horror BGM";
    private const string AudioFolder = "Assets/Audio/Horror/";
    private const string BgmPath = AudioFolder + "IndustrialHorrorBGM.mp3";
    private const string IdleGrowl01Path = AudioFolder + "EnemyIdleGrowl01.mp3";
    private const string IdleGrowl02Path = AudioFolder + "EnemyIdleGrowl02.mp3";
    private const string AttackPath = AudioFolder + "EnemyAttackRoar.mp3";
    private const string MeleeImpactPath = AudioFolder + "EnemyMeleeBite.mp3";
    private const string HurtPath = AudioFolder + "EnemyHurt.mp3";
    private const string DeathPath = AudioFolder + "EnemyDeath.mp3";

    [MenuItem("Tools/FPS Game/Apply Horror Audio And BGM")]
    public static void ApplyHorrorAudio()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[HorrorAudioSetup] Exit Play Mode before applying audio setup.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
        {
            Debug.LogError($"[HorrorAudioSetup] Open '{ScenePath}' before running this command.");
            return;
        }

        ConfigureImport(BgmPath, true);
        ConfigureImport(IdleGrowl01Path, false);
        ConfigureImport(IdleGrowl02Path, false);
        ConfigureImport(AttackPath, false);
        ConfigureImport(MeleeImpactPath, false);
        ConfigureImport(HurtPath, false);
        ConfigureImport(DeathPath, false);

        AudioClip bgmClip = LoadRequiredClip(BgmPath);
        AudioClip idle01 = LoadRequiredClip(IdleGrowl01Path);
        AudioClip idle02 = LoadRequiredClip(IdleGrowl02Path);
        AudioClip attack = LoadRequiredClip(AttackPath);
        AudioClip impact = LoadRequiredClip(MeleeImpactPath);
        AudioClip hurt = LoadRequiredClip(HurtPath);
        AudioClip death = LoadRequiredClip(DeathPath);
        if (bgmClip == null || idle01 == null || idle02 == null || attack == null || impact == null || hurt == null || death == null)
        {
            return;
        }

        GameObject enemy = FindSingleObject(scene, EnemyName);
        if (enemy == null)
        {
            return;
        }

        EnemyHorrorAudio enemyAudio = GetOrAddComponent<EnemyHorrorAudio>(enemy);
        Undo.RecordObject(enemyAudio, "Configure enemy horror clips");
        enemyAudio.volume = 0.64f;
        enemyAudio.volumeMultiplier = 2f;
        enemyAudio.chaseRoarMultiplier = 4f;
        enemyAudio.hurtStingMultiplier = 4f;
        enemyAudio.minDistance = 2f;
        enemyAudio.maxDistance = 38f;
        enemyAudio.idleGrowls = new[] { idle01, idle02 };
        enemyAudio.attackClip = attack;
        enemyAudio.meleeImpactClip = impact;
        enemyAudio.hurtClip = hurt;
        enemyAudio.deathClip = death;
        enemyAudio.minIdleDelay = 3.5f;
        enemyAudio.maxIdleDelay = 7.5f;
        EditorUtility.SetDirty(enemyAudio);
        PrefabUtility.RecordPrefabInstancePropertyModifications(enemyAudio);

        AudioSource enemySource = enemy.GetComponent<AudioSource>();
        Undo.RecordObject(enemySource, "Configure enemy horror source");
        enemySource.playOnAwake = false;
        enemySource.loop = false;
        enemySource.volume = 0.64f;
        enemySource.pitch = 1f;
        enemySource.spatialBlend = 1f;
        enemySource.dopplerLevel = 0f;
        enemySource.rolloffMode = AudioRolloffMode.Logarithmic;
        enemySource.minDistance = 2f;
        enemySource.maxDistance = 38f;
        enemySource.priority = 32;
        EditorUtility.SetDirty(enemySource);
        PrefabUtility.RecordPrefabInstancePropertyModifications(enemySource);

        GameObject bgmObject = FindOptionalObject(scene, BgmObjectName);
        if (bgmObject == null)
        {
            bgmObject = new GameObject(BgmObjectName);
            Undo.RegisterCreatedObjectUndo(bgmObject, "Add horror BGM");
            SceneManager.MoveGameObjectToScene(bgmObject, scene);
        }

        HorrorBgmPlayer bgmPlayer = GetOrAddComponent<HorrorBgmPlayer>(bgmObject);
        Undo.RecordObject(bgmPlayer, "Configure horror BGM");
        bgmPlayer.bgmClip = bgmClip;
        bgmPlayer.volume = 0.7f;
        bgmPlayer.volumeMultiplier = 4f;
        bgmPlayer.fadeInDuration = 0.8f;
        EditorUtility.SetDirty(bgmPlayer);

        AudioSource bgmSource = bgmObject.GetComponent<AudioSource>();
        Undo.RecordObject(bgmSource, "Configure horror BGM source");
        bgmSource.clip = bgmClip;
        bgmSource.playOnAwake = false;
        bgmSource.loop = true;
        bgmSource.volume = 0.7f;
        bgmSource.pitch = 1f;
        bgmSource.spatialBlend = 0f;
        bgmSource.dopplerLevel = 0f;
        bgmSource.priority = 64;
        EditorUtility.SetDirty(bgmSource);

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene))
        {
            Debug.LogError($"[HorrorAudioSetup] Failed to save '{scene.path}'.");
            return;
        }

        Selection.activeGameObject = bgmObject;
        Debug.Log($"[HorrorAudioSetup] SUCCESS: enemyVolume={enemyAudio.volume:F2}, " +
                  $"enemyMultiplier={enemyAudio.volumeMultiplier:F2}x, effectiveEnemyVolume={enemyAudio.EffectiveVolume:F2}, " +
                  $"chaseMultiplier={enemyAudio.chaseRoarMultiplier:F2}x, effectiveChaseVolume={enemyAudio.EffectiveChaseVolume:F2}, " +
                  $"hurtMultiplier={enemyAudio.hurtStingMultiplier:F2}x, effectiveHurtVolume={enemyAudio.EffectiveHurtVolume:F2}, " +
                  $"idleClips={enemyAudio.idleGrowls.Length}, " +
                  $"attack='{attack.name}', impact='{impact.name}', hurtAndChase='{hurt.name}', death='{death.name}', " +
                  $"bgm='{bgmClip.name}', duration={bgmClip.length:F2}s, bgmVolume={bgmPlayer.volume:F2}, " +
                  $"bgmMultiplier={bgmPlayer.volumeMultiplier:F2}x, effectiveBgmVolume={bgmPlayer.EffectiveVolume:F2}, " +
                  $"saved='{scene.path}'.");
    }

    [MenuItem("Tools/FPS Game/Validate Horror Audio And BGM")]
    public static void ValidateHorrorAudio()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject enemy = FindSingleObject(scene, EnemyName);
        GameObject bgmObject = FindOptionalObject(scene, BgmObjectName);
        EnemyHorrorAudio enemyAudio = enemy != null ? enemy.GetComponent<EnemyHorrorAudio>() : null;
        HorrorBgmPlayer bgmPlayer = bgmObject != null ? bgmObject.GetComponent<HorrorBgmPlayer>() : null;
        AudioSource bgmSource = bgmObject != null ? bgmObject.GetComponent<AudioSource>() : null;

        int enabledListeners = 0;
        foreach (AudioListener listener in Resources.FindObjectsOfTypeAll<AudioListener>())
        {
            if (listener.gameObject.scene == scene && listener.enabled && listener.gameObject.activeInHierarchy)
            {
                enabledListeners++;
            }
        }

        Debug.Log($"[HorrorAudioValidate] enemyVolume={(enemyAudio != null ? enemyAudio.volume : 0f):F2}, " +
                  $"enemyMultiplier={(enemyAudio != null ? enemyAudio.volumeMultiplier : 0f):F2}x, " +
                  $"effectiveEnemyVolume={(enemyAudio != null ? enemyAudio.EffectiveVolume : 0f):F2}, " +
                  $"chaseMultiplier={(enemyAudio != null ? enemyAudio.chaseRoarMultiplier : 0f):F2}x, " +
                  $"effectiveChaseVolume={(enemyAudio != null ? enemyAudio.EffectiveChaseVolume : 0f):F2}, " +
                  $"hurtMultiplier={(enemyAudio != null ? enemyAudio.hurtStingMultiplier : 0f):F2}x, " +
                  $"effectiveHurtVolume={(enemyAudio != null ? enemyAudio.EffectiveHurtVolume : 0f):F2}, " +
                  $"externalClips={(enemyAudio != null && enemyAudio.UsesExternalClips)}, " +
                  $"idleClips={(enemyAudio != null && enemyAudio.idleGrowls != null ? enemyAudio.idleGrowls.Length : 0)}, " +
                  $"bgm='{(bgmPlayer != null && bgmPlayer.bgmClip != null ? bgmPlayer.bgmClip.name : "<none>")}', " +
                  $"bgmVolume={(bgmPlayer != null ? bgmPlayer.volume : 0f):F2}, " +
                  $"bgmMultiplier={(bgmPlayer != null ? bgmPlayer.volumeMultiplier : 0f):F2}x, " +
                  $"effectiveBgmVolume={(bgmPlayer != null ? bgmPlayer.EffectiveVolume : 0f):F2}, " +
                  $"bgmVoices={(bgmPlayer != null ? bgmPlayer.PlaybackVoiceCount : 0)}, loop={(bgmSource != null && bgmSource.loop)}, " +
                  $"currentBgmVolume={(bgmPlayer != null ? bgmPlayer.CurrentVolume : 0f):F2}, " +
                  $"spatialBlend={(bgmSource != null ? bgmSource.spatialBlend : -1f):F1}, playing={(bgmPlayer != null && bgmPlayer.IsPlaying)}, " +
                  $"enabledListeners={enabledListeners}.");
    }

    private static void ConfigureImport(string path, bool music)
    {
        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null)
        {
            Debug.LogError($"[HorrorAudioSetup] Missing AudioImporter for '{path}'.");
            return;
        }

        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
        settings.quality = music ? 0.72f : 1f;
        settings.preloadAudioData = !music;
        importer.defaultSampleSettings = settings;
        importer.forceToMono = !music;
        importer.loadInBackground = music;
        importer.SaveAndReimport();
    }

    private static AudioClip LoadRequiredClip(string path)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null)
        {
            Debug.LogError($"[HorrorAudioSetup] Failed to load '{path}'.");
        }
        return clip;
    }

    private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(gameObject);
    }

    private static GameObject FindSingleObject(Scene scene, string objectName)
    {
        GameObject match = null;
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name != objectName)
                {
                    continue;
                }
                match = candidate.gameObject;
                count++;
            }
        }

        if (count != 1)
        {
            Debug.LogError($"[HorrorAudioSetup] Expected exactly one '{objectName}' in '{scene.path}', found {count}.");
            return null;
        }
        return match;
    }

    private static GameObject FindOptionalObject(Scene scene, string objectName)
    {
        GameObject match = null;
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name != objectName)
                {
                    continue;
                }
                match = candidate.gameObject;
                count++;
            }
        }

        if (count > 1)
        {
            Debug.LogError($"[HorrorAudioSetup] Expected at most one '{objectName}' in '{scene.path}', found {count}.");
            return null;
        }
        return match;
    }
}

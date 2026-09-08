using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Management;

/// <summary>
/// Starts the tiny PICO scene, waits for XR, then loads the authored jogo
/// scene from StreamingAssets. Keeping the large scene out of Build Settings
/// avoids PICO 0.13 startup preload of the legacy scene data.
/// </summary>
[DefaultExecutionOrder(-1500)]
public sealed class PicoFreshBootstrapLoader : MonoBehaviour
{
    [SerializeField] private GameObject bootstrapRig;
    [SerializeField] private string gameplayBundle = "fpsgame-jogo-fresh.bundle";
    [SerializeField] private string gameplaySceneName = "jogo";

    private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
    private Scene bootstrapScene;
    private AssetBundle loadedBundle;
    private bool handoff;

    public void Configure(GameObject rig, string bundleName, string sceneName)
    {
        bootstrapRig = rig;
        gameplayBundle = bundleName;
        gameplaySceneName = sceneName;
    }

    private void Awake()
    {
        bootstrapScene = gameObject.scene;
        Debug.Log("[PICO-FRESH] PICO_FRESH_BOOTSTRAP_READY bundle='" + gameplayBundle + "'.", this);
    }

    private IEnumerator Start()
    {
        yield return WaitForXr();
        if (!HasActiveLoader())
        {
            Debug.LogError("[PICO-FRESH] PICO_FRESH_XR_TIMEOUT bootstrap_kept=true.", this);
            yield break;
        }

        yield return LoadGameplayBundle();
    }

    private IEnumerator WaitForXr()
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        bool loaderLogged = false;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (HasActiveLoader())
            {
                if (!loaderLogged)
                {
                    loaderLogged = true;
                    Debug.Log("[PICO-FRESH] PICO_FRESH_XR_LOADER_READY displayRunning=" + HasRunningDisplay() + ".", this);
                }

                if (HasRunningDisplay() || Time.realtimeSinceStartup + 1.5f >= deadline)
                {
                    Debug.Log("[PICO-FRESH] PICO_FRESH_XR_SESSION_READY displayRunning=" + HasRunningDisplay() + ".", this);
                    yield break;
                }
            }

            yield return null;
        }
    }

    private IEnumerator LoadGameplayBundle()
    {
        string url = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + gameplayBundle;
        Debug.Log("[PICO-FRESH] PICO_FRESH_BUNDLE_LOAD_BEGIN url='" + url + "'.", this);

        using (UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(url))
        {
            request.timeout = 120;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[PICO-FRESH] PICO_FRESH_BUNDLE_LOAD_FAILED result=" + request.result +
                               " error='" + request.error + "'.", this);
                yield break;
            }

            loadedBundle = DownloadHandlerAssetBundle.GetContent(request);
        }

        if (loadedBundle == null)
        {
            Debug.LogError("[PICO-FRESH] PICO_FRESH_BUNDLE_LOAD_FAILED content=null.", this);
            yield break;
        }

        string[] scenes = loadedBundle.GetAllScenePaths();
        if (scenes == null || scenes.Length == 0)
        {
            Debug.LogError("[PICO-FRESH] PICO_FRESH_BUNDLE_INVALID scenes=0.", this);
            loadedBundle.Unload(false);
            loadedBundle = null;
            yield break;
        }

        string scenePath = scenes[0];
        foreach (string candidate in scenes)
        {
            if (!string.IsNullOrEmpty(candidate) && candidate.EndsWith("/" + gameplaySceneName + ".unity", System.StringComparison.OrdinalIgnoreCase))
            {
                scenePath = candidate;
                break;
            }
        }

        if (bootstrapRig != null)
        {
            bootstrapRig.SetActive(false);
        }

        Debug.Log("[PICO-FRESH] PICO_FRESH_GAMEPLAY_LOAD_BEGIN scene='" + scenePath + "'.", this);
        AsyncOperation load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
        if (load == null)
        {
            Debug.LogError("[PICO-FRESH] PICO_FRESH_GAMEPLAY_LOAD_FAILED operation=null.", this);
            yield break;
        }

        while (!load.isDone)
        {
            yield return null;
        }

        Scene gameplay = SceneManager.GetSceneByPath(scenePath);
        if (!gameplay.IsValid() || !gameplay.isLoaded)
        {
            Debug.LogError("[PICO-FRESH] PICO_FRESH_GAMEPLAY_LOAD_FAILED scene-invalid.", this);
            yield break;
        }

        SceneManager.SetActiveScene(gameplay);
        loadedBundle.Unload(false);
        loadedBundle = null;
        handoff = true;
        Debug.Log("[PICO-FRESH] PICO_FRESH_HANDOFF_COMPLETE scene='" + gameplay.name + "'.", this);
        if (bootstrapScene.IsValid() && bootstrapScene.isLoaded)
        {
            SceneManager.UnloadSceneAsync(bootstrapScene);
        }
    }

    private void Update()
    {
        if (handoff && Time.frameCount % 300 == 0)
        {
            Debug.Log("[PICO-FRESH] PICO_FRESH_HEARTBEAT xrLoader=" + HasActiveLoader() +
                      " displayRunning=" + HasRunningDisplay() + ".", this);
        }
    }

    private static bool HasActiveLoader()
    {
        XRGeneralSettings settings = XRGeneralSettings.Instance;
        return settings != null && settings.Manager != null && settings.Manager.activeLoader != null;
    }

    private bool HasRunningDisplay()
    {
        displays.Clear();
        SubsystemManager.GetSubsystems(displays);
        foreach (XRDisplaySubsystem display in displays)
        {
            if (display != null && display.running)
            {
                return true;
            }
        }
        return false;
    }
}

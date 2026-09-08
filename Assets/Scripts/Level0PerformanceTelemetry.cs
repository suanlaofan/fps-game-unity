using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;

public sealed class Level0PerformanceTelemetry : MonoBehaviour
{
    private readonly float[] samples = new float[2048];
    private readonly FrameTiming[] timing = new FrameTiming[1];
    private ProfilerRecorder drawCalls, triangles, gc;
    private int count;
    private double start;
    private string path;
    private bool trackingRecorded;
    private bool windowFocused;
    private int focusChanges;
    private System.Collections.IEnumerator Start()
    {
        string request = Path.Combine(Application.persistentDataPath,"level0-diagnostics.once");
        if (!File.Exists(request)) yield break;
        File.Delete(request);
        yield return new WaitForSecondsRealtime(3);
        yield return new WaitForEndOfFrame();
        var capture = ScreenCapture.CaptureScreenshotAsTexture();
        if (capture)
        {
            File.WriteAllBytes(Path.Combine(Application.persistentDataPath,"level0-app-frame.png"),capture.EncodeToPNG());
            Destroy(capture);
            Debug.Log("LEVEL0_DIAGNOSTIC_FRAME_SAVED");
        }
    }
    private void OnEnable()
    {
        drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count");
        triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render,"Triangles Count");
        gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame");
        path = Path.Combine(Application.persistentDataPath,"level0-performance.csv");
        File.WriteAllText(path,"utc,mode,seconds,frames,fps,p95_ms,p99_ms,cpu_ms,gpu_ms,draw_calls,triangles,gc_bytes_last_frame,allocated_mb,focused,focus_changes\n");
        windowFocused = Application.isFocused; focusChanges = 0;
        start = Time.realtimeSinceStartupAsDouble;
        Debug.Log("LEVEL0_PERF_FILE " + path);
    }
    private void Update()
    {
        if (windowFocused != Application.isFocused) { windowFocused = Application.isFocused; focusChanges++; }
        if (count < samples.Length) samples[count++] = Time.unscaledDeltaTime * 1000;
        FrameTimingManager.CaptureFrameTimings();
        double elapsed = Time.realtimeSinceStartupAsDouble - start;
        if (elapsed < 5 || count == 0) return;
        if (!trackingRecorded && PicoFreshRuntime.Instance)
        {
            trackingRecorded = true;
            var rig = PicoFreshRuntime.Instance;
            var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
            head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,out bool tracked);
            head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition,out Vector3 rawHead);
            Debug.Log("LEVEL0_TRACKING headTracked="+tracked+" rawHead="+rawHead+" camera="+rig.Camera.transform.position+
                " body="+rig.Motor.transform.position+" blocked="+rig.Motor.HeadBlocked+" left="+rig.LeftTracked+" right="+rig.RightTracked+
                " rightLocalToHead="+rig.Camera.transform.InverseTransformPoint(rig.RightHand.position)+
                " rightForwardToHead="+rig.Camera.transform.InverseTransformDirection(rig.RightHand.forward)+
                " msaa="+QualitySettings.antiAliasing);
        }
        Array.Sort(samples,0,count);
        bool hasTiming = FrameTimingManager.GetLatestTimings(1,timing) > 0;
        string cpu = hasTiming && timing[0].cpuFrameTime > 0 ? timing[0].cpuFrameTime.ToString("F2",CultureInfo.InvariantCulture) : "unavailable";
        string gpu = hasTiming && timing[0].gpuFrameTime > 0 ? timing[0].gpuFrameTime.ToString("F2",CultureInfo.InvariantCulture) : "unavailable";
        string row = string.Format(CultureInfo.InvariantCulture,"{0},{1},{2:F2},{3},{4:F2},{5:F2},{6:F2},{7},{8},{9},{10},{11},{12:F1},{13},{14}",
            DateTime.UtcNow.ToString("O"),PicoFreshRuntime.Instance != null && PicoFreshRuntime.Instance.Gameplay ? "playing" : "menu",elapsed,count,count/elapsed,
            samples[Math.Min(count-1,(int)(count*0.95))],samples[Math.Min(count-1,(int)(count*0.99))],cpu,gpu,
            drawCalls.Valid ? drawCalls.LastValue : -1,triangles.Valid ? triangles.LastValue : -1,gc.Valid ? gc.LastValue : -1,
            Profiler.GetTotalAllocatedMemoryLong()/(1024.0*1024.0),windowFocused ? 1 : 0,focusChanges);
        File.AppendAllText(path,row+"\n");
        Debug.Log("LEVEL0_PERF " + row); count = 0; focusChanges = 0; start = Time.realtimeSinceStartupAsDouble;
    }
    private void OnDisable() { drawCalls.Dispose(); triangles.Dispose(); gc.Dispose(); }
}

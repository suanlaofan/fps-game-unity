using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DefaultExecutionOrder(-100)]
public sealed class Level0WorldUi : MonoBehaviour
{
    const int UiLayer = 5;
    public Canvas MenuCanvas { get; private set; }
    public Canvas HudCanvas { get; private set; }
    public bool FullscreenMenuActive { get; private set; }
    private PicoFreshRuntime rig;
    private Level0GameFlow flow;
    private Canvas pauseCanvas, blackout;
    private Text healthText, ammoText, statusText, helpText;
    private Image reloadFill;
    private Button hover, pressed;
    private Button[] buttons;
    private EventSystem events;
    private PointerEventData pointer;
    private LineRenderer rayLine;
    private Material rayMaterial;
    private int state = -1, lastHp = -1, lastAmmo = -1, lastReloadInput = -1;
    private bool lastReload;
    private int worldMask;
    private CameraClearFlags worldClearFlags;
    private Color worldBackground;
    private float hudYaw, recenterAt = -1, noticeUntil;
    private bool hudPlaced, hudRecentering;

    public void Configure(PicoFreshRuntime runtime, Level0GameFlow gameFlow)
    {
        rig = runtime; flow = gameFlow;
        if (!flow || !flow.menuScreen) { Debug.LogError("LEVEL0_UI_MISSING_FLOW"); return; }
        worldMask = rig.Camera.cullingMask; worldClearFlags = rig.Camera.clearFlags; worldBackground = rig.Camera.backgroundColor;
        MenuCanvas = flow.menuScreen.GetComponentInParent<Canvas>();
        MenuCanvas.renderMode = RenderMode.WorldSpace; MenuCanvas.worldCamera = rig.Camera;
        var mr = (RectTransform)MenuCanvas.transform;
        mr.pivot = new Vector2(0.5f,0.5f); mr.sizeDelta = new Vector2(1920,1080);
        var scaler = MenuCanvas.GetComponent<CanvasScaler>(); if (scaler) scaler.enabled = false;
        SetLayer(MenuCanvas.gameObject);
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if (canvas != MenuCanvas && !canvas.transform.IsChildOf(MenuCanvas.transform)) canvas.gameObject.SetActive(false);
        events = FindFirstObjectByType<EventSystem>();
        if (!events) events = new GameObject("Level0 UI Events").AddComponent<EventSystem>();
        foreach (var module in events.GetComponents<BaseInputModule>()) module.enabled = false;
        pointer = new PointerEventData(events) { button = PointerEventData.InputButton.Left };

        HudCanvas = MakeCanvas("Front World HUD",null,new Vector2(900,210),0.001f);
        healthText = Label(HudCanvas.transform,"HP 100 / 100",new Vector2(400,65),38,new Vector2(-220,58));
        ammoText = Label(HudCanvas.transform,"24 / 24",new Vector2(400,65),40,new Vector2(220,58));
        statusText = Label(HudCanvas.transform,"PREPARE",new Vector2(850,52),29,new Vector2(0,-4));
        helpText = Label(HudCanvas.transform,"LEFT STICK / X: SPRINT     B / GRIP: RELOAD",new Vector2(870,48),23,new Vector2(0,-67));
        var bar = new GameObject("Reload Progress",typeof(RectTransform),typeof(Image)); bar.transform.SetParent(HudCanvas.transform,false);bar.layer=UiLayer;
        reloadFill=bar.GetComponent<Image>();reloadFill.color=new Color(0.88f,0.71f,0.27f);
        var br=(RectTransform)bar.transform;br.sizeDelta=new Vector2(840,5);br.anchoredPosition=new Vector2(0,-35);

        pauseCanvas = MakeCanvas("Fullscreen Pause",rig.Camera.transform,new Vector2(900,600),0.001f);
        Label(pauseCanvas.transform,"PAUSED",new Vector2(800,100),60,new Vector2(0,170));
        AddButton(pauseCanvas.transform,"RESUME",new Vector2(0,30),()=>rig.SetPaused(false));
        AddButton(pauseCanvas.transform,"MAIN MENU",new Vector2(0,-100),()=>flow.ReturnToMainMenu());
        blackout = MakeCanvas("Head Boundary Occlusion",rig.Camera.transform,new Vector2(2000,2000),0.001f);
        blackout.transform.localPosition = new Vector3(0,0,0.04f);
        blackout.GetComponent<Image>().color=Color.black;blackout.gameObject.SetActive(false);
        var rayObject = new GameObject("Controller UI Ray"); rayObject.layer=UiLayer;rayObject.transform.SetParent(rig.transform,false);
        rayLine = rayObject.AddComponent<LineRenderer>(); rayLine.positionCount=2;rayLine.useWorldSpace=true;
        rayLine.startWidth=0.003f;rayLine.endWidth=0.0015f;
        rayMaterial=new Material(Shader.Find("Sprites/Default"));rayLine.sharedMaterial=rayMaterial;
        rayLine.startColor=rayLine.endColor=new Color(0.9f,0.8f,0.4f);
        RecenterMenu(); RefreshState();
    }
    public void RecenterMenu()
    {
        if (!MenuCanvas || !rig) return;
        FitMenu(MenuCanvas);FitMenu(pauseCanvas);
        hudYaw=rig.Camera.transform.eulerAngles.y;hudPlaced=false;recenterAt=-1;hudRecentering=false;
    }
    private int menuFitFirstFrame = -1, menuFitLoggedEyes;
    private void FitMenu(Canvas canvas)
    {
        if (!canvas || !rig || !rig.Camera) return;
        const float distance = 1.25f, inset = 0.03f;
        var camera = rig.Camera;
        var cameraTransform = camera.transform;
        var rect = (RectTransform)canvas.transform;
        if (rect.rect.width <= 0 || rect.rect.height <= 0) return;
        if (menuFitFirstFrame < 0) menuFitFirstFrame = Time.frameCount;
        int eyes = camera.stereoEnabled ? 2 : 1;
        Matrix4x4 toLocal = cameraTransform.worldToLocalMatrix;
        Matrix4x4 toWorld = cameraTransform.localToWorldMatrix;
        float left = float.NegativeInfinity, bottom = float.NegativeInfinity;
        float right = float.PositiveInfinity, top = float.PositiveInfinity;
        bool valid = true;
        for (int eyeIndex = 0; eyeIndex < eyes; eyeIndex++)
        {
            var eye = eyes == 1 ? Camera.MonoOrStereoscopicEye.Mono :
                (eyeIndex == 0 ? Camera.MonoOrStereoscopicEye.Left : Camera.MonoOrStereoscopicEye.Right);
            float eyeLeft = float.NegativeInfinity, eyeBottom = float.NegativeInfinity;
            float eyeRight = float.PositiveInfinity, eyeTop = float.PositiveInfinity;
            for (int corner = 0; corner < 4; corner++)
            {
                bool isRight = corner == 1 || corner == 2, isTop = corner >= 2;
                float x = isRight ? 1f - inset : inset, y = isTop ? 1f - inset : inset;
                // Two depths define the actual eye ray, including asymmetric projection,
                // IPD and eye-view rotation. Intersect with the shared camera-local plane.
                Vector3 a = toLocal.MultiplyPoint3x4(camera.ViewportToWorldPoint(new Vector3(x, y, 1f), eye));
                Vector3 b = toLocal.MultiplyPoint3x4(camera.ViewportToWorldPoint(new Vector3(x, y, 2f), eye));
                Vector3 ray = b - a;
                if (Mathf.Abs(ray.z) < 0.00001f) { valid = false; break; }
                Vector3 point = a + ray * ((distance - a.z) / ray.z);
                if (float.IsNaN(point.x) || float.IsInfinity(point.x) ||
                    float.IsNaN(point.y) || float.IsInfinity(point.y)) { valid = false; break; }
                // Inscribe an axis-aligned rectangle even when the eye frustum is canted.
                if (isRight) eyeRight = Mathf.Min(eyeRight, point.x);
                else eyeLeft = Mathf.Max(eyeLeft, point.x);
                if (isTop) eyeTop = Mathf.Min(eyeTop, point.y);
                else eyeBottom = Mathf.Max(eyeBottom, point.y);
            }
            left = Mathf.Max(left, eyeLeft); right = Mathf.Min(right, eyeRight);
            bottom = Mathf.Max(bottom, eyeBottom); top = Mathf.Min(top, eyeTop);
        }
        valid &= right > left && top > bottom;
        Vector2 center = valid ? new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f) : Vector2.zero;
        float scale = valid ? Mathf.Min((right - left) / rect.rect.width,
            (top - bottom) / rect.rect.height) * 0.995f : 0f;
        // Keep the whole page in the comfortable central view. The PICO emulator
        // mirror crops the eye texture, so eye-frustum bounds alone are insufficient.
        scale = Mathf.Min(scale, 2f * distance * Mathf.Tan(36f * Mathf.Deg2Rad) / rect.rect.width);
        Vector4 leftViewport = Vector4.zero, rightViewport = Vector4.zero;
        bool allCornersInside = false;
        // Check the final four artwork corners through both real eye projections.
        // Usually the first pass succeeds; bounded shrinking handles numerical edge cases.
        for (int attempt = 0; valid && attempt < 12; attempt++)
        {
            allCornersInside = true;
            for (int eyeIndex = 0; eyeIndex < eyes; eyeIndex++)
            {
                var eye = eyes == 1 ? Camera.MonoOrStereoscopicEye.Mono :
                    (eyeIndex == 0 ? Camera.MonoOrStereoscopicEye.Left : Camera.MonoOrStereoscopicEye.Right);
                Vector4 bounds = new Vector4(float.PositiveInfinity, float.PositiveInfinity,
                    float.NegativeInfinity, float.NegativeInfinity);
                for (int corner = 0; corner < 4; corner++)
                {
                    float x = center.x + ((corner == 1 || corner == 2) ? 1f : -1f) * rect.rect.width * scale * 0.5f;
                    float y = center.y + (corner >= 2 ? 1f : -1f) * rect.rect.height * scale * 0.5f;
                    Vector3 viewport = camera.WorldToViewportPoint(toWorld.MultiplyPoint3x4(new Vector3(x, y, distance)), eye);
                    bool inside = viewport.z > camera.nearClipPlane && viewport.x >= inset && viewport.x <= 1f - inset &&
                        viewport.y >= inset && viewport.y <= 1f - inset;
                    allCornersInside &= inside;
                    bounds.x = Mathf.Min(bounds.x, viewport.x); bounds.y = Mathf.Min(bounds.y, viewport.y);
                    bounds.z = Mathf.Max(bounds.z, viewport.x); bounds.w = Mathf.Max(bounds.w, viewport.y);
                }
                if (eyeIndex == 0) leftViewport = bounds; else rightViewport = bounds;
            }
            if (allCornersInside) break;
            scale *= 0.95f;
        }
        if (canvas.transform.parent != cameraTransform) canvas.transform.SetParent(cameraTransform, false);
        canvas.transform.localRotation = Quaternion.identity;
        // Rect center compensates for a non-centered pivot without changing authored aspect.
        canvas.transform.localPosition = new Vector3(center.x - rect.rect.center.x * scale,
            center.y - rect.rect.center.y * scale, distance);
        canvas.transform.localScale = Vector3.one * (allCornersInside ? scale : 0f);
        int logBit = allCornersInside ? eyes : eyes * 4;
        if (canvas == MenuCanvas && Time.frameCount >= menuFitFirstFrame + 5 && (menuFitLoggedEyes & logBit) == 0)
        {
            menuFitLoggedEyes |= logBit;
            var lp = eyes == 2 ? camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left) : camera.projectionMatrix;
            var rp = eyes == 2 ? camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right) : camera.projectionMatrix;
            Debug.Log("LEVEL0_MENU_STEREO_FIT eyes=" + eyes + " pass=" + allCornersInside +
                " localRect=" + new Vector4(left, bottom, right, top).ToString("F4") +
                " center=" + center.ToString("F4") + " scale=" + scale.ToString("F6") +
                " viewportL=" + leftViewport.ToString("F4") + " viewportR=" + rightViewport.ToString("F4") +
                " projectionL=" + new Vector4(lp.m00, lp.m11, lp.m02, lp.m12).ToString("F4") +
                " projectionR=" + new Vector4(rp.m00, rp.m11, rp.m02, rp.m12).ToString("F4"));
        }
    }
    public void RefreshState() { state=-1; }
    private void Update()
    {
        if(!rig||!flow||!MenuCanvas)return;
        int nextState=(int)flow.CurrentState+(rig.Paused?10:0);
        if(state!=nextState)
        {
            CancelPointer();state=nextState;
            bool menu=flow.CurrentState!=Level0GameFlow.GameState.Playing;
            FullscreenMenuActive=menu||rig.Paused;
            MenuCanvas.enabled=menu; // GameFlow must remain active on this root.
            pauseCanvas.gameObject.SetActive(rig.Paused);
            rig.Camera.cullingMask=FullscreenMenuActive ? 1<<UiLayer : worldMask;
            rig.Camera.clearFlags=FullscreenMenuActive ? CameraClearFlags.SolidColor : worldClearFlags;
            rig.Camera.backgroundColor=FullscreenMenuActive ? Color.black : worldBackground;
            if(FullscreenMenuActive) RecenterMenu(); else {hudYaw=rig.Camera.transform.eulerAngles.y;hudPlaced=false;}
            buttons=(rig.Paused?pauseCanvas:MenuCanvas).GetComponentsInChildren<Button>(true);
            Debug.Log("LEVEL0_UI_PRESENTATION fullscreen="+FullscreenMenuActive+" mask="+rig.Camera.cullingMask+" hud=front-world");
        }
        bool playing=rig.Gameplay;
        HudCanvas.gameObject.SetActive(playing&&!rig.Motor.HeadBlocked);
        blackout.gameObject.SetActive(playing&&rig.Motor.HeadBlocked);
        if(flow.playerHealth&&flow.playerHealth.CurrentHealth!=lastHp)
        {lastHp=flow.playerHealth.CurrentHealth;healthText.text="HP  "+lastHp+" / "+flow.playerHealth.MaxHealth;}
        var weapon=rig.Weapon;
        if(weapon&&(lastAmmo!=weapon.Ammo||lastReload!=weapon.Reloading))
        {lastAmmo=weapon.Ammo;lastReload=weapon.Reloading;ammoText.text=weapon.Ammo+" / "+weapon.magazineSize;}
        if(lastReloadInput!=rig.ReloadInputCount)
        {
            if(lastReloadInput>=0&&weapon&&!weapon.Reloading&&weapon.Ammo==weapon.magazineSize)noticeUntil=Time.unscaledTime+1.3f;
            lastReloadInput=rig.ReloadInputCount;
        }
        string status;
        if(weapon&&weapon.Reloading)status="RELOADING  "+Mathf.RoundToInt(weapon.ReloadProgress*100)+"%";
        else if(Time.unscaledTime<noticeUntil)status="MAGAZINE FULL";
        else if(flow.PreparationRemaining>0)status="PREPARE  "+Mathf.CeilToInt(flow.PreparationRemaining)+"s  |  "+(rig.SprintActive?"SPRINT":"WALK");
        else status=(rig.SprintActive?"SPRINT":"WALK")+"  |  ENTITY  "+(flow.enemy?flow.enemy.CurrentHealth:0)+" / "+(flow.enemy?flow.enemy.MaxHealth:300);
        if(statusText.text!=status)statusText.text=status;
        float progress=weapon&&weapon.Reloading?weapon.ReloadProgress:0;
        reloadFill.gameObject.SetActive(progress>0);
        ((RectTransform)reloadFill.transform).sizeDelta=new Vector2(840*progress,5);
        rayLine.enabled=FullscreenMenuActive&&rig.RightTracked;
        if(rayLine.enabled)UpdatePointer();else CancelPointer();
    }
    private void LateUpdate()
    {
        if(!rig||!HudCanvas)return;
        if(FullscreenMenuActive){FitMenu(MenuCanvas);if(rig.Paused)FitMenu(pauseCanvas);return;}
        if(!rig.Gameplay)return;
        float targetYaw=rig.Camera.transform.eulerAngles.y;
        if(!hudRecentering)
        {
            if(Mathf.Abs(Mathf.DeltaAngle(hudYaw,targetYaw))>28)
            {if(recenterAt<0)recenterAt=Time.unscaledTime+0.5f;}
            else recenterAt=-1;
            if(recenterAt>=0&&Time.unscaledTime>=recenterAt)hudRecentering=true;
        }
        if(hudRecentering)
        {
            hudYaw=Mathf.LerpAngle(hudYaw,targetYaw,1-Mathf.Exp(-5*Time.unscaledDeltaTime));
            if(Mathf.Abs(Mathf.DeltaAngle(hudYaw,targetYaw))<1)
            {hudYaw=targetYaw;hudRecentering=false;recenterAt=-1;}
        }
        Vector3 forward=Quaternion.Euler(0,hudYaw,0)*Vector3.forward;
        Vector3 eye=rig.Camera.transform.position;
        float distance=1.35f;
        if(Physics.Raycast(eye,forward,out RaycastHit wall,distance,1<<12,QueryTriggerInteraction.Ignore))distance=Mathf.Max(0.22f,wall.distance-0.12f);
        float ratio=distance/1.35f;
        Vector3 desired=eye+forward*distance+Vector3.down*(0.30f*ratio);
        HudCanvas.transform.position=hudPlaced?Vector3.Lerp(HudCanvas.transform.position,desired,1-Mathf.Exp(-16*Time.unscaledDeltaTime)):desired;
        HudCanvas.transform.rotation=Quaternion.LookRotation(forward);
        HudCanvas.transform.localScale=Vector3.one*0.001f*ratio;hudPlaced=true;
    }
    private void UpdatePointer()
    {
        Ray ray=new Ray(rig.RightHand.position,rig.RightHand.forward);
        float nearest=8;Button target=null;
        // The environment is hidden during full-screen menus and cannot block their buttons.
        if(buttons!=null)foreach(var button in buttons)
        {
            if(!button||!button.gameObject.activeInHierarchy||!button.IsInteractable())continue;
            var rect=(RectTransform)button.transform;Plane plane=new Plane(rect.forward,rect.position);
            if(!plane.Raycast(ray,out float distance)||distance<0||distance>=nearest)continue;
            Vector3 local=rect.InverseTransformPoint(ray.GetPoint(distance));
            if(!rect.rect.Contains(new Vector2(local.x,local.y)))continue;
            target=button;nearest=distance;
        }
        rayLine.SetPosition(0,ray.origin);rayLine.SetPosition(1,ray.GetPoint(nearest));
        if(hover!=target)
        {
            if(hover)ExecuteEvents.Execute(hover.gameObject,pointer,ExecuteEvents.pointerExitHandler);
            hover=target;
            if(hover){ExecuteEvents.Execute(hover.gameObject,pointer,ExecuteEvents.pointerEnterHandler);rig.Haptic(0.08f,0.015f);}
        }
        if(rig.UiTriggerDown&&hover){pressed=hover;ExecuteEvents.Execute(pressed.gameObject,pointer,ExecuteEvents.pointerDownHandler);}
        if(rig.UiTriggerUp&&pressed)
        {
            var clicked=pressed;pressed=null;ExecuteEvents.Execute(clicked.gameObject,pointer,ExecuteEvents.pointerUpHandler);
            if(clicked==hover&&clicked.IsInteractable())ExecuteEvents.Execute(clicked.gameObject,pointer,ExecuteEvents.pointerClickHandler);
        }
    }
    private void CancelPointer()
    {
        if(pointer==null)return;
        if(pressed)ExecuteEvents.Execute(pressed.gameObject,pointer,ExecuteEvents.pointerUpHandler);
        if(hover)ExecuteEvents.Execute(hover.gameObject,pointer,ExecuteEvents.pointerExitHandler);
        pressed=null;hover=null;
    }
    private void SetLayer(GameObject go){foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=UiLayer;}
    private Canvas MakeCanvas(string name,Transform parent,Vector2 size,float scale)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(Image));go.layer=UiLayer;go.transform.SetParent(parent,false);
        var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=rig.Camera;
        var rect=(RectTransform)go.transform;rect.sizeDelta=size;rect.localScale=Vector3.one*scale;
        var image=go.GetComponent<Image>();image.color=new Color(0.035f,0.03f,0.015f,0.94f);image.raycastTarget=false;return canvas;
    }
    private Text Label(Transform parent,string content,Vector2 size,int fontSize,Vector2 position)
    {
        var go=new GameObject(content,typeof(RectTransform),typeof(Text));go.layer=UiLayer;go.transform.SetParent(parent,false);
        var text=go.GetComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=fontSize;
        text.text=content;text.color=new Color(0.95f,0.87f,0.57f);text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;
        var rect=(RectTransform)go.transform;rect.sizeDelta=size;rect.anchoredPosition=position;return text;
    }
    private void AddButton(Transform parent,string name,Vector2 pos,UnityEngine.Events.UnityAction action)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));go.layer=UiLayer;go.transform.SetParent(parent,false);
        var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(600,90);rect.anchoredPosition=pos;
        go.GetComponent<Image>().color=new Color(0.22f,0.18f,0.08f);
        go.GetComponent<Button>().onClick.AddListener(action);Label(go.transform,name,new Vector2(580,80),36,Vector2.zero);
    }
    private void OnDestroy()
    {
        if(rayMaterial)Destroy(rayMaterial);
        if(HudCanvas)Destroy(HudCanvas.gameObject);
    }
}

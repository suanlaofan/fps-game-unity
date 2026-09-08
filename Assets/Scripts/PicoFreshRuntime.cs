using UnityEngine;
using UnityEngine.XR;

// Input/pose adapter only. Movement, gun and UI own their separate behaviours.
[DefaultExecutionOrder(-1200)]
public sealed class PicoFreshRuntime : MonoBehaviour
{
    public static bool IsPicoXrActive { get; private set; }
    public static bool FirePressed { get; private set; }
    public static bool FirePressedThisFrame { get; private set; }
    public static bool ReloadPressedThisFrame { get; private set; }
    public static bool MenuPressedThisFrame { get; private set; }
    public static Vector2 LocomotionInput { get; private set; }
    public static float LocomotionMagnitude => LocomotionInput.magnitude;
    public static bool LocomotionRunning { get; private set; }
    public static PicoFreshRuntime Instance { get; private set; }
    [SerializeField] private Camera xrCamera;
    [SerializeField] private BoxCollider groundSafety;
    [SerializeField] private float moveSpeed = 2f;
    public bool editorPreview = true;
    public Transform LeftHand { get; private set; }
    public Transform RightHand { get; private set; }
    public Camera Camera => xrCamera;
    public bool RightTracked { get; private set; }
    public bool LeftTracked { get; private set; }
    public bool UiTrigger { get; private set; }
    public bool UiTriggerDown { get; private set; }
    public bool UiTriggerUp { get; private set; }
    public bool SprintActive => LocomotionRunning;
    public bool SprintLatched { get; private set; }
    public string LastReloadInput { get; private set; } = "none";
    public int ReloadInputCount { get; private set; }
    public string InputSource => Application.isEditor ? "editor-keyboard+xr" : "native-xr";
    public bool Gameplay => flow != null && flow.CurrentState == Level0GameFlow.GameState.Playing && !paused;
    public bool Paused => paused;
    public Level0VrMotor Motor { get; private set; }
    public Level0VrWeapon Weapon { get; private set; }
    public Level0WorldUi WorldUi { get; private set; }
    private Level0GameFlow flow;
    private InputDevice leftDevice, rightDevice;
    private bool lastTrigger, lastMenu, armed, lastPlaying, paused, snapLatched, uiArmed;
    private bool lastReloadSecondary, lastReloadGrip, lastReloadKeyboard, lastSprintClick;
    private bool reloadSecondaryArmed, reloadGripArmed, reloadKeyboardArmed, sprintClickArmed, sprintHoldArmed;
    private InputDevice loggedRightDevice, loggedLeftDevice;
    private bool rightDeviceLogged, leftDeviceLogged;
    private Transform trackingSpace;

    public void Configure(Camera camera, BoxCollider safety) { xrCamera = camera; groundSafety = safety; }
    private void Awake()
    {
        if (Application.platform != RuntimePlatform.Android && !(Application.isEditor && editorPreview)) return;
        Instance = this; IsPicoXrActive = true;
        foreach (var legacy in FindObjectsByType<movimentacaoPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) legacy.enabled = false;
        flow = FindFirstObjectByType<Level0GameFlow>(FindObjectsInactive.Include);
        if (xrCamera == null) xrCamera = GetComponentInChildren<Camera>(true);
        trackingSpace = xrCamera.transform.parent;
        QualitySettings.shadows = ShadowQuality.Disable; QualitySettings.pixelLightCount = 1;
        QualitySettings.antiAliasing = 2; QualitySettings.softParticles = false;
        QualitySettings.realtimeReflectionProbes = false;
        var picoManager = GetComponent<ByteDance.PICO.XR.PXR_Manager>();
        if (picoManager) picoManager.useRecommendedAntiAliasingLevel = false;
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None)) listener.enabled = listener.gameObject == xrCamera.gameObject;
        foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            c.enabled = c == xrCamera; c.tag = c == xrCamera ? "MainCamera" : "Untagged";
            var listener = c.GetComponent<AudioListener>(); if (listener) listener.enabled = c == xrCamera;
        }
        if (Application.isEditor)
        {
            foreach (var b in xrCamera.GetComponents<Behaviour>())
                if (b.GetType().Name == "TrackedPoseDriver") b.enabled = false;
            xrCamera.transform.localPosition = new Vector3(0, 1.65f, 0);
        }
        LeftHand = CreateHand("Left Controller"); RightHand = CreateHand("Right Controller");
        var health = FindFirstObjectByType<PlayerHealth>(FindObjectsInactive.Include);
        if (health == null) { Debug.LogError("LEVEL0_VR_PLAYER_MISSING"); enabled = false; return; }
        Motor = health.GetComponent<Level0VrMotor>(); if (!Motor) Motor = health.gameObject.AddComponent<Level0VrMotor>();
        Motor.Configure(this, transform, health, groundSafety);
        var gun = FindFirstObjectByType<AutomaticGunScriptLPFP>(FindObjectsInactive.Include);
        if (gun != null)
        {
            Weapon = gun.GetComponent<Level0VrWeapon>(); if (!Weapon) Weapon = gun.gameObject.AddComponent<Level0VrWeapon>();
            Weapon.Configure(this, gun);
        }
        foreach (var hud in FindObjectsByType<PicoFreshOriginalHud>(FindObjectsInactive.Include, FindObjectsSortMode.None)) hud.enabled = false;
        foreach (var pointer in FindObjectsByType<PicoFreshUiPointer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) pointer.enabled = false;
        WorldUi = gameObject.GetComponent<Level0WorldUi>(); if (!WorldUi) WorldUi = gameObject.AddComponent<Level0WorldUi>();
        WorldUi.Configure(this, flow);
        gameObject.AddComponent<Level0PerformanceTelemetry>();
        Debug.Log("LEVEL0_VR_READY locomotion=capsule gun=right-controller ui=world-space " +
            "reload=right-B-or-grip sprint=left-stick-click-toggle-or-X-hold input=" + InputSource);
    }
    private Transform CreateHand(string name)
    {
        var t = trackingSpace.Find(name);
        if (!t) { t = new GameObject(name).transform; t.SetParent(trackingSpace, false); }
        return t;
    }
    private void OnEnable() { Application.onBeforeRender += UpdateHandPoses; }
    private void OnDisable() { Application.onBeforeRender -= UpdateHandPoses; }
    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null; IsPicoXrActive = false; FirePressed = FirePressedThisFrame = false;
        ReloadPressedThisFrame = MenuPressedThisFrame = LocomotionRunning = false;
        LocomotionInput = Vector2.zero; Time.timeScale = 1f;
    }
    private bool Pose(ref InputDevice device, XRNode node, Transform target)
    {
        if (!device.isValid) device = InputDevices.GetDeviceAtXRNode(node);
        bool valid = device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        if (valid && device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos) &&
            device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
        { target.localPosition = pos; target.localRotation = rot; return true; }
        return false;
    }
    private void UpdateHandPoses()
    {
        if (Instance != this || LeftHand == null) return;
        if (Application.isEditor)
        {
            LeftTracked = RightTracked = true;
            return; // Editor tests move these nodes independently of the head.
        }
        LeftTracked = Pose(ref leftDevice, XRNode.LeftHand, LeftHand);
        RightTracked = Pose(ref rightDevice, XRNode.RightHand, RightHand);
    }
    private void Start()
    {
        if (Instance != this) return;
        if (Application.isEditor)
        {
            LeftHand.localPosition = new Vector3(-0.25f, 1.2f, 0.35f);
            RightHand.localPosition = new Vector3(0.25f, 1.3f, 0.4f);
        }
    }
    private void Update()
    {
        if (Instance != this) return;
        UpdateHandPoses();
        bool trigger = false, reloadSecondary = false, reloadGrip = false, reloadKeyboard = false;
        bool menu = false, runHeld = false, sprintClick = false;
        Vector2 stick = Vector2.zero, turn = Vector2.zero;
        if (RightTracked)
        {
            rightDevice.TryGetFeatureValue(CommonUsages.trigger, out float value);
            rightDevice.TryGetFeatureValue(CommonUsages.triggerButton, out trigger);
            trigger |= value >= 0.55f;
            bool hasSecondary = rightDevice.TryGetFeatureValue(CommonUsages.secondaryButton, out reloadSecondary);
            bool hasGripButton = rightDevice.TryGetFeatureValue(CommonUsages.gripButton, out reloadGrip);
            bool hasGrip = rightDevice.TryGetFeatureValue(CommonUsages.grip, out float grip);
            reloadGrip |= grip >= (lastReloadGrip ? 0.35f : 0.7f);
            rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out menu);
            rightDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out turn);
            if (rightDevice.isValid && (!rightDeviceLogged || !rightDevice.Equals(loggedRightDevice)))
            {
                loggedRightDevice = rightDevice; rightDeviceLogged = true;
                Debug.Log("LEVEL0_INPUT_RIGHT device=" + rightDevice.name + " secondary=" + hasSecondary +
                    " gripButton=" + hasGripButton + " gripAxis=" + hasGrip);
            }
        }
        if (LeftTracked)
        {
            leftDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick);
            leftDevice.TryGetFeatureValue(CommonUsages.primaryButton, out runHeld);
            bool hasClick = leftDevice.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out sprintClick);
            if (leftDevice.isValid && (!leftDeviceLogged || !leftDevice.Equals(loggedLeftDevice)))
            {
                loggedLeftDevice = leftDevice; leftDeviceLogged = true;
                Debug.Log("LEVEL0_INPUT_LEFT device=" + leftDevice.name + " sprintStickClick=" + hasClick);
            }
        }
        if (Application.isEditor)
        {
            var keys = UnityEngine.InputSystem.Keyboard.current;
            if (keys != null)
            {
                trigger |= keys.uKey.isPressed; reloadKeyboard = keys.rKey.isPressed || keys.oKey.isPressed;
                menu |= keys.escapeKey.isPressed;
                stick = new Vector2((keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0),
                    (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0));
                runHeld |= keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed || keys.xKey.isPressed;
                sprintClick |= keys.leftCtrlKey.isPressed;
                if (keys.qKey.wasPressedThisFrame) turn.x = -1;
                if (keys.eKey.wasPressedThisFrame) turn.x = 1;
            }
        }
        bool playing = Gameplay;
        if (playing != lastPlaying)
        {
            ResetSprint(); ResetReloadArming();
        }
        if (!LeftTracked) ResetSprint();
        if (!RightTracked) ResetReloadArming();
        // Held controls must be released after a pause/restart or tracking recovery.
        if (LeftTracked && !sprintClick) sprintClickArmed = true;
        if (LeftTracked && !runHeld) sprintHoldArmed = true;
        if (playing && LeftTracked && sprintClickArmed && sprintClick && !lastSprintClick)
        {
            SprintLatched = !SprintLatched;
            Debug.Log("LEVEL0_INPUT_SPRINT latched=" + SprintLatched +
                " source=" + (Application.isEditor ? "editor-Ctrl" : "left-stick-click"));
        }
        if (RightTracked)
        {
            if (!reloadSecondary) reloadSecondaryArmed = true;
            if (!reloadGrip) reloadGripArmed = true;
            if (!reloadKeyboard) reloadKeyboardArmed = true;
        }
        // Track B and grip separately: holding the grip must never swallow a later B press.
        bool secondaryEdge = reloadSecondaryArmed && reloadSecondary && !lastReloadSecondary;
        bool gripEdge = reloadGripArmed && reloadGrip && !lastReloadGrip;
        bool keyboardEdge = reloadKeyboardArmed && reloadKeyboard && !lastReloadKeyboard;
        if (!RightTracked) uiArmed = false;
        if (RightTracked && !trigger) uiArmed = true;
        UiTriggerDown = uiArmed && trigger && !lastTrigger; UiTriggerUp = uiArmed && !trigger && lastTrigger; UiTrigger = uiArmed && trigger;
        // Every mode transition and tracking loss require a fresh release.
        if (Gameplay != lastPlaying || !RightTracked) armed = false;
        if (!trigger && RightTracked) armed = true;
        FirePressed = Gameplay && RightTracked && armed && trigger && !Motor.HeadBlocked;
        FirePressedThisFrame = FirePressed && !lastTrigger;
        ReloadPressedThisFrame = playing && RightTracked && (secondaryEdge || gripEdge || keyboardEdge);
        if (ReloadPressedThisFrame)
        {
            LastReloadInput = secondaryEdge ? "right-B" : gripEdge ? "right-grip" : "editor-R/O";
            ReloadInputCount++;
            Debug.Log("LEVEL0_INPUT_RELOAD source=" + LastReloadInput + " count=" + ReloadInputCount +
                " ammo=" + (Weapon ? Weapon.Ammo : -1));
        }
        MenuPressedThisFrame = false;
        if (menu && !lastMenu)
        {
            if (flow != null && flow.CurrentState == Level0GameFlow.GameState.Playing) SetPaused(!paused);
            else if (WorldUi) WorldUi.RecenterMenu();
        }
        LocomotionInput = Gameplay ? Vector2.ClampMagnitude(stick, 1f) : Vector2.zero;
        if (LocomotionInput.magnitude < 0.15f) LocomotionInput = Vector2.zero;
        LocomotionRunning = Gameplay && LeftTracked && (SprintLatched || (sprintHoldArmed && runHeld)) && LocomotionMagnitude > 0.1f;
        if (Gameplay && !snapLatched && Mathf.Abs(turn.x) > 0.7f) Motor.SnapTurn(Mathf.Sign(turn.x) * 30f);
        if (Mathf.Abs(turn.x) < 0.3f) snapLatched = false; else if (Mathf.Abs(turn.x) > 0.7f) snapLatched = true;
        lastTrigger = trigger; lastMenu = menu; lastPlaying = Gameplay;
        lastReloadSecondary = reloadSecondary; lastReloadGrip = reloadGrip; lastReloadKeyboard = reloadKeyboard;
        lastSprintClick = sprintClick;
    }
    private void ResetSprint()
    {
        SprintLatched = false; LocomotionRunning = false; sprintClickArmed = sprintHoldArmed = false;
    }
    private void ResetReloadArming()
    {
        reloadSecondaryArmed = reloadGripArmed = reloadKeyboardArmed = false;
    }
    public void SetPaused(bool value)
    {
        paused = value; Time.timeScale = value ? 0 : 1;
        armed = false; FirePressed = FirePressedThisFrame = false;
        ReloadPressedThisFrame = false; LocomotionInput = Vector2.zero;
        ResetSprint(); ResetReloadArming();
        if (WorldUi) WorldUi.RefreshState();
    }
    public static void SetViewModelVisible(bool visible)
    {
        if (Instance != null && Instance.Weapon != null) Instance.Weapon.SetVisible(visible);
    }
    public void Haptic(float amplitude, float duration)
    {
        if (rightDevice.isValid && rightDevice.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
            rightDevice.SendHapticImpulse(0, amplitude, duration);
    }
}

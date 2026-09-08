using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// PICO HUD pointer whose ray always follows the XR camera. The right
/// controller trigger supplies press/release input while pointer enter/exit
/// keeps UGUI button preview states working in XR.
/// </summary>
[DefaultExecutionOrder(-1180)]
[DisallowMultipleComponent]
public sealed class PicoFreshUiPointer : MonoBehaviour
{
    private const int PointerId = -9081;
    private const float MaxRayDistance = 8f;
    private const float RayOriginOffset = 0.02f;
    private const float ClickInterval = 0.3f;

    [SerializeField] private Camera xrCamera;
    [SerializeField] private Canvas uiCanvas;

    private EventSystem eventSystem;
    private GraphicRaycaster graphicRaycaster;
    private PointerEventData pointerData;
    private readonly List<RaycastResult> raycastResults = new List<RaycastResult>(16);
    private InputDevice rightHand;
    private GameObject hoveredObject;
    private GameObject pressedObject;
    private RaycastResult currentRaycast;
    private Vector2 previousScreenPosition;
    private bool previousTrigger;
    private float lastClickTime = -10f;
    private int clickCount;
    private LineRenderer rayLine;
    private Material rayMaterial;
    private bool readyLogged;
    private string lastRaySource;
    private float nextDiagnosticTime;
    private float nextButtonScanTime;
    private Button[] interactiveButtons;
    private bool manualRaycastLogged;
    private bool autoTargetLogged;
    private bool allowCameraAutoTarget;

    public void Configure(Camera camera, Canvas canvas)
    {
        xrCamera = camera;
        uiCanvas = canvas;
        TryInitialize();
    }

    private void Start()
    {
        TryInitialize();
    }

    private void Update()
    {
        if (!TryInitialize() || xrCamera == null || uiCanvas == null)
        {
            HideRay();
            ClearHover();
            return;
        }

        if (!TryGetPointerRay(out Ray ray, out string source))
        {
            HideRay();
            ClearHover();
            return;
        }

        Vector2 screenPosition;
        Vector3 rayEnd;
        bool hasScreenPosition = TryGetScreenPosition(ray, out screenPosition, out rayEnd);

        // The emulator can expose a tracked controller pose before it is
        // aimed at the head-locked menu. Keep the primary ray camera-origin
        // and controller-directed, then use a camera-origin ray to the
        // nearest visible button when that pose misses the UI.
        allowCameraAutoTarget = source == "xr-camera";
        if (!hasScreenPosition && allowCameraAutoTarget && TryGetCameraAutoTarget(out Ray autoRay, out RaycastResult autoHit))
        {
            ray = autoRay;
            screenPosition = autoHit.screenPosition;
            rayEnd = autoHit.worldPosition;
            hasScreenPosition = true;
        }

        UpdateRaySourceLog(source);

        if (!hasScreenPosition || (screenPosition == default && rayEnd == default))
        {
            if (Time.unscaledTime >= nextDiagnosticTime)
            {
                nextDiagnosticTime = Time.unscaledTime + 1f;
                Debug.Log("[PICO-FRESH] PICO_FRESH_UI_RAY_MISS origin=" + ray.origin.ToString("F3") +
                          " direction=" + ray.direction.ToString("F3") +
                          " cameraPos=" + xrCamera.transform.position.ToString("F3") +
                          " cameraForward=" + xrCamera.transform.forward.ToString("F3") +
                          " canvasPos=" + uiCanvas.transform.position.ToString("F3") +
                          " canvasForward=" + uiCanvas.transform.forward.ToString("F3") +
                          " planeDistance=" + uiCanvas.planeDistance.ToString("F3") + ".", this);
            }
            rayEnd = ray.origin + ray.direction * MaxRayDistance;
            HideRay(ray.origin, rayEnd);
            ClearHover();
            return;
        }

        UpdateRayVisual(ray.origin, rayEnd);
        UpdatePointer(ray, screenPosition);

        bool trigger = ReadTrigger();
        if (trigger && !previousTrigger)
        {
            PressHoveredObject();
        }
        else if (!trigger && previousTrigger)
        {
            ReleasePressedObject();
        }
        previousTrigger = trigger;
    }

    private bool TryInitialize()
    {
        if (xrCamera == null)
        {
            xrCamera = GetComponentInChildren<Camera>(true);
        }

        if (uiCanvas == null)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Canvas canvas in canvases)
            {
                if (canvas != null && canvas.GetComponent<Level0GameFlow>() != null)
                {
                    uiCanvas = canvas;
                    break;
                }
            }
        }

        if (eventSystem == null)
        {
            eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystem = FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            }
        }

        if (uiCanvas != null && graphicRaycaster == null)
        {
            graphicRaycaster = uiCanvas.GetComponent<GraphicRaycaster>();
        }

        if (eventSystem == null || graphicRaycaster == null)
        {
            return false;
        }

        // Screen-space camera canvases can be reported as back-facing while
        // the XR runtime is still updating the per-eye projection. The UI ray
        // remains valid in that state, so do not discard it on winding alone.
        graphicRaycaster.ignoreReversedGraphics = false;

        DisableDesktopInputModules();
        if (pointerData == null)
        {
            pointerData = new PointerEventData(eventSystem)
            {
                pointerId = PointerId,
                button = PointerEventData.InputButton.Left,
                useDragThreshold = true
            };
        }

        if (rayLine == null)
        {
            CreateRayVisual();
        }

        if (!readyLogged)
        {
            readyLogged = true;
            Debug.Log("[PICO-FRESH] PICO_FRESH_UI_POINTER_READY eventSystem=true graphicRaycaster=true " +
                      "source=xr-camera trigger=right-index-trigger rayOriginDistance=" +
                      RayOriginOffset.ToString("F2") + " hudDistance=0.90.", this);
        }
        return true;
    }

    private void DisableDesktopInputModules()
    {
        BaseInputModule[] modules = eventSystem.GetComponents<BaseInputModule>();
        foreach (BaseInputModule module in modules)
        {
            if (module != null && module.enabled)
            {
                module.enabled = false;
            }
        }
    }

    private bool TryGetPointerRay(out Ray ray, out string source)
    {
        if (xrCamera != null)
        {
            Vector3 cameraForward = xrCamera.transform.forward.normalized;
            Vector3 direction = cameraForward;
            source = "xr-camera";

            // Keep the origin on the XR camera as required by the PICO HUD
            // layout, while allowing the tracked right controller to aim at
            // a button that is not centered in the head-locked artwork.
            if (rightHand.isValid &&
                rightHand.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked &&
                rightHand.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion controllerRotation))
            {
                Vector3 controllerForward = (controllerRotation * Vector3.forward).normalized;
                if (controllerForward.sqrMagnitude > 0.0001f &&
                    Vector3.Dot(controllerForward, cameraForward) > -0.25f)
                {
                    direction = controllerForward;
                    source = "xr-camera+right-controller";
                }
            }

            if (direction.sqrMagnitude > 0.0001f)
            {
                // Start just in front of the tracked camera. The original HUD
                // is at 0.90 m, so the pointer origin is always nearer than the
                // surface it is targeting.
                Vector3 origin = xrCamera.transform.position + direction.normalized * RayOriginOffset;
                ray = new Ray(origin, direction.normalized);
                return true;
            }
        }

        ray = default;
        source = "none";
        return false;
    }

    private bool TryGetScreenPosition(Ray ray, out Vector2 screenPosition, out Vector3 worldHit)
    {
        Camera eventCamera = uiCanvas.worldCamera != null ? uiCanvas.worldCamera : xrCamera;
        Vector3 cameraPosition = eventCamera.transform.position;
        Vector3 cameraForward = eventCamera.transform.forward;
        Vector3 planePoint = uiCanvas.transform.position;
        Vector3 planeNormal = uiCanvas.transform.forward;

        // Unity may leave a formerly-overlay Canvas at the origin for one or
        // more frames after switching it to ScreenSpaceCamera. In that case
        // use the documented planeDistance relative to the XR camera.
        float cameraPlaneDistance = Mathf.Abs(Vector3.Dot(planePoint - cameraPosition, cameraForward));
        if (cameraPlaneDistance < 0.01f || cameraPlaneDistance > MaxRayDistance)
        {
            planePoint = cameraPosition + cameraForward * Mathf.Max(0.05f, uiCanvas.planeDistance);
            planeNormal = cameraForward;
        }

        Plane canvasPlane = new Plane(planeNormal, planePoint);
        if (!canvasPlane.Raycast(ray, out float distance) || distance < 0f || distance > MaxRayDistance)
        {
            screenPosition = default;
            worldHit = default;
            return false;
        }

        worldHit = ray.GetPoint(distance);
        Vector3 projected = eventCamera.WorldToScreenPoint(worldHit);
        if (projected.z <= 0f || float.IsNaN(projected.x) || float.IsNaN(projected.y))
        {
            screenPosition = default;
            return false;
        }

        screenPosition = new Vector2(projected.x, projected.y);
        return true;
    }

    private void UpdatePointer(Ray ray, Vector2 screenPosition)
    {
        pointerData.delta = screenPosition - previousScreenPosition;
        pointerData.position = screenPosition;
        pointerData.scrollDelta = Vector2.zero;
        pointerData.button = PointerEventData.InputButton.Left;
        pointerData.pointerCurrentRaycast = default;
        raycastResults.Clear();
        graphicRaycaster.Raycast(pointerData, raycastResults);

        if (Time.unscaledTime >= nextDiagnosticTime)
        {
            nextDiagnosticTime = Time.unscaledTime + 1f;
            string hit = raycastResults.Count == 0 ? "<none>" : raycastResults[0].gameObject.name;
            string button = DescribeFirstActiveButton();
            Debug.Log("[PICO-FRESH] PICO_FRESH_UI_RAY_DIAGNOSTIC screen=" + screenPosition.ToString("F1") +
                      " raycasts=" + raycastResults.Count + " first='" + hit + "' canvasRender=" + uiCanvas.renderMode +
                      " canvasPos=" + uiCanvas.transform.position.ToString("F3") +
                      " canvasForward=" + uiCanvas.transform.forward.ToString("F3") +
                      " rayOrigin=" + ray.origin.ToString("F3") +
                      " rayDirection=" + ray.direction.ToString("F3") +
                      " activeButton=" + button +
                      " cameraPixel=" + xrCamera.pixelWidth + "x" + xrCamera.pixelHeight + ".", this);
        }

        currentRaycast = FindInteractiveRaycast(raycastResults);
        if (currentRaycast.gameObject == null && TryManualButtonRaycast(ray, out RaycastResult manualRaycast))
        {
            currentRaycast = manualRaycast;
            if (!manualRaycastLogged)
            {
                manualRaycastLogged = true;
                Debug.Log("[PICO-FRESH] PICO_FRESH_UI_MANUAL_RAYCAST_READY canvas='" +
                          uiCanvas.name + "' source=xr-camera-world-rect.", this);
            }
        }
        if (currentRaycast.gameObject == null && allowCameraAutoTarget &&
            TryGetCameraAutoTarget(out Ray autoRay, out RaycastResult autoHit))
        {
            currentRaycast = autoHit;
            pointerData.position = autoHit.screenPosition;
            pointerData.delta = pointerData.position - previousScreenPosition;
            previousScreenPosition = pointerData.position;
            // Make the visible ray agree with the effective hit target.
            UpdateRayVisual(autoRay.origin, autoHit.worldPosition);
            if (!autoTargetLogged)
            {
                autoTargetLogged = true;
                Debug.Log("[PICO-FRESH] PICO_FRESH_UI_AUTO_TARGET_READY target='" +
                          autoHit.gameObject.name + "' source=xr-camera origin=" +
                          autoRay.origin.ToString("F3") + " distance=" +
                          autoHit.distance.ToString("F3") + ".", this);
            }
        }
        pointerData.pointerCurrentRaycast = currentRaycast;
        GameObject nextObject = currentRaycast.gameObject;
        if (nextObject != hoveredObject)
        {
            if (hoveredObject != null)
            {
                ExecuteEvents.Execute(hoveredObject, pointerData, ExecuteEvents.pointerExitHandler);
                Debug.Log("[PICO-FRESH] PICO_FRESH_UI_HOVER_EXIT target='" + hoveredObject.name + "'.", this);
            }

            hoveredObject = nextObject;
            pointerData.pointerEnter = hoveredObject;
            if (hoveredObject != null)
            {
                ExecuteEvents.Execute(hoveredObject, pointerData, ExecuteEvents.pointerEnterHandler);
                eventSystem.SetSelectedGameObject(hoveredObject);
                Debug.Log("[PICO-FRESH] PICO_FRESH_UI_HOVER_ENTER target='" + hoveredObject.name + "'.", this);
            }
        }
        else
        {
            pointerData.pointerEnter = hoveredObject;
        }

        previousScreenPosition = screenPosition;
    }

    private string DescribeFirstActiveButton()
    {
        if (interactiveButtons == null)
        {
            interactiveButtons = uiCanvas != null ? uiCanvas.GetComponentsInChildren<Button>(true) : null;
        }

        if (interactiveButtons == null)
        {
            return "<none>";
        }

        foreach (Button button in interactiveButtons)
        {
            if (button == null || !button.isActiveAndEnabled || !button.interactable ||
                !button.gameObject.activeInHierarchy)
            {
                continue;
            }

            RectTransform rect = button.transform as RectTransform;
            return rect == null ? button.name : button.name + "@" + rect.position.ToString("F3") +
                   " size=" + rect.rect.size.ToString("F1");
        }

        return "<none>";
    }

    /// <summary>
    /// PICO's per-eye screen projection can leave ScreenSpaceCamera UGUI with
    /// an empty GraphicRaycaster result even though the ray intersects the
    /// button geometry. Resolve the active button rectangles in world space as
    /// a deterministic fallback; the ray still starts at the XR camera.
    /// </summary>
    private bool TryManualButtonRaycast(Ray ray, out RaycastResult hit)
    {
        hit = default;
        if (uiCanvas == null)
        {
            return false;
        }

        if (interactiveButtons == null || Time.unscaledTime >= nextButtonScanTime)
        {
            nextButtonScanTime = Time.unscaledTime + 0.25f;
            interactiveButtons = uiCanvas.GetComponentsInChildren<Button>(true);
        }

        float closestDistance = float.MaxValue;
        Button closestButton = null;
        Vector3 closestPoint = default;
        foreach (Button button in interactiveButtons)
        {
            if (button == null || !button.isActiveAndEnabled || !button.interactable ||
                !button.gameObject.activeInHierarchy)
            {
                continue;
            }

            RectTransform rect = button.transform as RectTransform;
            if (rect == null)
            {
                continue;
            }

            Plane buttonPlane = new Plane(rect.forward, rect.position);
            if (!buttonPlane.Raycast(ray, out float distance) || distance < 0f ||
                distance > MaxRayDistance || distance >= closestDistance)
            {
                continue;
            }

            Vector3 worldPoint = ray.GetPoint(distance);
            Vector3 localPoint = rect.InverseTransformPoint(worldPoint);
            if (!rect.rect.Contains(new Vector2(localPoint.x, localPoint.y)))
            {
                continue;
            }

            closestDistance = distance;
            closestButton = button;
            closestPoint = worldPoint;
        }

        if (closestButton == null)
        {
            return false;
        }

        hit = new RaycastResult
        {
            gameObject = closestButton.gameObject,
            module = graphicRaycaster,
            distance = closestDistance,
            worldPosition = closestPoint,
            worldNormal = (closestButton.transform as RectTransform).forward,
            screenPosition = pointerData.position
        };
        return true;
    }

    /// <summary>
    /// A deterministic camera-origin fallback for menu/result buttons. The
    /// closest visible button to the HMD forward direction is selected and a
    /// ray is aimed at its world rectangle. This is used only after the
    /// controller ray and the normal GraphicRaycaster both miss.
    /// </summary>
    private bool TryGetCameraAutoTarget(out Ray ray, out RaycastResult hit)
    {
        ray = default;
        hit = default;
        if (xrCamera == null || uiCanvas == null)
        {
            return false;
        }

        if (interactiveButtons == null || Time.unscaledTime >= nextButtonScanTime)
        {
            nextButtonScanTime = Time.unscaledTime + 0.25f;
            interactiveButtons = uiCanvas.GetComponentsInChildren<Button>(true);
        }

        float bestAngle = 180f;
        float bestDistance = float.MaxValue;
        Button bestButton = null;
        Vector3 bestPoint = default;
        Ray bestRay = default;
        Vector3 cameraPosition = xrCamera.transform.position;
        Vector3 cameraForward = xrCamera.transform.forward.normalized;

        foreach (Button button in interactiveButtons)
        {
            if (button == null || !button.isActiveAndEnabled || !button.interactable ||
                !button.gameObject.activeInHierarchy)
            {
                continue;
            }

            RectTransform rect = button.transform as RectTransform;
            if (rect == null)
            {
                continue;
            }

            Vector3 toButton = rect.position - cameraPosition;
            float distance = toButton.magnitude;
            if (distance < 0.05f || distance > MaxRayDistance)
            {
                continue;
            }

            Vector3 direction = toButton / distance;
            if (Vector3.Dot(cameraForward, direction) <= 0f)
            {
                continue;
            }

            float angle = Vector3.Angle(cameraForward, direction);
            // Prefer the first active button (the primary menu/result action)
            // when the controller is unavailable. This fallback is only for
            // keyboard-style emulator navigation; tracked-controller rays use
            // their actual direction and never auto-target.
            if (bestButton != null && (angle > bestAngle ||
                (Mathf.Abs(angle - bestAngle) < 0.01f && distance >= bestDistance)))
            {
                continue;
            }

            Ray candidateRay = new Ray(cameraPosition + direction * RayOriginOffset, direction);
            Plane buttonPlane = new Plane(rect.forward, rect.position);
            if (!buttonPlane.Raycast(candidateRay, out float hitDistance) || hitDistance < 0f ||
                hitDistance > MaxRayDistance)
            {
                continue;
            }

            Vector3 point = candidateRay.GetPoint(hitDistance);
            Vector3 localPoint = rect.InverseTransformPoint(point);
            if (!rect.rect.Contains(new Vector2(localPoint.x, localPoint.y)))
            {
                continue;
            }

            bestAngle = angle;
            bestDistance = distance;
            bestButton = button;
            bestPoint = point;
            bestRay = candidateRay;
        }

        if (bestButton == null)
        {
            return false;
        }

        Vector3 projected = xrCamera.WorldToScreenPoint(bestPoint);
        if (projected.z <= 0f || float.IsNaN(projected.x) || float.IsNaN(projected.y))
        {
            return false;
        }

        ray = bestRay;
        hit = new RaycastResult
        {
            gameObject = bestButton.gameObject,
            module = graphicRaycaster,
            distance = Vector3.Distance(bestRay.origin, bestPoint),
            worldPosition = bestPoint,
            worldNormal = (bestButton.transform as RectTransform).forward,
            screenPosition = new Vector2(projected.x, projected.y)
        };
        return true;
    }

    private static RaycastResult FindInteractiveRaycast(List<RaycastResult> results)
    {
        foreach (RaycastResult result in results)
        {
            if (result.gameObject == null)
            {
                continue;
            }

            GameObject clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(result.gameObject);
            GameObject enterHandler = ExecuteEvents.GetEventHandler<IPointerEnterHandler>(result.gameObject);
            GameObject handler = clickHandler != null ? clickHandler : enterHandler;
            if (handler != null)
            {
                // RaycastResult is a struct; foreach iteration variables are
                // read-only, so copy before retargeting the event handler.
                RaycastResult interactive = result;
                interactive.gameObject = handler;
                return interactive;
            }
        }

        return default;
    }

    private bool ReadTrigger()
    {
        bool trigger = false;
        if (!rightHand.isValid)
        {
            rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        }

        if (rightHand.isValid)
        {
            rightHand.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerButton);
            rightHand.TryGetFeatureValue(CommonUsages.trigger, out float triggerValue);
            trigger = triggerButton || triggerValue >= 0.55f;
        }

        // U remains the documented emulator trigger fallback used by gameplay.
        return trigger || Input.GetKey(KeyCode.U);
    }

    private void PressHoveredObject()
    {
        if (hoveredObject == null)
        {
            return;
        }

        GameObject pressHandler = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hoveredObject) ?? hoveredObject;
        pointerData.pressPosition = pointerData.position;
        pointerData.pointerPressRaycast = currentRaycast;
        pointerData.rawPointerPress = hoveredObject;
        pointerData.pointerPress = pressHandler;
        float now = Time.unscaledTime;
        clickCount = now - lastClickTime <= ClickInterval ? clickCount + 1 : 1;
        lastClickTime = now;
        pointerData.clickCount = clickCount;
        pointerData.clickTime = now;
        ExecuteEvents.Execute(pressHandler, pointerData, ExecuteEvents.pointerDownHandler);
        pressedObject = pressHandler;
        Debug.Log("[PICO-FRESH] PICO_FRESH_UI_PRESS target='" + pressHandler.name + "'.", this);
    }

    private void ReleasePressedObject()
    {
        if (pressedObject == null)
        {
            return;
        }

        ExecuteEvents.Execute(pressedObject, pointerData, ExecuteEvents.pointerUpHandler);
        GameObject clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hoveredObject);
        if (clickHandler == pressedObject)
        {
            ExecuteEvents.Execute(pressedObject, pointerData, ExecuteEvents.pointerClickHandler);
            Debug.Log("[PICO-FRESH] PICO_FRESH_UI_CLICK target='" + pressedObject.name + "'.", this);
        }

        pressedObject = null;
        pointerData.pointerPress = null;
        pointerData.rawPointerPress = null;
    }

    private void ClearHover()
    {
        if (hoveredObject != null && pointerData != null)
        {
            ExecuteEvents.Execute(hoveredObject, pointerData, ExecuteEvents.pointerExitHandler);
            Debug.Log("[PICO-FRESH] PICO_FRESH_UI_HOVER_EXIT target='" + hoveredObject.name + "'.", this);
        }

        hoveredObject = null;
        currentRaycast = default;
        if (pointerData != null)
        {
            pointerData.pointerEnter = null;
        }
    }

    private void CreateRayVisual()
    {
        GameObject rayObject = new GameObject("PICO XR UI Ray");
        rayObject.transform.SetParent(transform, false);
        rayLine = rayObject.AddComponent<LineRenderer>();
        rayLine.useWorldSpace = true;
        rayLine.positionCount = 2;
        rayLine.startWidth = 0.0025f;
        rayLine.endWidth = 0.0015f;
        rayLine.numCapVertices = 2;
        rayLine.enabled = false;

        Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            rayMaterial = new Material(shader) { color = new Color(0.98f, 0.72f, 0.12f, 0.9f) };
            rayLine.material = rayMaterial;
        }
    }

    private void UpdateRayVisual(Vector3 origin, Vector3 end)
    {
        if (rayLine == null)
        {
            return;
        }

        rayLine.SetPosition(0, origin);
        rayLine.SetPosition(1, end);
        rayLine.enabled = true;
    }

    private void HideRay()
    {
        if (rayLine != null)
        {
            rayLine.enabled = false;
        }
    }

    private void HideRay(Vector3 origin, Vector3 end)
    {
        UpdateRayVisual(origin, end);
    }

    private void UpdateRaySourceLog(string source)
    {
        if (source == lastRaySource)
        {
            return;
        }

        lastRaySource = source;
        Debug.Log("[PICO-FRESH] PICO_FRESH_UI_RAY_SOURCE source=" + source + ".", this);
    }

    private void OnDisable()
    {
        ClearHover();
        ReleasePressedObject();
        HideRay();
    }

    private void OnDestroy()
    {
        if (rayMaterial != null)
        {
            Destroy(rayMaterial);
        }
    }
}

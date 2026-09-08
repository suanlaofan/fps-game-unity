using UnityEngine;

[DefaultExecutionOrder(-1150)]
public sealed class Level0VrMotor : MonoBehaviour
{
    public CharacterController Body { get; private set; }
    public bool HeadBlocked { get; private set; }
    [Min(0.1f)] public float walkSpeed = 2f;
    [Min(0.1f)] public float sprintSpeed = 3.6f;
    public float CurrentMoveSpeed { get; private set; }
    public LayerMask environmentMask = 1 << 12;
    private PicoFreshRuntime rig;
    private Transform origin;
    private float verticalSpeed;
    private Vector3 safeFeet;
    private float safeTimer;
    private readonly Collider[] overlaps = new Collider[16];

    public void Configure(PicoFreshRuntime runtime, Transform xrOrigin, PlayerHealth health, BoxCollider safety)
    {
        rig = runtime; origin = xrOrigin;
        var oldBody = GetComponent<Rigidbody>();
        if (oldBody) { oldBody.linearVelocity = Vector3.zero; oldBody.useGravity = false; oldBody.isKinematic = true; oldBody.detectCollisions = false; }
        foreach (var c in GetComponents<Collider>()) if (!(c is CharacterController)) c.enabled = false;
        var oldMovement = GetComponent<FPSControllerLPFP.FpsControllerLPFP>(); if (oldMovement) oldMovement.enabled = false;
        Vector3 feet = transform.position;
        if (Physics.Raycast(feet + Vector3.up * 2, Vector3.down, out RaycastHit hit, 6, environmentMask, QueryTriggerInteraction.Ignore)) feet.y = hit.point.y + 0.03f;
        else Debug.LogError("LEVEL0_SPAWN_GROUND_MISSING position=" + feet);
        transform.position = feet;
        origin.SetParent(transform, true); origin.localPosition = Vector3.zero; origin.localRotation = Quaternion.identity;
        Body = GetComponent<CharacterController>(); if (!Body) Body = gameObject.AddComponent<CharacterController>();
        Body.radius = 0.25f; Body.height = 1.65f; Body.center = new Vector3(0, 0.825f, 0);
        Body.skinWidth = 0.025f; Body.stepOffset = 0.18f; Body.slopeLimit = 45; Body.minMoveDistance = 0;
        Body.enabled = true; safeFeet = feet;
        if (safety) safety.enabled = false; // Authored collision is the normal ground, never an invisible replacement.
        Physics.IgnoreLayerCollision(gameObject.layer, 12, false);
    }
    private void Update()
    {
        if (rig == null || Body == null) return;
        CurrentMoveSpeed = 0;
        float height = Mathf.Clamp(rig.Camera.transform.position.y - transform.position.y, 0.8f, 2.1f);
        if (Mathf.Abs(Body.height - height) > 0.03f) { Body.height = height; Body.center = Vector3.up * height * 0.5f; }
        if (rig.Gameplay)
        {
            // Follow room-scale movement without adding it a second time to the tracked camera.
            Vector3 physicalDelta = rig.Camera.transform.position - transform.position; physicalDelta.y = 0;
            Vector3 before = transform.position;
            Body.Move(Vector3.ClampMagnitude(physicalDelta, 0.3f));
            origin.position -= transform.position - before;
            Vector3 forward = Vector3.ProjectOnPlane(rig.Camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector2 stick = PicoFreshRuntime.LocomotionInput;
            float speed = PicoFreshRuntime.LocomotionRunning ? Mathf.Max(walkSpeed, sprintSpeed) : walkSpeed;
            CurrentMoveSpeed = speed * stick.magnitude;
            if (Body.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
            verticalSpeed = Mathf.Max(verticalSpeed - 9.81f * Time.deltaTime, -15);
            Vector3 motion = (forward * stick.y + right * stick.x) * speed + Vector3.up * verticalSpeed;
            // Substeps keep large frame deltas from tunnelling through thin authored walls.
            int steps = Mathf.Clamp(Mathf.CeilToInt(motion.magnitude * Time.deltaTime / 0.1f), 1, 32);
            for (int i = 0; i < steps; i++) Body.Move(motion * (Time.deltaTime / steps));
            if (Body.isGrounded && Time.time >= safeTimer) { safeTimer = Time.time + 1; safeFeet = transform.position; }
            if (transform.position.y < safeFeet.y - 3) Recover();
        }
        int n = Physics.OverlapSphereNonAlloc(rig.Camera.transform.position, 0.12f, overlaps, environmentMask, QueryTriggerInteraction.Ignore);
        // Keep the boundary active after the tracked head has crossed a thin wall.
        // The physical capsule stays on the reachable side of that wall.
        Vector3 bodyEye = transform.position + Vector3.up * Mathf.Clamp(height - 0.12f, 0.3f, Body.height);
        HeadBlocked = n > 0 || Physics.Linecast(bodyEye, rig.Camera.transform.position,
            environmentMask, QueryTriggerInteraction.Ignore);
    }
    public void SnapTurn(float degrees)
    {
        Vector3 pivot = rig.Camera.transform.position;
        origin.RotateAround(pivot, Vector3.up, degrees); // Rotate the tracked space; never teleport the collision capsule.
    }
    private void Recover()
    {
        Body.enabled = false; transform.position = safeFeet; Body.enabled = true; verticalSpeed = 0;
        Debug.LogWarning("LEVEL0_GROUND_RECOVERY " + safeFeet);
    }
}

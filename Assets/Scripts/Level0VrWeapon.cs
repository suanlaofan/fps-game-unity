using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-1050)]
public sealed class Level0VrWeapon : MonoBehaviour
{
    public int magazineSize = 24;
    public int bodyDamage = 15;
    public float shotsPerSecond = 5f;
    public float reloadSeconds = 1.8f;
    [Tooltip("Controller-local mounting angles. Positive X lowers the muzzle while the grip stays on the controller.")]
    public Vector3 gripMountEuler = new Vector3(15f, 0f, 0f);
    public int Ammo { get; private set; }
    public bool Reloading { get; private set; }
    public float ReloadProgress => Reloading ? 1f - Mathf.Clamp01((reloadUntil - Time.time) / Mathf.Max(0.01f, reloadSeconds)) : 0f;
    public int ShotsFired { get; private set; }
    public int Hits { get; private set; }
    public Transform Muzzle { get; private set; }
    public bool MuzzleBlocked { get; private set; }
    public Transform GripAnchor { get; private set; }
    public int VisibleWeaponRendererCount { get; private set; }
    public int RemovedArmRendererCount { get; private set; }
    // Measured from the independent 44-vertex trigger component in the original baked mesh.
    // Align the controller's trigger reference to the trigger contact center, above the pistol grip.
    // The stock remains the complete rear mesh; no model scale or clipping hides it.
    private static readonly Vector3 RifleTriggerContact = new Vector3(0f, -0.01266f, 0.16069f);
    private static readonly Vector3 RifleMuzzle = new Vector3(0f, 0.0300f, 0.7402f);
    private PicoFreshRuntime rig;
    private AutomaticGunScriptLPFP legacy;
    private float nextShot, reloadUntil, recoil, tracerUntil;
    private bool visible = true;
    private Quaternion restRotation;
    private Renderer[] renderers;
    private LineRenderer tracer;
    private Material tracerMaterial;
    private readonly List<Mesh> frozenMeshes = new List<Mesh>();

    public void Configure(PicoFreshRuntime runtime, AutomaticGunScriptLPFP source)
    {
        rig = runtime; legacy = source; source.enabled = false;
        foreach (var a in GetComponentsInChildren<Animator>(true)) a.enabled = false;
        // These are separate meshes: hiding the arms GameObject would also hide its gun children.
        // Freeze only the two actual rifle renderers and keep source hierarchy/bones for audio and FX.
        BuildWeaponOnlyVisuals();
        Muzzle = source.Spawnpoints.bulletSpawnPoint;
        Muzzle.position = transform.TransformPoint(RifleMuzzle);
        GripAnchor = new GameObject("Controller trigger contact anchor").transform;
        GripAnchor.SetParent(transform, false);
        GripAnchor.localPosition = RifleTriggerContact;
        transform.SetParent(rig.RightHand, false);
        transform.localScale = Vector3.one;
        transform.localRotation = Quaternion.identity;
        transform.rotation = Quaternion.FromToRotation(Muzzle.forward, rig.RightHand.forward) * transform.rotation;
        restRotation = Quaternion.Euler(gripMountEuler) * transform.localRotation;
        transform.localRotation = restRotation;
        // The trigger contact stays at the tracked controller origin, including during recoil/reload.
        transform.localPosition = -(restRotation * RifleTriggerContact);
        if (legacy.muzzleParticles)
        {
            legacy.muzzleParticles.transform.SetPositionAndRotation(Muzzle.position, Muzzle.rotation);
            legacy.muzzleParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
        Ammo = magazineSize;
        var traceObject = new GameObject("Shot tracer pool"); traceObject.transform.SetParent(rig.transform, false);
        tracer = traceObject.AddComponent<LineRenderer>(); tracer.useWorldSpace = true;
        tracer.positionCount = 2; tracer.startWidth = 0.004f; tracer.endWidth = 0.001f;
        tracerMaterial = new Material(Shader.Find("Sprites/Default")); tracer.sharedMaterial = tracerMaterial;
        tracer.startColor = new Color(1,0.72f,0.25f,0.8f); tracer.endColor = new Color(1,0.5f,0.1f,0);
        tracer.enabled = false;
        Debug.Log("LEVEL0_WEAPON_READY parent=Right-Controller trigger_contact_error=" + Vector3.Distance(GripAnchor.position, rig.RightHand.position).ToString("F5") + " trigger_model_local=" + RifleTriggerContact + " mount_euler=" + gripMountEuler + " muzzle_controller_local=" + rig.RightHand.InverseTransformDirection(Muzzle.forward) + " weapon_renderers=" + VisibleWeaponRendererCount + " hidden_arm_renderers=" + RemovedArmRendererCount + " ammo=24 damage=15 rate=5");
    }
    private void BuildWeaponOnlyVisuals()
    {
        var visibleRenderers = new List<Renderer>();
        // The material check protects against accidentally accepting an arms/weapon mixed replacement.
        foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Mesh sourceMesh = skin.sharedMesh;
            bool rifleMesh = sourceMesh && (sourceMesh.name == "assault_rifle_01" ||
                sourceMesh.name == "assault_rifle_01_iron_sights");
            bool gunMaterialsOnly = skin.sharedMaterials.Length > 0;
            foreach (var material in skin.sharedMaterials)
                gunMaterialsOnly &= material && material.name == "Guns Material";
            if (sourceMesh && sourceMesh.name == "arms") RemovedArmRendererCount++;
            skin.enabled = false;
            skin.forceRenderingOff = true;
            if (!rifleMesh || !gunMaterialsOnly) continue;

            var mesh = new Mesh { name = "Level0 frozen " + sourceMesh.name };
            skin.BakeMesh(mesh);
            mesh.UploadMeshData(true);
            frozenMeshes.Add(mesh);
            var visual = new GameObject("VR mesh - " + sourceMesh.name);
            visual.layer = skin.gameObject.layer;
            visual.transform.SetParent(skin.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = skin.sharedMaterials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            visibleRenderers.Add(renderer);
            VisibleWeaponRendererCount++;
        }
        foreach (var particles in GetComponentsInChildren<ParticleSystemRenderer>(true))
            visibleRenderers.Add(particles);
        renderers = visibleRenderers.ToArray();
        if (VisibleWeaponRendererCount != 2 || RemovedArmRendererCount != 1)
            Debug.LogError("LEVEL0_WEAPON_ASSET_MISMATCH expected rifle/sights meshes and one separate arms mesh.");
    }

    public void SetVisible(bool value) { visible = value; }
    private void Update()
    {
        if (rig == null) return;
        bool shown = visible && rig.Gameplay && rig.RightTracked;
        foreach (var r in renderers) if (r) r.enabled = shown;
        legacy.enabled = false;
        if (tracer) tracer.enabled = rig.Gameplay && Time.time < tracerUntil;
        if (!rig.Gameplay) return;
        MuzzleBlocked = IsMuzzleObstructed();
        if (Reloading)
        {
            if (Time.time >= reloadUntil) { Ammo = magazineSize; Reloading = false; rig.Haptic(0.2f,0.08f); }
            return;
        }
        if (PicoFreshRuntime.ReloadPressedThisFrame || Ammo == 0) { BeginReload(); return; }
        if (PicoFreshRuntime.FirePressed && !MuzzleBlocked && Time.time >= nextShot) Fire();
    }
    private void LateUpdate()
    {
        if (!rig) return;
        recoil = Mathf.MoveTowards(recoil, 0, Time.deltaTime * 12f);
        transform.localRotation = Quaternion.Euler(-recoil, 0, 0) * restRotation;
        transform.localPosition = -(transform.localRotation * RifleTriggerContact);
    }
    public void BeginReload()
    {
        if (Reloading || Ammo == magazineSize) return;
        Reloading = true; reloadUntil = Time.time + reloadSeconds;
        if (legacy.shootAudioSource && legacy.SoundClips.reloadSoundOutOfAmmo)
            legacy.shootAudioSource.PlayOneShot(legacy.SoundClips.reloadSoundOutOfAmmo,0.7f);
        rig.Haptic(0.12f,0.08f);
    }
    public bool IsMuzzleObstructed()
    {
        Vector3 origin = rig.RightHand.position;
        Vector3 delta = Muzzle.position - origin;
        int mask = rig.Motor.environmentMask;
        return rig.Motor.HeadBlocked || Physics.CheckSphere(origin,0.04f,mask,QueryTriggerInteraction.Ignore) ||
            Physics.CheckSphere(Muzzle.position,0.04f,mask,QueryTriggerInteraction.Ignore) ||
            Physics.Raycast(origin,delta.normalized,delta.magnitude,mask,QueryTriggerInteraction.Ignore) ||
            Physics.Linecast(rig.Camera.transform.position,origin,mask,QueryTriggerInteraction.Ignore);
    }
    public bool Fire()
    {
        if (Ammo <= 0 || Reloading || !rig.Gameplay || !rig.RightTracked || IsMuzzleObstructed()) return false;
        nextShot = Time.time + 1f / shotsPerSecond; Ammo--; ShotsFired++; recoil = 1.0f;
        if (legacy.shootAudioSource && legacy.SoundClips.shootSound) legacy.shootAudioSource.PlayOneShot(legacy.SoundClips.shootSound,0.65f);
        if (legacy.muzzleParticles) legacy.muzzleParticles.Emit(1);
        Vector3 end = Muzzle.position + Muzzle.forward * 50;
        // Player and UI are excluded; closest environment/enemy hit wins.
        int mask = (1 << 12) | (1 << 13);
        if (Physics.Raycast(Muzzle.position,Muzzle.forward,out RaycastHit hit,50,mask,QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            var enemy = hit.collider.GetComponentInParent<EnemyController>();
            if (enemy != null && !enemy.IsDead)
            {
                bool weakPoint = hit.collider.GetComponent<Level0WeakPoint>() != null;
                if (enemy.TakeDamage(weakPoint ? bodyDamage * 2 : bodyDamage,hit.point,hit.normal)) Hits++;
            }
        }
        tracer.SetPosition(0,Muzzle.position); tracer.SetPosition(1,end); tracerUntil = Time.time + 0.045f;
        rig.Haptic(0.3f,0.035f);
        return true;
    }
    private void OnDestroy()
    {
        if (tracerMaterial) Destroy(tracerMaterial);
        foreach (var mesh in frozenMeshes) if (mesh) Destroy(mesh);
    }
}

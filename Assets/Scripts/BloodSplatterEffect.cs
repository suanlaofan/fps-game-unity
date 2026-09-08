using UnityEngine;

public static class BloodSplatterEffect
{
    private static Material dropletMaterial;

    /// <summary>
    /// A close-range creature hit. This deliberately uses a compact, short-lived
    /// procedural droplet burst only: no blood artwork, decals, or texture assets
    /// are loaded at runtime.
    /// </summary>
    public static void SpawnCreatureHit(Vector3 position, Vector3 normal, float scale = 1f)
    {
        if (PicoFreshRuntime.IsPicoXrActive) { Level0HitFx.Emit(position, normal); return; }
        Vector3 direction = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
        float size = Mathf.Clamp(scale, 0.65f, 1.45f);
        GameObject effect = new GameObject("Creature Blood Impact");
        effect.transform.SetPositionAndRotation(position + direction * 0.025f, Quaternion.LookRotation(direction));
        CreateCreatureHitDropletSpray(effect, size);

        Object.Destroy(effect, 0.9f);
    }

    public static void Spawn(Vector3 position, Vector3 normal, float scale = 1f)
    {
        if (PicoFreshRuntime.IsPicoXrActive) { Level0HitFx.Emit(position, normal); return; }
        Vector3 direction = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
        GameObject effect = new GameObject("Blood Splatter");
        effect.transform.SetPositionAndRotation(position + direction * 0.03f, Quaternion.LookRotation(direction));
        effect.transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);

        ParticleSystem particles = effect.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.25f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 7.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.11f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.55f, 0.005f, 0.005f, 1f),
            new Color(0.95f, 0.03f, 0.015f, 1f));
        main.gravityModifier = 0.65f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.maxParticles = 48;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 24f;
        shape.radius = 0.045f;
        shape.length = 0.08f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.9f, 0.015f, 0.01f), 0f),
                new GradientColorKey(new Color(0.28f, 0.002f, 0.002f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.85f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

        ParticleSystemRenderer particleRenderer = effect.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.velocityScale = 0.22f;
        particleRenderer.lengthScale = 1f;
        particleRenderer.sharedMaterial = GetDropletMaterial();

        particles.Emit(36);
        particles.Play();
        Object.Destroy(effect, 2f);
    }

    private static void CreateCreatureHitDropletSpray(GameObject parent, float scale)
    {
        ParticleSystem particles = CreateParticleSystem(parent, "Small Blood Droplet Spray");
        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.08f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.52f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.020f * scale, 0.065f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.26f, 0.002f, 0.002f, 1f),
            new Color(0.70f, 0.008f, 0.005f, 1f));
        main.gravityModifier = 0.95f;
        main.maxParticles = 20;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 38f;
        shape.radius = 0.022f * scale;
        shape.length = 0.045f * scale;

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.025f;
        noise.frequency = 0.75f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.72f, 0.009f, 0.006f), 0f),
                new GradientColorKey(new Color(0.20f, 0.001f, 0.001f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0f),
                new GradientAlphaKey(0.78f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = new ParticleSystem.MinMaxGradient(fade);

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.velocityScale = 0.18f;
        renderer.lengthScale = 1.1f;
        renderer.sharedMaterial = GetDropletMaterial();
        renderer.sortMode = ParticleSystemSortMode.Distance;
        particles.Emit(Random.Range(14, 19));
        particles.Play();
    }

    private static ParticleSystem CreateParticleSystem(GameObject parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        ParticleSystem particles = child.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.None;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;
        return particles;
    }

    private static Material GetDropletMaterial()
    {
        if (dropletMaterial != null)
        {
            return dropletMaterial;
        }

        dropletMaterial = CreateParticleMaterial("Creature Blood Droplets", new Color(0.68f, 0.004f, 0.003f, 1f));
        return dropletMaterial;
    }

    private static Material CreateParticleMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
        }
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.HideAndDontSave
        };
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        return material;
    }
}

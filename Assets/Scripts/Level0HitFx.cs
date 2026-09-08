using UnityEngine;

// One bounded world-space emitter replaces per-hit object/material creation in VR.
public static class Level0HitFx
{
    private static ParticleSystem particles;
    public static void Emit(Vector3 point,Vector3 normal)
    {
        if(!particles)
        {
            var go=new GameObject("Level0 pooled hit particles");particles=go.AddComponent<ParticleSystem>();
            particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=particles.main;main.loop=false;main.playOnAwake=false;main.maxParticles=256;
            main.simulationSpace=ParticleSystemSimulationSpace.World;main.startLifetime=0.45f;
            main.startSize=0.035f;main.startSpeed=0;main.gravityModifier=0.7f;main.startColor=new Color(0.55f,0.008f,0.008f,1);
            var emission=particles.emission;emission.enabled=false;
            var shape=particles.shape;shape.enabled=false;
            var renderer=particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial=new Material(Shader.Find("Sprites/Default"));
            go.AddComponent<Level0HitFxLifetime>().OwnedMaterial=renderer.sharedMaterial;
        }
        particles.Play();
        for(int i=0;i<18;i++)
        {
            var data=new ParticleSystem.EmitParams{position=point+normal*0.03f,velocity=(normal+Random.insideUnitSphere*0.65f)*Random.Range(1f,3f)};
            particles.Emit(data,1);
        }
    }
}
public sealed class Level0HitFxLifetime:MonoBehaviour
{
    public Material OwnedMaterial;
    private void OnDestroy(){if(OwnedMaterial)Destroy(OwnedMaterial);}
}

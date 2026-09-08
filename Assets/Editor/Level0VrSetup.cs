#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Unity.AI.Navigation;

public static class Level0VrSetup
{
    const string Generated="Assets/Level0VR/Generated";
    [MenuItem("Tools/Level0/Apply VR Rectification")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/jogo.unity") scene=EditorSceneManager.OpenScene("Assets/Scenes/jogo.unity");
        Directory.CreateDirectory(Generated);AssetDatabase.Refresh();
        SetLayer(12,"Level0Environment");SetLayer(13,"Level0Enemy");
        var player=UnityEngine.Object.FindFirstObjectByType<PlayerHealth>(FindObjectsInactive.Include);
        var enemy=UnityEngine.Object.FindFirstObjectByType<EnemyController>(FindObjectsInactive.Include);
        if(!player||!enemy) throw new InvalidOperationException("Gameplay references missing");
        var origin=GameObject.Find("PICO XR Origin");
        if(!origin) throw new InvalidOperationException("Fresh XR Origin missing");
        var runtime=origin.GetComponent<PicoFreshRuntime>();runtime.editorPreview=true;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        enemy.maxHealth=300;enemy.moveSpeed=2.08f;enemy.attackDamage=15;enemy.attackImpactDelay=0.6f;
        enemy.timeBetweenAttacks=1.5f;enemy.attackRange=1;enemy.hitReactionDuration=0.12f;
        enemy.whatIsGround=1<<12;enemy.whatIsPlayer=1<<player.gameObject.layer;
        var flow=UnityEngine.Object.FindFirstObjectByType<Level0GameFlow>(FindObjectsInactive.Include);
        flow.preparationSeconds=8f;flow.minimumEnemyDistance=18f;
        foreach(var t in enemy.GetComponentsInChildren<Transform>(true))t.gameObject.layer=13;
        var capsule=enemy.GetComponent<CapsuleCollider>();
        if(capsule)
        {
            var head=enemy.transform.Find("WeakPoint");
            if(!head){head=new GameObject("WeakPoint").transform;head.SetParent(enemy.transform,false);}
            head.gameObject.layer=13;
            head.localPosition=capsule.center+Vector3.up*(capsule.height*0.38f);
            var sphere=head.GetComponent<SphereCollider>();if(!sphere)sphere=head.gameObject.AddComponent<SphereCollider>();sphere.radius=capsule.radius*0.85f;
            if(!head.GetComponent<Level0WeakPoint>())head.gameObject.AddComponent<Level0WeakPoint>();
        }
        var filters=UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .Where(f=>f.sharedMesh&&AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith("Assets/fpsgamemoxing/")&&f.GetComponent<MeshRenderer>()).ToArray();
        int boxes=0;
        foreach(var f in filters)
        {
            f.gameObject.layer=12;
            foreach(var c in f.GetComponents<MeshCollider>())c.enabled=false;
            bool decoration=f.name.StartsWith("deng")||f.GetComponent<Renderer>().bounds.min.y>3.2f;
            var box=f.GetComponent<BoxCollider>();
            if(!decoration)
            {
                if(!box)box=f.gameObject.AddComponent<BoxCollider>();
                box.center=f.sharedMesh.bounds.center;box.size=f.sharedMesh.bounds.size;box.isTrigger=false;box.enabled=true;boxes++;
            }
            else if(box)box.enabled=false;
        }
        Physics.IgnoreLayerCollision(player.gameObject.layer,12,false);
        Physics.IgnoreLayerCollision(player.gameObject.layer,13,false);
        Physics.IgnoreLayerCollision(12,13,false);
        var safety=GameObject.Find("PICO Fresh XR Ground Safety");if(safety)safety.GetComponent<BoxCollider>().enabled=false;
        var old=GameObject.Find("Level0 Render Clusters");if(old)UnityEngine.Object.DestroyImmediate(old);
        var clusterRoot=new GameObject("Level0 Render Clusters");
        var groups=new Dictionary<string,List<CombineInstance>>();var materials=new Dictionary<string,Material>();
        foreach(var f in filters)
        {
            var r=f.GetComponent<MeshRenderer>();var cell=Vector3Int.FloorToInt(r.bounds.center/8f);
            for(int sub=0;sub<f.sharedMesh.subMeshCount;sub++)
            {
                if(sub>=r.sharedMaterials.Length||!r.sharedMaterials[sub])continue;
                var mat=r.sharedMaterials[sub];var key=cell.x+"_"+cell.z+"_"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mat));
                if(!groups.ContainsKey(key)){groups[key]=new List<CombineInstance>();materials[key]=mat;}
                groups[key].Add(new CombineInstance{mesh=f.sharedMesh,subMeshIndex=sub,transform=f.transform.localToWorldMatrix});
            }
            r.enabled=false;
        }
        int index=0;
        foreach(var pair in groups.OrderBy(g=>g.Key))
        {
            var mesh=new Mesh{name="Level0Cluster"+index,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(pair.Value.ToArray(),true,true);mesh.RecalculateBounds();
            string path=Generated+"/Cluster"+index+".asset";
            var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved){EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);mesh=saved;}else AssetDatabase.CreateAsset(mesh,path);
            var go=new GameObject("Cluster "+index,typeof(MeshFilter),typeof(MeshRenderer));go.layer=12;go.transform.SetParent(clusterRoot.transform,false);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=materials[pair.Key];renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.OccluderStatic|StaticEditorFlags.OccludeeStatic);
            index++;
        }
        // Keep the current pipeline and authored lights, with bounded mobile shadow cost.
        QualitySettings.shadows=ShadowQuality.Disable;QualitySettings.pixelLightCount=1;
        QualitySettings.antiAliasing=2;QualitySettings.softParticles=false;QualitySettings.realtimeReflectionProbes=false;
        QualitySettings.lodBias=0.7f;
        foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None))light.shadows=LightShadows.None;
        var camera=origin.GetComponentInChildren<Camera>(true);camera.useOcclusionCulling=true;camera.allowHDR=false;
        var nav=UnityEngine.Object.FindFirstObjectByType<NavMeshSurface>(FindObjectsInactive.Include);
        if(nav)
        {
            nav.useGeometry=NavMeshCollectGeometry.PhysicsColliders;nav.layerMask=1<<12;nav.collectObjects=CollectObjects.All;
            nav.BuildNavMesh();
            string navPath=Generated+"/Level0NavMesh.asset";
            var saved=AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath);
            if(saved){EditorUtility.CopySerialized(nav.navMeshData,saved);nav.navMeshData=saved;}
            else AssetDatabase.CreateAsset(nav.navMeshData,navPath);
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        string report="LEVEL0_SETUP_PASS renderers_before="+filters.Length+" render_clusters="+index+" box_colliders="+boxes+" enemy_hp=300 enemy_speed=2.08 enemy_damage=15 preparation=8 spawn_minimum=18\n";
        Directory.CreateDirectory("Evidence");File.WriteAllText("Evidence/setup.txt",report);Debug.Log(report);
    }
    static void SetLayer(int index,string name)
    {
        var manager=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layer=manager.FindProperty("layers").GetArrayElementAtIndex(index);
        if(!string.IsNullOrEmpty(layer.stringValue)&&layer.stringValue!=name)throw new InvalidOperationException("Layer "+index+" already used: "+layer.stringValue);
        layer.stringValue=name;manager.ApplyModifiedProperties();
    }
    [MenuItem("Tools/Level0/Bake Occlusion")]
    public static void BakeOcclusion(){StaticOcclusionCulling.Compute();EditorSceneManager.SaveOpenScenes();}
}
#endif

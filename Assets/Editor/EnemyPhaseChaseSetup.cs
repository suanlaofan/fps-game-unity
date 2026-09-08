#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class EnemyPhaseChaseSetup
{
    private const float DoubledSpeed = 7f;

    [MenuItem("Tools/FPS Game/Apply NavMesh Chase And Double Speed")]
    public static void Apply()
    {
        EnemyController enemy = FindEnemy();
        if (enemy == null)
        {
            Debug.LogError("[EnemyPhase] Could not find the 'diren' EnemyController in the active scene.");
            return;
        }

        Undo.RecordObject(enemy, "Configure Enemy NavMesh Chase");
        enemy.moveSpeed = DoubledSpeed;
        EditorUtility.SetDirty(enemy);

        NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            Undo.RecordObject(agent, "Double Enemy NavMesh Speed");
            agent.speed = DoubledSpeed;
            EditorUtility.SetDirty(agent);
        }

        Scene scene = enemy.gameObject.scene;
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log($"[EnemyNav] SUCCESS enemy='{enemy.name}' speed={DoubledSpeed:0.##}, wallPhasing=false, navMeshOnly=true.", enemy);
    }

    [MenuItem("Tools/FPS Game/Validate NavMesh Chase")]
    public static void Validate()
    {
        EnemyController enemy = FindEnemy();
        if (enemy == null)
        {
            Debug.LogError("[EnemyNav] VALIDATE failed: no EnemyController found.");
            return;
        }

        NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
        Camera[] cameras = enemy.player != null ? enemy.player.GetComponentsInChildren<Camera>(true) : new Camera[0];
        int eligibleCameras = 0;
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].enabled && cameras[i].farClipPlane > 100f &&
                (cameras[i].cullingMask & (1 << enemy.gameObject.layer)) != 0)
            {
                eligibleCameras++;
            }
        }

        bool speedValid = Mathf.Abs(enemy.moveSpeed - DoubledSpeed) < 0.01f &&
                          (agent == null || Mathf.Abs(agent.speed - DoubledSpeed) < 0.01f);
        if (!speedValid)
        {
            Debug.LogError($"[EnemyNav] VALIDATE failed: moveSpeed={enemy.moveSpeed:0.##}, agentSpeed={(agent != null ? agent.speed : -1f):0.##}.", enemy);
            return;
        }

        Debug.Log($"[EnemyNav] VALIDATE speed={enemy.moveSpeed:0.##}, eligibleViewCameras={eligibleCameras}, wallPhasing=false, navMeshOnly=true.", enemy);
    }

    private static EnemyController FindEnemy()
    {
        EnemyController[] enemies = Object.FindObjectsByType<EnemyController>(FindObjectsInactive.Include);
        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemies[i] != null && enemies[i].gameObject.name == "diren")
            {
                return enemies[i];
            }
        }
        return enemies.Length > 0 ? enemies[0] : null;
    }
}
#endif

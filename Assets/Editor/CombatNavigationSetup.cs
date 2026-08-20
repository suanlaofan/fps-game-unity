using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class CombatNavigationSetup
{
    private const string ScenePath = "Assets/Scenes/jogo.unity";
    private const string EnemyName = "diren";
    private const string VisualName = "ShitiEnemyModel";
    private const string NavigationName = "NavMesh";
    private const string BulletPrefabPath = "Assets/Low Poly FPS Pack - Free (Sample)/Prefabs/Example_Prefabs/Projectiles/Bullet/Bullet_Prefab.prefab";
    private const float VisualYaw = 90f;

    private static NavMeshSurface pendingSurface;
    private static EnemyController pendingEnemy;
    private static PlayerHealth pendingPlayer;
    private static int pendingColliderCount;

    [MenuItem("Tools/FPS Game/Apply Combat And Full Map Navigation")]
    public static void ApplyCombatAndNavigation()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[CombatSetup] Exit Play Mode before applying the setup.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
        {
            Debug.LogError($"[CombatSetup] Open '{ScenePath}' before running this command.");
            return;
        }

        GameObject enemyObject = FindSingleObject(scene, EnemyName);
        if (enemyObject == null)
        {
            return;
        }

        EnemyController enemy = enemyObject.GetComponent<EnemyController>();
        if (enemy == null || enemy.player == null)
        {
            Debug.LogError("[CombatSetup] 'diren' must have EnemyController with its player reference assigned.");
            return;
        }

        GameObject playerObject = enemy.player.gameObject;
        ConfigureModelFacing(enemyObject);
        PlayerHealth playerHealth = ConfigureCombat(playerObject, enemy);
        ConfigureBulletDamage();
        ConfigureAudioListeners(scene, playerObject);

        NavMeshSurface surface = ConfigureNavigationSurface(scene, enemy, playerHealth, out int colliderCount);
        if (surface == null)
        {
            return;
        }

        ClearLegacyNavMeshReference();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        pendingSurface = surface;
        pendingEnemy = enemy;
        pendingPlayer = playerHealth;
        pendingColliderCount = colliderCount;
        EditorApplication.update -= FinishBakeWhenReady;
        EditorApplication.update += FinishBakeWhenReady;
        NavMeshAssetManager.instance.StartBakingSurfaces(new UnityEngine.Object[] { surface });

        Debug.Log($"[CombatSetup] Combat configured. Baking a full-map NavMesh from {colliderCount} active environment MeshColliders...");
    }

    [MenuItem("Tools/FPS Game/Validate Combat And Navigation")]
    public static void ValidateCombatAndNavigation()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject enemyObject = FindSingleObject(scene, EnemyName);
        if (enemyObject == null)
        {
            return;
        }

        EnemyController enemy = enemyObject.GetComponent<EnemyController>();
        PlayerHealth player = enemy != null && enemy.player != null ? enemy.player.GetComponent<PlayerHealth>() : null;
        NavMeshSurface surface = FindSingleObject(scene, NavigationName)?.GetComponent<NavMeshSurface>();
        Transform visual = enemyObject.transform.Find(VisualName);
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();

        bool enemyOnMesh = enemy != null && TryGetGroundNavMeshPoint(enemy.transform, out _);
        bool playerOnMesh = player != null && TryGetGroundNavMeshPoint(player.transform, out _);
        Debug.Log(
            $"[CombatValidate] enemyHP={(enemy != null ? enemy.MaxHealth : 0)}, playerHP={(player != null ? player.MaxHealth : 0)}, " +
            $"attack={(enemy != null ? enemy.attackDamage : 0)}, bullet={GetBulletDamage()}, yaw={(visual != null ? visual.localEulerAngles.y : -1f):F1}, " +
            $"surface={(surface != null && surface.navMeshData != null)}, navVertices={triangulation.vertices.Length}, " +
            $"navTriangles={triangulation.indices.Length / 3}, enemyOnMesh={enemyOnMesh}, playerOnMesh={playerOnMesh}, " +
            $"path={GetPathStatus(enemy, player)}.");
    }

    [MenuItem("Tools/FPS Game/Diagnose NavMesh Connectivity")]
    public static void DiagnoseNavMeshConnectivity()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject enemyObject = FindSingleObject(scene, EnemyName);
        if (enemyObject == null)
        {
            return;
        }

        EnemyController enemy = enemyObject.GetComponent<EnemyController>();
        Transform player = enemy != null ? enemy.player : null;
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        if (triangulation.indices == null || triangulation.indices.Length < 3)
        {
            Debug.LogError("[CombatNavDiag] The scene has no NavMesh triangles.");
            return;
        }

        int triangleCount = triangulation.indices.Length / 3;
        int[] component = BuildTriangleComponents(triangulation);
        int componentCount = 0;
        for (int i = 0; i < component.Length; i++)
        {
            componentCount = Mathf.Max(componentCount, component[i] + 1);
        }

        Vector3 enemyPoint = enemy != null && TryGetGroundNavMeshPoint(enemy.transform, out NavMeshHit enemyHit)
            ? enemyHit.position
            : Vector3.zero;
        Vector3 playerPoint = player != null && TryGetGroundNavMeshPoint(player, out NavMeshHit playerHit)
            ? playerHit.position
            : Vector3.zero;
        int enemyTriangle = FindNearestTriangle(triangulation, enemyPoint);
        int playerTriangle = FindNearestTriangle(triangulation, playerPoint);
        int enemyComponent = enemyTriangle >= 0 ? component[enemyTriangle] : -1;
        int playerComponent = playerTriangle >= 0 ? component[playerTriangle] : -1;

        Debug.Log($"[CombatNavDiag] triangles={triangleCount}, components={componentCount}, " +
                  $"enemyTriangle={enemyTriangle}, enemyComponent={enemyComponent}, " +
                  $"playerTriangle={playerTriangle}, playerComponent={playerComponent}, " +
                  $"enemyPoint={enemyPoint}, playerPoint={playerPoint}.");

        for (int id = 0; id < componentCount; id++)
        {
            Bounds bounds = new Bounds();
            int triangles = 0;
            bool hasBounds = false;
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                if (component[triangle] != id)
                {
                    continue;
                }

                Vector3 a = triangulation.vertices[triangulation.indices[triangle * 3]];
                Vector3 b = triangulation.vertices[triangulation.indices[triangle * 3 + 1]];
                Vector3 c = triangulation.vertices[triangulation.indices[triangle * 3 + 2]];
                if (!hasBounds)
                {
                    bounds = new Bounds(a, Vector3.zero);
                    hasBounds = true;
                }
                bounds.Encapsulate(a);
                bounds.Encapsulate(b);
                bounds.Encapsulate(c);
                triangles++;
            }

            Debug.Log($"[CombatNavDiag] component={id}, triangles={triangles}, bounds={bounds}.");
        }
    }

    private static PlayerHealth ConfigureCombat(GameObject playerObject, EnemyController enemy)
    {
        PlayerHealth playerHealth = GetOrAddComponent<PlayerHealth>(playerObject);
        Undo.RecordObject(playerHealth, "Configure player health");
        playerHealth.maxHealth = 100;
        playerHealth.ResetHealth();
        EditorUtility.SetDirty(playerHealth);
        PrefabUtility.RecordPrefabInstancePropertyModifications(playerHealth);

        EnemyHorrorAudio horrorAudio = GetOrAddComponent<EnemyHorrorAudio>(enemy.gameObject);
        horrorAudio.volume = 0.64f;
        horrorAudio.volumeMultiplier = 2f;
        horrorAudio.chaseRoarMultiplier = 4f;
        horrorAudio.hurtStingMultiplier = 4f;
        horrorAudio.minDistance = 2f;
        horrorAudio.maxDistance = 38f;
        EditorUtility.SetDirty(horrorAudio);

        AudioSource horrorSource = enemy.GetComponent<AudioSource>();
        if (horrorSource != null)
        {
            Undo.RecordObject(horrorSource, "Configure enemy horror audio");
            horrorSource.playOnAwake = false;
            horrorSource.loop = false;
            horrorSource.spatialBlend = 1f;
            horrorSource.dopplerLevel = 0f;
            horrorSource.minDistance = 2f;
            horrorSource.maxDistance = 38f;
            horrorSource.volume = 0.64f;
            EditorUtility.SetDirty(horrorSource);
        }

        AudioReverbFilter reverb = GetOrAddComponent<AudioReverbFilter>(enemy.gameObject);
        reverb.reverbPreset = AudioReverbPreset.Cave;
        EditorUtility.SetDirty(reverb);

        CombatHUD hud = GetOrAddComponent<CombatHUD>(playerObject);
        Undo.RecordObject(hud, "Configure combat HUD");
        hud.playerHealth = playerHealth;
        hud.enemy = enemy;
        EditorUtility.SetDirty(hud);
        PrefabUtility.RecordPrefabInstancePropertyModifications(hud);

        Undo.RecordObject(enemy, "Configure enemy combat");
        enemy.maxHealth = 500;
        enemy.attackDamage = 10;
        enemy.attackImpactDelay = 0.3f;
        enemy.hitReactionDuration = 0.45f;
        enemy.playerHealth = playerHealth;
        enemy.horrorAudio = horrorAudio;
        enemy.ResetHealth();
        EditorUtility.SetDirty(enemy);
        PrefabUtility.RecordPrefabInstancePropertyModifications(enemy);
        return playerHealth;
    }

    private static void ConfigureModelFacing(GameObject enemyObject)
    {
        Transform visual = enemyObject.transform.Find(VisualName);
        if (visual == null)
        {
            Debug.LogError($"[CombatSetup] '{EnemyName}' has no '{VisualName}' child.");
            return;
        }

        Undo.RecordObject(visual, "Face Shiti model forward");
        visual.localRotation = Quaternion.Euler(0f, VisualYaw, 0f);

        Bounds bounds = CalculateBounds(visual.gameObject);
        CapsuleCollider capsule = enemyObject.GetComponent<CapsuleCollider>();
        if (capsule != null && bounds.size.y > 0.001f)
        {
            Vector3 targetCenter = enemyObject.transform.TransformPoint(capsule.center);
            visual.position += targetCenter - bounds.center;
        }

        EditorUtility.SetDirty(visual);
        PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
    }

    private static void ConfigureBulletDamage()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(BulletPrefabPath);
        try
        {
            BulletScript bullet = prefabRoot.GetComponent<BulletScript>();
            if (bullet == null)
            {
                Debug.LogError($"[CombatSetup] No BulletScript found on '{BulletPrefabPath}'.");
                return;
            }

            bullet.damage = 10;
            EditorUtility.SetDirty(bullet);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, BulletPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static int GetBulletDamage()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BulletPrefabPath);
        BulletScript bullet = prefab != null ? prefab.GetComponent<BulletScript>() : null;
        return bullet != null ? bullet.damage : 0;
    }

    private static string GetPathStatus(EnemyController enemy, PlayerHealth player)
    {
        if (enemy == null || player == null || !TryGetGroundNavMeshPoint(enemy.transform, out NavMeshHit enemyHit) ||
            !TryGetGroundNavMeshPoint(player.transform, out NavMeshHit playerHit))
        {
            return "Unavailable";
        }

        NavMeshPath path = new NavMeshPath();
        return NavMesh.CalculatePath(enemyHit.position, playerHit.position, NavMesh.AllAreas, path)
            ? path.status.ToString()
            : "Unavailable";
    }

    private static bool TryGetGroundNavMeshPoint(Transform target, out NavMeshHit hit)
    {
        Vector3 queryPoint = target.position;
        Collider body = target.GetComponent<Collider>();
        if (body != null && body.enabled)
        {
            queryPoint.y = body.bounds.min.y + 0.1f;
        }
        return NavMesh.SamplePosition(queryPoint, out hit, 1.25f, NavMesh.AllAreas);
    }

    private static int[] BuildTriangleComponents(NavMeshTriangulation triangulation)
    {
        int triangleCount = triangulation.indices.Length / 3;
        var parent = new int[triangleCount];
        for (int i = 0; i < triangleCount; i++)
        {
            parent[i] = i;
        }

        var edgeOwners = new System.Collections.Generic.Dictionary<string, int>();
        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            int first = triangulation.indices[triangle * 3];
            int second = triangulation.indices[triangle * 3 + 1];
            int third = triangulation.indices[triangle * 3 + 2];
            UnionTriangles(parent, edgeOwners, triangle, triangulation.vertices[first], triangulation.vertices[second]);
            UnionTriangles(parent, edgeOwners, triangle, triangulation.vertices[second], triangulation.vertices[third]);
            UnionTriangles(parent, edgeOwners, triangle, triangulation.vertices[third], triangulation.vertices[first]);
        }

        var roots = new System.Collections.Generic.Dictionary<int, int>();
        var result = new int[triangleCount];
        int nextComponent = 0;
        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            int root = FindTriangleRoot(parent, triangle);
            if (!roots.TryGetValue(root, out int component))
            {
                component = nextComponent++;
                roots.Add(root, component);
            }
            result[triangle] = component;
        }
        return result;
    }

    private static void UnionTriangles(int[] parent, System.Collections.Generic.Dictionary<string, int> edgeOwners, int triangle, Vector3 first, Vector3 second)
    {
        string firstKey = QuantizeVertex(first);
        string secondKey = QuantizeVertex(second);
        string key = string.CompareOrdinal(firstKey, secondKey) <= 0
            ? firstKey + "|" + secondKey
            : secondKey + "|" + firstKey;
        if (edgeOwners.TryGetValue(key, out int other))
        {
            int rootA = FindTriangleRoot(parent, triangle);
            int rootB = FindTriangleRoot(parent, other);
            if (rootA != rootB)
            {
                parent[rootB] = rootA;
            }
        }
        else
        {
            edgeOwners.Add(key, triangle);
        }
    }

    private static string QuantizeVertex(Vector3 vertex)
    {
        return $"{Mathf.RoundToInt(vertex.x * 1000f)},{Mathf.RoundToInt(vertex.y * 1000f)},{Mathf.RoundToInt(vertex.z * 1000f)}";
    }

    private static int FindTriangleRoot(int[] parent, int value)
    {
        int root = value;
        while (parent[root] != root)
        {
            root = parent[root];
        }
        while (parent[value] != value)
        {
            int next = parent[value];
            parent[value] = root;
            value = next;
        }
        return root;
    }

    private static int FindNearestTriangle(NavMeshTriangulation triangulation, Vector3 position)
    {
        int nearest = -1;
        float bestDistance = float.PositiveInfinity;
        int triangleCount = triangulation.indices.Length / 3;
        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            Vector3 a = triangulation.vertices[triangulation.indices[triangle * 3]];
            Vector3 b = triangulation.vertices[triangulation.indices[triangle * 3 + 1]];
            Vector3 c = triangulation.vertices[triangulation.indices[triangle * 3 + 2]];
            float distance = (Vector3.Lerp(Vector3.Lerp(a, b, 0.5f), c, 0.3333333f) - position).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = triangle;
            }
        }
        return nearest;
    }

    private static void ConfigureAudioListeners(Scene scene, GameObject playerObject)
    {
        AudioListener[] playerListeners = playerObject.GetComponentsInChildren<AudioListener>(true);
        AudioListener keeper = null;
        foreach (AudioListener listener in playerListeners)
        {
            if (listener.GetComponent<Camera>() != null)
            {
                keeper = listener;
                break;
            }
        }
        if (keeper == null && playerListeners.Length > 0)
        {
            keeper = playerListeners[0];
        }

        int disabledCount = 0;
        foreach (AudioListener listener in Resources.FindObjectsOfTypeAll<AudioListener>())
        {
            if (listener.gameObject.scene != scene)
            {
                continue;
            }

            bool shouldEnable = listener == keeper;
            if (listener.enabled != shouldEnable)
            {
                Undo.RecordObject(listener, "Keep one scene Audio Listener");
                listener.enabled = shouldEnable;
                EditorUtility.SetDirty(listener);
                PrefabUtility.RecordPrefabInstancePropertyModifications(listener);
                if (!shouldEnable)
                {
                    disabledCount++;
                }
            }
        }

        Debug.Log($"[CombatSetup] Kept Audio Listener '{(keeper != null ? keeper.name : "<none>")}' and disabled {disabledCount} duplicate listener(s).");
    }

    private static NavMeshSurface ConfigureNavigationSurface(Scene scene, EnemyController enemy, PlayerHealth player, out int colliderCount)
    {
        colliderCount = 0;
        GameObject navigationObject = FindSingleObject(scene, NavigationName);
        if (navigationObject == null)
        {
            return null;
        }

        Undo.RecordObject(navigationObject.transform, "Reset NavMesh surface transform");
        navigationObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        navigationObject.transform.localScale = Vector3.one;

        NavMeshSurface surface = GetOrAddComponent<NavMeshSurface>(navigationObject);
        if (!TryCalculateEnvironmentBounds(scene, enemy, player, out Bounds sourceBounds, out colliderCount))
        {
            Debug.LogError("[CombatSetup] No active Default-layer environment MeshColliders were found for NavMesh baking.");
            return null;
        }

        sourceBounds.Expand(new Vector3(10f, 0f, 10f));
        float floorY = Mathf.Min(enemy.transform.position.y, player.transform.position.y);
        Bounds navigationBounds = new Bounds(
            new Vector3(sourceBounds.center.x, floorY + 1.5f, sourceBounds.center.z),
            new Vector3(sourceBounds.size.x, 6f, sourceBounds.size.z));

        Undo.RecordObject(surface, "Configure full map NavMesh");
        surface.agentTypeID = enemy.agent != null ? enemy.agent.agentTypeID : 0;
        surface.collectObjects = CollectObjects.Volume;
        surface.center = navigationBounds.center;
        surface.size = navigationBounds.size;
        surface.layerMask = 1 << 0;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.defaultArea = 0;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;
        surface.overrideTileSize = false;
        surface.overrideVoxelSize = false;
        surface.minRegionArea = 0.5f;
        surface.buildHeightMesh = false;
        EditorUtility.SetDirty(surface);

        Debug.Log($"[CombatSetup] NavMesh bake volume center={navigationBounds.center}, size={navigationBounds.size}.");
        return surface;
    }

    private static bool TryCalculateEnvironmentBounds(Scene scene, EnemyController enemy, PlayerHealth player, out Bounds bounds, out int colliderCount)
    {
        bounds = default;
        colliderCount = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MeshCollider meshCollider in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (!meshCollider.enabled || !meshCollider.gameObject.activeInHierarchy || meshCollider.isTrigger ||
                    meshCollider.sharedMesh == null || meshCollider.gameObject.layer != 0)
                {
                    continue;
                }
                if (meshCollider.GetComponentInParent<NavMeshAgent>() != null || meshCollider.GetComponentInParent<Rigidbody>() != null)
                {
                    continue;
                }

                if (colliderCount == 0)
                {
                    bounds = meshCollider.bounds;
                }
                else
                {
                    bounds.Encapsulate(meshCollider.bounds);
                }
                colliderCount++;
            }
        }
        return colliderCount > 0;
    }

    private static void ClearLegacyNavMeshReference()
    {
#pragma warning disable CS0618
        UnityEngine.Object settingsObject = UnityEditor.AI.NavMeshBuilder.navMeshSettingsObject;
#pragma warning restore CS0618
        if (settingsObject == null)
        {
            return;
        }

        SerializedObject settings = new SerializedObject(settingsObject);
        SerializedProperty dataProperty = settings.FindProperty("m_NavMeshData");
        if (dataProperty != null && dataProperty.objectReferenceValue != null)
        {
            dataProperty.objectReferenceValue = null;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[CombatSetup] Cleared the scene reference to the legacy Unity 2019 NavMesh asset.");
        }
    }

    private static void FinishBakeWhenReady()
    {
        if (pendingSurface == null)
        {
            EditorApplication.update -= FinishBakeWhenReady;
            return;
        }
        if (NavMeshAssetManager.instance.IsSurfaceBaking(pendingSurface))
        {
            return;
        }

        EditorApplication.update -= FinishBakeWhenReady;
        if (pendingSurface.navMeshData == null)
        {
            Debug.LogError("[CombatSetup] Full-map NavMesh bake did not produce NavMeshData.");
            ClearPendingBake();
            return;
        }

        Scene scene = pendingSurface.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        Bounds navBounds = CalculateBounds(triangulation.vertices);
        bool enemyOnMesh = TryGetGroundNavMeshPoint(pendingEnemy.transform, out NavMeshHit enemyHit);
        bool playerOnMesh = TryGetGroundNavMeshPoint(pendingPlayer.transform, out NavMeshHit playerHit);
        NavMeshPath path = new NavMeshPath();
        bool pathFound = enemyOnMesh && playerOnMesh && NavMesh.CalculatePath(enemyHit.position, playerHit.position, NavMesh.AllAreas, path);

        bool pathComplete = pathFound && path.status == NavMeshPathStatus.PathComplete;
        string resultPrefix = pathComplete ? "SUCCESS" : "NAVIGATION INCOMPLETE";
        string result =
            $"[CombatSetup] {resultPrefix}: saved '{scene.path}', NavMesh asset='{AssetDatabase.GetAssetPath(pendingSurface.navMeshData)}', " +
            $"sources={pendingColliderCount}, vertices={triangulation.vertices.Length}, triangles={triangulation.indices.Length / 3}, " +
            $"bounds={navBounds}, enemyOnMesh={enemyOnMesh}, playerOnMesh={playerOnMesh}, " +
            $"path={(pathFound ? path.status.ToString() : "Unavailable")}, enemyHP={pendingEnemy.MaxHealth}, playerHP={pendingPlayer.MaxHealth}.";

        if (pathComplete)
        {
            Debug.Log(result);
        }
        else
        {
            Debug.LogError(result);
        }

        Selection.activeGameObject = pendingSurface.gameObject;
        EditorGUIUtility.PingObject(pendingSurface.navMeshData);
        ClearPendingBake();
    }

    private static void ClearPendingBake()
    {
        pendingSurface = null;
        pendingEnemy = null;
        pendingPlayer = null;
        pendingColliderCount = 0;
    }

    private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(gameObject);
    }

    private static GameObject FindSingleObject(Scene scene, string objectName)
    {
        GameObject match = null;
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name != objectName)
                {
                    continue;
                }
                match = candidate.gameObject;
                count++;
            }
        }

        if (count != 1)
        {
            Debug.LogError($"[CombatSetup] Expected exactly one '{objectName}' in '{scene.path}', found {count}.");
            return null;
        }
        return match;
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position, Vector3.zero);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    private static Bounds CalculateBounds(Vector3[] vertices)
    {
        if (vertices == null || vertices.Length == 0)
        {
            return new Bounds();
        }

        Bounds bounds = new Bounds(vertices[0], Vector3.zero);
        for (int i = 1; i < vertices.Length; i++)
        {
            bounds.Encapsulate(vertices[i]);
        }
        return bounds;
    }
}

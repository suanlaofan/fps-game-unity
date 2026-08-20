using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class EnemyController : MonoBehaviour
{
    public NavMeshAgent agent;
    public Transform player;
    public LayerMask whatIsGround, whatIsPlayer;
    public Animator animator;

    [Header("Chase")]
    [Min(0.1f)] public float moveSpeed = 7f;
    [Min(0.1f)] public float phaseRecoveryRadius = 2f;
    [Min(0.05f)] public float phaseGroundTolerance = 0.2f;
    [Min(0f)] public float phaseEnterDelay = 0.03f;
    [Min(0f)] public float phaseExitDelay = 0.15f;

    [Header("Combat")]
    [Min(1)] public int maxHealth = 500;
    [SerializeField] private int currentHealth = 500;
    [Min(1)] public int attackDamage = 10;
    [Min(0f)] public float attackImpactDelay = 0.3f;
    [Min(0f)] public float hitReactionDuration = 0.45f;
    public PlayerHealth playerHealth;
    public EnemyHorrorAudio horrorAudio;

    // Patroling
    public Vector3 walkPoint;
    bool walkPointSet;
    public float walkPointRange;

    // Attacking
    public float timeBetweenAttacks;
    bool alreadyAttacked;

    // States
    public float sightRange, attackRange;
    public bool playerInSightRange, playerInAttackRange;

    private static readonly int MotionStateHash = Animator.StringToHash("MotionState");
    private const float CameraSearchInterval = 0.5f;
    private int currentMotionState = -1;
    private float hitReactionUntil;
    private bool isDead;
    private Collider playerCollider;
    private Camera playerViewCamera;
    private float nextCameraSearchTime;
    private bool isPhasing;
    private float phaseGroundY;
    private bool playerCanSeeEnemy;
    private bool rawPlayerVisibility = true;
    private float rawVisibilitySince;
    private Renderer[] visibilityRenderers;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => isDead;
    public bool PlayerCanSeeEnemy => playerCanSeeEnemy;
    public bool IsPhasing => isPhasing;
    public float CurrentMoveSpeed => moveSpeed;
    public Camera PlayerViewCamera => playerViewCamera;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        // Existing scene instances predate the chase fields. Keep their runtime speed
        // at the requested doubled value even before the editor setup command runs.
        if (moveSpeed <= 0.1f)
        {
            moveSpeed = 7f;
        }
        phaseRecoveryRadius = Mathf.Max(0.1f, phaseRecoveryRadius);
        phaseGroundTolerance = Mathf.Max(0.05f, phaseGroundTolerance);
        phaseEnterDelay = Mathf.Max(0f, phaseEnterDelay);
        phaseExitDelay = Mathf.Max(0f, phaseExitDelay);
        phaseGroundY = transform.position.y;
        rawVisibilitySince = Time.unscaledTime;
        playerCanSeeEnemy = true;
        visibilityRenderers = GetComponentsInChildren<Renderer>(true);
        if (agent != null)
        {
            agent.speed = moveSpeed;
        }

        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = maxHealth;
        isDead = false;

        if (player != null && playerHealth == null)
        {
            playerHealth = player.GetComponent<PlayerHealth>();
        }
        if (player != null)
        {
            playerCollider = player.GetComponent<Collider>();
        }
        if (horrorAudio == null)
        {
            horrorAudio = GetComponent<EnemyHorrorAudio>();
        }

        if (animator == null)
        {
            foreach (Animator candidate in GetComponentsInChildren<Animator>(true))
            {
                if (candidate.gameObject != gameObject)
                {
                    animator = candidate;
                    break;
                }
            }

            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }
        }
    }

    private void Pratroling()
    {
        if (!TryResumeNavigation())
        {
            SetMotionState(1);
            return;
        }

        SetMotionState(1);
        if (!walkPointSet) SearchWalkPoint();
        if (walkPointSet && CanNavigate()) agent.SetDestination(walkPoint);

        Vector3 distanceToWalkPoint = transform.position - walkPoint;

        if (distanceToWalkPoint.magnitude < 1f) walkPointSet = false;
    }

    private void SearchWalkPoint()
    {
        float randomZ = Random.Range(-walkPointRange, walkPointRange);
        float randomX = Random.Range(-walkPointRange, walkPointRange);
        Vector3 candidate = new Vector3(transform.position.x + randomX, transform.position.y, transform.position.z + randomZ);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            walkPoint = hit.position;
            walkPointSet = true;
        }
    }

    private void ChasePlayer()
    {
        // A visible enemy must use the baked walkable surface. If recovery fails
        // while it is still inside geometry, wait for the next frame and retry.
        if (!TryResumeNavigation())
        {
            SetMotionState(2);
            return;
        }

        SetMotionState(2);
        if (CanNavigate())
        {
            Vector3 target = GetPlayerNavigationTarget();
            if (NavMesh.SamplePosition(target, out NavMeshHit hit, 1.25f, agent.areaMask))
            {
                agent.SetDestination(hit.position);
            }
        }
    }

    private void PhaseChasePlayer()
    {
        BeginPhaseChase();
        SetMotionState(2);

        if (player == null)
        {
            return;
        }

        Vector3 target = player.position;
        target.y = phaseGroundY;
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        float angularSpeed = agent != null ? agent.angularSpeed : 180f;
        Quaternion desiredRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredRotation, angularSpeed * Time.deltaTime);
        transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
        Physics.SyncTransforms();
    }

    private void BeginPhaseChase()
    {
        if (isPhasing)
        {
            return;
        }

        phaseGroundY = transform.position.y;
        isPhasing = true;
        if (agent != null && agent.enabled)
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            agent.enabled = false;
        }

        Debug.Log($"[EnemyPhase] ENTER enemy='{name}' speed={moveSpeed:0.##} groundY={phaseGroundY:0.###}.", this);
    }

    private bool TryResumeNavigation()
    {
        if (agent == null)
        {
            return false;
        }

        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.speed = moveSpeed;
            agent.isStopped = false;
            ExitPhaseChase();
            return true;
        }

        int areaMask = agent.areaMask;
        Vector3 queryPosition = transform.position;
        queryPosition.y = phaseGroundY;
        if (!NavMesh.SamplePosition(queryPosition, out NavMeshHit hit, phaseRecoveryRadius, areaMask) ||
            Mathf.Abs(hit.position.y - phaseGroundY) > phaseGroundTolerance)
        {
            return false;
        }

        if (agent.enabled)
        {
            agent.enabled = false;
        }

        Vector3 recoveredPosition = transform.position;
        recoveredPosition.y = hit.position.y;
        transform.position = recoveredPosition;
        Physics.SyncTransforms();
        agent.enabled = true;
        if (!agent.isOnNavMesh)
        {
            agent.enabled = false;
            return false;
        }

        agent.Warp(recoveredPosition);
        agent.speed = moveSpeed;
        agent.isStopped = false;
        ExitPhaseChase();
        return true;
    }

    private void ExitPhaseChase()
    {
        if (!isPhasing)
        {
            return;
        }

        isPhasing = false;
        Debug.Log($"[EnemyPhase] EXIT enemy='{name}' navMesh={agent != null && agent.isOnNavMesh} speed={moveSpeed:0.##}.", this);
    }

    private void AttackPlayer()
    {
        SetMotionState(3);
        StopNavigation();

        Vector3 lookPosition = new Vector3(player.position.x, transform.position.y, player.position.z);
        transform.LookAt(lookPosition);

        if (!alreadyAttacked)
        {
            alreadyAttacked = true;
            StartCoroutine(DealAttackDamage());
            if (horrorAudio != null)
            {
                horrorAudio.PlayAttackSting();
            }
            Invoke(nameof(ResetAttack), timeBetweenAttacks);
        }
    }

    private IEnumerator DealAttackDamage()
    {
        if (attackImpactDelay > 0f)
        {
            yield return new WaitForSeconds(attackImpactDelay);
        }

        if (isDead || player == null || playerHealth == null || playerHealth.IsDead)
        {
            yield break;
        }

        Collider enemyCollider = GetComponent<Collider>();
        if (playerCollider == null)
        {
            playerCollider = player.GetComponent<Collider>();
        }

        Vector3 enemyChest = enemyCollider != null ? enemyCollider.bounds.center : transform.position + Vector3.up * 1.1f;
        Vector3 playerChest = playerCollider != null ? playerCollider.bounds.center : player.position;
        float distance = Vector3.Distance(enemyChest, playerChest);
        if (distance <= attackRange + 0.75f)
        {
            Vector3 sprayDirection = (playerChest - enemyChest).normalized;
            if (Physics.Linecast(enemyChest, playerChest, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<PlayerHealth>() == playerHealth)
            {
                if (playerHealth.TakeDamage(attackDamage, hit.point, sprayDirection) && horrorAudio != null)
                {
                    horrorAudio.PlayMeleeImpact();
                }
            }
        }
    }

    private Vector3 GetPlayerNavigationTarget()
    {
        Vector3 target = player.position;
        if (playerCollider == null)
        {
            playerCollider = player.GetComponent<Collider>();
        }
        if (playerCollider != null)
        {
            target.y = playerCollider.bounds.min.y + 0.1f;
        }
        return target;
    }

    private void ResetAttack()
    {
        alreadyAttacked = false;
    }

    public bool TakeDamage(int damage, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (isDead || damage <= 0)
        {
            return false;
        }

        currentHealth = Mathf.Max(0, currentHealth - damage);
        BloodSplatterEffect.SpawnCreatureHit(hitPoint, hitNormal, 1.25f);
        Debug.Log($"[Combat] Enemy took {damage} damage. HP={currentHealth}/{maxHealth}.", this);

        if (currentHealth == 0)
        {
            Die();
            return true;
        }

        hitReactionUntil = Time.time + hitReactionDuration;
        StopNavigation();
        SetMotionState(4);
        if (horrorAudio != null)
        {
            horrorAudio.PlayHurtSting();
        }
        return true;
    }

    public void ResetHealth()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = maxHealth;
        isDead = false;
    }

    private void Die()
    {
        isDead = true;
        currentHealth = 0;
        StopAllCoroutines();
        CancelInvoke();
        StopNavigation();
        SetMotionState(5);

        if (agent != null && agent.enabled)
        {
            agent.enabled = false;
        }

        CapsuleCollider bodyCollider = GetComponent<CapsuleCollider>();
        if (bodyCollider != null)
        {
            bodyCollider.enabled = false;
        }

        if (horrorAudio != null)
        {
            horrorAudio.PlayDeathSting();
        }
        Debug.Log("[Combat] Enemy defeated.", this);
    }

    private bool CanNavigate()
    {
        return agent != null && agent.enabled && agent.isOnNavMesh;
    }

    private void StopNavigation()
    {
        if (!CanNavigate())
        {
            return;
        }

        agent.isStopped = true;
        agent.ResetPath();
    }

    private void SetMotionState(int state)
    {
        if (animator == null || !animator.isActiveAndEnabled || currentMotionState == state)
        {
            return;
        }

        animator.SetInteger(MotionStateHash, state);
        currentMotionState = state;
    }

    private bool EvaluatePlayerVisibility()
    {
        Camera camera = FindPlayerViewCamera();
        if (camera == null || !camera.isActiveAndEnabled || !IsLayerRenderedByCamera(camera))
        {
            // If the camera is unavailable during scene startup, keep the safe
            // NavMesh behavior until it can be resolved instead of phasing blindly.
            return true;
        }

        Bounds bounds = GetVisibilityBounds();
        if (!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), bounds))
        {
            return false;
        }

        Vector3 center = bounds.center;
        Vector3 upper = Vector3.Lerp(center, bounds.max, 0.72f);
        Vector3 lower = Vector3.Lerp(center, bounds.min, 0.35f);
        return IsVisibleSample(camera, center) || IsVisibleSample(camera, upper) || IsVisibleSample(camera, lower);
    }

    private Bounds GetVisibilityBounds()
    {
        Collider bodyCollider = GetComponent<Collider>();
        if (bodyCollider != null && bodyCollider.enabled)
        {
            return bodyCollider.bounds;
        }

        Renderer[] renderers = GetVisibilityRenderers();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        return new Bounds(transform.position + Vector3.up, Vector3.one);
    }

    private bool IsVisibleSample(Camera camera, Vector3 sample)
    {
        Vector3 ray = sample - camera.transform.position;
        float distance = ray.magnitude;
        if (distance <= camera.nearClipPlane)
        {
            return true;
        }

        RaycastHit[] hits = Physics.RaycastAll(camera.transform.position, ray / distance, distance + 0.05f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Collider nearestCollider = null;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider candidate = hits[i].collider;
            if (candidate == null || IsColliderOnPlayer(candidate))
            {
                continue;
            }

            if (hits[i].distance < nearestDistance)
            {
                nearestDistance = hits[i].distance;
                nearestCollider = candidate;
            }
        }

        return nearestCollider == null || IsColliderOnThisEnemy(nearestCollider);
    }

    private bool HasClearLineToPlayer()
    {
        if (player == null)
        {
            return false;
        }

        if (playerCollider == null)
        {
            playerCollider = player.GetComponent<Collider>();
        }

        Collider enemyCollider = GetComponent<Collider>();
        Vector3 from = enemyCollider != null ? enemyCollider.bounds.center : transform.position + Vector3.up;
        Vector3 to = playerCollider != null ? playerCollider.bounds.center : player.position;
        Vector3 ray = to - from;
        float distance = ray.magnitude;
        if (distance <= 0.001f)
        {
            return true;
        }

        RaycastHit[] hits = Physics.RaycastAll(from, ray / distance, distance + 0.05f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Collider nearestCollider = null;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider candidate = hits[i].collider;
            if (candidate == null || IsColliderOnThisEnemy(candidate))
            {
                continue;
            }

            if (hits[i].distance < nearestDistance)
            {
                nearestDistance = hits[i].distance;
                nearestCollider = candidate;
            }
        }

        return nearestCollider == null || IsColliderOnPlayer(nearestCollider);
    }

    private Camera FindPlayerViewCamera()
    {
        if (player == null)
        {
            playerViewCamera = null;
            return null;
        }

        if (playerViewCamera != null && playerViewCamera.isActiveAndEnabled && IsLayerRenderedByCamera(playerViewCamera))
        {
            return playerViewCamera;
        }

        if (Time.unscaledTime < nextCameraSearchTime)
        {
            return playerViewCamera;
        }

        nextCameraSearchTime = Time.unscaledTime + CameraSearchInterval;
        Camera[] cameras = player.GetComponentsInChildren<Camera>(true);
        Camera best = null;
        float bestFarClip = -1f;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (!candidate.isActiveAndEnabled || !IsLayerRenderedByCamera(candidate))
            {
                continue;
            }

            if (candidate.farClipPlane > bestFarClip)
            {
                best = candidate;
                bestFarClip = candidate.farClipPlane;
            }
        }

        playerViewCamera = best;
        return playerViewCamera;
    }

    private bool IsLayerRenderedByCamera(Camera camera)
    {
        if (camera == null)
        {
            return false;
        }

        Renderer[] renderers = GetVisibilityRenderers();
        if (renderers.Length == 0)
        {
            return (camera.cullingMask & (1 << gameObject.layer)) != 0;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if ((camera.cullingMask & (1 << renderers[i].gameObject.layer)) != 0)
            {
                return true;
            }
        }
        return false;
    }

    private Renderer[] GetVisibilityRenderers()
    {
        if (visibilityRenderers == null)
        {
            visibilityRenderers = GetComponentsInChildren<Renderer>(true);
        }
        return visibilityRenderers;
    }

    private bool IsColliderOnThisEnemy(Collider candidate)
    {
        return candidate != null && candidate.GetComponentInParent<EnemyController>() == this;
    }

    private bool IsColliderOnPlayer(Collider candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        if (candidate.transform == player || (player != null && candidate.transform.IsChildOf(player)))
        {
            return true;
        }

        return playerHealth != null && candidate.GetComponentInParent<PlayerHealth>() == playerHealth;
    }

    // Update is called once per frame
    private void Update()
    {
        if (isDead || player == null)
        {
            playerCanSeeEnemy = false;
            if (horrorAudio != null)
            {
                horrorAudio.SetChasing(false);
            }
            return;
        }

        if (playerHealth == null)
        {
            playerHealth = player.GetComponent<PlayerHealth>();
        }
        if (playerHealth != null && playerHealth.IsDead)
        {
            playerCanSeeEnemy = false;
            if (horrorAudio != null)
            {
                horrorAudio.SetChasing(false);
            }
            StopNavigation();
            SetMotionState(0);
            return;
        }

        UpdatePlayerVisibility();
        playerInSightRange = Physics.CheckSphere(transform.position, sightRange, whatIsPlayer);
        playerInAttackRange = Physics.CheckSphere(transform.position, attackRange, whatIsPlayer);

        if (Time.time < hitReactionUntil)
        {
            return;
        }

        bool canAttack = playerInAttackRange && HasClearLineToPlayer();
        if (!playerInSightRange && !playerInAttackRange)
        {
            if (horrorAudio != null)
            {
                horrorAudio.SetChasing(false);
            }
            Pratroling();
        }
        else if (canAttack)
        {
            if (horrorAudio != null)
            {
                horrorAudio.SetChasing(false);
            }
            AttackPlayer();
        }
        else if (!playerCanSeeEnemy)
        {
            if (horrorAudio != null)
            {
                horrorAudio.SetChasing(true);
            }
            PhaseChasePlayer();
        }
        else
        {
            if (horrorAudio != null)
            {
                horrorAudio.SetChasing(true);
            }
            ChasePlayer();
        }
    }

    private void UpdatePlayerVisibility()
    {
        bool rawVisible = EvaluatePlayerVisibility();
        if (rawVisible != rawPlayerVisibility)
        {
            rawPlayerVisibility = rawVisible;
            rawVisibilitySince = Time.unscaledTime;
        }

        if (rawVisible == playerCanSeeEnemy)
        {
            return;
        }

        float requiredDelay = rawVisible ? phaseExitDelay : phaseEnterDelay;
        if (Time.unscaledTime - rawVisibilitySince >= requiredDelay)
        {
            playerCanSeeEnemy = rawVisible;
        }
    }
}

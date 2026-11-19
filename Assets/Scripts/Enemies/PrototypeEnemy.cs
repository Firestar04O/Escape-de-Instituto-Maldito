using UnityEngine;
using UnityEngine.AI;

public enum EnemyRole
{
    Chaser,
    Ambusher,
    Cutter,
    Kidnapper,
    Guard
}

public class PrototypeEnemy : MonoBehaviour
{
    private static int nextSnapshotId = 1;
    private SchoolEscapePrototype session;
    public bool IsSmall;
    private CharacterController controller;
    private NavMeshAgent agent;
    private float stunnedUntil;
    private float nextHit;
    private float nextDestinationUpdate;
    private float nextNavMeshRecovery;
    private PrototypeActor dragTarget;
    private PrototypeActor priorityTarget;
    public EnemyRole Role { get; private set; } = EnemyRole.Chaser;
    public int SnapshotId { get; private set; }

    public void Initialize(SchoolEscapePrototype gameSession)
    {
        session = gameSession;
        ConfigureMovement();
    }

    private void Awake()
    {
        SnapshotId = nextSnapshotId++;
        controller = GetComponent<CharacterController>();
        agent = GetComponent<NavMeshAgent>();
    }

    private void Update()
    {
        if (session == null || session.Victory || session.Defeat)
        {
            if (HasNavigation) agent.isStopped = true;
            return;
        }
        if (Time.time < stunnedUntil)
        {
            if (HasNavigation) agent.isStopped = true;
            return;
        }
        if (HasNavigation && agent.isStopped) agent.isStopped = false;
        PrototypePlayer player = session.Player;
        PrototypeAlly ally = session.Ally;
        if (player == null || ally == null) return;

        if (dragTarget != null)
        {
            Vector3 dragDestination = session.ClosestEnemySpawn(transform.position);
            float draggingSpeed = (IsSmall ? 3.35f : 2.55f) * .5f;
            MoveTowards(dragDestination, draggingSpeed);
            Vector3 dragDirection = dragDestination - transform.position;
            dragDirection.y = 0f;
            dragTarget.DragTo(transform.position, dragDirection.normalized);
            if (dragDirection.magnitude < 1f)
            {
                PrototypeActor captured = dragTarget;
                dragTarget = null;
                captured.Captured(dragDestination);
            }
            return;
        }

        PrototypeActor target = ChooseTarget(player, ally);
        if (target == null) return;
        Vector3 destination = GetRoleDestination(target);
        float speed = IsSmall ? 3.35f : 2.55f;
        MoveTowards(destination, speed);

        Vector3 direction = target.transform.position - transform.position;
        direction.y = 0f;

        if (direction.magnitude < 1.05f && Time.time > nextHit)
        {
            nextHit = Time.time + 1.25f;
            if (target.IsDown && target.StartDrag(this))
                dragTarget = target;
            else
                target.Damage(1);
        }
    }

    private PrototypeActor ChooseTarget(PrototypePlayer player, PrototypeAlly ally)
    {
        if (Role == EnemyRole.Kidnapper && IsFragileTarget(priorityTarget))
            return priorityTarget;

        if (Role == EnemyRole.Kidnapper)
        {
            PrototypeActor fragilePlayer = IsFragileTarget(player) ? player : null;
            PrototypeActor fragileAlly = IsFragileTarget(ally) ? ally : null;
            if (fragilePlayer != null && fragileAlly != null)
                return Closest(fragilePlayer, fragileAlly);
            if (fragilePlayer != null) return fragilePlayer;
            if (fragileAlly != null) return fragileAlly;
        }

        bool playerAvailable = player.CanDrawEnemyAggro;
        bool allyAvailable = ally.CanDrawEnemyAggro;
        if (!playerAvailable) return allyAvailable ? ally : null;
        if (!allyAvailable) return player;
        return Closest(player, ally);
    }

    private PrototypeActor Closest(PrototypeActor first, PrototypeActor second) =>
        Vector3.Distance(transform.position, first.transform.position) <= Vector3.Distance(transform.position, second.transform.position) ? first : second;

    private static bool IsFragileTarget(PrototypeActor actor) =>
        actor != null && actor.CanDrawEnemyAggro && actor.Health <= 1;

    private Vector3 GetRoleDestination(PrototypeActor target)
    {
        Vector3 targetPosition = target.transform.position;
        Vector3 forward = target.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < .05f) forward = (targetPosition - transform.position).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
        float sideSign = (GetInstanceID() & 1) == 0 ? 1f : -1f;

        switch (Role)
        {
            case EnemyRole.Ambusher:
                return targetPosition + forward.normalized * 3.6f;
            case EnemyRole.Cutter:
                return targetPosition + forward.normalized * 2.2f + side * sideSign * 2.7f;
            default:
                return targetPosition;
        }
    }

    private bool HasNavigation => agent != null && agent.enabled && agent.isOnNavMesh;

    private void ConfigureMovement()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (controller == null) controller = GetComponent<CharacterController>();
        if (agent == null) return;

        agent.speed = IsSmall ? 3.35f : 2.55f;
        agent.autoTraverseOffMeshLink = true;
        agent.autoRepath = true;
        agent.updatePosition = true;
        agent.updateRotation = true;
        agent.areaMask = NavMesh.AllAreas;
        if (controller != null) controller.enabled = false;

        Vector3 requestedPosition = transform.position;
        if (agent.enabled) agent.enabled = false;
        Vector3 navigablePosition = requestedPosition;
        bool foundPosition;
        if (session != null)
        {
            foundPosition = session.TryGetNavigablePoint(requestedPosition, out navigablePosition, 24f);
        }
        else
        {
            foundPosition = NavMesh.SamplePosition(requestedPosition, out NavMeshHit hit, 24f, NavMesh.AllAreas);
            if (foundPosition) navigablePosition = hit.position;
        }
        if (!foundPosition)
        {
            nextNavMeshRecovery = Time.time + .5f;
            return;
        }

        transform.position = navigablePosition;
        agent.enabled = true;
        if (!agent.isOnNavMesh || !agent.Warp(navigablePosition))
        {
            agent.enabled = false;
            nextNavMeshRecovery = Time.time + .5f;
        }
    }

    private void MoveTowards(Vector3 destination, float speed)
    {
        if (!HasNavigation && agent != null && Time.time >= nextNavMeshRecovery)
        {
            nextNavMeshRecovery = Time.time + .5f;
            ConfigureMovement();
        }

        if (HasNavigation)
        {
            agent.speed = speed;
            if (Time.time >= nextDestinationUpdate)
            {
                nextDestinationUpdate = Time.time + Random.Range(.18f, .32f);
                if (session.TryGetNavigablePoint(destination, out Vector3 navigableDestination, 8f))
                    agent.SetDestination(navigableDestination);
            }
            return;
        }
    }

    public void ConfigureRole(EnemyRole role, PrototypeActor target)
    {
        Role = role;
        priorityTarget = role == EnemyRole.Kidnapper ? target : null;
    }

    public void ForgetTarget(PrototypeActor capturedActor)
    {
        if (capturedActor == null) return;
        if (dragTarget == capturedActor) dragTarget = null;
        if (priorityTarget == capturedActor) priorityTarget = null;
        nextDestinationUpdate = 0f;
        nextHit = Time.time + .25f;
        if (HasNavigation && agent.hasPath)
            agent.ResetPath();
    }

    public void HitByFlash()
    {
        if (dragTarget != null)
        {
            dragTarget.RescueFromDrag();
            dragTarget = null;
        }
        if (IsSmall)
        {
            session.UnregisterEnemy(this);
            Destroy(gameObject);
        }
        else
        {
            stunnedUntil = Time.time + 1.2f;
            if (HasNavigation) agent.isStopped = true;
        }
    }

    public void StopDragging(PrototypeActor actor)
    {
        if (dragTarget == actor) dragTarget = null;
    }
}


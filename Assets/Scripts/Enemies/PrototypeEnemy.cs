using UnityEngine;

public class PrototypeEnemy : MonoBehaviour
{
    private SchoolEscapePrototype session;
    public bool IsSmall;
    private CharacterController controller;
    private float stunnedUntil;
    private float nextHit;
    private PrototypeActor dragTarget;

    public void Initialize(SchoolEscapePrototype gameSession) => session = gameSession;

    private void Awake() => controller = GetComponent<CharacterController>();

    private void Update()
    {
        if (session == null || session.Victory || Time.time < stunnedUntil) return;
        PrototypePlayer player = session.Player;
        PrototypeAlly ally = session.Ally;
        if (player == null || ally == null) return;

        if (dragTarget != null)
        {
            Vector3 destination = session.ClosestEnemySpawn(transform.position);
            Vector3 delta = destination - transform.position;
            delta.y = 0f;
            float draggingSpeed = (IsSmall ? 3.35f : 2.55f) * .5f;
            if (delta.sqrMagnitude > .1f) transform.rotation = Quaternion.LookRotation(delta);
            controller.Move(delta.normalized * draggingSpeed * Time.deltaTime);
            dragTarget.DragTo(transform.position, delta.normalized);
            if (delta.magnitude < 1f)
            {
                PrototypeActor captured = dragTarget;
                dragTarget = null;
                captured.Captured(destination);
            }
            return;
        }

        PrototypeActor target = ChooseTarget(player, ally);
        if (target == null) return;
        Vector3 direction = target.transform.position - transform.position;
        direction.y = 0f;
        float speed = IsSmall ? 3.35f : 2.55f;
        controller.Move(direction.normalized * speed * Time.deltaTime);
        if (direction.sqrMagnitude > .1f) transform.rotation = Quaternion.LookRotation(direction);

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
        PrototypeActor downedPlayer = player.CanBeDragged ? player : null;
        PrototypeActor downedAlly = ally.CanBeDragged ? ally : null;
        if (downedPlayer != null && downedAlly != null)
            return Vector3.Distance(transform.position, player.transform.position) <= Vector3.Distance(transform.position, ally.transform.position) ? player : ally;
        if (downedPlayer != null) return downedPlayer;
        if (downedAlly != null) return downedAlly;

        bool playerAvailable = !player.IsDown && !player.IsCaptive;
        bool allyAvailable = !ally.IsDown && !ally.IsCaptive;
        if (!playerAvailable) return allyAvailable ? ally : null;
        if (!allyAvailable) return player;
        return Vector3.Distance(transform.position, player.transform.position) <= Vector3.Distance(transform.position, ally.transform.position) ? player : ally;
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
            session.ShowMessage("Cultista aturdido 1.2 s", 1.2f);
        }
    }

    public void StopDragging(PrototypeActor actor)
    {
        if (dragTarget == actor) dragTarget = null;
    }
}


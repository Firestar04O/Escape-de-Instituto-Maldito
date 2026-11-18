using UnityEngine;

public abstract class PrototypeActor : MonoBehaviour
{
    protected SchoolEscapePrototype Session { get; private set; }
    public int Health { get; protected set; } = 3;
    public bool IsDown => Health <= 0;
    public bool IsCaptive { get; protected set; }
    public PrototypeEnemy Dragger { get; private set; }
    public bool IsBeingDragged => Dragger != null;
    public bool CanDrawEnemyAggro => !IsCaptive && !IsBeingDragged;
    public bool CanBeDragged => IsDown && !IsCaptive && Dragger == null;
    protected CharacterController Controller;
    private float nextDamageTime;

    protected virtual void Awake() => Controller = GetComponent<CharacterController>();

    public virtual void Initialize(SchoolEscapePrototype session) => Session = session;

    public virtual void Damage(int amount)
    {
        if (Time.time < nextDamageTime || IsDown) return;
        nextDamageTime = Time.time + 1f;
        Health = Mathf.Max(0, Health - amount);
        if (IsDown) OnDowned();
    }

    protected virtual void OnDowned() { }
    public void HealFull() => Health = 3;

    public virtual bool StartDrag(PrototypeEnemy enemy)
    {
        if (!CanBeDragged) return false;
        Dragger = enemy;
        if (Controller != null) Controller.enabled = false;
        return true;
    }

    public void DragTo(Vector3 position, Vector3 draggerForward)
    {
        draggerForward.y = 0f;
        if (draggerForward.sqrMagnitude < .01f) draggerForward = Vector3.forward;
        transform.position = position - draggerForward.normalized * .65f;
    }

    public virtual void Captured(Vector3 spawnPosition)
    {
        IsCaptive = true;
        Dragger = null;
        transform.position = spawnPosition + Vector3.up * .2f;
    }

    public void ReleaseFromDrag()
    {
        PrototypeEnemy previousDragger = Dragger;
        if (previousDragger != null)
            transform.position = FindSafeReleasePosition(previousDragger);

        Dragger = null;
        previousDragger?.StopDragging(this);
        if (Controller != null) Controller.enabled = true;
    }

    public virtual void RescueFromDrag()
    {
        ReleaseFromDrag();
        Health = 1;
        nextDamageTime = Time.time + 1.5f;
    }

    private Vector3 FindSafeReleasePosition(PrototypeEnemy dragger)
    {
        Vector3 forward = dragger.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 origin = dragger.transform.position;
        Vector3[] candidates =
        {
            origin - forward * 1.15f,
            origin + right * 1.15f,
            origin - right * 1.15f,
            origin + forward * 1.15f,
            origin
        };

        foreach (Vector3 candidate in candidates)
        {
            Vector3 bottom = candidate + Vector3.up * .45f;
            Vector3 top = candidate + Vector3.up * 1.45f;
            Collider[] overlaps = Physics.OverlapCapsule(bottom, top, .38f, ~0, QueryTriggerInteraction.Ignore);
            bool blocked = false;
            foreach (Collider overlap in overlaps)
            {
                if (overlap == null || overlap.gameObject == gameObject || overlap.gameObject == dragger.gameObject)
                    continue;
                blocked = true;
                break;
            }

            if (!blocked) return candidate;
        }

        return origin;
    }

    public virtual void Rescue()
    {
        IsCaptive = false;
        ReleaseFromDrag();
        Health = 1;
    }

    protected void Move(Vector3 velocity)
    {
        if (Controller != null && Controller.enabled)
            Controller.Move(velocity * Time.deltaTime);
    }
}


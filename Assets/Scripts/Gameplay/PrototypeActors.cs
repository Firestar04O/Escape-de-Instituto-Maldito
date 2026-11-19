using UnityEngine;

public abstract class PrototypeActor : MonoBehaviour
{
    protected SchoolEscapePrototype Session { get; private set; }
    public int Health { get; protected set; } = 3;
    public bool IsDown => Health <= 0;
    public bool IsCaptive { get; protected set; }
    public bool IsEscaped { get; private set; }
    public bool PresentationVisible { get; private set; } = true;
    public PrototypeEnemy Dragger { get; private set; }
    public PrototypeRescue RescueInteraction { get; private set; }
    public bool IsBeingDragged => Dragger != null;
    public bool CanDrawEnemyAggro => !IsCaptive && !IsEscaped && !IsBeingDragged;
    public bool CanBeDragged => IsDown && !IsCaptive && !IsEscaped && Dragger == null;
    public bool CanAct => !IsDown && !IsCaptive && !IsEscaped && Dragger == null;
    protected CharacterController Controller;
    private float nextDamageTime;

    protected virtual void Awake()
    {
        Controller = GetComponent<CharacterController>();
        RescueInteraction = GetComponent<PrototypeRescue>();
        if (RescueInteraction == null) RescueInteraction = gameObject.AddComponent<PrototypeRescue>();
        RescueInteraction.enabled = false;
    }

    public virtual void Initialize(SchoolEscapePrototype session)
    {
        Session = session;
        RescueInteraction.Initialize(session);
    }

    public virtual void Damage(int amount)
    {
        if (Time.time < nextDamageTime || IsDown || IsCaptive || IsEscaped) return;
        nextDamageTime = Time.time + 1f;
        Health = Mathf.Max(0, Health - amount);
        if (IsDown) OnDowned();
    }

    protected virtual void OnDowned()
    {
        RescueInteraction.enabled = true;
    }
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
        if (IsCaptive || IsEscaped) return;
        IsCaptive = true;
        Dragger = null;
        transform.position = spawnPosition + Vector3.up * .2f;
        RescueInteraction.enabled = false;
        SetPresentationVisible(false);
        Session?.NotifyActorCaptured(this);
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
        SetPresentationVisible(true);
        RescueInteraction.enabled = false;
        nextDamageTime = Time.time + 1.5f;
        Session?.NotifyActorRescued(this);
    }

    public void PlaceCaptiveForRescue(Vector3 position)
    {
        if (!IsCaptive) return;
        if (Controller != null) Controller.enabled = false;
        transform.position = position;
        SetPresentationVisible(true);
        if (Controller != null) Controller.enabled = false;
        RescueInteraction.enabled = true;
    }

    public void MarkEscaped()
    {
        if (IsCaptive || IsEscaped) return;
        IsEscaped = true;
        ReleaseFromDrag();
        RescueInteraction.enabled = false;
        SetPresentationVisible(false);
    }

    public void SetPresentationVisible(bool visible)
    {
        PresentationVisible = visible;
        foreach (Renderer targetRenderer in GetComponentsInChildren<Renderer>(true))
            targetRenderer.enabled = visible;
        foreach (Collider targetCollider in GetComponentsInChildren<Collider>(true))
            targetCollider.enabled = visible;
        if (Controller != null) Controller.enabled = visible;
    }

    public void ApplyReplicatedState(int health, bool captive, bool escaped, bool visible)
    {
        Health = Mathf.Clamp(health, 0, 3);
        IsCaptive = captive;
        IsEscaped = escaped;
        SetPresentationVisible(visible);
        if (captive || escaped) RescueInteraction.enabled = false;
    }

    protected void Move(Vector3 velocity)
    {
        if (Controller != null && Controller.enabled)
            Controller.Move(velocity * Time.deltaTime);
    }
}


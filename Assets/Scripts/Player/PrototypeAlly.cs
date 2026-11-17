using UnityEngine;

public class PrototypeAlly : PrototypeActor
{
    public PrototypeRescue RescueInteraction { get; private set; }
    private Vector3 followOffset = new Vector3(1.7f, 0f, -.7f);

    protected override void Awake()
    {
        base.Awake();
        RescueInteraction = gameObject.AddComponent<PrototypeRescue>();
        RescueInteraction.enabled = false;
    }

    public override void Initialize(SchoolEscapePrototype session)
    {
        base.Initialize(session);
        RescueInteraction.Initialize(session);
    }

    private void Update()
    {
        if (Session == null || Session.Victory || IsDown || IsCaptive || Dragger != null) return;
        PrototypePlayer player = Session.Player;
        if (player == null) return;
        Vector3 target = player.transform.position + followOffset;
        Vector3 delta = target - transform.position;
        delta.y = 0f;
        if (delta.magnitude > 1.2f) Move(delta.normalized * 4.3f);
    }

    protected override void OnDowned()
    {
        RescueInteraction.enabled = true;
        Session.ShowMessage("Tu compañero está caído", 3f);
    }

    public override void Captured(Vector3 spawnPosition)
    {
        base.Captured(spawnPosition);
        RescueInteraction.enabled = false;
        SetPresentationVisible(false);
        Session.ShowMessage("Compañero cautivo: el peligro aumenta", 4f);
    }

    public override void Rescue()
    {
        SetPresentationVisible(true);
        base.Rescue();
        RescueInteraction.enabled = false;
        Session.ShowMessage("Compañero rescatado", 3f);
    }

    public override void RescueFromDrag()
    {
        base.RescueFromDrag();
        RescueInteraction.enabled = false;
        Session.ShowMessage("Compañero liberado: 1 corazón e invulnerabilidad temporal", 3f);
    }

    private void SetPresentationVisible(bool visible)
    {
        foreach (Renderer targetRenderer in GetComponentsInChildren<Renderer>(true))
            targetRenderer.enabled = visible;

        foreach (Collider targetCollider in GetComponentsInChildren<Collider>(true))
            targetCollider.enabled = visible;

        if (Controller != null)
            Controller.enabled = visible;
    }
}


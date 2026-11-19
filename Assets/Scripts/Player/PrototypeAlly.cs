using UnityEngine;

public class PrototypeAlly : PrototypeActor
{
    private Vector3 followOffset = new Vector3(1.7f, 0f, -.7f);

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

    public void ApplyNetworkMovement(Vector3 velocity)
    {
        if (velocity.sqrMagnitude > .01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(velocity.normalized), Time.deltaTime * 14f);
        Move(velocity);
    }

    protected override void OnDowned()
    {
        base.OnDowned();
        Session.ShowMessage("Tu compañero está caído", 3f);
    }

    public override void Captured(Vector3 spawnPosition)
    {
        base.Captured(spawnPosition);
        Session.ShowMessage("Compañero cautivo: el peligro aumenta", 4f);
    }

    public override void Rescue()
    {
        base.Rescue();
        Session.ShowMessage("Compañero rescatado", 3f);
    }

    public override void RescueFromDrag()
    {
        base.RescueFromDrag();
        RescueInteraction.enabled = false;
        Session.ShowMessage("Compañero liberado: 1 corazón e invulnerabilidad temporal", 3f);
    }

}


using UnityEngine;

public abstract class PrototypeInteractable : MonoBehaviour
{
    protected SchoolEscapePrototype Session { get; private set; }
    public void Initialize(SchoolEscapePrototype session) => Session = session;
    public virtual bool Available => enabled && gameObject.activeInHierarchy;
    public abstract float Duration { get; }
    public abstract string Prompt { get; }
    public abstract void Complete(PrototypePlayer player);

    public float DistanceOnFloorFrom(Vector3 worldPosition)
    {
        Vector3 closest = transform.position;
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        float bestSqrDistance = float.MaxValue;

        foreach (Collider candidate in colliders)
        {
            if (candidate == null || !candidate.enabled || candidate.isTrigger) continue;
            Vector3 point = candidate.ClosestPoint(worldPosition);
            point.y = worldPosition.y;
            float sqrDistance = (point - worldPosition).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance) continue;
            bestSqrDistance = sqrDistance;
            closest = point;
        }

        closest.y = worldPosition.y;
        return Vector3.Distance(worldPosition, closest);
    }
}

public class PrototypeLocker : PrototypeInteractable
{
    private bool searched;
    public override bool Available => base.Available && !searched;
    public override float Duration => 1.5f;
    public override string Prompt => "Registrando taquilla";

    public override void Complete(PrototypePlayer player)
    {
        if (searched) return;
        searched = true;
        GetComponent<Renderer>().material.color = new Color(.09f, .12f, .13f);
        if (Random.value < .67f) Session.AddBattery();
        else Session.AddBandage();
    }
}

public class PrototypeKey : PrototypeInteractable
{
    public override float Duration => 1.5f;
    public override string Prompt => "Recogiendo llave";
    public override void Complete(PrototypePlayer player)
    {
        Session.CollectKey();
        gameObject.SetActive(false);
    }
}

public class PrototypeExitPanel : PrototypeInteractable
{
    private bool activated;
    public override bool Available => base.Available && !activated;
    public override float Duration => 1.5f;
    public override string Prompt => "Activando panel eléctrico";

    public override void Complete(PrototypePlayer player)
    {
        if (activated) return;
        activated = true;
        Session.ActivateExitPanel(this);
    }
}

public abstract class PrototypeGuaranteedResourceFurniture : PrototypeInteractable
{
    private bool searched;
    public override bool Available => base.Available && !searched;
    public override float Duration => 2f;

    public override void Complete(PrototypePlayer player)
    {
        if (searched) return;
        searched = true;
        SpawnGuaranteedResource(player);

        Renderer targetRenderer = GetComponent<Renderer>();
        if (targetRenderer != null)
            targetRenderer.material.color *= .55f;
    }

    protected abstract void SpawnGuaranteedResource(PrototypePlayer player);
}

public class PrototypeBatteryFurniture : PrototypeGuaranteedResourceFurniture
{
    public override string Prompt => "Abriendo mueble de pilas";
    protected override void SpawnGuaranteedResource(PrototypePlayer player)
    {
        Vector3 position = GetDropPosition(player);
        GameObject pickup = SchoolEscapePrototype.CreateBox(
            "Pila garantizada",
            position,
            new Vector3(.35f, .2f, .2f),
            SchoolEscapePrototype.MakeMaterial(new Color(1f, .82f, .12f)),
            false);
        Session.Register(pickup.AddComponent<PrototypeBatteryWorldPickup>());
    }

    private Vector3 GetDropPosition(PrototypePlayer player)
    {
        Vector3 direction = player != null ? player.transform.position - transform.position : transform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude < .01f) direction = transform.forward;
        return transform.position + direction.normalized * .8f + Vector3.up * .3f;
    }
}

public class PrototypeBandageFurniture : PrototypeGuaranteedResourceFurniture
{
    public override string Prompt => "Abriendo botiquín";
    protected override void SpawnGuaranteedResource(PrototypePlayer player)
    {
        Vector3 direction = player != null ? player.transform.position - transform.position : transform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude < .01f) direction = transform.forward;
        Vector3 position = transform.position + direction.normalized * .8f + Vector3.up * .3f;
        GameObject pickup = SchoolEscapePrototype.CreateBox(
            "Venda garantizada",
            position,
            new Vector3(.4f, .12f, .28f),
            SchoolEscapePrototype.MakeMaterial(new Color(.86f, .95f, .92f)),
            false);
        Session.Register(pickup.AddComponent<PrototypeBandageWorldPickup>());
    }
}

public class PrototypeBatteryWorldPickup : PrototypeInteractable
{
    public override float Duration => .1f;
    public override string Prompt => "Recogiendo pila";

    public override void Complete(PrototypePlayer player)
    {
        Session.AddBattery();
        gameObject.SetActive(false);
    }
}

public class PrototypeBandageWorldPickup : PrototypeInteractable
{
    public override float Duration => .1f;
    public override string Prompt => "Recogiendo venda";

    public override void Complete(PrototypePlayer player)
    {
        Session.AddBandage();
        gameObject.SetActive(false);
    }
}

public class PrototypeDoor : PrototypeInteractable
{
    private bool completed;
    public override bool Available => false;
    public override float Duration => .25f;
    public override string Prompt => "Salida abierta";

    private void Update()
    {
        if (completed || Session == null || !Session.HasKey || Session.Player == null) return;

        Renderer targetRenderer = GetComponent<Renderer>();
        Vector3 closest = targetRenderer != null
            ? targetRenderer.bounds.ClosestPoint(Session.Player.transform.position)
            : transform.position;
        closest.y = Session.Player.transform.position.y;
        if (Vector3.Distance(closest, Session.Player.transform.position) <= 2.2f)
            Complete(Session.Player);
    }

    public override void Complete(PrototypePlayer player)
    {
        if (completed || !Session.HasKey) return;
        completed = true;
        GetComponent<SchoolEscape.Rooms.RoomExitGate>()?.Unlock();
        Session.Win();
    }
}

public class PrototypeRescue : PrototypeInteractable
{
    public override bool Available
    {
        get
        {
            PrototypeAlly ally = GetComponent<PrototypeAlly>();
            return ally != null && (ally.IsDown || ally.IsCaptive);
        }
    }
    public override float Duration => 2.5f;
    public override string Prompt => "Rescatando compañero";
    public override void Complete(PrototypePlayer player) => GetComponent<PrototypeAlly>().Rescue();
}


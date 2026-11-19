using UnityEngine;
using SchoolEscape.Rooms;

public abstract class PrototypeInteractable : MonoBehaviour
{
    protected SchoolEscapePrototype Session { get; private set; }
    public void Initialize(SchoolEscapePrototype session) => Session = session;
    public virtual bool Available => enabled && gameObject.activeInHierarchy;
    public abstract float Duration { get; }
    public abstract string Prompt { get; }
    public abstract void Complete(PrototypeActor actor);

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

    public override void Complete(PrototypeActor actor)
    {
        if (searched) return;
        searched = true;
        Session.AddDirectorActivity(.12f);
        GetComponent<Renderer>().material.color = new Color(.09f, .12f, .13f);
        if (Random.value < .67f) Session.AddBattery(actor);
        else Session.AddBandage(actor);
    }
}

public class PrototypeKey : PrototypeInteractable
{
    public override float Duration => 1.5f;
    public override string Prompt => "Recogiendo llave";
    public override void Complete(PrototypeActor actor)
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

    public override void Complete(PrototypeActor actor)
    {
        if (activated) return;
        activated = true;
        Session.AddDirectorActivity(.35f);
        Session.ActivateExitPanel(this);
    }
}

public abstract class PrototypeGuaranteedResourceFurniture : PrototypeInteractable
{
    private bool searched;
    public override bool Available => base.Available && !searched;
    public override float Duration => 2f;

    public override void Complete(PrototypeActor actor)
    {
        if (searched) return;
        searched = true;
        Session.AddDirectorActivity(.18f);
        SpawnGuaranteedResource(actor);

        Renderer targetRenderer = GetComponent<Renderer>();
        if (targetRenderer != null)
            targetRenderer.material.color *= .55f;
    }

    protected abstract void SpawnGuaranteedResource(PrototypeActor actor);
}

public class PrototypeBatteryFurniture : PrototypeGuaranteedResourceFurniture
{
    public override string Prompt => "Abriendo mueble de pilas";
    protected override void SpawnGuaranteedResource(PrototypeActor actor)
    {
        Vector3 position = GetDropPosition(actor);
        GameObject pickup = SchoolEscapePrototype.CreateBox(
            "Pila garantizada",
            position,
            new Vector3(.35f, .2f, .2f),
            SchoolEscapePrototype.MakeMaterial(new Color(1f, .82f, .12f)),
            false);
        Session.Register(pickup.AddComponent<PrototypeBatteryWorldPickup>());
    }

    private Vector3 GetDropPosition(PrototypeActor actor)
    {
        Vector3 direction = actor != null ? actor.transform.position - transform.position : transform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude < .01f) direction = transform.forward;
        return transform.position + direction.normalized * .8f + Vector3.up * .3f;
    }
}

public class PrototypeBandageFurniture : PrototypeGuaranteedResourceFurniture
{
    public override string Prompt => "Abriendo botiquín";
    protected override void SpawnGuaranteedResource(PrototypeActor actor)
    {
        Vector3 direction = actor != null ? actor.transform.position - transform.position : transform.forward;
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

    public override void Complete(PrototypeActor actor)
    {
        Session.AddBattery(actor);
        gameObject.SetActive(false);
    }
}

public class PrototypeBandageWorldPickup : PrototypeInteractable
{
    public override float Duration => .1f;
    public override string Prompt => "Recogiendo venda";

    public override void Complete(PrototypeActor actor)
    {
        Session.AddBandage(actor);
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
        if (completed || Session == null || !Session.HasKey) return;

        Renderer targetRenderer = GetComponent<Renderer>();
        if (IsActiveActorNear(Session.Player, targetRenderer))
            Complete(Session.Player);
        else if (IsActiveActorNear(Session.Ally, targetRenderer))
            Complete(Session.Ally);
    }

    private bool IsActiveActorNear(PrototypeActor actor, Renderer targetRenderer)
    {
        if (actor == null || actor.IsCaptive || actor.IsDown || actor.Dragger != null) return false;
        Vector3 closest = targetRenderer != null
            ? targetRenderer.bounds.ClosestPoint(actor.transform.position)
            : transform.position;
        closest.y = actor.transform.position.y;
        return Vector3.Distance(closest, actor.transform.position) <= 2.2f;
    }

    public override void Complete(PrototypeActor actor)
    {
        if (completed || !Session.HasKey) return;
        completed = !Session.IsFinalRoom;
        GetComponent<SchoolEscape.Rooms.RoomExitGate>()?.Unlock();
        Session.ActorReachedExit(actor);
    }
}

public class PrototypeRescue : PrototypeInteractable
{
    public override bool Available
    {
        get
        {
            PrototypeActor actor = GetComponent<PrototypeActor>();
            return actor != null && (actor.IsDown || actor.IsCaptive);
        }
    }
    public override float Duration => 2.5f;
    public override string Prompt => "Rescatando compañero";
    public override void Complete(PrototypeActor actor) => GetComponent<PrototypeActor>()?.Rescue();
}

public class PrototypeRescueEntrance : PrototypeInteractable
{
    public RescueEntranceAnchor Anchor { get; private set; }
    public override bool Available => base.Available && Session != null && Session.CanUseRescueEntrance(this);
    public override float Duration => 1.5f;
    public override string Prompt => "Preparando entrada de rescate";

    public void Configure(RescueEntranceAnchor anchor) => Anchor = anchor;

    public override void Complete(PrototypeActor actor)
    {
        Session.AddDirectorActivity(.45f);
        Session.BeginRescueEntranceOpening(this);
    }
}


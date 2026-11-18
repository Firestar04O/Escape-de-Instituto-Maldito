using UnityEngine;

public class PrototypeRitual : MonoBehaviour
{
    private void Update() => transform.Rotate(0f, 25f * Time.deltaTime, 0f);
}

public class PrototypeCamera : MonoBehaviour
{
    private SchoolEscapePrototype session;
    private Vector3 velocity;
    public void Initialize(SchoolEscapePrototype gameSession) => session = gameSession;
    private void LateUpdate()
    {
        if (session == null || session.CameraFollowTarget == null) return;
        Vector3 target = session.CameraFollowTarget.position + new Vector3(0f, 17f, -10f);
        transform.position = Vector3.SmoothDamp(transform.position, target, ref velocity, .18f);
        transform.rotation = Quaternion.Euler(59f, 0f, 0f);
    }
}


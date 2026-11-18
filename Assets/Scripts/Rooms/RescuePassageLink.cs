using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace SchoolEscape.Rooms
{
    [DisallowMultipleComponent]
    public sealed class RescuePassageLink : MonoBehaviour
    {
        private NavMeshLink passageLink;
        private Vector3 regularEndpoint;
        private Vector3 rescueEndpoint;

        public void Configure(Transform doorway, Transform regularRoom, Transform rescueRoom)
        {
            Vector3 doorwayCenter = doorway.position;
            Vector3 regularCenter = GetNavigationCenter(regularRoom, doorwayCenter - doorway.forward * 3f);
            Vector3 rescueCenter = GetNavigationCenter(rescueRoom, doorwayCenter + doorway.forward * 3f);
            Vector3 roomAxis = rescueCenter - regularCenter;
            roomAxis.y = 0f;
            if (roomAxis.sqrMagnitude < .01f)
            {
                roomAxis = doorway.forward;
                roomAxis.y = 0f;
            }
            roomAxis.Normalize();

            Vector3 connectionCenter = doorwayCenter;
            regularEndpoint = FindEndpoint(connectionCenter, regularCenter, -roomAxis);
            rescueEndpoint = FindEndpoint(connectionCenter, rescueCenter, roomAxis);
            connectionCenter = (regularEndpoint + rescueEndpoint) * .5f;

            transform.SetPositionAndRotation(connectionCenter, Quaternion.identity);
            passageLink = GetComponent<NavMeshLink>();
            if (passageLink == null) passageLink = gameObject.AddComponent<NavMeshLink>();
            passageLink.bidirectional = true;
            passageLink.width = 1.8f;
            passageLink.startPoint = regularEndpoint - connectionCenter;
            passageLink.endPoint = rescueEndpoint - connectionCenter;
            passageLink.costModifier = -1f;
            passageLink.autoUpdate = true;
            Debug.Log($"[SchoolEscape/NavMesh] Enlace de rescate: salón {regularEndpoint:F2} ↔ rescate {rescueEndpoint:F2}; hueco cubierto {Vector3.Distance(regularEndpoint, rescueEndpoint):0.00} m.");
            SetTraversable(false);
        }

        public void SetTraversable(bool traversable)
        {
            if (passageLink == null) passageLink = GetComponent<NavMeshLink>();
            if (passageLink == null) return;

            passageLink.enabled = traversable;
            if (!traversable) return;

            NavMeshPath path = new NavMeshPath();
            bool foundPath = NavMesh.CalculatePath(regularEndpoint, rescueEndpoint, NavMesh.AllAreas, path);
            Debug.Log(foundPath && path.status == NavMeshPathStatus.PathComplete
                ? "[SchoolEscape/NavMesh] Ruta bidireccional hacia Mapa_Rescate validada."
                : $"[SchoolEscape/NavMesh] El enlace se activó, pero la ruta quedó {path.status}.");
        }

        private static Vector3 GetNavigationCenter(Transform roomRoot, Vector3 fallback)
        {
            if (roomRoot == null) return fallback;
            NavMeshSurface surface = roomRoot.GetComponentInChildren<NavMeshSurface>(true);
            if (surface == null || surface.navMeshData == null) return roomRoot.position;
            return surface.transform.TransformPoint(surface.navMeshData.sourceBounds.center);
        }

        private static Vector3 FindEndpoint(Vector3 doorway, Vector3 roomCenter, Vector3 fallbackDirection)
        {
            Vector3 direction = roomCenter - doorway;
            direction.y = 0f;
            if (direction.sqrMagnitude < .01f) direction = fallbackDirection;
            direction.Normalize();

            float[] distances = { 1.25f, 2f, 3f, 4.5f };
            foreach (float distance in distances)
            {
                Vector3 probe = doorway + direction * distance;
                if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 1.1f, NavMesh.AllAreas)) continue;
                Vector3 fromDoor = hit.position - doorway;
                fromDoor.y = 0f;
                if (Vector3.Dot(fromDoor, direction) > .1f) return hit.position;
            }

            if (NavMesh.SamplePosition(roomCenter, out NavMeshHit centerHit, 12f, NavMesh.AllAreas))
                return centerHit.position;
            return doorway + direction * 1.25f;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace SchoolEscape.Rooms
{
    [DisallowMultipleComponent]
    public sealed class RescueRoomDefinition : MonoBehaviour
    {
        [SerializeField] private List<RescueDoorAnchor> doors = new List<RescueDoorAnchor>();
        [SerializeField] private Transform captiveSpawn;
        public Transform CaptiveSpawn => captiveSpawn;
        public IReadOnlyList<RescueDoorAnchor> Doors => doors;

        public void Configure(IEnumerable<RescueDoorAnchor> directionalDoors, Transform targetSpawn)
        {
            doors.Clear();
            if (directionalDoors != null)
                foreach (RescueDoorAnchor door in directionalDoors)
                    if (door != null && !doors.Contains(door)) doors.Add(door);
            captiveSpawn = targetSpawn;
        }

        public RescueDoorAnchor GetDoor(RescueDirection direction)
        {
            foreach (RescueDoorAnchor door in doors)
                if (door != null && door.Direction == direction) return door;
            return null;
        }
    }
}

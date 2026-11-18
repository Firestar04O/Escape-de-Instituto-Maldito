using System.Collections.Generic;
using UnityEngine;

namespace SchoolEscape.Rooms
{
    [CreateAssetMenu(fileName = "RoomSequenceCatalog", menuName = "School Escape/Room Sequence Catalog")]
    public sealed class RoomSequenceCatalog : ScriptableObject
    {
        [SerializeField] private List<GameObject> rooms = new List<GameObject>();
        [SerializeField] private GameObject rescueRoom;

        public int Count => rooms.Count;
        public GameObject RescueRoom => rescueRoom;

        public GameObject GetRoom(int index)
        {
            return index >= 0 && index < rooms.Count ? rooms[index] : null;
        }

        public void Configure(IReadOnlyList<GameObject> orderedRooms, GameObject rescueRoomPrefab = null)
        {
            rooms.Clear();
            if (orderedRooms == null) return;

            for (int index = 0; index < orderedRooms.Count; index++)
                rooms.Add(orderedRooms[index]);
            rescueRoom = rescueRoomPrefab;
        }
    }
}

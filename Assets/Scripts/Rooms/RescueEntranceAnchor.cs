using UnityEngine;

namespace SchoolEscape.Rooms
{
    [DisallowMultipleComponent]
    public sealed class RescueEntranceAnchor : MonoBehaviour
    {
        [SerializeField] private RescueDirection direction;
        [SerializeField] private RoomExitGate gate;
        public RescueDirection Direction => direction;
        public RoomExitGate Gate => gate;

        public void Configure(RescueDirection value, RoomExitGate entranceGate)
        {
            direction = value;
            gate = entranceGate;
        }
    }
}

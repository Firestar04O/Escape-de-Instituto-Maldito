using UnityEngine;

namespace SchoolEscape.Rooms
{
    [DisallowMultipleComponent]
    public sealed class RescueDoorAnchor : MonoBehaviour
    {
        [SerializeField] private RescueDirection direction;
        [SerializeField] private RoomExitGate gate;
        public RescueDirection Direction => direction;
        public RoomExitGate Gate => gate;

        public void Configure(RescueDirection value, RoomExitGate doorGate)
        {
            direction = value;
            gate = doorGate;
        }
    }
}

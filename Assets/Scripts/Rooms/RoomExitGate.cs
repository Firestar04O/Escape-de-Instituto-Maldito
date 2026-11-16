using UnityEngine;
using UnityEngine.Events;

namespace SchoolEscape.Rooms
{
    [DisallowMultipleComponent]
    public sealed class RoomExitGate : MonoBehaviour
    {
        [SerializeField] private bool unlocked;
        [SerializeField] private UnityEvent onUnlocked = new UnityEvent();

        public bool IsUnlocked => unlocked;

        public void ResetGate()
        {
            unlocked = false;
        }

        public void Unlock()
        {
            if (unlocked) return;
            unlocked = true;
            onUnlocked.Invoke();
        }
    }
}

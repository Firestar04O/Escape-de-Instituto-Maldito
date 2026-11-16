using System;
using UnityEngine;

namespace SchoolEscape.Rooms
{
    public enum RoomObjectiveState
    {
        Locked,
        Activating,
        Unlocked
    }

    /// <summary>
    /// Owns only the shared exit-panel objective. The interaction system supplies
    /// validated progress; a future server-authoritative adapter can call the same API.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomObjectiveController : MonoBehaviour
    {
        [SerializeField] private SchoolRoomDefinition room;
        [SerializeField, Min(0.1f)] private float activationDuration = 1.5f;
        [SerializeField, Min(0.1f)] private float maximumActivationDistance = 2.2f;

        private Transform activatingPlayer;
        private float progress;

        public RoomObjectiveState State { get; private set; } = RoomObjectiveState.Locked;
        public float Progress01 => activationDuration <= 0f ? 1f : Mathf.Clamp01(progress / activationDuration);
        public event Action<RoomObjectiveState> StateChanged;

        public void Configure(SchoolRoomDefinition definition)
        {
            room = definition;
        }

        public void ResetObjective()
        {
            activatingPlayer = null;
            progress = 0f;
            SetState(RoomObjectiveState.Locked);
        }

        public void UnlockImmediately()
        {
            if (State == RoomObjectiveState.Unlocked) return;
            activatingPlayer = null;
            progress = activationDuration;
            SetState(RoomObjectiveState.Unlocked);
        }

        public bool AdvanceActivation(Transform player, float deltaTime)
        {
            if (State == RoomObjectiveState.Unlocked || player == null || room == null || room.ExitPanel == null)
                return false;

            if (Vector3.Distance(player.position, room.ExitPanel.position) > maximumActivationDistance)
            {
                CancelActivation(player);
                return false;
            }

            if (activatingPlayer != null && activatingPlayer != player)
                return false;

            activatingPlayer = player;
            SetState(RoomObjectiveState.Activating);
            progress += Mathf.Max(0f, deltaTime);
            if (progress < activationDuration) return false;

            progress = activationDuration;
            activatingPlayer = null;
            SetState(RoomObjectiveState.Unlocked);
            return true;
        }

        public void CancelActivation(Transform player)
        {
            if (State != RoomObjectiveState.Activating || activatingPlayer != player) return;
            activatingPlayer = null;
            progress = 0f;
            SetState(RoomObjectiveState.Locked);
        }

        private void SetState(RoomObjectiveState nextState)
        {
            if (State == nextState) return;
            State = nextState;
            StateChanged?.Invoke(State);
        }
    }
}

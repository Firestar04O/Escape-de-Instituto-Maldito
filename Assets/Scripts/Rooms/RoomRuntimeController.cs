using System;
using System.Collections.Generic;
using UnityEngine;

namespace SchoolEscape.Rooms
{
    public enum RoomRuntimeState
    {
        NotStarted,
        Exploring,
        ExitUnlocked,
        Completed
    }

    /// <summary>
    /// Coordinates the authored room and its objective. It deliberately does not own
    /// player inventory, enemy AI, UI or audio.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SchoolRoomDefinition))]
    public sealed class RoomRuntimeController : MonoBehaviour
    {
        [SerializeField] private SchoolRoomDefinition definition;
        [SerializeField] private RoomObjectiveController objective;
        [SerializeField] private List<RoomExitGate> exits = new List<RoomExitGate>();

        public SchoolRoomDefinition Definition => definition;
        public RoomRuntimeState State { get; private set; } = RoomRuntimeState.NotStarted;
        public event Action<RoomRuntimeState> StateChanged;

        public void Configure(
            SchoolRoomDefinition roomDefinition,
            RoomObjectiveController roomObjective,
            IEnumerable<RoomExitGate> roomExits)
        {
            definition = roomDefinition;
            objective = roomObjective;
            exits.Clear();
            foreach (RoomExitGate roomExit in roomExits)
                if (roomExit != null) exits.Add(roomExit);
        }

        private void Awake()
        {
            if (definition == null) definition = GetComponent<SchoolRoomDefinition>();
            if (objective == null) objective = GetComponent<RoomObjectiveController>();
        }

        private void OnEnable()
        {
            if (objective != null) objective.StateChanged += HandleObjectiveStateChanged;
        }

        private void OnDisable()
        {
            if (objective != null) objective.StateChanged -= HandleObjectiveStateChanged;
        }

        public void BeginRoom()
        {
            objective?.ResetObjective();
            foreach (RoomExitGate exit in exits)
                if (exit != null) exit.ResetGate();
            SetState(RoomRuntimeState.Exploring);
        }

        public bool TryCompleteRoom()
        {
            if (State != RoomRuntimeState.ExitUnlocked) return false;
            SetState(RoomRuntimeState.Completed);
            return true;
        }

        private void HandleObjectiveStateChanged(RoomObjectiveState objectiveState)
        {
            if (objectiveState != RoomObjectiveState.Unlocked) return;
            foreach (RoomExitGate exit in exits)
                if (exit != null) exit.Unlock();
            SetState(RoomRuntimeState.ExitUnlocked);
        }

        private void SetState(RoomRuntimeState nextState)
        {
            if (State == nextState) return;
            State = nextState;
            StateChanged?.Invoke(State);
        }
    }
}

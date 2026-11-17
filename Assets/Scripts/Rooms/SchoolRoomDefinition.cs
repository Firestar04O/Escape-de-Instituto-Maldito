using System.Collections.Generic;
using UnityEngine;

namespace SchoolEscape.Rooms
{
    public enum RoomArchetype
    {
        Classroom,
        Cafeteria,
        RiskRewardHallway
    }

    /// <summary>
    /// Scene references and authored content for one room. This component contains no
    /// match, player, enemy or UI state, so the room prefab stays reusable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SchoolRoomDefinition : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string roomId = "aula_a";
        [SerializeField] private RoomArchetype archetype = RoomArchetype.Classroom;

        [Header("Required references")]
        [SerializeField] private Transform playerSpawnRoot;
        [SerializeField] private Transform enemySpawnRoot;
        [SerializeField] private Transform exitPanel;
        [SerializeField] private List<Transform> exitPanels = new List<Transform>();
        [SerializeField] private List<Transform> exits = new List<Transform>();

        [Header("Content groups")]
        [SerializeField] private Transform randomInteractablesRoot;
        [SerializeField] private Transform guaranteedBatteryRoot;
        [SerializeField] private Transform guaranteedBandageRoot;
        [SerializeField] private Transform obstaclesRoot;

        public string RoomId => roomId;
        public RoomArchetype Archetype => archetype;
        public Transform ExitPanel => exitPanel;
        public IReadOnlyList<Transform> ExitPanels => exitPanels;
        public IReadOnlyList<Transform> Exits => exits;
        public Transform RandomInteractablesRoot => randomInteractablesRoot;
        public Transform GuaranteedBatteryRoot => guaranteedBatteryRoot;
        public Transform GuaranteedBandageRoot => guaranteedBandageRoot;
        public Transform ObstaclesRoot => obstaclesRoot;
        public int PlayerSpawnCount => playerSpawnRoot == null ? 0 : playerSpawnRoot.childCount;
        public int EnemySpawnCount => enemySpawnRoot == null ? 0 : enemySpawnRoot.childCount;

        public void ConfigureIdentity(string id, RoomArchetype roomArchetype)
        {
            roomId = id;
            archetype = roomArchetype;
        }

        public Transform GetPlayerSpawn(int playerIndex)
        {
            if (PlayerSpawnCount == 0) return null;
            return playerSpawnRoot.GetChild(Mathf.Abs(playerIndex) % PlayerSpawnCount);
        }

        public Transform GetEnemySpawn(int spawnIndex)
        {
            if (EnemySpawnCount == 0) return null;
            return enemySpawnRoot.GetChild(Mathf.Abs(spawnIndex) % EnemySpawnCount);
        }

        [ContextMenu("Auto Configure From Hierarchy")]
        public void AutoConfigureFromHierarchy()
        {
            playerSpawnRoot = FindByName("PlayersSpawn", "PlayerSpawns");
            enemySpawnRoot = FindByName("EnemySpawns");
            exitPanels.Clear();
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (IsNumberedName(child.name, "Palanca") || IsNumberedName(child.name, "PanelSalida"))
                    exitPanels.Add(child);
            }
            exitPanel = exitPanels.Count > 0 ? exitPanels[0] : FindByName("Panel");
            randomInteractablesRoot = FindByName("NormalInteratables", "NormalInteractables");
            guaranteedBatteryRoot = FindByName("Pilas", "Baterias", "Batteries");
            guaranteedBandageRoot = FindByName("Botiquines", "Vendas", "Bandages");
            obstaclesRoot = FindByName("Obstáculos", "Obstaculos", "Obstacles");

            exits.Clear();
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child != transform && child.name == "Salida")
                    exits.Add(child);
            }
        }

        public List<string> GetValidationErrors()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(roomId)) errors.Add("Room Id is empty.");
            if (PlayerSpawnCount < 2) errors.Add("The MVP needs at least 2 player spawn points.");
            if (PlayerSpawnCount > 4) errors.Add("The MVP supports at most 4 player spawn points.");
            if (EnemySpawnCount == 0) errors.Add("At least one enemy spawn point is required.");
            if (exitPanels.Count == 0 && exitPanel == null) errors.Add("The exit panel/lever is missing.");
            if (exits.Count == 0 || exits.TrueForAll(item => item == null)) errors.Add("At least one object named Salida is required.");
            if (randomInteractablesRoot == null) errors.Add("The random interactables group is missing.");
            if (guaranteedBatteryRoot == null) errors.Add("The guaranteed battery group is missing.");
            if (guaranteedBandageRoot == null) errors.Add("The guaranteed bandage group is missing.");
            if (obstaclesRoot == null) errors.Add("The obstacle collision group is missing.");
            return errors;
        }

        private Transform FindByName(params string[] acceptedNames)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                foreach (string acceptedName in acceptedNames)
                {
                    if (child.name == acceptedName)
                        return child;
                }
            }
            return null;
        }

        private static bool IsNumberedName(string candidate, string baseName)
        {
            return candidate == baseName || candidate.StartsWith(baseName + " (", System.StringComparison.Ordinal);
        }
    }
}

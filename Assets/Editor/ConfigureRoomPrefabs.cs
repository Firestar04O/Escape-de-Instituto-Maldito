using System;
using System.Linq;
using SchoolEscape.Rooms;
using UnityEditor;
using UnityEngine;

internal static class ConfigureRoomPrefabs
{
    private const string Folder = "Assets/Pruebas/Prefabs";
    private const string ClosedDoorMaterialPath = "Assets/PolygonHorrorMansion/Materials/PuertaCerrada.mat";
    private const string OpenDoorMaterialPath = "Assets/PolygonHorrorMansion/Materials/PuertaAbierta.mat";

    private static readonly RoomEntry[] Rooms =
    {
        new RoomEntry(Folder + "/Salon1.prefab", "salon_1", RoomArchetype.Classroom),
        new RoomEntry(Folder + "/Salon2.prefab", "salon_2", RoomArchetype.Classroom),
        new RoomEntry(Folder + "/Salon3.prefab", "salon_3", RoomArchetype.Classroom),
        new RoomEntry(Folder + "/Cafeteria1.prefab", "cafeteria_1", RoomArchetype.Cafeteria)
    };

    [MenuItem("Tools/School Escape/Configure All Room Prefabs")]
    private static void ConfigureAll()
    {
        Material closedDoorMaterial = AssetDatabase.LoadAssetAtPath<Material>(ClosedDoorMaterialPath);
        Material openDoorMaterial = AssetDatabase.LoadAssetAtPath<Material>(OpenDoorMaterialPath);
        foreach (RoomEntry entry in Rooms)
            Configure(entry, closedDoorMaterial, openDoorMaterial);

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog(
            "Room prefabs configured",
            "Configured Salon1, Salon2, Salon3 and Cafeteria1. See Console for validation details.",
            "OK");
    }

    private static void Configure(RoomEntry entry, Material closedDoorMaterial, Material openDoorMaterial)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(entry.Path);
        try
        {
            SchoolRoomDefinition definition = GetOrAdd<SchoolRoomDefinition>(root);
            definition.ConfigureIdentity(entry.Id, entry.Archetype);
            definition.AutoConfigureFromHierarchy();

            RoomObjectiveController objective = GetOrAdd<RoomObjectiveController>(root);
            objective.Configure(definition);

            RoomExitGate[] exits = definition.Exits
                .Where(item => item != null)
                .Select(item => GetOrAdd<RoomExitGate>(item.gameObject))
                .ToArray();

            foreach (RoomExitGate exit in exits)
            {
                Renderer targetRenderer = exit.GetComponent<Renderer>();
                exit.ConfigureVisuals(
                    targetRenderer,
                    1,
                    closedDoorMaterial,
                    openDoorMaterial,
                    exit.GetComponent<Collider>());
            }

            RoomRuntimeController runtime = GetOrAdd<RoomRuntimeController>(root);
            runtime.Configure(definition, objective, exits);

            EditorUtility.SetDirty(definition);
            EditorUtility.SetDirty(objective);
            EditorUtility.SetDirty(runtime);
            foreach (RoomExitGate exit in exits) EditorUtility.SetDirty(exit);

            PrefabUtility.SaveAsPrefabAsset(root, entry.Path);
            string errors = string.Join(" | ", definition.GetValidationErrors());
            if (string.IsNullOrEmpty(errors))
            {
                Debug.Log($"[School Escape] {root.name}: {definition.PlayerSpawnCount} player spawns, " +
                          $"{definition.EnemySpawnCount} enemy spawns, {definition.ExitPanels.Count} levers, " +
                          $"{exits.Length} exits. Validation passed.");
            }
            else
            {
                Debug.LogError($"[School Escape] {root.name}: {errors}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private readonly struct RoomEntry
    {
        public RoomEntry(string path, string id, RoomArchetype archetype)
        {
            Path = path;
            Id = id;
            Archetype = archetype;
        }

        public string Path { get; }
        public string Id { get; }
        public RoomArchetype Archetype { get; }
    }
}

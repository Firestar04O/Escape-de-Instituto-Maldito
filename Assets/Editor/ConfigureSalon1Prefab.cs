using System.Linq;
using SchoolEscape.Rooms;
using UnityEditor;
using UnityEngine;

public static class ConfigureSalon1Prefab
{
    private const string PrefabPath = "Assets/Pruebas/Prefabs/Salon1.prefab";

    [MenuItem("Tools/School Escape/Configure Salon1 Prefab")]
    public static void Configure()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            SchoolRoomDefinition definition = GetOrAdd<SchoolRoomDefinition>(root);
            definition.AutoConfigureFromHierarchy();

            RoomObjectiveController objective = GetOrAdd<RoomObjectiveController>(root);
            objective.Configure(definition);

            RoomExitGate[] exits = definition.Exits
                .Where(item => item != null)
                .Select(item => GetOrAdd<RoomExitGate>(item.gameObject))
                .ToArray();

            RoomRuntimeController runtime = GetOrAdd<RoomRuntimeController>(root);
            runtime.Configure(definition, objective, exits);

            EditorUtility.SetDirty(definition);
            EditorUtility.SetDirty(objective);
            EditorUtility.SetDirty(runtime);
            foreach (RoomExitGate exit in exits) EditorUtility.SetDirty(exit);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[School Escape] Salon1 configured: {definition.PlayerSpawnCount} player spawns, " +
                      $"{definition.EnemySpawnCount} enemy spawns, {exits.Length} exits.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Validate();
    }

    [MenuItem("Tools/School Escape/Validate Salon1 Prefab")]
    public static void Validate()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[School Escape] Prefab not found: {PrefabPath}");
            return;
        }

        SchoolRoomDefinition definition = prefab.GetComponent<SchoolRoomDefinition>();
        if (definition == null)
        {
            Debug.LogError("[School Escape] Salon1 has no SchoolRoomDefinition component.");
            return;
        }

        var errors = definition.GetValidationErrors();
        if (errors.Count == 0)
        {
            Debug.Log("[School Escape] Salon1 passes the structural MVP validation.");
            return;
        }

        foreach (string error in errors)
            Debug.LogError($"[School Escape] Salon1: {error}");
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }
}

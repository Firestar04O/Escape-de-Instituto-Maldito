using System.Collections.Generic;
using SchoolEscape.Rooms;
using UnityEditor;
using UnityEngine;

public static class ConfigureRescueSystem
{
    private const string PrefabFolder = "Assets/Pruebas/Prefabs";
    private const string RescuePath = PrefabFolder + "/Mapa_Rescate.prefab";
    private const string ClosedDoorMaterialPath = "Assets/PolygonHorrorMansion/Materials/PuertaCerrada.mat";
    private const string OpenDoorMaterialPath = "Assets/PolygonHorrorMansion/Materials/PuertaAbierta.mat";
    private static readonly string[] RegularRoomNames =
    {
        "Mapa_Salon1", "Mapa_Salon2", "Mapa_Salon3",
        "Mapa_Cafeteria1", "Mapa_Cafeteria2", "Mapa_Cafeteria3"
    };

    [MenuItem("Tools/School Escape/Rescue/Configure Directional Doors")]
    public static void ConfigureAll()
    {
        Material closed = AssetDatabase.LoadAssetAtPath<Material>(ClosedDoorMaterialPath);
        Material open = AssetDatabase.LoadAssetAtPath<Material>(OpenDoorMaterialPath);
        int configuredRooms = 0;
        foreach (string roomName in RegularRoomNames)
            if (ConfigureRegularRoom($"{PrefabFolder}/{roomName}.prefab", closed, open)) configuredRooms++;

        bool rescueConfigured = ConfigureRescueRoom(closed, open);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SchoolEscape/Rescate] Configuración terminada: {configuredRooms}/6 entradas y " +
                  $"Mapa_Rescate={(rescueConfigured ? "listo" : "con errores")}.");
    }

    private static bool ConfigureRegularRoom(string path, Material closed, Material open)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            RemoveMissingScripts(root);
            List<Transform> validEntrances = new List<Transform>();
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (RescueDirectionUtility.TryParseEntrance(child.name, out _)) validEntrances.Add(child);

            if (validEntrances.Count != 1)
            {
                Debug.LogError($"[SchoolEscape/Rescate] {root.name} debe tener exactamente una EntradaRescate<Dirección>; tiene {validEntrances.Count}.");
                return false;
            }

            Transform entrance = validEntrances[0];
            RescueDirectionUtility.TryParseEntrance(entrance.name, out RescueDirection direction);
            RoomExitGate gate = GetOrAdd<RoomExitGate>(entrance.gameObject);
            Dictionary<Transform, List<GameObject>> pieces = AssignAdjacentPieces(root.transform, validEntrances);
            ConfigureGate(gate, closed, open, pieces[entrance]);
            RescueEntranceAnchor anchor = GetOrAdd<RescueEntranceAnchor>(entrance.gameObject);
            anchor.Configure(direction, gate);
            EditorUtility.SetDirty(gate);
            EditorUtility.SetDirty(anchor);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[SchoolEscape/Rescate] {root.name}: entrada {direction} configurada.");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool ConfigureRescueRoom(Material closed, Material open)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(RescuePath);
        try
        {
            RemoveMissingScripts(root);
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == "Puerta (Arriba) (1)") child.name = "Puerta (Abajo)";

            Dictionary<RescueDirection, Transform> doorTransforms = new Dictionary<RescueDirection, Transform>();
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!RescueDirectionUtility.TryParseDoor(child.name, out RescueDirection direction)) continue;
                if (doorTransforms.ContainsKey(direction))
                {
                    Debug.LogError($"[SchoolEscape/Rescate] Mapa_Rescate tiene una puerta {direction} duplicada: {child.name}.");
                    continue;
                }

                doorTransforms.Add(direction, child);
            }

            Dictionary<Transform, List<GameObject>> pieces = AssignAdjacentPieces(root.transform, new List<Transform>(doorTransforms.Values));
            Dictionary<RescueDirection, RescueDoorAnchor> anchors = new Dictionary<RescueDirection, RescueDoorAnchor>();
            foreach (KeyValuePair<RescueDirection, Transform> pair in doorTransforms)
            {
                RescueDirection direction = pair.Key;
                Transform child = pair.Value;

                RoomExitGate gate = GetOrAdd<RoomExitGate>(child.gameObject);
                ConfigureGate(gate, closed, open, pieces[child]);
                RescueDoorAnchor anchor = GetOrAdd<RescueDoorAnchor>(child.gameObject);
                anchor.Configure(direction, gate);
                anchors.Add(direction, anchor);
                EditorUtility.SetDirty(gate);
                EditorUtility.SetDirty(anchor);
            }

            bool complete = anchors.Count == 4;
            if (!complete)
                Debug.LogError($"[SchoolEscape/Rescate] Mapa_Rescate necesita 4 puertas direccionales y se encontraron {anchors.Count}.");

            Transform captiveSpawn = FindChild(root.transform, "PlayersSpawn");
            if (captiveSpawn != null && captiveSpawn.childCount > 0)
                captiveSpawn = captiveSpawn.GetChild(captiveSpawn.childCount - 1);

            RescueRoomDefinition definition = GetOrAdd<RescueRoomDefinition>(root);
            definition.Configure(anchors.Values, captiveSpawn);
            EditorUtility.SetDirty(definition);

            SchoolRoomDefinition regularDefinition = root.GetComponent<SchoolRoomDefinition>();
            RoomObjectiveController objective = root.GetComponent<RoomObjectiveController>();
            RoomRuntimeController runtime = root.GetComponent<RoomRuntimeController>();
            if (regularDefinition != null) regularDefinition.enabled = false;
            if (objective != null) objective.enabled = false;
            if (runtime != null) runtime.enabled = false;

            PrefabUtility.SaveAsPrefabAsset(root, RescuePath);
            return complete && captiveSpawn != null;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureGate(RoomExitGate gate, Material closed, Material open, List<GameObject> adjacentPieces)
    {
        Renderer renderer = gate.GetComponent<Renderer>();
        Collider collider = gate.GetComponent<Collider>();
        int materialSlot = renderer != null && renderer.sharedMaterials.Length > 1 ? 1 : 0;
        gate.ConfigureVisuals(renderer, materialSlot, closed, open, collider);
        gate.ConfigureConnectionPieces(adjacentPieces.ToArray(), true);
    }

    private static Dictionary<Transform, List<GameObject>> AssignAdjacentPieces(Transform root, List<Transform> doors)
    {
        Dictionary<Transform, List<GameObject>> result = new Dictionary<Transform, List<GameObject>>();
        foreach (Transform door in doors) result.Add(door, new List<GameObject>());

        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (!IsAdjacentPiece(candidate.name) || doors.Count == 0) continue;
            Transform closest = doors[0];
            float closestDistance = (candidate.position - closest.position).sqrMagnitude;
            for (int index = 1; index < doors.Count; index++)
            {
                float distance = (candidate.position - doors[index].position).sqrMagnitude;
                if (distance >= closestDistance) continue;
                closest = doors[index];
                closestDistance = distance;
            }
            result[closest].Add(candidate.gameObject);
        }

        return result;
    }

    private static bool IsAdjacentPiece(string objectName)
    {
        string normalized = objectName.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        return normalized.IndexOf("AdyacenteIzquierda", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("AdyacenteDerecha", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Transform FindChild(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static void RemoveMissingScripts(GameObject root)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
    }
}

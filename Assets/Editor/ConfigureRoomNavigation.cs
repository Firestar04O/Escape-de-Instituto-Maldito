using System;
using System.Collections.Generic;
using System.IO;
using SchoolEscape.Rooms;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class ConfigureRoomNavigation
{
    private const string PrefabFolder = "Assets/Pruebas/Prefabs";
    private const string DataFolder = "Assets/Navigation/Rooms";
    private const string RescueRoomName = "Mapa_Rescate";
    private static readonly string[] RoomNames =
    {
        "Mapa_Salon1", "Mapa_Salon2", "Mapa_Salon3",
        "Mapa_Cafeteria1", "Mapa_Cafeteria2", "Mapa_Cafeteria3",
        RescueRoomName
    };

    [MenuItem("Tools/School Escape/Navigation/Bake Rescue Room Only")]
    public static void BakeRescueRoomOnly()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            Debug.LogWarning("[SchoolEscape/NavMesh] Espera a que Unity termine de compilar y sal del modo Play.");
            return;
        }

        EnsureFolder("Assets/Navigation");
        EnsureFolder(DataFolder);
        string prefabPath = $"{PrefabFolder}/{RescueRoomName}.prefab";
        bool baked = BakePrefab(prefabPath, RescueRoomName, true);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(baked
            ? "[SchoolEscape/NavMesh] Mapa_Rescate horneado de forma aislada con sus puertas excluidas del bake."
            : "[SchoolEscape/NavMesh] No se pudo hornear Mapa_Rescate.");
    }

    [MenuItem("Tools/School Escape/Navigation/Bake All Room Prefabs")]
    public static void BakeAllRoomPrefabs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            Debug.LogWarning("[SchoolEscape/NavMesh] Espera a que Unity termine de compilar y sal del modo Play.");
            return;
        }

        EnsureFolder("Assets/Navigation");
        EnsureFolder(DataFolder);
        int baked = 0;
        foreach (string roomName in RoomNames)
        {
            string prefabPath = $"{PrefabFolder}/{roomName}.prefab";
            if (BakePrefab(prefabPath, roomName, roomName == RescueRoomName)) baked++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SchoolEscape/NavMesh] Listo: {baked}/{RoomNames.Length} prefabs configurados y horneados. " +
                  "Cada raíz conserva su NavMeshSurface editable; vuelve a ejecutar este menú después de corregir un mapa.");
    }

    private static bool BakePrefab(string prefabPath, string roomName, bool excludeDirectionalDoors = false)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
            Debug.LogError($"[SchoolEscape/NavMesh] No existe {prefabPath}");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        List<GameObject> temporarilyDisabled = new List<GameObject>();
        try
        {
            NavMeshSurface surface = root.GetComponent<NavMeshSurface>();
            if (surface == null) surface = root.AddComponent<NavMeshSurface>();
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.Children;
            surface.layerMask = ~0;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.defaultArea = 0;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;
            surface.overrideVoxelSize = true;
            surface.voxelSize = .12f;
            surface.overrideTileSize = true;
            surface.tileSize = 128;
            surface.minRegionArea = .35f;

            int leverCount = ConfigureLevers(root);
            ConfigureBlockingGroup(root.transform, "Obstaculos");
            ConfigureBlockingGroup(root.transform, "Botiquines");

            if (excludeDirectionalDoors)
                temporarilyDisabled = DisableDirectionalDoors(root);

            surface.BuildNavMesh();
            RestoreActiveObjects(temporarilyDisabled);
            temporarilyDisabled.Clear();
            if (surface.navMeshData == null)
            {
                Debug.LogError($"[SchoolEscape/NavMesh] Unity no produjo datos para {roomName}.", root);
                return false;
            }

            string dataPath = $"{DataFolder}/{roomName}_NavMesh.asset";
            NavMeshData generated = surface.navMeshData;
            generated.name = $"{roomName}_NavMesh";
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(dataPath) != null)
                AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(generated, dataPath);
            surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(dataPath);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[SchoolEscape/NavMesh] {roomName}: bake guardado, {leverCount} palanca(s) transitables.");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return false;
        }
        finally
        {
            RestoreActiveObjects(temporarilyDisabled);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static List<GameObject> DisableDirectionalDoors(GameObject root)
    {
        List<GameObject> disabled = new List<GameObject>();
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (!RescueDirectionUtility.TryParseDoor(child.name, out _) || !child.gameObject.activeSelf)
                continue;

            child.gameObject.SetActive(false);
            disabled.Add(child.gameObject);
        }

        Debug.Log($"[SchoolEscape/NavMesh] Mapa_Rescate: {disabled.Count} puertas desactivadas temporalmente durante el bake.");
        return disabled;
    }

    private static void RestoreActiveObjects(List<GameObject> objects)
    {
        foreach (GameObject target in objects)
            if (target != null) target.SetActive(true);
    }

    private static int ConfigureLevers(GameObject root)
    {
        int count = 0;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.StartsWith("Palanca", StringComparison.OrdinalIgnoreCase) || child.name.Equals("Palancas", StringComparison.OrdinalIgnoreCase))
                continue;

            count++;
            foreach (Collider collider in child.GetComponentsInChildren<Collider>(true))
            {
                if (collider is MeshCollider meshCollider && !meshCollider.convex)
                    meshCollider.convex = true;
                collider.isTrigger = true;
            }

            NavMeshModifier modifier = child.GetComponent<NavMeshModifier>();
            if (modifier == null) modifier = child.gameObject.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = true;
            modifier.applyToChildren = true;
        }
        return count;
    }

    private static void ConfigureBlockingGroup(Transform root, string groupName)
    {
        Transform group = FindDeepChild(root, groupName);
        if (group == null) return;

        NavMeshModifier modifier = group.GetComponent<NavMeshModifier>();
        if (modifier == null) modifier = group.gameObject.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = false;
        modifier.overrideArea = true;
        modifier.area = 1;
        modifier.applyToChildren = true;
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name.Equals(childName, StringComparison.OrdinalIgnoreCase)) return child;
        return null;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;
        string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
        string folder = Path.GetFileName(folderPath);
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder);
    }
}

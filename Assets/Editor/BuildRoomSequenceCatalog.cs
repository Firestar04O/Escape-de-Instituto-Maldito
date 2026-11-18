using System.Collections.Generic;
using SchoolEscape.Rooms;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
internal static class BuildRoomSequenceCatalog
{
    private const string CatalogPath = "Assets/Resources/RoomSequenceCatalog.asset";
    private const string RescueRoomPath = "Assets/Pruebas/Prefabs/Mapa_Rescate.prefab";

    private static readonly string[] OrderedRoomPaths =
    {
        "Assets/Pruebas/Prefabs/Mapa_Salon1.prefab",
        "Assets/Pruebas/Prefabs/Mapa_Salon2.prefab",
        "Assets/Pruebas/Prefabs/Mapa_Salon3.prefab",
        "Assets/Pruebas/Prefabs/Mapa_Cafeteria1.prefab",
        "Assets/Pruebas/Prefabs/Mapa_Cafeteria2.prefab",
        "Assets/Pruebas/Prefabs/Mapa_Cafeteria3.prefab"
    };

    static BuildRoomSequenceCatalog()
    {
        EditorApplication.delayCall += EnsureCatalog;
    }

    [MenuItem("School Escape/Actualizar secuencia de salas")]
    public static void EnsureCatalog()
    {
        var roomPrefabs = new List<GameObject>(OrderedRoomPaths.Length);
        foreach (string roomPath in OrderedRoomPaths)
        {
            GameObject roomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(roomPath);
            if (roomPrefab == null)
            {
                Debug.LogError($"[SchoolEscape] No se encontró la sala requerida: {roomPath}");
                return;
            }

            roomPrefabs.Add(roomPrefab);
        }

        RoomSequenceCatalog catalog = AssetDatabase.LoadAssetAtPath<RoomSequenceCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<RoomSequenceCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        GameObject rescueRoom = AssetDatabase.LoadAssetAtPath<GameObject>(RescueRoomPath);
        if (rescueRoom == null)
        {
            Debug.LogError($"[SchoolEscape] No se encontró la sala de rescate: {RescueRoomPath}");
            return;
        }

        catalog.Configure(roomPrefabs, rescueRoom);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log("[SchoolEscape] Secuencia actualizada: Salón 1-3, Cafetería 1-3 y ramificación de rescate.");
    }
}

internal sealed class RoomSequenceCatalogBuildCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        BuildRoomSequenceCatalog.EnsureCatalog();
    }
}

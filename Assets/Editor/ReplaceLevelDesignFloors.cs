using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class ReplaceLevelDesignFloors
{
    private const string SceneName = "LevelDesign";
    private const string OldFloorPrefix = "SM_Bld_Base_Floor_Combined_01";
    private const string FloorPrefabPath = "Assets/Pruebas/ParaEscape/PisoPrueba.prefab";
    private const string ReplacementRootName = "Pisos_Prefab";
    private const string CurrentFloorRootName = "Pisos";
    private const string FloorMaterial1Path = "Assets/PolygonHorrorMansion/Materials/PisoLab1.mat";
    private const string FloorMaterial2Path = "Assets/PolygonHorrorMansion/Materials/PisoLab2.mat";
    private const int TopMaterialSlot = 1;
    private const int DistributionSeed = 20261116;

    [MenuItem("GameObject/Escape/Reemplazar pisos de LevelDesign")]
    private static void ReplaceFloors()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != SceneName)
        {
            EditorUtility.DisplayDialog(
                "Escena incorrecta",
                $"Abre la escena {SceneName} antes de ejecutar el reemplazo.",
                "Aceptar");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FloorPrefabPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog(
                "Prefab no encontrado",
                $"No se encontró el prefab en:\n{FloorPrefabPath}",
                "Aceptar");
            return;
        }

        List<GameObject> oldFloors = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(transform => transform.gameObject)
            .Where(gameObject => gameObject.name.StartsWith(OldFloorPrefix, StringComparison.Ordinal))
            .ToList();

        if (oldFloors.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Sin pisos antiguos",
                "No se encontraron objetos base_floor_combined para reemplazar.",
                "Aceptar");
            return;
        }

        GameObject existingRoot = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == ReplacementRootName);
        if (existingRoot != null)
        {
            EditorUtility.DisplayDialog(
                "Reemplazo ya existente",
                $"Ya existe el contenedor {ReplacementRootName}. Elimínalo o deshaz el reemplazo antes de repetir la operación.",
                "Aceptar");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Reemplazar pisos por PisoPrueba");

        GameObject replacementRoot = new GameObject(ReplacementRootName);
        Undo.RegisterCreatedObjectUndo(replacementRoot, "Crear contenedor de pisos");
        SceneManager.MoveGameObjectToScene(replacementRoot, scene);

        oldFloors.Sort((left, right) => string.CompareOrdinal(left.name, right.name));

        for (int index = 0; index < oldFloors.Count; index++)
        {
            GameObject oldFloor = oldFloors[index];
            Vector3 position = oldFloor.transform.position;
            Quaternion rotation = oldFloor.transform.rotation;
            Vector3 scale = oldFloor.transform.lossyScale;
            int siblingIndex = oldFloor.transform.GetSiblingIndex();

            GameObject replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(replacement, "Crear PisoPrueba");
            replacement.name = $"PisoPrueba ({index + 1:000})";
            replacement.transform.SetParent(replacementRoot.transform, false);
            replacement.transform.SetPositionAndRotation(position, rotation);
            replacement.transform.localScale = scale;
            replacement.transform.SetSiblingIndex(Mathf.Min(siblingIndex, replacementRoot.transform.childCount - 1));

            Undo.DestroyObjectImmediate(oldFloor);
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[LevelDesign] Reemplazo terminado: {oldFloors.Count} pisos antiguos sustituidos por instancias de PisoPrueba.");
        EditorUtility.DisplayDialog(
            "Reemplazo terminado",
            $"Se reemplazaron {oldFloors.Count} pisos por instancias del prefab PisoPrueba.\n\nPuedes deshacer toda la operación con Ctrl+Z mientras la escena siga abierta.",
            "Aceptar");
    }

    [MenuItem("GameObject/Escape/Validar pisos de LevelDesign")]
    private static void ValidateFloors()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FloorPrefabPath);

        GameObject[] objects = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(transform => transform.gameObject)
            .ToArray();

        int oldCount = objects.Count(gameObject =>
            gameObject.name.StartsWith(OldFloorPrefix, StringComparison.Ordinal));
        int prefabCount = objects.Count(gameObject =>
            PrefabUtility.GetCorrespondingObjectFromSource(gameObject) == prefab);

        Debug.Log($"[LevelDesign] Validación de pisos: antiguos={oldCount}, instancias PisoPrueba={prefabCount}.");
        EditorUtility.DisplayDialog(
            "Validación de pisos",
            $"Pisos antiguos: {oldCount}\nInstancias de PisoPrueba: {prefabCount}",
            "Aceptar");
    }

    [MenuItem("GameObject/Escape/Distribuir PisoLab1 y PisoLab2 por zonas")]
    private static void DistributeFloorMaterials()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != SceneName)
        {
            EditorUtility.DisplayDialog(
                "Escena incorrecta",
                $"Abre la escena {SceneName} antes de distribuir los materiales.",
                "Aceptar");
            return;
        }

        Material material1 = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterial1Path);
        Material material2 = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterial2Path);
        if (material1 == null || material2 == null)
        {
            EditorUtility.DisplayDialog(
                "Materiales no encontrados",
                $"Se necesitan estos dos materiales:\n{FloorMaterial1Path}\n{FloorMaterial2Path}",
                "Aceptar");
            return;
        }

        GameObject replacementRoot = FindFloorRoot(scene);
        if (replacementRoot == null)
        {
            EditorUtility.DisplayDialog(
                "Pisos no encontrados",
                $"No se encontró el contenedor {CurrentFloorRootName} ni {ReplacementRootName}.",
                "Aceptar");
            return;
        }

        List<MeshRenderer> floors = replacementRoot
            .GetComponentsInChildren<MeshRenderer>(true)
            .Where(renderer => renderer.name.StartsWith("PisoPrueba", StringComparison.Ordinal))
            .OrderBy(renderer => renderer.transform.position.x)
            .ThenBy(renderer => renderer.transform.position.z)
            .ToList();

        if (floors.Count == 0 || floors.Any(renderer => renderer.sharedMaterials.Length <= TopMaterialSlot))
        {
            EditorUtility.DisplayDialog(
                "Configuración de piso inesperada",
                $"No se encontraron pisos con el slot superior Element {TopMaterialSlot}.",
                "Aceptar");
            return;
        }

        System.Random random = new System.Random(DistributionSeed);
        int regionCount = Mathf.Max(2, Mathf.RoundToInt(floors.Count / 6.5f));
        List<int> seedIndices = ChooseRegionSeeds(floors, regionCount, random);
        int[] floorRegion = AssignNearestRegions(floors, seedIndices);
        HashSet<int> regionsUsingMaterial1 = BalanceRegions(floorRegion, regionCount, floors.Count / 2, random);

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Distribuir PisoLab1 y PisoLab2 por zonas");

        int material1Count = 0;
        for (int index = 0; index < floors.Count; index++)
        {
            MeshRenderer renderer = floors[index];
            Undo.RecordObject(renderer, "Cambiar material superior del piso");
            Material[] materials = renderer.sharedMaterials;
            bool useMaterial1 = regionsUsingMaterial1.Contains(floorRegion[index]);
            materials[TopMaterialSlot] = useMaterial1 ? material1 : material2;
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            if (useMaterial1)
                material1Count++;
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        int material2Count = floors.Count - material1Count;
        Debug.Log(
            $"[LevelDesign] Distribución por zonas terminada: PisoLab1={material1Count}, " +
            $"PisoLab2={material2Count}, regiones={regionCount}, semilla={DistributionSeed}.");
        EditorUtility.DisplayDialog(
            "Distribución terminada",
            $"PisoLab1: {material1Count}\nPisoLab2: {material2Count}\n" +
            $"Regiones: {regionCount} (promedio aproximado de 5–8 piezas)\n\n" +
            "El resultado quedó guardado en la escena y puede deshacerse con Ctrl+Z.",
            "Aceptar");
    }

    [MenuItem("GameObject/Escape/Validar distribución PisoLab")]
    private static void ValidateFloorMaterialDistribution()
    {
        Scene scene = SceneManager.GetActiveScene();
        Material material1 = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterial1Path);
        Material material2 = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterial2Path);
        GameObject replacementRoot = FindFloorRoot(scene);

        if (replacementRoot == null || material1 == null || material2 == null)
        {
            EditorUtility.DisplayDialog("Validación fallida", "Falta el contenedor o alguno de los materiales.", "Aceptar");
            return;
        }

        MeshRenderer[] floors = replacementRoot.GetComponentsInChildren<MeshRenderer>(true);
        int material1Count = floors.Count(renderer =>
            renderer.sharedMaterials.Length > TopMaterialSlot && renderer.sharedMaterials[TopMaterialSlot] == material1);
        int material2Count = floors.Count(renderer =>
            renderer.sharedMaterials.Length > TopMaterialSlot && renderer.sharedMaterials[TopMaterialSlot] == material2);

        Debug.Log($"[LevelDesign] Validación PisoLab: PisoLab1={material1Count}, PisoLab2={material2Count}.");
        EditorUtility.DisplayDialog(
            "Validación PisoLab",
            $"PisoLab1: {material1Count}\nPisoLab2: {material2Count}\nTotal configurado: {material1Count + material2Count}",
            "Aceptar");
    }

    private static List<int> ChooseRegionSeeds(
        IReadOnlyList<MeshRenderer> floors,
        int regionCount,
        System.Random random)
    {
        List<int> seeds = new List<int> { random.Next(floors.Count) };
        HashSet<int> used = new HashSet<int>(seeds);

        while (seeds.Count < regionCount)
        {
            int bestIndex = -1;
            double bestScore = double.MinValue;

            for (int candidate = 0; candidate < floors.Count; candidate++)
            {
                if (used.Contains(candidate))
                    continue;

                Vector3 candidatePosition = floors[candidate].transform.position;
                float nearestSeedDistance = seeds.Min(seed =>
                {
                    Vector3 delta = candidatePosition - floors[seed].transform.position;
                    return delta.x * delta.x + delta.z * delta.z;
                });

                double jitter = 0.82 + random.NextDouble() * 0.36;
                double score = nearestSeedDistance * jitter;
                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestIndex = candidate;
            }

            seeds.Add(bestIndex);
            used.Add(bestIndex);
        }

        return seeds;
    }

    private static int[] AssignNearestRegions(
        IReadOnlyList<MeshRenderer> floors,
        IReadOnlyList<int> seedIndices)
    {
        int[] assignments = new int[floors.Count];
        for (int floorIndex = 0; floorIndex < floors.Count; floorIndex++)
        {
            Vector3 floorPosition = floors[floorIndex].transform.position;
            float bestDistance = float.MaxValue;
            int bestRegion = 0;

            for (int regionIndex = 0; regionIndex < seedIndices.Count; regionIndex++)
            {
                Vector3 delta = floorPosition - floors[seedIndices[regionIndex]].transform.position;
                float distance = delta.x * delta.x + delta.z * delta.z;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                bestRegion = regionIndex;
            }

            assignments[floorIndex] = bestRegion;
        }

        return assignments;
    }

    private static HashSet<int> BalanceRegions(
        IReadOnlyList<int> floorRegions,
        int regionCount,
        int targetMaterial1Count,
        System.Random random)
    {
        int[] regionSizes = new int[regionCount];
        foreach (int region in floorRegions)
            regionSizes[region]++;

        List<int> shuffledRegions = Enumerable.Range(0, regionCount)
            .OrderBy(_ => random.Next())
            .ToList();

        Dictionary<int, HashSet<int>> reachable = new Dictionary<int, HashSet<int>>
        {
            [0] = new HashSet<int>()
        };

        foreach (int region in shuffledRegions)
        {
            foreach (int currentTotal in reachable.Keys.OrderByDescending(value => value).ToArray())
            {
                int nextTotal = currentTotal + regionSizes[region];
                if (nextTotal > targetMaterial1Count || reachable.ContainsKey(nextTotal))
                    continue;

                HashSet<int> selected = new HashSet<int>(reachable[currentTotal]) { region };
                reachable[nextTotal] = selected;
            }
        }

        int bestTotal = reachable.Keys.Max();
        return reachable[bestTotal];
    }

    private static GameObject FindFloorRoot(Scene scene)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(transform => transform.gameObject)
            .FirstOrDefault(gameObject =>
                gameObject.name == CurrentFloorRootName || gameObject.name == ReplacementRootName);
    }
}

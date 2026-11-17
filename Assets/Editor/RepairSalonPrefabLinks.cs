using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

internal static class RepairSalonPrefabLinks
{
    private const string PrefabFolder = "Assets/Pruebas/Prefabs";
    private const string FloorPrefabPath = PrefabFolder + "/PisoPrueba.prefab";
    private const string WallPrefabPath = PrefabFolder + "/ParedPrueba.prefab";
    private const string InvisibleWallPrefabPath = PrefabFolder + "/ParedInvisible.prefab";

    private static readonly string[] RoomPrefabPaths =
    {
        PrefabFolder + "/Salon1.prefab",
        PrefabFolder + "/Salon2.prefab",
        PrefabFolder + "/Salon3.prefab",
        PrefabFolder + "/Cafeteria1.prefab"
    };

    [MenuItem("Tools/School Escape/Repair Salon Floor and Wall Links")]
    private static void RepairAllRooms()
    {
        NormalizeWallPrefabColliders();
        GameObject floorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FloorPrefabPath);
        GameObject wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
        GameObject invisibleWallPrefab = CreateInvisibleWallPrefabIfMissing(wallPrefab);
        if (floorPrefab == null || wallPrefab == null || invisibleWallPrefab == null)
        {
            EditorUtility.DisplayDialog(
                "Repair failed",
                $"Missing base prefab.\n\nFloor: {FloorPrefabPath}\nWall: {WallPrefabPath}",
                "OK");
            return;
        }

        var report = new StringBuilder();
        int totalRepaired = 0;
        int totalSkipped = 0;

        try
        {
            for (int index = 0; index < RoomPrefabPaths.Length; index++)
            {
                string roomPath = RoomPrefabPaths[index];
                EditorUtility.DisplayProgressBar(
                    "Repairing room prefabs",
                    $"Processing {System.IO.Path.GetFileNameWithoutExtension(roomPath)}...",
                    (float)index / RoomPrefabPaths.Length);

                GameObject root = PrefabUtility.LoadPrefabContents(roomPath);
                try
                {
                    RepairResult floor = RepairNamedObjects(root, "PisoPrueba", floorPrefab, FloorPrefabPath);
                    RepairResult wall = RepairNamedObjects(root, "ParedPrueba", wallPrefab, WallPrefabPath);
                    RepairResult invisibleWall = RepairColliderOnlyWalls(root, invisibleWallPrefab);

                    if (floor.Repaired > 0 || wall.Repaired > 0 || invisibleWall.Repaired > 0)
                        PrefabUtility.SaveAsPrefabAsset(root, roomPath);

                    int linkedWalls = wall.Linked + invisibleWall.Linked;
                    int skippedWalls = Mathf.Max(0, wall.Total - linkedWalls);
                    totalRepaired += floor.Repaired + wall.Repaired + invisibleWall.Repaired;
                    totalSkipped += floor.Skipped + skippedWalls;
                    report.AppendLine(
                        $"{root.name}: floors {floor.Linked}/{floor.Total} linked " +
                        $"({floor.Repaired} repaired), walls {linkedWalls}/{wall.Total} linked " +
                        $"({wall.Repaired + invisibleWall.Repaired} repaired; {invisibleWall.Linked} invisible), " +
                        $"skipped {floor.Skipped + skippedWalls}.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        string summary = report.ToString().TrimEnd();
        Debug.Log($"[School Escape] Salon prefab-link repair complete.\n{summary}");
        EditorUtility.DisplayDialog(
            "Salon prefab links repaired",
            $"Repaired objects: {totalRepaired}\nSkipped unsafe objects: {totalSkipped}\n\n{summary}",
            "OK");
    }

    private static void NormalizeWallPrefabColliders()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(WallPrefabPath);
        try
        {
            BoxCollider[] colliders = root.GetComponents<BoxCollider>();
            bool changed = false;
            for (int index = colliders.Length - 1; index > 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(colliders[index]);
                changed = true;
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, WallPrefabPath);
                Debug.Log("[School Escape] Removed duplicate identical BoxCollider components from ParedPrueba.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
    }

    private static GameObject CreateInvisibleWallPrefabIfMissing(GameObject visibleWallPrefab)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(InvisibleWallPrefabPath);
        if (existing != null)
            return existing;
        if (visibleWallPrefab == null)
            return null;

        var temporary = new GameObject("ParedInvisible");
        try
        {
            temporary.layer = visibleWallPrefab.layer;
            BoxCollider source = visibleWallPrefab.GetComponent<BoxCollider>();
            BoxCollider collider = temporary.AddComponent<BoxCollider>();
            collider.center = source.center;
            collider.size = source.size;
            collider.isTrigger = source.isTrigger;
            collider.sharedMaterial = source.sharedMaterial;
            collider.enabled = source.enabled;
            PrefabUtility.SaveAsPrefabAsset(temporary, InvisibleWallPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(temporary);
        }

        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<GameObject>(InvisibleWallPrefabPath);
    }

    private static RepairResult RepairColliderOnlyWalls(GameObject roomRoot, GameObject sourcePrefab)
    {
        List<GameObject> candidates = roomRoot.GetComponentsInChildren<Transform>(true)
            .Select(item => item.gameObject)
            .Where(item => item.name == "ParedPrueba" || item.name.StartsWith("ParedPrueba (", StringComparison.Ordinal))
            .Where(item => HasMatchingStructure(item, sourcePrefab))
            .ToList();
        int repaired = 0;

        foreach (GameObject oldObject in candidates)
        {
            if (IsLinkedTo(oldObject, sourcePrefab, InvisibleWallPrefabPath))
                continue;

            Transform oldTransform = oldObject.transform;
            Transform parent = oldTransform.parent;
            int siblingIndex = oldTransform.GetSiblingIndex();
            Vector3 localPosition = oldTransform.localPosition;
            Quaternion localRotation = oldTransform.localRotation;
            Vector3 localScale = oldTransform.localScale;
            string objectName = oldObject.name;
            bool active = oldObject.activeSelf;
            StaticEditorFlags staticFlags = GameObjectUtility.GetStaticEditorFlags(oldObject);
            BoxCollider oldCollider = oldObject.GetComponent<BoxCollider>();

            GameObject replacement = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab, roomRoot.scene);
            replacement.transform.SetParent(parent, false);
            replacement.transform.SetSiblingIndex(siblingIndex);
            replacement.transform.localPosition = localPosition;
            replacement.transform.localRotation = localRotation;
            replacement.transform.localScale = localScale;
            replacement.name = objectName;
            replacement.SetActive(active);
            GameObjectUtility.SetStaticEditorFlags(replacement, staticFlags);

            BoxCollider replacementCollider = replacement.GetComponent<BoxCollider>();
            replacementCollider.center = oldCollider.center;
            replacementCollider.size = oldCollider.size;
            replacementCollider.isTrigger = oldCollider.isTrigger;
            replacementCollider.sharedMaterial = oldCollider.sharedMaterial;
            replacementCollider.enabled = oldCollider.enabled;
            PrefabUtility.RecordPrefabInstancePropertyModifications(replacementCollider);

            UnityEngine.Object.DestroyImmediate(oldObject);
            repaired++;
        }

        return new RepairResult(candidates.Count, candidates.Count, repaired, 0);
    }

    [MenuItem("Tools/School Escape/Validate Salon Prefabs")]
    private static void ValidateAllRooms()
    {
        GameObject floorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FloorPrefabPath);
        GameObject wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
        GameObject invisibleWallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(InvisibleWallPrefabPath);
        var report = new StringBuilder();

        foreach (string roomPath in RoomPrefabPaths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(roomPath);
            try
            {
                LinkCount floor = CountLinks(root, "PisoPrueba", floorPrefab, FloorPrefabPath);
                LinkCount wall = CountLinks(root, "ParedPrueba", wallPrefab, WallPrefabPath);
                LinkCount invisibleWall = CountLinks(root, "ParedPrueba", invisibleWallPrefab, InvisibleWallPrefabPath);
                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);

                int players = transforms.Count(item => IsNumberedName(item.name, "PlayerSpawn"));
                int enemies = transforms.Count(item => IsNumberedName(item.name, "EnemySpawn"));
                int levers = transforms.Count(item => IsNumberedName(item.name, "Palanca"));
                int exits = transforms.Count(item => item.name == "Salida");
                int randomInteractables = transforms.Count(item => item.name.StartsWith("Interactable", StringComparison.Ordinal));
                int batteries = transforms.Count(item => IsNumberedName(item.name, "Pilas"));
                int bandages = transforms.Count(item => IsNumberedName(item.name, "Botiquin"));
                int colliders = root.GetComponentsInChildren<Collider>(true).Length;

                report.AppendLine(
                    $"{root.name}: floors {floor.Linked}/{floor.Total}, " +
                    $"walls {wall.Linked + invisibleWall.Linked}/{wall.Total} ({invisibleWall.Linked} invisible); " +
                    $"player spawns {players}, enemy spawns {enemies}, lever {levers}, exits {exits}, " +
                    $"random {randomInteractables}, batteries {batteries}, bandages {bandages}, colliders {colliders}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        string summary = report.ToString().TrimEnd();
        Debug.Log($"[School Escape] Salon prefab validation.\n{summary}");
        EditorUtility.DisplayDialog("Salon prefab validation", summary, "OK");
    }

    private static RepairResult RepairNamedObjects(
        GameObject roomRoot,
        string namePrefix,
        GameObject sourcePrefab,
        string sourcePath)
    {
        List<GameObject> candidates = roomRoot.GetComponentsInChildren<Transform>(true)
            .Select(item => item.gameObject)
            .Where(item => item.name.StartsWith(namePrefix, StringComparison.Ordinal))
            .Where(item => item.name == namePrefix || item.name.StartsWith(namePrefix + " (", StringComparison.Ordinal))
            .ToList();

        int repaired = 0;
        int skipped = 0;
        foreach (GameObject oldObject in candidates)
        {
            if (IsLinkedTo(oldObject, sourcePrefab, sourcePath))
                continue;

            if (!HasMatchingStructure(oldObject, sourcePrefab))
            {
                skipped++;
                Debug.LogWarning(
                    $"[School Escape] Skipped {GetHierarchyPath(oldObject.transform)} in {roomRoot.name}: " +
                    "its components or children do not match the base prefab.");
                continue;
            }

            Transform oldTransform = oldObject.transform;
            Transform parent = oldTransform.parent;
            int siblingIndex = oldTransform.GetSiblingIndex();
            Vector3 localPosition = oldTransform.localPosition;
            Quaternion localRotation = oldTransform.localRotation;
            Vector3 localScale = oldTransform.localScale;
            string objectName = oldObject.name;
            bool active = oldObject.activeSelf;
            int layer = oldObject.layer;
            string tag = oldObject.tag;
            StaticEditorFlags staticFlags = GameObjectUtility.GetStaticEditorFlags(oldObject);
            Material[][] rendererMaterials = oldObject.GetComponents<Renderer>()
                .Select(renderer => renderer.sharedMaterials.ToArray())
                .ToArray();

            GameObject replacement = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab, roomRoot.scene);
            replacement.transform.SetParent(parent, false);
            replacement.transform.SetSiblingIndex(siblingIndex);
            replacement.transform.localPosition = localPosition;
            replacement.transform.localRotation = localRotation;
            replacement.transform.localScale = localScale;
            replacement.name = objectName;
            replacement.SetActive(active);
            replacement.layer = layer;
            replacement.tag = tag;
            GameObjectUtility.SetStaticEditorFlags(replacement, staticFlags);

            Renderer[] replacementRenderers = replacement.GetComponents<Renderer>();
            for (int rendererIndex = 0; rendererIndex < replacementRenderers.Length; rendererIndex++)
            {
                replacementRenderers[rendererIndex].sharedMaterials = rendererMaterials[rendererIndex];
                PrefabUtility.RecordPrefabInstancePropertyModifications(replacementRenderers[rendererIndex]);
            }

            UnityEngine.Object.DestroyImmediate(oldObject);
            repaired++;
        }

        LinkCount result = CountLinks(roomRoot, namePrefix, sourcePrefab, sourcePath);
        return new RepairResult(result.Total, result.Linked, repaired, skipped);
    }

    private static LinkCount CountLinks(
        GameObject roomRoot,
        string namePrefix,
        GameObject sourcePrefab,
        string sourcePath)
    {
        GameObject[] candidates = roomRoot.GetComponentsInChildren<Transform>(true)
            .Select(item => item.gameObject)
            .Where(item => item.name == namePrefix || item.name.StartsWith(namePrefix + " (", StringComparison.Ordinal))
            .ToArray();
        int linked = candidates.Count(item => IsLinkedTo(item, sourcePrefab, sourcePath));
        return new LinkCount(candidates.Length, linked);
    }

    private static bool IsLinkedTo(GameObject candidate, GameObject sourcePrefab, string sourcePath)
    {
        UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(candidate);
        if (source == sourcePrefab)
            return true;

        string nearestPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(candidate);
        return string.Equals(nearestPath, sourcePath, StringComparison.Ordinal);
    }

    private static bool HasMatchingStructure(GameObject candidate, GameObject sourcePrefab)
    {
        if (candidate.transform.childCount != sourcePrefab.transform.childCount)
            return false;

        Type[] candidateTypes = candidate.GetComponents<Component>()
            .Where(component => component != null)
            .Select(component => component.GetType())
            .OrderBy(type => type.FullName)
            .ToArray();
        Type[] sourceTypes = sourcePrefab.GetComponents<Component>()
            .Where(component => component != null)
            .Select(component => component.GetType())
            .OrderBy(type => type.FullName)
            .ToArray();
        return candidateTypes.SequenceEqual(sourceTypes);
    }

    private static bool IsNumberedName(string candidate, string baseName)
    {
        return candidate == baseName || candidate.StartsWith(baseName + " (", StringComparison.Ordinal);
    }

    private static string GetHierarchyPath(Transform item)
    {
        var names = new Stack<string>();
        while (item != null)
        {
            names.Push(item.name);
            item = item.parent;
        }

        return string.Join("/", names);
    }

    private readonly struct LinkCount
    {
        public LinkCount(int total, int linked)
        {
            Total = total;
            Linked = linked;
        }

        public int Total { get; }
        public int Linked { get; }
    }

    private readonly struct RepairResult
    {
        public RepairResult(int total, int linked, int repaired, int skipped)
        {
            Total = total;
            Linked = linked;
            Repaired = repaired;
            Skipped = skipped;
        }

        public int Total { get; }
        public int Linked { get; }
        public int Repaired { get; }
        public int Skipped { get; }
    }
}

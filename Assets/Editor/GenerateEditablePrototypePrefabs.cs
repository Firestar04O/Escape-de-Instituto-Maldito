using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class GenerateEditablePrototypePrefabs
{
    private const string Root = "Assets/Art/EditablePrefabs";
    private const string Materials = Root + "/Materials";
    private const string Items = Root + "/Items";
    private const string Characters = Root + "/Characters";
    private const string Enemies = Root + "/Enemies";
    private const string GeneratedAssetPath = Items + "/Item_Battery.prefab";

    static GenerateEditablePrototypePrefabs()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(GeneratedAssetPath) == null)
            EditorApplication.delayCall += GenerateMissingAssets;
    }

    [MenuItem("Tools/School Escape/Generate Editable Prototype Prefabs")]
    public static void GenerateMissingAssets()
    {
        EnsureFolder("Assets", "Art");
        EnsureFolder("Assets/Art", "EditablePrefabs");
        EnsureFolder(Root, "Materials");
        EnsureFolder(Root, "Items");
        EnsureFolder(Root, "Characters");
        EnsureFolder(Root, "Enemies");

        Material battery = GetOrCreateMaterial("Item_Battery", new Color(1f, .82f, .12f));
        Material bandage = GetOrCreateMaterial("Item_Bandage", new Color(.2f, .72f, 1f));
        Material red = GetOrCreateMaterial("Character_Red", new Color(.92f, .18f, .18f));
        Material green = GetOrCreateMaterial("Character_Green", new Color(.25f, .9f, .38f));
        Material blue = GetOrCreateMaterial("Character_Blue", new Color(.18f, .52f, 1f));
        Material yellow = GetOrCreateMaterial("Character_Yellow", new Color(1f, .82f, .12f));
        Material smallEnemy = GetOrCreateMaterial("Enemy_Small", new Color(.95f, .25f, .15f));
        Material normalEnemy = GetOrCreateMaterial("Enemy_Normal", new Color(.62f, .08f, .09f));

        CreateItemPrefab(
            Items + "/Item_Battery.prefab",
            "Item_Battery",
            new Vector3(.35f, .2f, .2f),
            battery,
            true);
        CreateItemPrefab(
            Items + "/Item_Bandage.prefab",
            "Item_Bandage",
            new Vector3(.4f, .12f, .28f),
            bandage,
            false);

        CreateCharacterPrefab(Characters + "/Character_Red.prefab", "Character_Red", red);
        CreateCharacterPrefab(Characters + "/Character_Green.prefab", "Character_Green", green);
        CreateCharacterPrefab(Characters + "/Character_Blue.prefab", "Character_Blue", blue);
        CreateCharacterPrefab(Characters + "/Character_Yellow.prefab", "Character_Yellow", yellow);

        CreateEnemyPrefab(Enemies + "/Enemy_Cultist_Small.prefab", "Enemy_Cultist_Small", smallEnemy, true);
        CreateEnemyPrefab(Enemies + "/Enemy_Cultist_Normal.prefab", "Enemy_Cultist_Normal", normalEnemy, false);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SchoolEscape] Biblioteca editable creada: 2 items, 4 personajes y 2 enemigos.");
    }

    private static Material GetOrCreateMaterial(string assetName, Color color)
    {
        string path = $"{Materials}/{assetName}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader)
        {
            name = assetName,
            color = color
        };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void CreateItemPrefab(string path, string assetName, Vector3 visualScale, Material material, bool battery)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

        GameObject root = new GameObject(assetName);
        GameObject visual = CreateVisual(PrimitiveType.Cube, "Visual", visualScale, material, root.transform);
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        if (battery)
            root.AddComponent<PrototypeBatteryWorldPickup>();
        else
            root.AddComponent<PrototypeBandageWorldPickup>();

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }

    private static void CreateCharacterPrefab(string path, string assetName, Material material)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

        GameObject root = new GameObject(assetName);
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = .45f;
        controller.center = Vector3.zero;
        GameObject visual = CreateVisual(PrimitiveType.Capsule, "Visual", Vector3.one, material, root.transform);
        Object.DestroyImmediate(visual.GetComponent<Collider>());

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }

    private static void CreateEnemyPrefab(string path, string assetName, Material material, bool small)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

        GameObject root = new GameObject(assetName);
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.height = small ? 1.45f : 2.3f;
        controller.radius = small ? .32f : .52f;
        controller.center = Vector3.zero;
        PrototypeEnemy enemy = root.AddComponent<PrototypeEnemy>();
        enemy.IsSmall = small;
        GameObject visual = CreateVisual(
            PrimitiveType.Capsule,
            "Visual",
            small ? Vector3.one * .72f : Vector3.one * 1.15f,
            material,
            root.transform);
        Object.DestroyImmediate(visual.GetComponent<Collider>());

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }

    private static GameObject CreateVisual(
        PrimitiveType primitive,
        string objectName,
        Vector3 scale,
        Material material,
        Transform parent)
    {
        GameObject visual = GameObject.CreatePrimitive(primitive);
        visual.name = objectName;
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = scale;
        visual.GetComponent<Renderer>().sharedMaterial = material;
        return visual;
    }

    private static void EnsureFolder(string parent, string folderName)
    {
        string path = $"{parent}/{folderName}";
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, folderName);
    }
}

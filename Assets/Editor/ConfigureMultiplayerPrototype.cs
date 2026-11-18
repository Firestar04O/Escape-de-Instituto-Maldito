using System.IO;
using SchoolEscape.Multiplayer;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

public static class ConfigureMultiplayerPrototype
{
    private const string Root = "Assets/Resources/Multiplayer";

    [MenuItem("Tools/School Escape/Configurar multijugador")]
    public static void Configure()
    {
        Directory.CreateDirectory(Root);
        CreateHostPlayer();
        CreateClientAlly();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Multiplayer] Prefabs de host y jugador 2 configurados.");
    }

    private static void CreateHostPlayer()
    {
        GameObject root = CreateBase("NetworkHostPlayer", new Color(.18f, .52f, 1f));
        root.AddComponent<PrototypePlayer>();
        root.AddComponent<NetworkHostPlayerBridge>();
        Save(root, Root + "/NetworkHostPlayer.prefab");
    }

    private static void CreateClientAlly()
    {
        GameObject root = CreateBase("NetworkClientAlly", new Color(.25f, .9f, .38f));
        root.AddComponent<PrototypeAlly>();
        root.AddComponent<NetworkClientAllyBridge>();
        Save(root, Root + "/NetworkClientAlly.prefab");
    }

    private static GameObject CreateBase(string name, Color color)
    {
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = name;
        Object.DestroyImmediate(root.GetComponent<CapsuleCollider>());
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = .45f;
        controller.center = Vector3.zero;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        string materialPath = Root + "/" + name + " Material.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader) { name = name + " Material" };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.shader = shader;
        material.color = color;
        EditorUtility.SetDirty(material);
        root.GetComponent<Renderer>().sharedMaterial = material;

        root.AddComponent<NetworkObject>();
        NetworkTransform transform = root.AddComponent<NetworkTransform>();
        transform.Interpolate = true;
        return root;
    }

    private static void Save(GameObject root, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }
}

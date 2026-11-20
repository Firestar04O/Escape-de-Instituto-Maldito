#if UNITY_EDITOR
using System.Reflection;
using SchoolEscape.UI;
using UnityEditor;
using UnityEngine;

namespace SchoolEscape.EditorTools
{
    public static class SchoolEscapeLobbyCanvasBuilder
    {
        private const string PrefabPath = "Assets/Resources/UI/SchoolEscapeGameFlowCanvas.prefab";

        [MenuItem("Tools/School Escape/Regenerar Canvas de menú y lobby")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/UI"))
                AssetDatabase.CreateFolder("Assets/Resources", "UI");

            GameObject root = new GameObject("School Escape UI - Canvas editable");
            BasicGameFlowUI flow = root.AddComponent<BasicGameFlowUI>();
            MethodInfo build = typeof(BasicGameFlowUI).GetMethod("BuildUi", BindingFlags.Instance | BindingFlags.NonPublic);
            build.Invoke(flow, null);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SchoolEscape] Canvas editable generado en {PrefabPath}");
        }
    }
}
#endif

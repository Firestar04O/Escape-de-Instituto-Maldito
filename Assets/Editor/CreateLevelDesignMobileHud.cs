using SchoolEscape.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class CreateLevelDesignMobileHud
{
    private const string ScenePath = "Assets/Scenes/LevelDesign.unity";

    [MenuItem("Tools/School Escape/Create Or Update Mobile HUD")]
    public static void CreateOrUpdate()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        SchoolEscapePrototype session = Object.FindFirstObjectByType<SchoolEscapePrototype>();
        if (session == null)
        {
            GameObject sessionObject = new GameObject("School Escape Session");
            session = sessionObject.AddComponent<SchoolEscapePrototype>();
        }

        MobileHudController existing = Object.FindFirstObjectByType<MobileHudController>();
        if (existing != null)
        {
            FixExistingLayout(existing.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[School Escape] Mobile HUD layout updated in LevelDesign.");
            return;
        }

        EnsureEventSystem();

        GameObject canvasObject = new GameObject("MobileHUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        MobileHudController hud = canvasObject.AddComponent<MobileHudController>();
        Text status = CreateText(canvasObject.transform, "Status", "Vida 3/3   Pilas 2/3", 30, TextAnchor.UpperLeft);
        SetTopLeftRect(status.rectTransform, new Vector2(30f, -30f), new Vector2(660f, 100f));

        Text ally = CreateText(canvasObject.transform, "AllyStatus", "Compañero 3/3", 26, TextAnchor.UpperLeft);
        SetTopLeftRect(ally.rectTransform, new Vector2(30f, -135f), new Vector2(420f, 55f));

        RectTransform joystick = CreateControl(canvasObject.transform, "MovementJoystick", "MOVIMIENTO", new Color(.1f, .2f, .3f, .55f), new Vector2(210f, 210f));
        SetRect(joystick, new Vector2(0f, 0f), new Vector2(145f, 145f), new Vector2(210f, 210f));

        RectTransform flash = CreateControl(canvasObject.transform, "FlashButton", "FLASH", new Color(.85f, .65f, .08f, .85f), new Vector2(210f, 105f));
        SetRect(flash, new Vector2(1f, 0f), new Vector2(-135f, 205f), new Vector2(210f, 105f));

        RectTransform interact = CreateControl(canvasObject.transform, "InteractButton", "INTERACTUAR", new Color(.1f, .45f, .75f, .85f), new Vector2(230f, 105f));
        SetRect(interact, new Vector2(1f, 0f), new Vector2(-145f, 80f), new Vector2(230f, 105f));
        MobileHoldButton holdButton = interact.gameObject.AddComponent<MobileHoldButton>();

        RectTransform bandageRect = CreateControl(canvasObject.transform, "BandageButton", "VENDA", new Color(.2f, .65f, .35f, .85f), new Vector2(190f, 105f));
        SetRect(bandageRect, new Vector2(1f, 0f), new Vector2(-365f, 80f), new Vector2(190f, 105f));
        Button bandage = bandageRect.gameObject.AddComponent<Button>();
        bandage.targetGraphic = bandageRect.GetComponent<Image>();

        hud.Configure(session, joystick, flash, holdButton, bandage, status, ally);
        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = canvasObject;
        Debug.Log("[School Escape] Editable MobileHUD created and saved in LevelDesign.");
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    private static RectTransform CreateControl(Transform parent, string name, string label, Color color, Vector2 size)
    {
        GameObject control = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        control.transform.SetParent(parent, false);
        RectTransform rect = control.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        control.GetComponent<Image>().color = color;

        Text text = CreateText(control.transform, "Label", label, 28, TextAnchor.MiddleCenter);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        return rect;
    }

    private static Text CreateText(Transform parent, string name, string value, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private static void SetTopLeftRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private static void FixExistingLayout(Transform hudRoot)
    {
        Transform status = hudRoot.Find("Status");
        if (status != null)
            SetTopLeftRect((RectTransform)status, new Vector2(30f, -30f), new Vector2(660f, 100f));

        Transform ally = hudRoot.Find("AllyStatus");
        if (ally != null)
            SetTopLeftRect((RectTransform)ally, new Vector2(30f, -135f), new Vector2(420f, 55f));
    }
}

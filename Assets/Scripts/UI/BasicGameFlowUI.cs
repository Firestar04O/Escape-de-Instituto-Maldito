using System.Collections.Generic;
using MenuUIBase.UI;
using SchoolEscape.Multiplayer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SchoolEscape.UI
{
    [DisallowMultipleComponent]
    public sealed class BasicGameFlowUI : MonoBehaviour
    {
        private static readonly Color Backdrop = new Color(.018f, .025f, .04f, .94f);
        private static readonly Color Surface = new Color(.07f, .085f, .12f, .98f);
        private static readonly Color Primary = new Color(.65f, .08f, .1f, 1f);
        private static readonly Color Secondary = new Color(.14f, .2f, .28f, 1f);

        private SchoolEscapePrototype session;
        private PanelNavigator navigator;
        private GameObject mainPanel;
        private GameObject helpPanel;
        private GameObject multiplayerPanel;
        private GameObject resultPanel;
        private Text resultTitle;
        private Text resultBody;
        private Text networkStatus;
        private InputField addressInput;
        private GameObject announcementPanel;
        private Text announcementText;
        private bool resultShown;

        public static bool HasAnnouncementPresenter { get; private set; }

        public static void EnsureFor(SchoolEscapePrototype gameSession)
        {
            BasicGameFlowUI existing = FindFirstObjectByType<BasicGameFlowUI>();
            if (existing != null)
            {
                existing.session = gameSession;
                return;
            }

            GameObject root = new GameObject("Basic Game Flow UI");
            BasicGameFlowUI flow = root.AddComponent<BasicGameFlowUI>();
            flow.session = gameSession;
        }

        private void Start()
        {
            BuildUi();
            ShowMainMenu();
        }

        private void Update()
        {
            if (networkStatus != null && SchoolEscapeNetworkBootstrap.Instance != null)
                networkStatus.text = SchoolEscapeNetworkBootstrap.Instance.Status;
            UpdateAnnouncement();
            if (session == null || resultShown) return;
            PrototypeMatchResult matchResult = session.DisplayMatchResult;
            if (matchResult == PrototypeMatchResult.InProgress) return;

            resultShown = true;
            Time.timeScale = 0f;
            resultTitle.text = matchResult == PrototypeMatchResult.PerfectEscape
                ? "ESCAPE PERFECTO"
                : matchResult == PrototypeMatchResult.PartialEscape
                    ? "ESCAPE PARCIAL"
                    : "DERROTA TOTAL";
            resultBody.text =
                $"Tiempo: {session.DisplayRunTime:0.0} s · {session.DisplayRoomProgressText}\n{session.MemorialText}";
            navigator.Open(resultPanel);
        }

        private void OnDestroy()
        {
            HasAnnouncementPresenter = false;
            if (Time.timeScale == 0f) Time.timeScale = 1f;
        }

        private void BuildUi()
        {
            EnsureEventSystem();
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();

            navigator = gameObject.AddComponent<PanelNavigator>();
            BuildAnnouncement();
            mainPanel = CreatePanel("Menú principal");
            helpPanel = CreatePanel("Cómo jugar");
            multiplayerPanel = CreatePanel("Multijugador");
            resultPanel = CreatePanel("Resultado");

            BuildMainPanel();
            BuildHelpPanel();
            BuildMultiplayerPanel();
            BuildResultPanel();
            navigator.Configure(mainPanel, new List<GameObject> { mainPanel, helpPanel, multiplayerPanel, resultPanel });
        }

        private void BuildAnnouncement()
        {
            announcementPanel = new GameObject("Anuncio sincronizado", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            announcementPanel.transform.SetParent(transform, false);
            RectTransform panelRect = announcementPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(.5f, 1f);
            panelRect.pivot = new Vector2(.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -24f);
            panelRect.sizeDelta = new Vector2(980f, 72f);
            UnityEngine.UI.Image background = announcementPanel.GetComponent<UnityEngine.UI.Image>();
            background.color = new Color(.025f, .035f, .055f, .9f);
            background.raycastTarget = false;

            GameObject textObject = new GameObject("Texto", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            textObject.transform.SetParent(announcementPanel.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24f, 8f);
            textRect.offsetMax = new Vector2(-24f, -8f);
            announcementText = textObject.GetComponent<UnityEngine.UI.Text>();
            announcementText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            announcementText.fontSize = 28;
            announcementText.fontStyle = FontStyle.Bold;
            announcementText.alignment = TextAnchor.MiddleCenter;
            announcementText.color = Color.white;
            announcementText.raycastTarget = false;
            announcementPanel.SetActive(false);
            HasAnnouncementPresenter = true;
        }

        private void UpdateAnnouncement()
        {
            if (announcementPanel == null || announcementText == null) return;
            string value = session != null ? session.DisplayAnnouncementText : string.Empty;
            bool visible = !string.IsNullOrEmpty(value);
            if (visible && announcementText.text != value) announcementText.text = value;
            if (announcementPanel.activeSelf != visible) announcementPanel.SetActive(visible);
        }

        private void BuildMainPanel()
        {
            CreateText(mainPanel.transform, "ESCAPE DEL\nINSTITUTO MALDITO", 54, FontStyle.Bold, new Vector2(0f, 190f), new Vector2(850f, 160f));
            CreateText(mainPanel.transform, "PROTOTIPO MVP", 20, FontStyle.Bold, new Vector2(0f, 100f), new Vector2(500f, 40f), new Color(.72f, .75f, .8f));
            CreateButton(mainPanel.transform, "JUGAR SOLO", new Vector2(0f, 45f), Primary, StartGame);
            CreateButton(mainPanel.transform, "MULTIJUGADOR", new Vector2(0f, -35f), Secondary, () => navigator.Open(multiplayerPanel));
            CreateButton(mainPanel.transform, "CÓMO JUGAR", new Vector2(0f, -115f), Secondary, () => navigator.Open(helpPanel));
            CreateButton(mainPanel.transform, "SALIR", new Vector2(0f, -195f), Secondary, QuitGame);
        }

        private void BuildHelpPanel()
        {
            CreateText(helpPanel.transform, "CÓMO JUGAR", 42, FontStyle.Bold, new Vector2(0f, 205f), new Vector2(700f, 70f));
            CreateText(helpPanel.transform,
                "Activa todas las palancas de la sala.\nResiste 30 segundos mientras se abre la salida.\nEvita que los cultistas rapten al jugador o a su aliado.\n\nPC: WASD · E interactuar · Espacio flash · B venda\nMóvil: joystick y botones en pantalla.",
                25, FontStyle.Normal, new Vector2(0f, 20f), new Vector2(850f, 270f));
            CreateButton(helpPanel.transform, "VOLVER", new Vector2(0f, -205f), Secondary, navigator.Back);
        }

        private void BuildMultiplayerPanel()
        {
            CreateText(multiplayerPanel.transform, "MULTIJUGADOR LOCAL", 42, FontStyle.Bold, new Vector2(0f, 230f), new Vector2(760f, 65f));
            CreateText(multiplayerPanel.transform, "Máximo 2 jugadores · el jugador 2 sustituye al aliado IA", 22, FontStyle.Normal, new Vector2(0f, 175f), new Vector2(800f, 42f), new Color(.72f, .75f, .8f));
            CreateButton(multiplayerPanel.transform, "CREAR PARTIDA", new Vector2(0f, 105f), Primary, StartHostGame);
            addressInput = CreateInputField(multiplayerPanel.transform, "127.0.0.1:7977", new Vector2(0f, 25f));
            CreateButton(multiplayerPanel.transform, "UNIRSE", new Vector2(0f, -55f), Primary, StartClientGame);
            networkStatus = CreateText(multiplayerPanel.transform, "Sin conexión", 20, FontStyle.Normal, new Vector2(0f, -115f), new Vector2(760f, 36f), new Color(.85f, .85f, .55f));
            CreateButton(multiplayerPanel.transform, "VOLVER", new Vector2(0f, -195f), Secondary, navigator.Back);
        }

        private void BuildResultPanel()
        {
            resultTitle = CreateText(resultPanel.transform, "RESULTADO", 52, FontStyle.Bold, new Vector2(0f, 180f), new Vector2(780f, 90f));
            resultBody = CreateText(resultPanel.transform, "", 26, FontStyle.Normal, new Vector2(0f, 65f), new Vector2(780f, 110f));
            CreateButton(resultPanel.transform, "REINTENTAR", new Vector2(0f, -65f), Primary, RestartGame);
            CreateButton(resultPanel.transform, "MENÚ", new Vector2(0f, -150f), Secondary, ShowMainMenu);
        }

        private GameObject CreatePanel(string panelName)
        {
            GameObject panel = new GameObject(panelName, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(transform, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = Backdrop;

            GameObject card = new GameObject("Tarjeta", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(panel.transform, false);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(.5f, .5f);
            cardRect.sizeDelta = new Vector2(980f, 650f);
            card.GetComponent<Image>().color = Surface;
            panel.SetActive(false);
            return panel;
        }

        private static Text CreateText(Transform panel, string value, int size, FontStyle style, Vector2 position, Vector2 dimensions, Color? color = null)
        {
            Transform card = panel.Find("Tarjeta");
            GameObject textObject = new GameObject("Texto", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(card, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color ?? Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void CreateButton(Transform panel, string label, Vector2 position, Color color, UnityEngine.Events.UnityAction action)
        {
            Transform card = panel.Find("Tarjeta");
            GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(card, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(440f, 66f);
            buttonObject.GetComponent<Image>().color = color;
            Button button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(action);
            CreateTextForButton(buttonObject.transform, label);
        }

        private static InputField CreateInputField(Transform panel, string value, Vector2 position)
        {
            Transform card = panel.Find("Tarjeta");
            GameObject fieldObject = new GameObject("Dirección IP", typeof(RectTransform), typeof(Image), typeof(InputField));
            fieldObject.transform.SetParent(card, false);
            RectTransform rect = fieldObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(440f, 58f);
            fieldObject.GetComponent<Image>().color = new Color(.04f, .055f, .08f, 1f);

            Text text = CreateTextForInput(fieldObject.transform, "Texto", value, Color.white);
            Text placeholder = CreateTextForInput(fieldObject.transform, "Placeholder", "IP:puerto del host (127.0.0.1:7977)", new Color(.55f, .58f, .65f));
            InputField input = fieldObject.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = value;
            input.contentType = InputField.ContentType.Standard;
            return input;
        }

        private static Text CreateTextForInput(Transform parent, string name, string value, Color color)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(18f, 6f);
            rect.offsetMax = new Vector2(-18f, -6f);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = 23;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = color;
            return text;
        }

        private static void CreateTextForButton(Transform button, string value)
        {
            GameObject textObject = new GameObject("Etiqueta", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(button, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = 25;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
        }

        private void StartGame()
        {
            Time.timeScale = 1f;
            mainPanel.SetActive(false);
            helpPanel.SetActive(false);
            resultPanel.SetActive(false);
            navigator.CloseAll();
        }

        private void StartHostGame()
        {
            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.EnsureFor(session);
            if (!bootstrap.StartHost(addressInput != null ? addressInput.text : "127.0.0.1:7977")) return;
            StartGame();
        }

        private void StartClientGame()
        {
            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.EnsureFor(session);
            if (!bootstrap.StartClient(addressInput != null ? addressInput.text : "127.0.0.1:7977")) return;
            StartGame();
        }

        private void RestartGame()
        {
            Time.timeScale = 1f;
            session.Restart();
        }

        private void ShowMainMenu()
        {
            Time.timeScale = 0f;
            navigator.Open(mainPanel);
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem.transform.SetAsFirstSibling();
        }
    }
}

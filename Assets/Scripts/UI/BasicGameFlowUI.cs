using System.Collections;
using System.Collections.Generic;
using System.Threading;
using SchoolEscape.Multiplayer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace SchoolEscape.UI
{
    [DisallowMultipleComponent]
    public sealed class BasicGameFlowUI : MonoBehaviour
    {
        private static readonly Color Backdrop = new Color(.018f, .025f, .04f, .96f);
        private static readonly Color Surface = new Color(.07f, .085f, .12f, .99f);
        private static readonly Color Primary = new Color(.65f, .08f, .1f, 1f);
        private static readonly Color Secondary = new Color(.14f, .2f, .28f, 1f);
        private static readonly Color Muted = new Color(.72f, .75f, .8f);

        private SchoolEscapePrototype session;
        private readonly List<GameObject> panels = new List<GameObject>();
        private GameObject mainPanel, helpPanel, creditsPanel, connectionPanel, lobbyPanel, soloConfirmPanel, resultPanel;
        private GameObject announcementPanel;
        private Text announcementText, networkStatus, lobbyCode, lobbyPlayers, lobbyStatus, readyLabel;
        private Text resultTitle, resultBody, resultStatus;
        private InputField codeInput, nameInput;
        private Button readyButton, retryButton, resultExitButton;
        private string confirmedPlayerName;
        private bool resultShown, victoryReturnScheduled, previousGameplayActive;
        private GameObject playPanel, friendsPanel;
        private Text playStatus, playName;
        private Button quickButton;
        private Button lobbyFriendsButton;
        private CancellationTokenSource quickSearch;
        private bool soloPlaying, soloStarting;
        private int playMode;
        private Text modeLabel;
        private static readonly string[] Modes = { "PARTIDA RÁPIDA", "CON AMIGOS", "JUGAR SOLO" };

        public static bool HasAnnouncementPresenter { get; private set; }
        public static bool IsMenuVisible { get; private set; }

        public static void EnsureFor(SchoolEscapePrototype gameSession)
        {
            BasicGameFlowUI existing = FindFirstObjectByType<BasicGameFlowUI>();
            if (existing != null) { existing.session = gameSession; return; }
            GameObject prefab = Resources.Load<GameObject>("UI/SchoolEscapeGameFlowCanvas");
            if (prefab != null)
            {
                BasicGameFlowUI prefabFlow = Instantiate(prefab).GetComponent<BasicGameFlowUI>();
                prefabFlow.session = gameSession;
                return;
            }
            GameObject root = new GameObject("School Escape UI - Canvas editable");
            root.AddComponent<BasicGameFlowUI>().session = gameSession;
        }

        private void Start()
        {
            if (transform.Find("03 - Lobby") != null) BindPrefabUi();
            else BuildUi();
            lobbyPlayers.raycastTarget = false;
            lobbyStatus.raycastTarget = false;
            readyLabel.raycastTarget = false;
            confirmedPlayerName = SchoolEscapeNetworkBootstrap.LoadSavedPlayerName();
            nameInput.text = confirmedPlayerName;
            BuildPlayCenter();
            SchoolEscapeSocialUI.EnsureFor(transform, friendsPanel, OpenLobby, () =>
                OpenPanel(SchoolEscapeNetworkBootstrap.Instance?.IsOnlineSession == true ? lobbyPanel : playPanel));
            WireButton(mainPanel, "Botón - JUGAR", OpenPlayCenter);
            WireButton(connectionPanel, "Botón - VOLVER", () => OpenPanel(playPanel));
            ShowMainMenu();
        }

        private void Update()
        {
            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.Instance;
            quickButton.interactable = quickSearch != null || (!soloStarting && (bootstrap == null || !bootstrap.IsConnecting));
            playName.text = confirmedPlayerName;
            if (soloPlaying && !resultShown && session != null && session.DisplayMatchResult != PrototypeMatchResult.InProgress)
                ShowResult(session.DisplayMatchResult, null);
            if (networkStatus != null && bootstrap != null) networkStatus.text = bootstrap.Status;
            UpdateAnnouncement();
            UpdateLobby(bootstrap);

            if (bootstrap != null && !previousGameplayActive && bootstrap.GameplayActive)
            {
                CloseAllPanels();
                if (session != null && session.MobileHud != null) session.MobileHud.gameObject.SetActive(true);
                Time.timeScale = 1f;
            }
            else if (bootstrap != null && previousGameplayActive && !bootstrap.GameplayActive)
            {
                resultShown = false;
                victoryReturnScheduled = false;
                if (bootstrap.IsOnlineSession) OpenPanel(lobbyPanel);
                else
                {
                    OpenPanel(playPanel);
                    playStatus.text = bootstrap.Status;
                }
                Time.timeScale = 0f;
            }
            previousGameplayActive = bootstrap != null && bootstrap.GameplayActive;

            if (bootstrap != null && !bootstrap.IsOnlineSession && !bootstrap.IsListening &&
                !soloPlaying && (lobbyPanel.activeSelf || resultPanel.activeSelf))
            {
                OpenPanel(playPanel);
                playStatus.text = bootstrap.Status;
            }
            if (session == null || resultShown || bootstrap == null || !bootstrap.GameplayActive) return;
            PrototypeMatchResult result = session.DisplayMatchResult;
            if (result != PrototypeMatchResult.InProgress) ShowResult(result, bootstrap);
        }

        private void OnDestroy()
        {
            quickSearch?.Cancel();
            HasAnnouncementPresenter = false;
            IsMenuVisible = false;
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
            BuildAnnouncement();
            mainPanel = CreatePanel("01 - Menú principal");
            connectionPanel = CreatePanel("02 - Crear o unirse");
            lobbyPanel = CreatePanel("03 - Lobby");
            soloConfirmPanel = CreatePanel("04 - Confirmar solitario");
            helpPanel = CreatePanel("05 - Cómo jugar");
            creditsPanel = CreatePanel("06 - Créditos");
            resultPanel = CreatePanel("07 - Resultado");
            BuildMainPanel(); BuildConnectionPanel(); BuildLobbyPanel(); BuildSoloConfirmPanel();
            BuildHelpPanel(); BuildCreditsPanel(); BuildResultPanel();
        }

        private void BuildMainPanel()
        {
            CreateText(mainPanel.transform, "ESCAPE DEL\nINSTITUTO MALDITO", 54, FontStyle.Bold, new Vector2(0, 215), new Vector2(850, 160));
            CreateText(mainPanel.transform, "PROTOTIPO MVP", 20, FontStyle.Bold, new Vector2(0, 120), new Vector2(500, 40), Muted);
            CreateButton(mainPanel.transform, "JUGAR", new Vector2(0, 45), Primary, () => OpenPanel(connectionPanel));
            CreateButton(mainPanel.transform, "CÓMO JUGAR", new Vector2(0, -35), Secondary, () => OpenPanel(helpPanel));
            CreateButton(mainPanel.transform, "CRÉDITOS", new Vector2(0, -115), Secondary, () => OpenPanel(creditsPanel));
            CreateButton(mainPanel.transform, "SALIR", new Vector2(0, -195), Secondary, QuitGame);
        }

        private void BuildConnectionPanel()
        {
            CreateText(connectionPanel.transform, "JUGAR", 46, FontStyle.Bold, new Vector2(0, 225), new Vector2(700, 70));
            CreateText(connectionPanel.transform, "Crea una sala o escribe el código de un amigo", 23, FontStyle.Normal, new Vector2(0, 165), new Vector2(800, 45), Muted);
            CreateButton(connectionPanel.transform, "CREAR SALA", new Vector2(0, 85), Primary, CreateRoom);
            codeInput = CreateInputField(connectionPanel.transform, "", "CÓDIGO DE SALA", new Vector2(0, 5));
            codeInput.characterLimit = 12;
            CreateButton(connectionPanel.transform, "UNIRSE", new Vector2(0, -75), Primary, JoinRoom);
            networkStatus = CreateText(connectionPanel.transform, "Sin conexión", 20, FontStyle.Normal, new Vector2(0, -135), new Vector2(800, 42), new Color(.85f, .85f, .55f));
            CreateButton(connectionPanel.transform, "VOLVER", new Vector2(0, -210), Secondary, () => OpenPanel(playPanel));
        }

        private void BuildPlayCenter()
        {
            playPanel = CreatePanel("08 - Centro de juego");
            friendsPanel = CreatePanel("09 - Amigos");
            lobbyFriendsButton = CreateButton(lobbyPanel.transform, "AMIGOS", new Vector2(350, -250), Secondary, OpenFriends, new Vector2(180, 48));
            lobbyFriendsButton.gameObject.SetActive(false);
            Transform panel = playPanel.transform;
            playName = CreateText(panel, confirmedPlayerName, 26, FontStyle.Bold, new Vector2(-270, 270), new Vector2(360, 52));
            CreateButton(panel, "AMIGOS", new Vector2(335, 270), Secondary, OpenFriends, new Vector2(220, 52));
            var avatar = CreateText(panel, "●", 130, FontStyle.Bold, new Vector2(0, 105), new Vector2(320, 180), new Color(.2f, .65f, 1f));
            avatar.gameObject.name = "Vista del personaje (reemplazable)";
            modeLabel = CreateText(panel, Modes[0], 30, FontStyle.Bold, new Vector2(0, -45), new Vector2(430, 60));
            CreateButton(panel, "‹", new Vector2(-265, -45), Secondary, () => ChangeMode(-1), new Vector2(72, 58));
            CreateButton(panel, "›", new Vector2(265, -45), Secondary, () => ChangeMode(1), new Vector2(72, 58));
            quickButton = CreateButton(panel, "BUSCAR PARTIDA", new Vector2(0, -130), Primary, ChooseMode);
            playStatus = CreateText(panel, "", 20, FontStyle.Normal, new Vector2(0, -208), new Vector2(780, 65), Muted);
            CreateButton(panel, "OPCIONES POR CÓDIGO", new Vector2(150, -280), Secondary, () =>
            {
                if (quickSearch == null) OpenPanel(connectionPanel);
            }, new Vector2(290, 52));
            CreateButton(panel, "VOLVER", new Vector2(-170, -280), Secondary, () =>
            {
                if (quickSearch == null) ShowMainMenu();
            }, new Vector2(260, 52));
        }

        private void ChangeMode(int direction)
        {
            if (quickSearch != null || soloStarting) return;
            playMode = (playMode + direction + Modes.Length) % Modes.Length;
            modeLabel.text = Modes[playMode];
            quickButton.GetComponentInChildren<Text>().text = playMode == 0 ? "BUSCAR PARTIDA" :
                playMode == 1 ? "ABRIR AMIGOS" : "EMPEZAR";
            playStatus.text = string.Empty;
        }

        private void OpenFriends()
        {
            if (quickSearch != null || soloStarting || SchoolEscapeNetworkBootstrap.Instance?.IsConnecting == true) return;
            OpenPanel(friendsPanel);
            _ = SchoolEscapeSocialServices.Ensure().ConnectFriendsAsync();
        }

        private void OpenPlayCenter()
        {
            OpenPanel(playPanel);
            _ = SchoolEscapeSocialServices.Ensure().ConnectFriendsAsync();
        }

        private async void ChooseMode()
        {
            if (quickSearch != null) { quickSearch.Cancel(); return; }
            if (playMode == 1) { OpenFriends(); return; }
            if (playMode == 2) { StartCoroutine(StartSolo()); return; }
            var bootstrap = SchoolEscapeNetworkBootstrap.EnsureFor(session);
            using var search = new CancellationTokenSource();
            quickSearch = search;
            quickButton.GetComponentInChildren<Text>().text = "CANCELAR";
            playStatus.text = "Buscando compañero…";
            try
            {
                if (await bootstrap.FindQuickMatchAsync(confirmedPlayerName, search.Token))
                    OpenLobby();
                if (this != null) playStatus.text = bootstrap.Status;
            }
            finally
            {
                quickSearch = null;
                if (this != null) quickButton.GetComponentInChildren<Text>().text = "BUSCAR PARTIDA";
            }
        }

        private IEnumerator StartSolo()
        {
            if (soloStarting || session == null) yield break;
            soloStarting = true;
            yield return session.ResetForNetworkMatch(forceLocal: true);
            soloPlaying = true;
            soloStarting = resultShown = victoryReturnScheduled = false;
            CloseAllPanels();
            Time.timeScale = 1f;
        }

        private void BuildLobbyPanel()
        {
            CreateText(lobbyPanel.transform, "LOBBY", 44, FontStyle.Bold, new Vector2(0, 250), new Vector2(700, 65));
            lobbyCode = CreateText(lobbyPanel.transform, "CÓDIGO: —", 25, FontStyle.Bold, new Vector2(-235, 185), new Vector2(430, 50));
            lobbyCode.gameObject.name = "Lobby - Código";
            CreateButton(lobbyPanel.transform, "COPIAR", new Vector2(245, 185), Secondary, CopyRoomCode, new Vector2(240, 52));
            CreateText(lobbyPanel.transform, "PERSONAJE", 18, FontStyle.Bold, new Vector2(0, 100), new Vector2(300, 35), Muted);
            Text character = CreateText(lobbyPanel.transform, "●", 108, FontStyle.Bold, new Vector2(0, 5), new Vector2(180, 150), new Color(.2f, .65f, 1f));
            character.gameObject.name = "Vista del personaje (reemplazable)";
            nameInput = CreateInputField(lobbyPanel.transform, "Jugador", "NOMBRE", new Vector2(255, 105));
            nameInput.onEndEdit.AddListener(ConfirmPlayerName);
            lobbyPlayers = CreateText(lobbyPanel.transform, "Host\nEsperando jugador 2…", 23, FontStyle.Normal, new Vector2(-250, -5), new Vector2(390, 150));
            lobbyPlayers.gameObject.name = "Lobby - Jugadores";
            lobbyStatus = CreateText(lobbyPanel.transform, "Marca LISTO para comenzar", 22, FontStyle.Normal, new Vector2(0, -105), new Vector2(780, 55), new Color(.9f, .85f, .55f));
            lobbyStatus.gameObject.name = "Lobby - Estado";
            readyButton = CreateButton(lobbyPanel.transform, "LISTO", new Vector2(0, -175), Primary, ToggleReady);
            readyLabel = readyButton.GetComponentInChildren<Text>();
            CreateButton(lobbyPanel.transform, "ABANDONAR", new Vector2(0, -250), Secondary, LeaveRoom);
        }

        private void BuildSoloConfirmPanel()
        {
            CreateText(soloConfirmPanel.transform, "PARTIDA SOLITARIA", 44, FontStyle.Bold, new Vector2(0, 150), new Vector2(760, 70));
            CreateText(soloConfirmPanel.transform, "Estás solo en la sala. Si continúas, jugarás con el aliado controlado por la IA.", 25, FontStyle.Normal, new Vector2(0, 35), new Vector2(800, 130));
            CreateButton(soloConfirmPanel.transform, "JUGAR SOLO", new Vector2(0, -75), Primary, ConfirmSoloStart);
            CreateButton(soloConfirmPanel.transform, "ESPERAR", new Vector2(0, -155), Secondary, () => OpenPanel(lobbyPanel));
        }

        private void BuildHelpPanel()
        {
            CreateText(helpPanel.transform, "CÓMO JUGAR", 42, FontStyle.Bold, new Vector2(0, 205), new Vector2(700, 70));
            CreateText(helpPanel.transform, "Activa todas las palancas de la sala.\nResiste 30 segundos mientras se abre la salida.\nEvita que los cultistas rapten al jugador o a su aliado.\n\nPC: WASD · E interactuar · Espacio flash · B venda\nMóvil: joystick y botones en pantalla.", 25, FontStyle.Normal, new Vector2(0, 20), new Vector2(850, 270));
            CreateButton(helpPanel.transform, "VOLVER", new Vector2(0, -205), Secondary, ShowMainMenu);
        }

        private void BuildCreditsPanel()
        {
            CreateText(creditsPanel.transform, "CRÉDITOS", 44, FontStyle.Bold, new Vector2(0, 205), new Vector2(700, 70));
            CreateText(creditsPanel.transform, "Ayudante John\nCoronel Oscar\nEspino Ricardo\nGutierrez Ismael", 29, FontStyle.Normal, new Vector2(0, 25), new Vector2(700, 250));
            CreateButton(creditsPanel.transform, "VOLVER", new Vector2(0, -205), Secondary, ShowMainMenu);
        }

        private void BuildResultPanel()
        {
            resultTitle = CreateText(resultPanel.transform, "RESULTADO", 52, FontStyle.Bold, new Vector2(0, 190), new Vector2(780, 90));
            resultTitle.gameObject.name = "Resultado - Título";
            resultBody = CreateText(resultPanel.transform, "", 26, FontStyle.Normal, new Vector2(0, 55), new Vector2(820, 180));
            resultBody.gameObject.name = "Resultado - Cuerpo";
            resultStatus = CreateText(resultPanel.transform, "", 21, FontStyle.Normal, new Vector2(0, -55), new Vector2(800, 55), new Color(.9f, .85f, .55f));
            resultStatus.gameObject.name = "Resultado - Estado";
            retryButton = CreateButton(resultPanel.transform, "VOLVER A INTENTAR", new Vector2(0, -130), Primary, RetryGame);
            resultExitButton = CreateButton(resultPanel.transform, "SALIR AL LOBBY", new Vector2(0, -210), Secondary, ExitResultToLobby);
        }

        private void BuildAnnouncement()
        {
            announcementPanel = new GameObject("Anuncio sincronizado", typeof(RectTransform), typeof(Image));
            announcementPanel.transform.SetParent(transform, false);
            RectTransform rect = announcementPanel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, -24); rect.sizeDelta = new Vector2(980, 72);
            announcementPanel.GetComponent<Image>().color = new Color(.025f, .035f, .055f, .9f);
            announcementText = CreateRawText(announcementPanel.transform, "Texto", 28, FontStyle.Bold, TextAnchor.MiddleCenter);
            announcementText.rectTransform.anchorMin = Vector2.zero; announcementText.rectTransform.anchorMax = Vector2.one;
            announcementText.rectTransform.offsetMin = new Vector2(24, 8); announcementText.rectTransform.offsetMax = new Vector2(-24, -8);
            announcementPanel.SetActive(false); HasAnnouncementPresenter = true;
        }

        private void BindPrefabUi()
        {
            EnsureEventSystem();
            mainPanel = transform.Find("01 - Menú principal").gameObject;
            connectionPanel = transform.Find("02 - Crear o unirse").gameObject;
            lobbyPanel = transform.Find("03 - Lobby").gameObject;
            soloConfirmPanel = transform.Find("04 - Confirmar solitario").gameObject;
            helpPanel = transform.Find("05 - Cómo jugar").gameObject;
            creditsPanel = transform.Find("06 - Créditos").gameObject;
            resultPanel = transform.Find("07 - Resultado").gameObject;
            panels.Clear();
            panels.AddRange(new[] { mainPanel, connectionPanel, lobbyPanel, soloConfirmPanel, helpPanel, creditsPanel, resultPanel });
            announcementPanel = transform.Find("Anuncio sincronizado").gameObject;
            announcementText = announcementPanel.transform.Find("Texto").GetComponent<Text>();
            HasAnnouncementPresenter = true;

            codeInput = FindComponent<InputField>(connectionPanel, "Campo - CÓDIGO DE SALA");
            networkStatus = FindComponent<Text>(connectionPanel, "Texto - Sin conexión");
            nameInput = FindComponent<InputField>(lobbyPanel, "Campo - NOMBRE");
            lobbyCode = FindComponent<Text>(lobbyPanel, "Lobby - Código");
            lobbyPlayers = FindComponent<Text>(lobbyPanel, "Lobby - Jugadores");
            lobbyStatus = FindComponent<Text>(lobbyPanel, "Lobby - Estado");
            readyButton = FindComponent<Button>(lobbyPanel, "Botón - LISTO");
            readyLabel = readyButton.GetComponentInChildren<Text>();
            resultTitle = FindComponent<Text>(resultPanel, "Resultado - Título");
            resultBody = FindComponent<Text>(resultPanel, "Resultado - Cuerpo");
            resultStatus = FindComponent<Text>(resultPanel, "Resultado - Estado");
            retryButton = FindComponent<Button>(resultPanel, "Botón - VOLVER A INTENTAR");
            resultExitButton = FindComponent<Button>(resultPanel, "Botón - SALIR AL LOBBY");

            WireButton(mainPanel, "Botón - JUGAR", () => OpenPanel(connectionPanel));
            WireButton(mainPanel, "Botón - CÓMO JUGAR", () => OpenPanel(helpPanel));
            WireButton(mainPanel, "Botón - CRÉDITOS", () => OpenPanel(creditsPanel));
            WireButton(mainPanel, "Botón - SALIR", QuitGame);
            WireButton(connectionPanel, "Botón - CREAR SALA", CreateRoom);
            WireButton(connectionPanel, "Botón - UNIRSE", JoinRoom);
            WireButton(connectionPanel, "Botón - VOLVER", ShowMainMenu);
            WireButton(lobbyPanel, "Botón - COPIAR", CopyRoomCode);
            WireButton(lobbyPanel, "Botón - LISTO", ToggleReady);
            WireButton(lobbyPanel, "Botón - ABANDONAR", LeaveRoom);
            WireButton(soloConfirmPanel, "Botón - JUGAR SOLO", ConfirmSoloStart);
            WireButton(soloConfirmPanel, "Botón - ESPERAR", () => OpenPanel(lobbyPanel));
            WireButton(helpPanel, "Botón - VOLVER", ShowMainMenu);
            WireButton(creditsPanel, "Botón - VOLVER", ShowMainMenu);
            WireButton(resultPanel, "Botón - VOLVER A INTENTAR", RetryGame);
            WireButton(resultPanel, "Botón - SALIR AL LOBBY", ExitResultToLobby);
            nameInput.onEndEdit.RemoveAllListeners();
            nameInput.onEndEdit.AddListener(ConfirmPlayerName);
        }

        private static T FindComponent<T>(GameObject root, string objectName) where T : Component
        {
            foreach (T component in root.GetComponentsInChildren<T>(true))
                if (component.gameObject.name == objectName) return component;
            return null;
        }

        private static void WireButton(GameObject panel, string objectName, UnityEngine.Events.UnityAction action)
        {
            Button button = FindComponent<Button>(panel, objectName);
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private void UpdateAnnouncement()
        {
            if (announcementPanel == null) return;
            string value = session != null ? session.DisplayAnnouncementText : string.Empty;
            bool visible = !string.IsNullOrEmpty(value) && panels.TrueForAll(panel => !panel.activeSelf);
            announcementText.text = value;
            if (announcementPanel.activeSelf != visible) announcementPanel.SetActive(visible);
        }

        private void UpdateLobby(SchoolEscapeNetworkBootstrap bootstrap)
        {
            if (bootstrap == null || lobbyPanel == null) return;
            lobbyFriendsButton.gameObject.SetActive(bootstrap.CanInvite);
            lobbyCode.text = "CÓDIGO: " + (string.IsNullOrEmpty(bootstrap.SessionCode) ? "—" : bootstrap.SessionCode);
            lobbyCode.gameObject.SetActive(bootstrap.ShowsRoomCode);
            var copy = FindComponent<Button>(lobbyPanel, "Botón - COPIAR");
            if (copy != null) copy.gameObject.SetActive(bootstrap.ShowsRoomCode);
            lobbyPlayers.text = $"{bootstrap.HostPlayerName}  {(bootstrap.HostReady ? "✓ LISTO" : "· ESPERANDO")}\n" +
                (bootstrap.HasRemoteClient || !string.IsNullOrEmpty(bootstrap.ClientPlayerName) ? $"{bootstrap.ClientPlayerName}  {(bootstrap.ClientReady ? "✓ LISTO" : "· ESPERANDO")}" : "Esperando jugador 2…");
            lobbyStatus.text = bootstrap.MatchStartInProgress ? "Sincronizando jugadores…"
                : bootstrap.IsReadyVotePending ? "Confirmando con el anfitrión…"
                : !bootstrap.CanSetReady ? (bootstrap.IsListening ? "Conectando al lobby…" : bootstrap.Status)
                : !string.IsNullOrEmpty(bootstrap.ReadyVoteError) ? bootstrap.ReadyVoteError
                : !string.IsNullOrEmpty(bootstrap.MatchStartError) ? bootstrap.MatchStartError
                : bootstrap.HasRemoteClient || !bootstrap.IsHost
                    ? "Ambos jugadores deben estar listos" : "Esperando al segundo jugador…";
            readyButton.interactable = bootstrap.CanSetReady;
            nameInput.interactable = bootstrap.CanSetReady;
            readyLabel.text = bootstrap.LocalReady ? "NO ESTOY LISTO" : "LISTO";
            if (bootstrap.MatchStartInProgress && nameInput.isFocused)
            {
                nameInput.DeactivateInputField();
                nameInput.text = confirmedPlayerName;
            }
        }

        private void ShowResult(PrototypeMatchResult result, SchoolEscapeNetworkBootstrap bootstrap)
        {
            resultShown = true; Time.timeScale = 0f;
            bool victory = result != PrototypeMatchResult.TotalDefeat;
            resultTitle.text = result == PrototypeMatchResult.PerfectEscape ? "ESCAPE PERFECTO" : result == PrototypeMatchResult.PartialEscape ? "ESCAPE PARCIAL" : "DERROTA TOTAL";
            resultBody.text = $"Tiempo: {session.DisplayRunTime:0.0} s · {session.DisplayRoomProgressText}\n{session.MemorialText}";
            retryButton.gameObject.SetActive(!victory); resultExitButton.gameObject.SetActive(!victory);
            retryButton.interactable = true;
            resultStatus.text = victory ? "Volviendo en 6 segundos…" : soloPlaying
                ? "Puedes reintentar o volver al centro de juego." : "Ambos jugadores deben aceptar para reintentar.";
            OpenPanel(resultPanel);
            if (victory && !victoryReturnScheduled) { victoryReturnScheduled = true; StartCoroutine(ReturnToLobbyAfterVictory(bootstrap)); }
        }

        private IEnumerator ReturnToLobbyAfterVictory(SchoolEscapeNetworkBootstrap bootstrap)
        {
            yield return new WaitForSecondsRealtime(6f);
            if (soloPlaying) { ExitResultToLobby(); yield break; }
            if (bootstrap != null && bootstrap.IsHost) bootstrap.ReturnAfterVictory();
        }

        private async void CreateRoom()
        {
            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.EnsureFor(session);
            networkStatus.text = "Creando sala…";
            if (await bootstrap.CreateOnlineSessionAsync(confirmedPlayerName)) OpenLobby();
        }

        private async void JoinRoom()
        {
            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.EnsureFor(session);
            networkStatus.text = "Uniéndose…";
            if (await bootstrap.JoinOnlineSessionAsync(codeInput.text, confirmedPlayerName)) OpenLobby();
        }

        private void OpenLobby()
        {
            SchoolEscapeNetworkBootstrap.Instance?.SetLocalPlayerName(confirmedPlayerName);
            nameInput.text = confirmedPlayerName; resultShown = false; victoryReturnScheduled = false;
            Time.timeScale = 0f; OpenPanel(lobbyPanel);
        }

        private void ToggleReady()
        {
            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.Instance;
            if (bootstrap == null || !bootstrap.CanSetReady) return;
            bootstrap.SetLocalReady(!bootstrap.LocalReady);
        }

        private void ConfirmSoloStart() { OpenPanel(lobbyPanel); SchoolEscapeNetworkBootstrap.Instance?.SetLocalReady(true); }

        private void ConfirmPlayerName(string value)
        {
            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.Instance;
            if (bootstrap != null && bootstrap.MatchStartInProgress) { nameInput.text = confirmedPlayerName; return; }
            confirmedPlayerName = string.IsNullOrWhiteSpace(value) ? "Jugador" : value.Trim();
            if (confirmedPlayerName.Length > 18) confirmedPlayerName = confirmedPlayerName.Substring(0, 18);
            nameInput.text = confirmedPlayerName; bootstrap?.SetLocalPlayerName(confirmedPlayerName);
        }

        private void RetryGame()
        {
            if (soloPlaying) { StartCoroutine(StartSolo()); return; }
            SchoolEscapeNetworkBootstrap.Instance?.SubmitRetryVote(true);
            retryButton.interactable = false;
            resultStatus.text = "Esperando la decisión del otro jugador…";
        }
        private void ExitResultToLobby()
        {
            if (!soloPlaying) { SchoolEscapeNetworkBootstrap.Instance?.SubmitRetryVote(false); return; }
            soloPlaying = false;
            OpenPanel(playPanel);
        }
        private async void LeaveRoom()
        {
            var bootstrap = SchoolEscapeNetworkBootstrap.Instance;
            if (bootstrap == null || bootstrap.IsConnecting) return;
            await bootstrap.LeaveOnlineSessionAsync();
            if (this != null) OpenPanel(playPanel);
        }
        private void CopyRoomCode() { string code = SchoolEscapeNetworkBootstrap.Instance?.SessionCode; if (!string.IsNullOrEmpty(code)) GUIUtility.systemCopyBuffer = code; }
        private void ShowMainMenu() { Time.timeScale = 0f; OpenPanel(mainPanel); }
        private void OpenPanel(GameObject target)
        {
            foreach (GameObject panel in panels) panel.SetActive(panel == target);
            IsMenuVisible = target != null;
            if (session != null && session.MobileHud != null) session.MobileHud.gameObject.SetActive(target == null);
        }

        private void CloseAllPanels() => OpenPanel(null);

        private GameObject CreatePanel(string panelName)
        {
            GameObject panel = new GameObject(panelName, typeof(RectTransform), typeof(Image)); panel.transform.SetParent(transform, false);
            RectTransform rect = panel.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = Backdrop;
            GameObject card = new GameObject("Tarjeta editable", typeof(RectTransform), typeof(Image)); card.transform.SetParent(panel.transform, false);
            RectTransform cardRect = card.GetComponent<RectTransform>(); cardRect.anchorMin = cardRect.anchorMax = new Vector2(.5f, .5f); cardRect.sizeDelta = new Vector2(980, 700);
            card.GetComponent<Image>().color = Surface; panel.SetActive(false); panels.Add(panel); return panel;
        }

        private static Text CreateText(Transform panel, string value, int size, FontStyle style, Vector2 position, Vector2 dimensions, Color? color = null)
        {
            Text text = CreateRawText(panel.Find("Tarjeta editable"), "Texto - " + value.Replace("\n", " "), size, style, TextAnchor.MiddleCenter);
            RectTransform rect = text.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = dimensions;
            text.text = value; text.color = color ?? Color.white; return text;
        }

        private static Button CreateButton(Transform panel, string label, Vector2 position, Color color, UnityEngine.Events.UnityAction action, Vector2? size = null)
        {
            GameObject go = new GameObject("Botón - " + label, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(panel.Find("Tarjeta editable"), false);
            RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size ?? new Vector2(440, 66);
            go.GetComponent<Image>().color = color; Button button = go.GetComponent<Button>(); button.onClick.AddListener(action);
            Text text = CreateRawText(go.transform, "Etiqueta", 25, FontStyle.Bold, TextAnchor.MiddleCenter); text.text = label;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private static InputField CreateInputField(Transform panel, string value, string hint, Vector2 position)
        {
            GameObject go = new GameObject("Campo - " + hint, typeof(RectTransform), typeof(Image), typeof(InputField)); go.transform.SetParent(panel.Find("Tarjeta editable"), false);
            RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(440, 58);
            go.GetComponent<Image>().color = new Color(.04f, .055f, .08f, 1);
            Text text = CreateRawText(go.transform, "Texto", 23, FontStyle.Normal, TextAnchor.MiddleLeft);
            Text placeholder = CreateRawText(go.transform, "Placeholder", 21, FontStyle.Normal, TextAnchor.MiddleLeft);
            foreach (Text item in new[] { text, placeholder }) { item.rectTransform.anchorMin = Vector2.zero; item.rectTransform.anchorMax = Vector2.one; item.rectTransform.offsetMin = new Vector2(18, 6); item.rectTransform.offsetMax = new Vector2(-18, -6); }
            placeholder.text = hint; placeholder.color = new Color(.55f, .58f, .65f);
            InputField input = go.GetComponent<InputField>(); input.textComponent = text; input.placeholder = placeholder; input.text = value; return input;
        }

        private static Text CreateRawText(Transform parent, string name, int size, FontStyle style, TextAnchor alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = size; text.fontStyle = style;
            text.alignment = alignment; text.color = Color.white; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow; return text;
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
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetAsFirstSibling();
        }
    }
}

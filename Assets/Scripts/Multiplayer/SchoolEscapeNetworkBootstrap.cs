using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using System.Collections;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace SchoolEscape.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class SchoolEscapeNetworkBootstrap : MonoBehaviour
    {
        public const ushort DefaultPort = 7977;

        public static SchoolEscapeNetworkBootstrap Instance { get; private set; }
        public string Status { get; private set; } = "Sin conexión";
        public bool IsListening => manager != null && manager.IsListening;
        public bool HasRemoteClient => manager != null && manager.IsServer && remoteClientId.HasValue;
        public bool IsOnlineSession => onlineSession != null;
        public bool IsHost => manager != null && manager.IsHost;
        public bool GameplayActive { get; private set; }
        public bool CountdownActive => countdownEndsAt > 0f;
        public int CountdownSeconds => CountdownActive ? Mathf.Max(1, Mathf.CeilToInt(countdownEndsAt - Time.unscaledTime)) : 0;
        public string SessionCode => onlineSession != null ? onlineSession.Code : string.Empty;
        public string LocalPlayerName { get; private set; } = "Jugador";
        public string HostPlayerName { get; private set; } = "Host";
        public string ClientPlayerName { get; private set; } = string.Empty;
        public bool HostReady { get; private set; }
        public bool ClientReady { get; private set; }
        public bool LocalReady => IsHost ? HostReady : ClientReady;
        public bool HostRetryVote { get; private set; }
        public bool ClientRetryVote { get; private set; }

        private const string HostPlayerResource = "Multiplayer/NetworkHostPlayer";
        private const string ClientAllyResource = "Multiplayer/NetworkClientAlly";
        private const string EnemySnapshotMessage = "SchoolEscape.EnemySnapshot";
        private const string LobbyCommandMessage = "SchoolEscape.LobbyCommand";
        private const string LobbySnapshotMessage = "SchoolEscape.LobbySnapshot";
        private const float EnemySnapshotInterval = 1f / 15f;
        public const float AoiEnterDistance = 18f;
        public const float AoiExitDistance = 22f;
        private const int SecurityKickThreshold = 6;
        private const double SecurityStrikeWindowSeconds = 10d;

        private SchoolEscapePrototype session;
        private NetworkManager manager;
        private UnityTransport transport;
        private GameObject hostPlayerPrefab;
        private GameObject clientAllyPrefab;
        private NetworkObject remoteAlly;
        private bool startInProgress;
        private ushort activePort = DefaultPort;
        private ulong? remoteClientId;
        private float nextEnemySnapshotTime;
        private bool enemyHandlerRegistered;
        private readonly List<EnemyVisualProxy> enemyVisuals = new List<EnemyVisualProxy>();
        private readonly List<PrototypeEnemy> relevantEnemies = new List<PrototypeEnemy>(24);
        private readonly HashSet<int> visibleEnemyIds = new HashSet<int>();
        private readonly HashSet<int> nextVisibleEnemyIds = new HashSet<int>();
        private readonly ServerRewindHistory rewindHistory = new ServerRewindHistory();
        private int securityStrikes;
        private double securityWindowStartedAt = -1d;
        private ISession onlineSession;
        private IHostSession hostOnlineSession;
        private bool lobbyHandlersRegistered;
        private float countdownEndsAt = -1f;
        private bool countdownStartsMatch = true;
        private bool matchStartInProgress;
        private bool leavingOnlineSession;
        private const string PlayerNameKey = "school_escape_player_name";

        public int RemoteEnemyCount => enemyVisuals.Count;
        public int LatestServerEnemyCount { get; private set; }
        public int RemoteNormalEnemyCount { get; private set; }
        public int RemoteSmallEnemyCount { get; private set; }
        public int KnownRelevantObjectCount =>
            (manager != null && manager.SpawnManager != null ? manager.SpawnManager.SpawnedObjects.Count : 0) +
            enemyVisuals.Count;
        public int AoiEnterEvents { get; private set; }
        public int AoiExitEvents { get; private set; }
        public int EnemySnapshotsSent { get; private set; }
        public long EnemySnapshotPayloadBytes { get; private set; }
        public string AoiStatusText => manager != null && manager.IsClient && !manager.IsServer
            ? $"Red: {KnownRelevantObjectCount} conocidos · AoI {RemoteEnemyCount}/{LatestServerEnemyCount}"
            : manager != null && manager.IsServer && remoteClientId.HasValue
                ? $"AoI cliente: {visibleEnemyIds.Count}/{(session != null ? session.ActiveEnemyCount : 0)}"
                : "Red: sin cliente remoto";
        public double LatestEnemySnapshotServerTime { get; private set; }
        public int LatestEnemySnapshotServerTick { get; private set; }
        public ServerRewindHistory RewindHistory => rewindHistory;
        public int RejectedMovementCount { get; private set; }
        public int RejectedActionCount { get; private set; }
        public int SecurityStrikeCount => securityStrikes;
        public bool SecurityClientKicked { get; private set; }
        public string LastSecurityEvent { get; private set; } = string.Empty;
        public double ObservedServerTime => LatestEnemySnapshotServerTime > 0d
            ? LatestEnemySnapshotServerTime
            : manager != null ? manager.ServerTime.Time : 0d;

        private sealed class EnemyVisualProxy
        {
            public int Id;
            public GameObject GameObject;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation;
            public bool IsSmall;
        }

        public static SchoolEscapeNetworkBootstrap EnsureFor(SchoolEscapePrototype gameSession)
        {
            if (Instance != null)
            {
                Instance.session = gameSession;
                return Instance;
            }

            SchoolEscapeNetworkBootstrap existing = FindFirstObjectByType<SchoolEscapeNetworkBootstrap>();
            if (existing != null)
            {
                existing.session = gameSession;
                return existing;
            }

            GameObject root = new GameObject("School Escape Multiplayer");
            SchoolEscapeNetworkBootstrap bootstrap = root.AddComponent<SchoolEscapeNetworkBootstrap>();
            bootstrap.session = gameSession;
            return bootstrap;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ConfigureNetworkManager();
        }

        private void ConfigureNetworkManager()
        {
            manager = GetComponent<NetworkManager>();
            if (manager == null) manager = gameObject.AddComponent<NetworkManager>();
            transport = GetComponent<UnityTransport>();
            if (transport == null) transport = gameObject.AddComponent<UnityTransport>();

            hostPlayerPrefab = Resources.Load<GameObject>(HostPlayerResource);
            clientAllyPrefab = Resources.Load<GameObject>(ClientAllyResource);
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ConnectionApproval = true,
                TickRate = 30,
                PlayerPrefab = null
            };

            if (hostPlayerPrefab != null) manager.AddNetworkPrefab(hostPlayerPrefab);
            if (clientAllyPrefab != null) manager.AddNetworkPrefab(clientAllyPrefab);

            manager.ConnectionApprovalCallback = ApproveConnection;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            manager.OnTransportFailure += OnTransportFailure;
        }

        private void Update()
        {
            if (manager == null || !manager.IsListening) return;

            if (manager.IsServer)
                UpdateLobbyCountdown();

            if (manager.IsServer)
                rewindHistory.RecordFrame(session, manager.ServerTime.Tick, manager.ServerTime.Time);

            if (manager.IsServer && remoteClientId.HasValue && Time.unscaledTime >= nextEnemySnapshotTime)
            {
                nextEnemySnapshotTime = Time.unscaledTime + EnemySnapshotInterval;
                SendEnemySnapshot(remoteClientId.Value);
            }

            if (!manager.IsClient || manager.IsServer) return;
            float blend = 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime);
            foreach (EnemyVisualProxy proxy in enemyVisuals)
            {
                if (proxy.GameObject == null) continue;
                proxy.GameObject.transform.position = Vector3.Lerp(proxy.GameObject.transform.position, proxy.TargetPosition, blend);
                proxy.GameObject.transform.rotation = Quaternion.Slerp(proxy.GameObject.transform.rotation, proxy.TargetRotation, blend);
            }
        }

        public async Task<bool> CreateOnlineSessionAsync(string playerName)
        {
            if (!ValidatePrefabs() || startInProgress) return false;
            startInProgress = true;
            Status = "Conectando con Unity Gaming Services…";
            try
            {
                await EnsureServicesAsync();
                SetLocalPlayerName(playerName, false);
                transport.UseWebSockets = true;
                SessionOptions options = new SessionOptions
                {
                    MaxPlayers = 2,
                    IsPrivate = true,
                    Name = "Escape del Instituto Maldito"
                }.WithRelayNetwork().WithNetworkOptions(new NetworkOptions
                {
                    RelayProtocol = RelayProtocol.WSS
                });
                hostOnlineSession = await MultiplayerService.Instance.CreateSessionAsync(options);
                onlineSession = hostOnlineSession;
                HookOnlineSessionEvents();
                HostPlayerName = LocalPlayerName;
                Status = $"Sala creada · código {onlineSession.Code}";
                RegisterLobbyHandlers();
                BroadcastLobbySnapshot();
                return true;
            }
            catch (Exception exception)
            {
                Status = FriendlyOnlineError("No se pudo crear la sala", exception);
                Debug.LogException(exception);
                SafeShutdown();
                onlineSession = null;
                hostOnlineSession = null;
                return false;
            }
            finally
            {
                startInProgress = false;
            }
        }

        public async Task<bool> JoinOnlineSessionAsync(string code, string playerName)
        {
            if (!ValidatePrefabs() || startInProgress) return false;
            if (string.IsNullOrWhiteSpace(code))
            {
                Status = "Escribe el código de la sala.";
                return false;
            }

            startInProgress = true;
            Status = "Buscando sala…";
            try
            {
                await EnsureServicesAsync();
                SetLocalPlayerName(playerName, false);
                transport.UseWebSockets = true;
                JoinSessionOptions joinOptions = new JoinSessionOptions().WithNetworkOptions(new NetworkOptions
                {
                    RelayProtocol = RelayProtocol.WSS
                });
                onlineSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(
                    code.Trim().ToUpperInvariant(), joinOptions);
                hostOnlineSession = null;
                HookOnlineSessionEvents();
                Status = "Entrando al lobby…";
                RegisterLobbyHandlers();
                SendLobbyCommand(0, LocalPlayerName);
                return true;
            }
            catch (Exception exception)
            {
                Status = FriendlyOnlineError("No se pudo entrar a la sala", exception);
                Debug.LogException(exception);
                SafeShutdown();
                onlineSession = null;
                return false;
            }
            finally
            {
                startInProgress = false;
            }
        }

        private static async Task EnsureServicesAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                AuthenticationService.Instance.SwitchProfile(BuildAuthenticationProfile());
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        private static string BuildAuthenticationProfile()
        {
            unchecked
            {
                uint hash = 2166136261;
                string source = $"{Application.persistentDataPath}|{GetMultiplayerInstanceName()}";
                for (int index = 0; index < source.Length; index++)
                    hash = (hash ^ source[index]) * 16777619;
                return $"school{hash:x8}";
            }
        }

        private static string GetMultiplayerInstanceName()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], "-name", StringComparison.OrdinalIgnoreCase))
                    return arguments[index + 1];
            }

            return SystemInfo.deviceUniqueIdentifier ?? "schoolescape";
        }

        private static string FriendlyOnlineError(string prefix, Exception exception)
        {
            string detail = exception != null ? exception.Message : "error desconocido";
            return $"{prefix}: {detail}";
        }

        private void HookOnlineSessionEvents()
        {
            if (onlineSession == null) return;
            onlineSession.Deleted += OnOnlineSessionEnded;
            onlineSession.RemovedFromSession += OnOnlineSessionEnded;
            onlineSession.PlayerHasLeft += OnOnlinePlayerLeft;
        }

        private void OnOnlineSessionEnded()
        {
            if (leavingOnlineSession) return;
            Status = "La sala se cerró.";
            onlineSession = null;
            hostOnlineSession = null;
            SafeShutdown();
        }

        private void OnOnlinePlayerLeft(string _)
        {
            if (manager != null && manager.IsServer)
                CancelCountdown("El jugador 2 salió; cuenta regresiva cancelada.");
        }

        public void SetLocalPlayerName(string value, bool notifyNetwork = true)
        {
            if (CountdownActive) return;
            string cleaned = string.IsNullOrWhiteSpace(value) ? "Jugador" : value.Trim();
            if (cleaned.Length > 18) cleaned = cleaned.Substring(0, 18);
            LocalPlayerName = cleaned;
            PlayerPrefs.SetString(PlayerNameKey, cleaned);
            PlayerPrefs.Save();
            if (!notifyNetwork || manager == null || !manager.IsListening) return;
            if (manager.IsServer)
            {
                HostPlayerName = cleaned;
                BroadcastLobbySnapshot();
            }
            else SendLobbyCommand(0, cleaned);
        }

        public static string LoadSavedPlayerName()
        {
            return PlayerPrefs.GetString(PlayerNameKey, "Jugador");
        }

        public void SetLocalReady(bool ready)
        {
            if (GameplayActive || matchStartInProgress) return;
            if (manager == null || !manager.IsListening) return;
            if (manager.IsServer)
            {
                HostReady = ready;
                if (!ready) CancelCountdown("El host ya no está listo.");
                EvaluateLobbyStart();
                BroadcastLobbySnapshot();
            }
            else SendLobbyCommand(1, ready ? "1" : "0");
        }

        public void SubmitRetryVote(bool retry)
        {
            if (manager == null || !manager.IsListening || session == null ||
                session.DisplayMatchResult == PrototypeMatchResult.InProgress) return;
            if (!retry)
            {
                if (manager.IsServer) ReturnEveryoneToLobby();
                else SendLobbyCommand(3, string.Empty);
                return;
            }

            if (manager.IsServer)
            {
                HostRetryVote = true;
                EvaluateRetryStart();
                BroadcastLobbySnapshot();
            }
            else SendLobbyCommand(2, "1");
        }

        public void ReturnAfterVictory()
        {
            if (manager != null && manager.IsServer) ReturnEveryoneToLobby();
        }

        public async void LeaveOnlineSession()
        {
            if (leavingOnlineSession) return;
            leavingOnlineSession = true;
            try
            {
                if (hostOnlineSession != null) await hostOnlineSession.DeleteAsync();
                else if (onlineSession != null) await onlineSession.LeaveAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Multiplayer] No se pudo cerrar la sesión limpiamente: {exception.Message}");
            }
            finally
            {
                onlineSession = null;
                hostOnlineSession = null;
                GameplayActive = false;
                SafeShutdown();
                Status = "Sin conexión";
                leavingOnlineSession = false;
            }
        }

        public bool StartHost(string endpoint)
        {
            if (!ValidatePrefabs()) return false;
            if (manager.IsListening) return manager.IsHost;
            if (startInProgress)
            {
                Status = "La conexión ya se está iniciando…";
                return false;
            }

            ParseEndpoint(endpoint, out _, out activePort);
            if (IsUdpPortInUse(activePort))
            {
                Status = $"Puerto {activePort} ocupado · prueba otro en IP:puerto";
                return false;
            }

            startInProgress = true;
            ResetSecurityAudit();
            ResetAoiAudit();
            transport.UseWebSockets = false;
            transport.SetConnectionData("127.0.0.1", activePort, "0.0.0.0");
            Status = $"Iniciando host en puerto {activePort}…";
            bool started;
            try
            {
                started = manager.StartHost();
            }
            finally
            {
                startInProgress = false;
            }

            if (!started)
            {
                Status = $"No se pudo iniciar el host en el puerto {activePort}";
                SafeShutdown();
                return false;
            }

            Status = $"Host activo · puerto {activePort}";
            GameplayActive = true;
            SpawnHostPlayer();
            return true;
        }

        public bool ReportSecurityViolation(ulong clientId, bool movement, string detail, bool countsAsStrike)
        {
            if (manager == null || !manager.IsServer || clientId == NetworkManager.ServerClientId) return false;

            if (movement) RejectedMovementCount++;
            else RejectedActionCount++;
            LastSecurityEvent = detail ?? "Solicitud inválida";

            double now = manager.ServerTime.Time;
            if (countsAsStrike)
            {
                if (securityWindowStartedAt < 0d || now - securityWindowStartedAt > SecurityStrikeWindowSeconds)
                {
                    securityWindowStartedAt = now;
                    securityStrikes = 0;
                }
                securityStrikes++;
            }

            Debug.LogWarning($"[Security] Cliente {clientId}: {LastSecurityEvent} " +
                             $"(movimiento={RejectedMovementCount}, acciones={RejectedActionCount}, " +
                             $"strikes={securityStrikes}/{SecurityKickThreshold})");

            if (!countsAsStrike || securityStrikes < SecurityKickThreshold ||
                !manager.ConnectedClientsIds.Contains(clientId)) return false;

            SecurityClientKicked = true;
            LastSecurityEvent = $"Expulsado: {LastSecurityEvent}";
            Debug.LogWarning($"[Security] Cliente {clientId} expulsado por {securityStrikes} infracciones en " +
                             $"{SecurityStrikeWindowSeconds:0} s.");
            manager.DisconnectClient(clientId, "Expulsado por solicitudes inválidas repetidas.");
            return true;
        }

        private void ResetSecurityAudit()
        {
            RejectedMovementCount = 0;
            RejectedActionCount = 0;
            securityStrikes = 0;
            securityWindowStartedAt = -1d;
            SecurityClientKicked = false;
            LastSecurityEvent = string.Empty;
        }

        public bool StartClient(string address)
        {
            if (!ValidatePrefabs()) return false;
            if (manager.IsListening) return manager.IsClient && !manager.IsHost;
            if (startInProgress)
            {
                Status = "La conexión ya se está iniciando…";
                return false;
            }

            ParseEndpoint(address, out string target, out activePort);
            transport.UseWebSockets = false;
            transport.SetConnectionData(target, activePort);
            Status = $"Conectando a {target}:{activePort}…";
            startInProgress = true;
            bool started;
            try
            {
                started = manager.StartClient();
            }
            finally
            {
                startInProgress = false;
            }
            if (started) return true;

            Status = $"No se pudo iniciar el cliente hacia {target}:{activePort}";
            SafeShutdown();
            return false;
        }

        private void RegisterLobbyHandlers()
        {
            if (lobbyHandlersRegistered || manager == null || manager.CustomMessagingManager == null || !manager.IsListening)
                return;
            if (manager.IsServer)
                manager.CustomMessagingManager.RegisterNamedMessageHandler(LobbyCommandMessage, OnLobbyCommand);
            else
                manager.CustomMessagingManager.RegisterNamedMessageHandler(LobbySnapshotMessage, OnLobbySnapshot);
            lobbyHandlersRegistered = true;
        }

        private void UnregisterLobbyHandlers()
        {
            if (!lobbyHandlersRegistered || manager == null || manager.CustomMessagingManager == null) return;
            if (manager.IsServer)
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(LobbyCommandMessage);
            else
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(LobbySnapshotMessage);
            lobbyHandlersRegistered = false;
        }

        private void SendLobbyCommand(byte command, string value)
        {
            if (manager == null || !manager.IsClient || manager.IsServer || manager.CustomMessagingManager == null) return;
            using FastBufferWriter writer = new FastBufferWriter(160, Allocator.Temp);
            writer.WriteValueSafe(command);
            writer.WriteValueSafe(new FixedString128Bytes(value ?? string.Empty));
            manager.CustomMessagingManager.SendNamedMessage(
                LobbyCommandMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }

        private void OnLobbyCommand(ulong senderId, FastBufferReader reader)
        {
            if (!manager.IsServer || senderId == NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out byte command);
            reader.ReadValueSafe(out FixedString128Bytes payload);
            string value = payload.ToString();
            switch (command)
            {
                case 0 when !CountdownActive && !GameplayActive:
                    ClientPlayerName = string.IsNullOrWhiteSpace(value) ? "Jugador 2" : value;
                    break;
                case 1 when !GameplayActive:
                    ClientReady = value == "1";
                    if (!ClientReady) CancelCountdown("Jugador 2 ya no está listo.");
                    EvaluateLobbyStart();
                    break;
                case 2 when !GameplayActive:
                    ClientRetryVote = true;
                    EvaluateRetryStart();
                    break;
                case 3:
                    ReturnEveryoneToLobby();
                    break;
            }
            BroadcastLobbySnapshot();
        }

        private void BroadcastLobbySnapshot()
        {
            if (manager == null || !manager.IsServer || manager.CustomMessagingManager == null) return;
            foreach (ulong clientId in manager.ConnectedClientsIds)
            {
                if (clientId == NetworkManager.ServerClientId) continue;
                using FastBufferWriter writer = new FastBufferWriter(320, Allocator.Temp);
                writer.WriteValueSafe(HostReady);
                writer.WriteValueSafe(ClientReady);
                writer.WriteValueSafe(HostRetryVote);
                writer.WriteValueSafe(ClientRetryVote);
                writer.WriteValueSafe(GameplayActive);
                writer.WriteValueSafe(matchStartInProgress);
                writer.WriteValueSafe(CountdownActive ? countdownEndsAt - Time.unscaledTime : -1f);
                writer.WriteValueSafe(new FixedString128Bytes(HostPlayerName ?? "Host"));
                writer.WriteValueSafe(new FixedString128Bytes(ClientPlayerName ?? string.Empty));
                manager.CustomMessagingManager.SendNamedMessage(
                    LobbySnapshotMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnLobbySnapshot(ulong _, FastBufferReader reader)
        {
            bool wasGameplayActive = GameplayActive;
            reader.ReadValueSafe(out bool hostReady);
            reader.ReadValueSafe(out bool clientReady);
            reader.ReadValueSafe(out bool hostRetry);
            reader.ReadValueSafe(out bool clientRetry);
            reader.ReadValueSafe(out bool gameplayActive);
            reader.ReadValueSafe(out bool preparing);
            reader.ReadValueSafe(out float countdownRemaining);
            reader.ReadValueSafe(out FixedString128Bytes hostName);
            reader.ReadValueSafe(out FixedString128Bytes clientName);
            HostReady = hostReady;
            ClientReady = clientReady;
            HostRetryVote = hostRetry;
            ClientRetryVote = clientRetry;
            GameplayActive = gameplayActive;
            matchStartInProgress = preparing;
            countdownEndsAt = countdownRemaining > 0f ? Time.unscaledTime + countdownRemaining : -1f;
            HostPlayerName = hostName.ToString();
            ClientPlayerName = clientName.ToString();
            if (manager != null && !manager.IsServer)
            {
                Status = countdownRemaining > 0f
                    ? "Partida comienza en 3…"
                    : GameplayActive ? "Partida multijugador" : "Lobby · marca LISTO para jugar";
            }
            if (!wasGameplayActive && GameplayActive)
                Time.timeScale = 1f;
            if (wasGameplayActive && !GameplayActive)
                Time.timeScale = 0f;
        }

        private void EvaluateLobbyStart()
        {
            if (!manager.IsServer || GameplayActive || CountdownActive || matchStartInProgress) return;
            bool canStart = HostReady && (!remoteClientId.HasValue || ClientReady);
            if (!canStart) return;
            StartCountdown(true);
        }

        private void EvaluateRetryStart()
        {
            if (!manager.IsServer || CountdownActive || matchStartInProgress || session == null ||
                session.DisplayMatchResult == PrototypeMatchResult.InProgress) return;
            bool canRestart = HostRetryVote && (!remoteClientId.HasValue || ClientRetryVote);
            if (!canRestart) return;
            StartCountdown(true);
        }

        private async void StartCountdown(bool startsMatch)
        {
            countdownStartsMatch = startsMatch;
            countdownEndsAt = Time.unscaledTime + 3f;
            Status = "Partida comienza en 3…";
            if (hostOnlineSession != null)
            {
                try
                {
                    hostOnlineSession.IsLocked = true;
                    await hostOnlineSession.SavePropertiesAsync();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Multiplayer] No se pudo bloquear la sala: {exception.Message}");
                }
            }
            BroadcastLobbySnapshot();
        }

        private void UpdateLobbyCountdown()
        {
            if (!CountdownActive) return;
            bool stillValid = countdownStartsMatch &&
                              ((HostReady && (!remoteClientId.HasValue || ClientReady)) ||
                               (HostRetryVote && (!remoteClientId.HasValue || ClientRetryVote)));
            if (!stillValid)
            {
                CancelCountdown("Cuenta regresiva cancelada.");
                return;
            }
            if (Time.unscaledTime < countdownEndsAt) return;
            countdownEndsAt = -1f;
            StartCoroutine(BeginMatchRoutine());
        }

        private IEnumerator BeginMatchRoutine()
        {
            if (matchStartInProgress) yield break;
            matchStartInProgress = true;
            GameplayActive = false;
            HostReady = ClientReady = false;
            HostRetryVote = ClientRetryVote = false;
            DespawnMatchPlayers();
            BroadcastLobbySnapshot();
            if (session != null)
                yield return session.ResetForNetworkMatch();
            else yield return null;

            SpawnHostPlayer();
            if (remoteClientId.HasValue) SpawnClientAlly(remoteClientId.Value);
            GameplayActive = true;
            matchStartInProgress = false;
            Status = remoteClientId.HasValue ? "Partida multijugador" : "Partida solitaria con aliado IA";
            Time.timeScale = 1f;
            BroadcastLobbySnapshot();
        }

        private async void CancelCountdown(string reason)
        {
            if (!CountdownActive) return;
            countdownEndsAt = -1f;
            Status = reason;
            if (hostOnlineSession != null)
            {
                try
                {
                    hostOnlineSession.IsLocked = false;
                    await hostOnlineSession.SavePropertiesAsync();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Multiplayer] No se pudo desbloquear la sala: {exception.Message}");
                }
            }
            BroadcastLobbySnapshot();
        }

        private async void ReturnEveryoneToLobby()
        {
            if (manager == null || !manager.IsServer) return;
            GameplayActive = false;
            matchStartInProgress = false;
            countdownEndsAt = -1f;
            HostReady = ClientReady = false;
            HostRetryVote = ClientRetryVote = false;
            DespawnMatchPlayers();
            Status = "Lobby · marca LISTO para jugar";
            Time.timeScale = 0f;
            if (hostOnlineSession != null)
            {
                try
                {
                    hostOnlineSession.IsLocked = false;
                    await hostOnlineSession.SavePropertiesAsync();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Multiplayer] No se pudo reabrir la sala: {exception.Message}");
                }
            }
            BroadcastLobbySnapshot();
        }

        private void DespawnMatchPlayers()
        {
            if (manager == null || !manager.IsServer || manager.SpawnManager == null) return;
            foreach (NetworkClient client in manager.ConnectedClients.Values.ToArray())
            {
                NetworkObject playerObject = client.PlayerObject;
                if (playerObject != null && playerObject.IsSpawned)
                    playerObject.Despawn(true);
            }
            remoteAlly = null;
        }

        public void StopSession()
        {
            SafeShutdown();
            Status = "Sin conexión";
        }

        private static void ParseEndpoint(string endpoint, out string address, out ushort port)
        {
            address = "127.0.0.1";
            port = DefaultPort;
            if (string.IsNullOrWhiteSpace(endpoint)) return;

            string value = endpoint.Trim();
            int separator = value.LastIndexOf(':');
            if (separator > 0 && separator < value.Length - 1 &&
                ushort.TryParse(value.Substring(separator + 1), out ushort parsedPort))
            {
                address = value.Substring(0, separator).Trim();
                port = parsedPort;
                return;
            }

            address = value;
        }

        private static bool IsUdpPortInUse(ushort port)
        {
            try
            {
                return IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveUdpListeners()
                    .Any(endpoint => endpoint.Port == port);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Multiplayer] No se pudo comprobar el puerto {port}: {exception.Message}");
                return false;
            }
        }

        private void SafeShutdown()
        {
            startInProgress = false;
            UnregisterEnemySnapshotHandler();
            UnregisterLobbyHandlers();
            ClearEnemyVisuals();
            visibleEnemyIds.Clear();
            nextVisibleEnemyIds.Clear();
            remoteClientId = null;
            GameplayActive = false;
            matchStartInProgress = false;
            countdownEndsAt = -1f;
            HostReady = ClientReady = false;
            HostRetryVote = ClientRetryVote = false;
            if (manager == null || manager.ShutdownInProgress) return;
            if (manager.IsListening || manager.IsClient || manager.IsServer)
                manager.Shutdown();
        }

        private bool ValidatePrefabs()
        {
            if (hostPlayerPrefab != null && clientAllyPrefab != null) return true;
            Status = "Faltan los prefabs de red. Ejecuta Tools/School Escape/Configurar multijugador.";
            Debug.LogError("[Multiplayer] No se encontraron los prefabs de red en Resources/Multiplayer.");
            return false;
        }

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool hasRoom = manager.ConnectedClientsIds.Count < 2;
            response.Approved = hasRoom;
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = hasRoom ? string.Empty : "La partida admite un máximo de 2 jugadores.";
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!manager.IsServer)
            {
                Status = "Conectado como jugador 2";
                RegisterEnemySnapshotHandler();
                RegisterLobbyHandlers();
                SendLobbyCommand(0, LocalPlayerName);
                return;
            }

            RegisterLobbyHandlers();
            if (clientId == NetworkManager.ServerClientId)
            {
                BroadcastLobbySnapshot();
                return;
            }
            remoteClientId = clientId;
            ClientReady = false;
            ClientRetryVote = false;
            Status = "Jugador 2 conectado";
            if (GameplayActive)
            {
                SpawnClientAlly(clientId);
                SendEnemySnapshot(clientId);
            }
            BroadcastLobbySnapshot();
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (manager != null && manager.IsServer && clientId != NetworkManager.ServerClientId)
            {
                bool wasRematchCountdown = CountdownActive && (HostRetryVote || ClientRetryVote);
                visibleEnemyIds.Clear();
                nextVisibleEnemyIds.Clear();
                remoteClientId = null;
                remoteAlly = null;
                ClientReady = false;
                ClientRetryVote = false;
                ClientPlayerName = string.Empty;
                CancelCountdown("Jugador 2 se desconectó; cuenta regresiva cancelada.");
                if (wasRematchCountdown)
                {
                    ReturnEveryoneToLobby();
                    return;
                }
                if (GameplayActive)
                {
                    session?.RestoreSoloAllyAfterDisconnect();
                    Status = "Jugador 2 desconectado · continúa el aliado IA";
                }
                else Status = "Jugador 2 desconectado · lobby abierto";
                BroadcastLobbySnapshot();
                return;
            }

            if (manager != null && !manager.IsServer)
            {
                Status = string.IsNullOrWhiteSpace(manager.DisconnectReason) ? "Desconectado" : manager.DisconnectReason;
                ClearEnemyVisuals();
            }
        }

        private void SendEnemySnapshot(ulong clientId)
        {
            if (manager == null || !manager.IsServer || session == null) return;
            IReadOnlyList<PrototypeEnemy> activeEnemies = session.ActiveEnemies;
            Vector3 observerPosition = remoteAlly != null ? remoteAlly.transform.position : Vector3.zero;
            relevantEnemies.Clear();
            nextVisibleEnemyIds.Clear();
            int totalCount = 0;
            for (int index = 0; index < activeEnemies.Count; index++)
            {
                PrototypeEnemy enemy = activeEnemies[index];
                if (enemy == null) continue;
                totalCount++;
                bool wasVisible = visibleEnemyIds.Contains(enemy.SnapshotId);
                float threshold = wasVisible ? AoiExitDistance : AoiEnterDistance;
                Vector3 delta = enemy.transform.position - observerPosition;
                delta.y = 0f;
                if (delta.sqrMagnitude > threshold * threshold) continue;

                relevantEnemies.Add(enemy);
                nextVisibleEnemyIds.Add(enemy.SnapshotId);
                if (!wasVisible) AoiEnterEvents++;
            }

            foreach (int previousId in visibleEnemyIds)
                if (!nextVisibleEnemyIds.Contains(previousId)) AoiExitEvents++;
            visibleEnemyIds.Clear();
            foreach (int id in nextVisibleEnemyIds) visibleEnemyIds.Add(id);

            using FastBufferWriter writer = new FastBufferWriter(36 + relevantEnemies.Count * 40, Allocator.Temp);
            writer.WriteValueSafe(manager.ServerTime.Time);
            writer.WriteValueSafe(manager.ServerTime.Tick);
            writer.WriteValueSafe(totalCount);
            writer.WriteValueSafe(relevantEnemies.Count);
            for (int index = 0; index < relevantEnemies.Count; index++)
            {
                PrototypeEnemy enemy = relevantEnemies[index];
                writer.WriteValueSafe(enemy.SnapshotId);
                Vector3 position = enemy.transform.position;
                Quaternion rotation = enemy.transform.rotation;
                writer.WriteValueSafe(position);
                writer.WriteValueSafe(rotation);
                writer.WriteByteSafe(enemy.IsSmall ? (byte)1 : (byte)0);
            }

            manager.CustomMessagingManager.SendNamedMessage(
                EnemySnapshotMessage,
                clientId,
                writer,
                NetworkDelivery.UnreliableSequenced);
            EnemySnapshotsSent++;
            EnemySnapshotPayloadBytes += writer.Length;
        }

        private void RegisterEnemySnapshotHandler()
        {
            if (enemyHandlerRegistered || manager == null || manager.CustomMessagingManager == null) return;
            manager.CustomMessagingManager.RegisterNamedMessageHandler(EnemySnapshotMessage, ReceiveEnemySnapshot);
            enemyHandlerRegistered = true;
        }

        private void ReceiveEnemySnapshot(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out double snapshotServerTime);
            reader.ReadValueSafe(out int snapshotServerTick);
            reader.ReadValueSafe(out int totalCount);
            reader.ReadValueSafe(out int count);
            LatestEnemySnapshotServerTime = snapshotServerTime;
            LatestEnemySnapshotServerTick = snapshotServerTick;
            LatestServerEnemyCount = Mathf.Clamp(totalCount, 0, 32);
            count = Mathf.Clamp(count, 0, 32);
            RemoteNormalEnemyCount = 0;
            RemoteSmallEnemyCount = 0;
            HashSet<int> receivedIds = new HashSet<int>();

            for (int index = 0; index < count; index++)
            {
                reader.ReadValueSafe(out int id);
                reader.ReadValueSafe(out Vector3 position);
                reader.ReadValueSafe(out Quaternion rotation);
                reader.ReadByteSafe(out byte smallValue);
                bool isSmall = smallValue != 0;
                receivedIds.Add(id);
                EnemyVisualProxy proxy = GetOrCreateEnemyVisual(id, position, rotation);
                proxy.TargetPosition = position;
                proxy.TargetRotation = rotation;
                ConfigureEnemyVisual(proxy, isSmall);
                if (isSmall) RemoteSmallEnemyCount++;
                else RemoteNormalEnemyCount++;
            }
            for (int index = enemyVisuals.Count - 1; index >= 0; index--)
            {
                if (receivedIds.Contains(enemyVisuals[index].Id)) continue;
                if (enemyVisuals[index].GameObject != null) Destroy(enemyVisuals[index].GameObject);
                enemyVisuals.RemoveAt(index);
            }
        }

        private EnemyVisualProxy GetOrCreateEnemyVisual(int id, Vector3 position, Quaternion rotation)
        {
            foreach (EnemyVisualProxy existing in enemyVisuals)
                if (existing.Id == id) return existing;

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Cultista remoto";
            visual.transform.SetPositionAndRotation(position, rotation);
            Collider collider = visual.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            visual.GetComponent<Renderer>().material = SchoolEscapePrototype.MakeMaterial(new Color(.62f, .08f, .09f));
            EnemyVisualProxy proxy = new EnemyVisualProxy
            {
                Id = id,
                GameObject = visual,
                TargetPosition = position,
                TargetRotation = rotation
            };
            enemyVisuals.Add(proxy);
            return proxy;
        }

        private static void ConfigureEnemyVisual(EnemyVisualProxy proxy, bool isSmall)
        {
            if (proxy.GameObject == null) return;
            bool alreadyConfigured = proxy.IsSmall == isSmall && proxy.GameObject.transform.localScale != Vector3.one;
            if (alreadyConfigured) return;

            proxy.IsSmall = isSmall;
            proxy.GameObject.name = isSmall ? "Cultista chico remoto" : "Cultista normal remoto";
            proxy.GameObject.transform.localScale = isSmall
                ? new Vector3(.72f, .72f, .72f)
                : new Vector3(1.15f, 1.15f, 1.15f);
            proxy.GameObject.GetComponent<Renderer>().material.color = isSmall
                ? new Color(.95f, .25f, .15f)
                : new Color(.62f, .08f, .09f);
        }

        private void ClearEnemyVisuals()
        {
            foreach (EnemyVisualProxy proxy in enemyVisuals)
                if (proxy.GameObject != null) Destroy(proxy.GameObject);
            enemyVisuals.Clear();
            RemoteNormalEnemyCount = 0;
            RemoteSmallEnemyCount = 0;
            LatestEnemySnapshotServerTime = 0d;
            LatestEnemySnapshotServerTick = 0;
            LatestServerEnemyCount = 0;
        }

        private void ResetAoiAudit()
        {
            visibleEnemyIds.Clear();
            nextVisibleEnemyIds.Clear();
            AoiEnterEvents = 0;
            AoiExitEvents = 0;
            EnemySnapshotsSent = 0;
            EnemySnapshotPayloadBytes = 0;
        }

        public bool TryGetClosestRemoteEnemyDirection(Vector3 origin, float maximumDistance, out Vector3 direction)
        {
            direction = Vector3.zero;
            float closestDistance = maximumDistance;
            foreach (EnemyVisualProxy proxy in enemyVisuals)
            {
                if (proxy.GameObject == null) continue;
                Vector3 delta = proxy.GameObject.transform.position - origin;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance >= closestDistance) continue;
                closestDistance = distance;
                direction = delta.normalized;
            }
            return direction.sqrMagnitude > .01f;
        }

        private void UnregisterEnemySnapshotHandler()
        {
            if (!enemyHandlerRegistered || manager == null || manager.CustomMessagingManager == null) return;
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(EnemySnapshotMessage);
            enemyHandlerRegistered = false;
        }

        private void SpawnHostPlayer()
        {
            if (!manager.IsServer || hostPlayerPrefab == null) return;
            Vector3 position = session != null ? session.GetNetworkPlayerSpawnPosition(false) : Vector3.up;
            GameObject instance = Instantiate(hostPlayerPrefab, position, Quaternion.identity);
            instance.GetComponent<NetworkObject>().SpawnAsPlayerObject(NetworkManager.ServerClientId, true);
        }

        private void SpawnClientAlly(ulong clientId)
        {
            if (remoteAlly != null && remoteAlly.IsSpawned) return;
            Vector3 position = session != null ? session.GetNetworkPlayerSpawnPosition(true) : Vector3.right + Vector3.up;
            GameObject instance = Instantiate(clientAllyPrefab, position, Quaternion.identity);
            remoteAlly = instance.GetComponent<NetworkObject>();
            remoteAlly.SpawnAsPlayerObject(clientId, true);
        }

        private void OnTransportFailure()
        {
            Status = $"Error de transporte en puerto {activePort}";
        }

        private void OnApplicationQuit() => SafeShutdown();

        private void OnDestroy()
        {
            if (manager != null)
            {
                manager.OnClientConnectedCallback -= OnClientConnected;
                manager.OnClientDisconnectCallback -= OnClientDisconnected;
                manager.OnTransportFailure -= OnTransportFailure;
                SafeShutdown();
            }
            if (Instance == this) Instance = null;
        }
    }
}

using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
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
        public bool IsConnecting => startInProgress || leavingOnlineSession;
        public bool IsFriendsRoom { get; private set; }
        public bool ShowsRoomCode { get; private set; }
        public bool CanInvite => IsHost && IsFriendsRoom && IsOnlineSession && !HasRemoteClient &&
            !GameplayActive && !MatchStartInProgress && !IsConnecting;
        public bool IsHost => manager != null && manager.IsHost;
        public bool GameplayActive { get; private set; }
        public bool MatchStartInProgress { get; private set; }
        public bool CanSetReady => IsListening && !manager.ShutdownInProgress &&
            (IsHost || manager.IsConnectedClient) && !leavingOnlineSession &&
            !GameplayActive && !MatchStartInProgress && !pendingReadyVote.HasValue;
        public bool IsReadyVotePending => pendingReadyVote.HasValue;
        public string ReadyVoteError { get; private set; } = string.Empty;
        public string MatchStartError { get; private set; } = string.Empty;
        public string SessionCode => onlineSession != null ? onlineSession.Code : string.Empty;
        public string SessionId => onlineSession != null ? onlineSession.Id : string.Empty;
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
        private const int TransportPacketQueueSize = 512;
        private const RelayProtocol OnlineRelayProtocol = RelayProtocol.Default;
        private const float MatchLoadTimeout = 15f;
        private const float LobbyConnectTimeout = 15f;
        private const float ReadyVoteTimeout = 5f;
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
        private CustomMessagingManager lobbyMessagingManager;
        private bool clientLoadedMatch;
        private bool? pendingReadyVote;
        private float readyVoteExpiresAt;
        private ulong? expectedHostObjectId;
        private ulong? expectedAllyObjectId;
        private Task roomLockUpdate = Task.CompletedTask;
        private bool leavingOnlineSession;
        private static Task servicesInitialization;
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
            transport.MaxPacketQueueSize = TransportPacketQueueSize;

            hostPlayerPrefab = Resources.Load<GameObject>(HostPlayerResource);
            clientAllyPrefab = Resources.Load<GameObject>(ClientAllyResource);
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ConnectionApproval = true,
                ProtocolVersion = 3,
                TickRate = 30,
                PlayerPrefab = null
            };

            if (hostPlayerPrefab != null) manager.AddNetworkPrefab(hostPlayerPrefab);
            if (clientAllyPrefab != null) manager.AddNetworkPrefab(clientAllyPrefab);

            manager.ConnectionApprovalCallback = ApproveConnection;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            manager.OnClientStopped += OnClientStopped;
            manager.OnTransportFailure += OnTransportFailure;
        }

        private void Update()
        {
            if (manager == null || !manager.IsListening) return;

            if (manager.IsServer)
                rewindHistory.RecordFrame(session, manager.ServerTime.Tick, manager.ServerTime.Time);

            if (manager.IsServer && GameplayActive && remoteClientId.HasValue && Time.unscaledTime >= nextEnemySnapshotTime)
            {
                nextEnemySnapshotTime = Time.unscaledTime + EnemySnapshotInterval;
                SendEnemySnapshot(remoteClientId.Value);
            }

            if (!manager.IsClient || manager.IsServer) return;
            if (pendingReadyVote.HasValue && Time.unscaledTime >= readyVoteExpiresAt)
            {
                pendingReadyVote = null;
                ReadyVoteError = "El anfitrión no confirmó tu voto. Pulsa LISTO otra vez.";
            }
            ConfirmMatchLoaded();
            float blend = 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime);
            foreach (EnemyVisualProxy proxy in enemyVisuals)
            {
                if (proxy.GameObject == null) continue;
                proxy.GameObject.transform.position = Vector3.Lerp(proxy.GameObject.transform.position, proxy.TargetPosition, blend);
                proxy.GameObject.transform.rotation = Quaternion.Slerp(proxy.GameObject.transform.rotation, proxy.TargetRotation, blend);
            }
        }

        public async Task<bool> CreateOnlineSessionAsync(string playerName, bool friendsRoom = false)
        {
            if (IsConnecting || IsOnlineSession || IsListening || !ValidatePrefabs()) return false;
            startInProgress = true;
            Status = "Conectando con Unity Gaming Services…";
            try
            {
                await EnsureServicesAsync();
                SetLocalPlayerName(playerName, false);
                transport.UseWebSockets = OnlineRelayProtocol == RelayProtocol.WSS;
                SessionOptions options = new SessionOptions
                {
                    MaxPlayers = 2,
                    IsPrivate = true,
                    Name = "Escape del Instituto Maldito"
                }.WithRelayNetwork().WithNetworkOptions(new NetworkOptions
                {
                    RelayProtocol = OnlineRelayProtocol
                });
                hostOnlineSession = await MultiplayerService.Instance.CreateSessionAsync(options);
                onlineSession = hostOnlineSession;
                IsFriendsRoom = friendsRoom;
                ShowsRoomCode = !friendsRoom;
                HookOnlineSessionEvents();
                HostPlayerName = LocalPlayerName;
                Status = $"Sala creada · código {onlineSession.Code}";
                RegisterLobbyHandlers();
                BroadcastLobbySnapshot();
                SchoolEscapeSocialServices.Ensure().EnterSession(SessionId, SessionCode, LocalPlayerName);
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

        public async Task<bool> JoinOnlineSessionAsync(string code, string playerName, string expectedSessionId = null)
        {
            if (IsConnecting || IsOnlineSession || IsListening || !ValidatePrefabs()) return false;
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
                transport.UseWebSockets = OnlineRelayProtocol == RelayProtocol.WSS;
                JoinSessionOptions joinOptions = new JoinSessionOptions().WithNetworkOptions(new NetworkOptions
                {
                    RelayProtocol = OnlineRelayProtocol
                });
                onlineSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(
                    code.Trim().ToUpperInvariant(), joinOptions);
                hostOnlineSession = null;
                if (expectedSessionId != null && onlineSession.Id != expectedSessionId)
                {
                    await LeaveOnlineSessionAsync();
                    Status = "La invitación ya no corresponde a esta sala.";
                    return false;
                }
                IsFriendsRoom = expectedSessionId != null;
                ShowsRoomCode = !IsFriendsRoom;
                HookOnlineSessionEvents();
                Status = "Conectando con el anfitrión…";
                await WaitForLobbyConnectionAsync();
                Status = "Conectado como jugador 2";
                RegisterLobbyHandlers();
                SendLobbyCommand(0, LocalPlayerName);
                SchoolEscapeSocialServices.Ensure().EnterSession(SessionId, SessionCode, LocalPlayerName);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (onlineSession != null) await LeaveOnlineSessionAsync();
                else SafeShutdown();
                Status = FriendlyOnlineError("No se pudo entrar a la sala", exception);
                return false;
            }
            finally
            {
                startInProgress = false;
            }
        }

        private async Task WaitForLobbyConnectionAsync()
        {
            float deadline = Time.realtimeSinceStartup + LobbyConnectTimeout;
            while (!manager.IsConnectedClient && manager.IsListening && !manager.ShutdownInProgress &&
                Time.realtimeSinceStartup < deadline)
                await Task.Yield();

            if (!manager.IsConnectedClient)
            {
                string reason = manager.DisconnectReason;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(reason)
                    ? "No se pudo conectar con el anfitrión por Relay." : reason);
            }
        }

        public async Task<bool> FindQuickMatchAsync(string playerName, CancellationToken cancellationToken)
        {
            if (IsConnecting || IsOnlineSession || IsListening || !ValidatePrefabs()) return false;
            startInProgress = true;
            Status = "Buscando compañero…";
            try
            {
                await EnsureServicesAsync();
                cancellationToken.ThrowIfCancellationRequested();
                SetLocalPlayerName(playerName, false);
                transport.UseWebSockets = OnlineRelayProtocol == RelayProtocol.WSS;
                const string quickMode = "escape-quick-coop-v2";
                var search = new QuickJoinOptions
                {
                    CreateSession = true,
                    Timeout = TimeSpan.FromSeconds(UnityEngine.Random.Range(1f, 4f)),
                    Filters = new List<FilterOption>
                    {
                        new FilterOption(FilterField.StringIndex1, quickMode, FilterOperation.Equal),
                        new FilterOption(FilterField.AvailableSlots, "1", FilterOperation.GreaterOrEqual)
                    }
                };
                var options = new SessionOptions
                {
                    Name = "Escape cooperativo",
                    MaxPlayers = 2,
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        { "mode", new SessionProperty(quickMode, VisibilityPropertyOptions.Public, PropertyIndex.String1) }
                    }
                }.WithRelayNetwork().WithNetworkOptions(new NetworkOptions { RelayProtocol = OnlineRelayProtocol });
                onlineSession = await MultiplayerService.Instance.MatchmakeSessionAsync(search, options, cancellationToken);
                hostOnlineSession = onlineSession.IsHost ? onlineSession.AsHost() : null;
                if (cancellationToken.IsCancellationRequested)
                {
                    await LeaveOnlineSessionAsync();
                    return false;
                }
                IsFriendsRoom = ShowsRoomCode = false;
                HookOnlineSessionEvents();
                RegisterLobbyHandlers();
                if (IsHost)
                {
                    HostPlayerName = LocalPlayerName;
                    BroadcastLobbySnapshot();
                }
                else SendLobbyCommand(0, LocalPlayerName);
                Status = "Partida rápida · esperando compañero";
                SchoolEscapeSocialServices.Ensure().EnterSession(SessionId, SessionCode, LocalPlayerName);
                return true;
            }
            catch (OperationCanceledException)
            {
                SafeShutdown();
                Status = "Búsqueda cancelada.";
                return false;
            }
            catch (Exception exception)
            {
                SafeShutdown();
                Status = FriendlyOnlineError("No se encontró compañero", exception);
                return false;
            }
            finally { startInProgress = false; }
        }

        private static Task EnsureServicesAsync()
        {
            if (servicesInitialization == null || servicesInitialization.IsCompleted)
                servicesInitialization = InitializeServicesAsync();
            return servicesInitialization;
        }

        private static async Task InitializeServicesAsync()
        {
            string profile = BuildAuthenticationProfile();
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(profile));
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                if (AuthenticationService.Instance.Profile != profile)
                    AuthenticationService.Instance.SwitchProfile(profile);
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        public static Task EnsureServicesReadyAsync() => EnsureServicesAsync();

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
#if UNITY_EDITOR
            // Virtual Players can share persistentDataPath and PlayerPrefs with the main editor.
            // Use the clone's stable identity before considering its display name.
            foreach (string argument in arguments)
            {
                const string prefix = "-vpId=";
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && argument.Length > prefix.Length)
                    return "virtual-player:" + argument.Substring(prefix.Length);
            }
            if (Array.Exists(arguments, argument => string.Equals(argument, "--virtual-project-clone", StringComparison.OrdinalIgnoreCase)))
                return "virtual-project:" + Application.dataPath;
#endif
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
        }

        private void UnhookOnlineSessionEvents()
        {
            if (onlineSession == null) return;
            onlineSession.Deleted -= OnOnlineSessionEnded;
            onlineSession.RemovedFromSession -= OnOnlineSessionEnded;
        }

        private void OnOnlineSessionEnded()
        {
            if (leavingOnlineSession) return;
            Status = "La sala se cerró.";
            SchoolEscapeSocialServices.Instance?.LeaveSession();
            UnhookOnlineSessionEvents();
            onlineSession = null;
            hostOnlineSession = null;
            SafeShutdown();
        }

        public void SetLocalPlayerName(string value, bool notifyNetwork = true)
        {
            if (MatchStartInProgress) return;
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
            if (!CanSetReady) return;
            ReadyVoteError = string.Empty;
            if (manager.IsServer)
            {
                HostReady = ready;
                EvaluateLobbyStart();
                BroadcastLobbySnapshot();
            }
            else
            {
                pendingReadyVote = ready;
                readyVoteExpiresAt = Time.unscaledTime + ReadyVoteTimeout;
                SendLobbyCommand(1, ready ? "1" : "0");
            }
        }

        public void SubmitRetryVote(bool retry)
        {
            if (manager == null || !manager.IsListening || MatchStartInProgress || session == null ||
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

        public void LeaveOnlineSession() => _ = LeaveOnlineSessionAsync();

        public Task LeaveOnlineSessionAsync() => LeaveOnlineSessionAsync("Sin conexión");

        private async Task LeaveOnlineSessionAsync(string finalStatus)
        {
            if (leavingOnlineSession) return;
            leavingOnlineSession = true;
            UnhookOnlineSessionEvents();
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
                SchoolEscapeSocialServices.Instance?.LeaveSession();
                onlineSession = null;
                hostOnlineSession = null;
                GameplayActive = false;
                SafeShutdown();
                Status = finalStatus;
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
            if (manager == null || !manager.IsListening) return;
            CustomMessagingManager messaging = manager.CustomMessagingManager;
            if (messaging == null || messaging == lobbyMessagingManager) return;
            UnregisterLobbyHandlers();
            if (manager.IsServer)
                messaging.RegisterNamedMessageHandler(LobbyCommandMessage, OnLobbyCommand);
            else
                messaging.RegisterNamedMessageHandler(LobbySnapshotMessage, OnLobbySnapshot);
            lobbyMessagingManager = messaging;
        }

        private void UnregisterLobbyHandlers()
        {
            if (lobbyMessagingManager == null) return;
            lobbyMessagingManager.UnregisterNamedMessageHandler(LobbyCommandMessage);
            lobbyMessagingManager.UnregisterNamedMessageHandler(LobbySnapshotMessage);
            lobbyMessagingManager = null;
        }

        private void SendLobbyCommand(byte command, string value)
        {
            if (manager == null || !manager.IsConnectedClient || manager.IsServer || manager.CustomMessagingManager == null) return;
            using FastBufferWriter writer = new FastBufferWriter(160, Allocator.Temp);
            writer.WriteValueSafe(command);
            writer.WriteValueSafe(new FixedString128Bytes(value ?? string.Empty));
            manager.CustomMessagingManager.SendNamedMessage(
                LobbyCommandMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }

        private void OnLobbyCommand(ulong senderId, FastBufferReader reader)
        {
            if (!manager.IsServer || senderId == NetworkManager.ServerClientId ||
                !manager.ConnectedClients.ContainsKey(senderId)) return;
            reader.ReadValueSafe(out byte command);
            reader.ReadValueSafe(out FixedString128Bytes payload);
            string value = payload.ToString();
            switch (command)
            {
                case 0 when !MatchStartInProgress && !GameplayActive:
                    ClientPlayerName = string.IsNullOrWhiteSpace(value) ? "Jugador 2" : value;
                    break;
                case 1 when !MatchStartInProgress && !GameplayActive:
                    remoteClientId ??= senderId;
                    ClientReady = value == "1";
                    Debug.Log($"[Multiplayer] Anfitrión recibió el voto del invitado: listo={ClientReady}.");
                    EvaluateLobbyStart();
                    break;
                case 2 when !MatchStartInProgress && GameplayActive && session != null &&
                    session.DisplayMatchResult != PrototypeMatchResult.InProgress:
                    ClientRetryVote = true;
                    EvaluateRetryStart();
                    break;
                case 3 when !MatchStartInProgress && GameplayActive && session != null &&
                    session.DisplayMatchResult != PrototypeMatchResult.InProgress:
                    ReturnEveryoneToLobby();
                    break;
                case 4 when MatchStartInProgress && remoteAlly != null && remoteAlly.IsSpawned:
                    if (ulong.TryParse(value, out ulong loadedAllyId) && loadedAllyId == remoteAlly.NetworkObjectId)
                        clientLoadedMatch = true;
                    return;
            }
            BroadcastLobbySnapshot();
        }

        private void BroadcastLobbySnapshot()
        {
            if (manager == null || !manager.IsServer || manager.CustomMessagingManager == null) return;
            foreach (ulong clientId in manager.ConnectedClientsIds)
            {
                if (clientId == NetworkManager.ServerClientId) continue;
                using FastBufferWriter writer = new FastBufferWriter(448, Allocator.Temp);
                writer.WriteValueSafe(HostReady);
                writer.WriteValueSafe(ClientReady);
                writer.WriteValueSafe(HostRetryVote);
                writer.WriteValueSafe(ClientRetryVote);
                writer.WriteValueSafe(GameplayActive);
                writer.WriteValueSafe(MatchStartInProgress);
                bool hasMatchPlayers = MatchStartInProgress && expectedHostObjectId.HasValue && expectedAllyObjectId.HasValue;
                writer.WriteValueSafe(hasMatchPlayers);
                if (hasMatchPlayers)
                {
                    writer.WriteValueSafe(expectedHostObjectId.Value);
                    writer.WriteValueSafe(expectedAllyObjectId.Value);
                }
                writer.WriteValueSafe(new FixedString128Bytes(HostPlayerName ?? "Host"));
                writer.WriteValueSafe(new FixedString128Bytes(ClientPlayerName ?? string.Empty));
                writer.WriteValueSafe(new FixedString128Bytes(MatchStartError));
                manager.CustomMessagingManager.SendNamedMessage(
                    LobbySnapshotMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnLobbySnapshot(ulong senderId, FastBufferReader reader)
        {
            if (senderId != NetworkManager.ServerClientId) return;
            bool wasGameplayActive = GameplayActive;
            reader.ReadValueSafe(out bool hostReady);
            reader.ReadValueSafe(out bool clientReady);
            reader.ReadValueSafe(out bool hostRetry);
            reader.ReadValueSafe(out bool clientRetry);
            reader.ReadValueSafe(out bool gameplayActive);
            reader.ReadValueSafe(out bool preparing);
            reader.ReadValueSafe(out bool hasMatchPlayers);
            expectedHostObjectId = expectedAllyObjectId = null;
            if (hasMatchPlayers)
            {
                reader.ReadValueSafe(out ulong hostObjectId);
                reader.ReadValueSafe(out ulong allyObjectId);
                expectedHostObjectId = hostObjectId;
                expectedAllyObjectId = allyObjectId;
            }
            reader.ReadValueSafe(out FixedString128Bytes hostName);
            reader.ReadValueSafe(out FixedString128Bytes clientName);
            reader.ReadValueSafe(out FixedString128Bytes matchError);
            HostReady = hostReady;
            ClientReady = clientReady;
            HostRetryVote = hostRetry;
            ClientRetryVote = clientRetry;
            GameplayActive = gameplayActive;
            MatchStartInProgress = preparing;
            if (preparing || gameplayActive || clientReady == pendingReadyVote)
                pendingReadyVote = null;
            if (!preparing) clientLoadedMatch = false;
            HostPlayerName = hostName.ToString();
            ClientPlayerName = clientName.ToString();
            MatchStartError = matchError.ToString();
            if (manager != null && !manager.IsServer)
            {
                Status = preparing
                    ? "Sincronizando jugadores…"
                    : GameplayActive ? "Partida multijugador" : "Lobby · marca LISTO para jugar";
            }
            if (!wasGameplayActive && GameplayActive)
                Time.timeScale = 1f;
            if (wasGameplayActive && !GameplayActive)
                Time.timeScale = 0f;
        }

        private void EvaluateLobbyStart()
        {
            if (!manager.IsServer || GameplayActive || MatchStartInProgress) return;
            bool canStart = HostReady && (IsOnlineSession
                ? remoteClientId.HasValue && ClientReady
                : !remoteClientId.HasValue || ClientReady);
            if (!canStart) return;
            StartCoroutine(BeginMatchRoutine());
        }

        private void EvaluateRetryStart()
        {
            if (!manager.IsServer || !GameplayActive || MatchStartInProgress || session == null ||
                session.DisplayMatchResult == PrototypeMatchResult.InProgress) return;
            bool canRestart = HostRetryVote && (remoteClientId.HasValue ? ClientRetryVote : !IsOnlineSession);
            if (!canRestart) return;
            StartCoroutine(BeginMatchRoutine());
        }

        private void ConfirmMatchLoaded()
        {
            if (!MatchStartInProgress || clientLoadedMatch || !manager.IsConnectedClient ||
                !expectedHostObjectId.HasValue || !expectedAllyObjectId.HasValue) return;
            var spawned = manager.SpawnManager.SpawnedObjects;
            if (!spawned.TryGetValue(expectedHostObjectId.Value, out NetworkObject host) ||
                !spawned.TryGetValue(expectedAllyObjectId.Value, out NetworkObject ally) ||
                !host.IsSpawned || host.OwnerClientId != NetworkManager.ServerClientId ||
                !ally.IsSpawned || !ally.IsOwner ||
                !host.TryGetComponent(out NetworkHostPlayerBridge _) ||
                !ally.TryGetComponent(out NetworkClientAllyBridge _)) return;

            SendLobbyCommand(4, ally.NetworkObjectId.ToString());
            clientLoadedMatch = true;
        }

        private IEnumerator BeginMatchRoutine()
        {
            ulong? startingClientId = remoteClientId;
            MatchStartInProgress = true;
            clientLoadedMatch = false;
            MatchStartError = string.Empty;
            GameplayActive = false;
            Time.timeScale = 0f;
            Status = "Sincronizando jugadores…";
            HostReady = ClientReady = false;
            HostRetryVote = ClientRetryVote = false;
            roomLockUpdate = SetRoomLockedAsync(true);
            DespawnMatchPlayers();
            BroadcastLobbySnapshot();
            if (session != null)
                yield return session.ResetForNetworkMatch(forceLocal: true);
            else yield return null;

            if (!MatchStartInProgress || !manager.IsServer) yield break;
            if (startingClientId != remoteClientId)
            {
                ReturnEveryoneToLobby("El jugador 2 se desconectó.");
                yield break;
            }

            NetworkObject spawnedHost = SpawnHostPlayer();
            if (startingClientId.HasValue)
            {
                SpawnClientAlly(startingClientId.Value);
                if (spawnedHost == null || !spawnedHost.IsSpawned || remoteAlly == null || !remoteAlly.IsSpawned)
                {
                    ReturnEveryoneToLobby("No se pudieron crear ambos personajes de red.");
                    yield break;
                }

                expectedHostObjectId = spawnedHost.NetworkObjectId;
                expectedAllyObjectId = remoteAlly.NetworkObjectId;
                BroadcastLobbySnapshot();
                float deadline = Time.unscaledTime + MatchLoadTimeout;
                while (MatchStartInProgress && startingClientId == remoteClientId &&
                    !clientLoadedMatch && Time.unscaledTime < deadline)
                    yield return null;

                if (!MatchStartInProgress || !manager.IsServer) yield break;
                if (startingClientId != remoteClientId || !clientLoadedMatch)
                {
                    if (startingClientId == remoteClientId)
                        Debug.LogWarning($"[Multiplayer] Sin confirmación de carga del jugador 2. Objetos de red: host={expectedHostObjectId}, invitado={expectedAllyObjectId}.");
                    ReturnEveryoneToLobby(startingClientId != remoteClientId
                        ? "El jugador 2 se desconectó."
                        : "El jugador 2 no pudo cargar la partida a tiempo.");
                    yield break;
                }
            }

            GameplayActive = true;
            MatchStartInProgress = false;
            Status = remoteClientId.HasValue ? "Partida multijugador" : "Partida solitaria con aliado IA";
            Time.timeScale = 1f;
            BroadcastLobbySnapshot();
        }

        private async Task SetRoomLockedAsync(bool locked)
        {
            IHostSession room = hostOnlineSession;
            if (room == null) return;
            try
            {
                room.IsLocked = locked;
                await room.SavePropertiesAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Multiplayer] No se pudo actualizar el bloqueo de la sala: {exception.Message}");
            }
        }

        private async void ReturnEveryoneToLobby(string matchError = "")
        {
            if (manager == null || !manager.IsServer) return;
            GameplayActive = false;
            MatchStartInProgress = true;
            HostReady = ClientReady = false;
            HostRetryVote = ClientRetryVote = false;
            DespawnMatchPlayers();
            Time.timeScale = 0f;
            IHostSession room = hostOnlineSession;
            await roomLockUpdate;
            if (!manager.IsServer || hostOnlineSession != room) return;
            await SetRoomLockedAsync(false);
            if (!manager.IsServer || hostOnlineSession != room) return;
            MatchStartInProgress = false;
            MatchStartError = matchError;
            Status = "Lobby · marca LISTO para jugar";
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
            expectedHostObjectId = expectedAllyObjectId = null;
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
            SchoolEscapeSocialServices.Instance?.LeaveSession();
            startInProgress = false;
            UnregisterEnemySnapshotHandler();
            UnregisterLobbyHandlers();
            ClearEnemyVisuals();
            visibleEnemyIds.Clear();
            nextVisibleEnemyIds.Clear();
            remoteClientId = null;
            GameplayActive = false;
            MatchStartInProgress = false;
            clientLoadedMatch = false;
            expectedHostObjectId = expectedAllyObjectId = null;
            pendingReadyVote = null;
            ReadyVoteError = string.Empty;
            MatchStartError = string.Empty;
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
            bool acceptingPlayers = !MatchStartInProgress && (!IsOnlineSession || !GameplayActive);
            bool hasRoom = manager.ConnectedClientsIds.Count < 2 && acceptingPlayers;
            response.Approved = hasRoom;
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = hasRoom ? string.Empty : "La sala está llena o la partida ya empezó.";
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!manager.IsServer)
            {
                Status = "Conectado como jugador 2";
                Debug.Log($"[Multiplayer] Invitado conectado a Netcode. Transporte online: {OnlineRelayProtocol}.");
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
            Debug.Log($"[Multiplayer] El anfitrión confirmó la conexión del invitado. Transporte online: {OnlineRelayProtocol}.");
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
                Debug.LogWarning($"[Multiplayer] El invitado se desconectó (partida activa={GameplayActive}, preparando={MatchStartInProgress}).");
                visibleEnemyIds.Clear();
                nextVisibleEnemyIds.Clear();
                remoteClientId = null;
                remoteAlly = null;
                ClientReady = false;
                ClientRetryVote = false;
                ClientPlayerName = string.Empty;
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
                if (!leavingOnlineSession)
                {
                    Status = string.IsNullOrWhiteSpace(manager.DisconnectReason)
                        ? "Se perdió la conexión con el anfitrión." : manager.DisconnectReason;
                    Debug.LogWarning($"[Multiplayer] {Status} Partida activa={GameplayActive}, preparando={MatchStartInProgress}.");
                }
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

        private NetworkObject SpawnHostPlayer()
        {
            if (!manager.IsServer || hostPlayerPrefab == null) return null;
            Vector3 position = session != null ? session.GetNetworkPlayerSpawnPosition(false) : Vector3.up;
            GameObject instance = Instantiate(hostPlayerPrefab, position, Quaternion.identity);
            NetworkObject playerObject = instance.GetComponent<NetworkObject>();
            playerObject.SpawnAsPlayerObject(NetworkManager.ServerClientId, true);
            return playerObject;
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
            Status = IsOnlineSession || IsConnecting
                ? $"Se perdió el transporte de Relay ({OnlineRelayProtocol}). Crea una sala nueva e inténtalo otra vez."
                : $"Error de transporte en puerto {activePort}";
            Debug.LogWarning($"[Multiplayer] {Status}");
        }

        private void OnClientStopped(bool wasHost)
        {
            SchoolEscapeSocialServices.Instance?.LeaveSession();
            if (IsOnlineSession && !startInProgress && !leavingOnlineSession)
            {
                string reason = Status;
                _ = LeaveOnlineSessionAsync(reason);
            }
        }

#if UNITY_EDITOR
        private void OnApplicationPause(bool paused)
        {
            if (!IsListening) return;
            string role = IsHost ? "Anfitrión" : "Invitado";
            string state = paused ? "pausado" : "reanudado";
            Debug.Log($"[Multiplayer] {role}: proceso {state}. Run In Background={Application.runInBackground}.");
        }
#endif

        private void OnApplicationQuit() => SafeShutdown();

        private void OnDestroy()
        {
            if (manager != null)
            {
                manager.OnClientConnectedCallback -= OnClientConnected;
                manager.OnClientDisconnectCallback -= OnClientDisconnected;
                manager.OnClientStopped -= OnClientStopped;
                manager.OnTransportFailure -= OnTransportFailure;
                SafeShutdown();
            }
            if (Instance == this) Instance = null;
        }
    }
}

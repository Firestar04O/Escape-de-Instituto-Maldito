using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
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

        private const string HostPlayerResource = "Multiplayer/NetworkHostPlayer";
        private const string ClientAllyResource = "Multiplayer/NetworkClientAlly";
        private const string EnemySnapshotMessage = "SchoolEscape.EnemySnapshot";
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
            ClearEnemyVisuals();
            visibleEnemyIds.Clear();
            nextVisibleEnemyIds.Clear();
            remoteClientId = null;
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
                return;
            }

            if (clientId == NetworkManager.ServerClientId) return;
            remoteClientId = clientId;
            Status = "Jugador 2 conectado";
            SpawnClientAlly(clientId);
            SendEnemySnapshot(clientId);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (manager != null && manager.IsServer && clientId != NetworkManager.ServerClientId)
            {
                visibleEnemyIds.Clear();
                nextVisibleEnemyIds.Clear();
                remoteClientId = null;
                remoteAlly = null;
                session?.RestoreSoloAllyAfterDisconnect();
                Status = "Jugador 2 desconectado · continúa el aliado IA";
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

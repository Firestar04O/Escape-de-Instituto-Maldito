using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.AI;
using Unity.AI.Navigation;
using SchoolEscape.Rooms;
using SchoolEscape.UI;
using SchoolEscape.Multiplayer;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public enum PrototypeMatchResult
{
    InProgress,
    PerfectEscape,
    PartialEscape,
    TotalDefeat
}

public class SchoolEscapePrototype : MonoBehaviour
{
    public PrototypePlayer Player { get; private set; }
    public PrototypeAlly Ally { get; private set; }

    public bool HasKey { get; private set; }
    public bool Victory { get; private set; }
    public PrototypeMatchResult MatchResult { get; private set; } = PrototypeMatchResult.InProgress;
    public int Batteries => inventory.Batteries;
    public int Bandages => inventory.Bandages;
    public float RunTime { get; private set; }
    public int CaptiveCount => CountCaptives();
    public float DangerMultiplier => 1f + RunTime / 80f + CaptiveCount * 1.5f +
                                     (exitOpening ? 1.25f : 0f) + recentDirectorActivity;
    public bool CanUseBandage => Bandages > 0 && Player != null && !Player.IsCaptive && !Player.IsDown && Player.Dragger == null && Player.Health < 3;
    public bool IsExitOpening => exitOpening;
    public float ExitOpeningRemaining => exitOpening ? Mathf.Max(0f, exitOpeningEndsAt - Time.time) : 0f;
    public string ExitStatusText => HasKey ? "ABIERTA" : exitOpening ? $"ABRIENDO {Mathf.CeilToInt(ExitOpeningRemaining)}s" : "bloqueada";
    public int CurrentRoomNumber => currentRoomIndex >= 0 ? currentRoomIndex + 1 : 1;
    public int CurrentRoomIndex => currentRoomIndex;
    public int TotalRooms => roomSequence != null && roomSequence.Count > 0 ? roomSequence.Count : 1;
    public bool IsFinalRoom => roomSequence == null || currentRoomIndex + 1 >= roomSequence.Count;
    public string CurrentRoomId => authoredRoom != null ? authoredRoom.RoomId : "prototipo";
    public string RoomProgressText => $"Sala {CurrentRoomNumber}/{TotalRooms}";
    private NetworkClientAllyBridge ClientBridge => NetworkClientAllyBridge.LocalOwner;
    public bool DisplayVictory => IsNetworkReplica && ClientBridge != null ? ClientBridge.DisplayVictory : Victory;
    public bool Defeat
    {
        get
        {
            if (IsNetworkReplica) return ClientBridge != null && ClientBridge.DisplayDefeat;
            return MatchResult == PrototypeMatchResult.TotalDefeat;
        }
    }
    public PrototypeMatchResult DisplayMatchResult => IsNetworkReplica && ClientBridge != null
        ? ClientBridge.DisplayMatchResult
        : MatchResult;
    public string MemorialText => BuildMemorialText();
    public int DisplayHealth => IsNetworkReplica && ClientBridge != null ? ClientBridge.DisplayHealth : Player != null ? Player.Health : 0;
    public int DisplayBatteries => IsNetworkReplica && ClientBridge != null ? ClientBridge.DisplayBatteries : Batteries;
    public int DisplayBandages => IsNetworkReplica && ClientBridge != null ? ClientBridge.DisplayBandages : Bandages;
    public float DisplayDangerMultiplier => IsNetworkReplica && ClientBridge != null ? ClientBridge.DisplayDanger : DangerMultiplier;
    public float DisplayRunTime => IsNetworkReplica && ClientBridge != null ? ClientBridge.DisplayRunTime : RunTime;
    public string DisplayRoomProgressText => IsNetworkReplica && ClientBridge != null ? ClientBridge.DisplayRoom : RoomProgressText;
    public string DisplayExitStatusText => IsNetworkReplica && ClientBridge != null ? ClientBridge.DisplayExit : ExitStatusText;
    public string CurrentAnnouncementText => Time.time < messageUntil ? message : string.Empty;
    public string DisplayAnnouncementText
    {
        get
        {
            if (IsNetworkReplica && ClientBridge != null) return ClientBridge.DisplayAnnouncement;
            string personal = CurrentPersonalAnnouncementFor(Player);
            return string.IsNullOrEmpty(personal) ? CurrentAnnouncementText : personal;
        }
    }
    public string DisplayInteractionStatus => IsNetworkReplica && ClientBridge != null
        ? ClientBridge.InteractionStatus
        : Player != null ? Player.InteractionStatus : "Sin objetivo cercano";
    public int ActiveEnemyCount => IsNetworkReplica && SchoolEscapeNetworkBootstrap.Instance != null
        ? SchoolEscapeNetworkBootstrap.Instance.RemoteEnemyCount
        : enemies.Count;
    public int ActiveNormalEnemyCount => IsNetworkReplica && SchoolEscapeNetworkBootstrap.Instance != null
        ? SchoolEscapeNetworkBootstrap.Instance.RemoteNormalEnemyCount
        : CountEnemies(false);
    public int ActiveSmallEnemyCount => IsNetworkReplica && SchoolEscapeNetworkBootstrap.Instance != null
        ? SchoolEscapeNetworkBootstrap.Instance.RemoteSmallEnemyCount
        : CountEnemies(true);
    public IReadOnlyList<PrototypeEnemy> ActiveEnemies => enemies;
    public int MaximumActiveEnemyCount => CurrentNormalCap + CurrentSmallCap;
    public int EnemiesSpawnedThisRoom => normalEnemiesSpawnedThisRoom + smallEnemiesSpawnedThisRoom;
    public int MaximumEnemiesPerRoom => NormalRoomBudget + SmallRoomBudget;
    public string EnemyCounterText =>
        $"Normales {ActiveNormalEnemyCount}/{CurrentNormalCap} ({normalEnemiesSpawnedThisRoom}/{NormalRoomBudget})  ·  " +
        $"Chicos {ActiveSmallEnemyCount}/{CurrentSmallCap} ({smallEnemiesSpawnedThisRoom}/{SmallRoomBudget})";
    public string NetworkAoiStatusText => SchoolEscapeNetworkBootstrap.Instance != null
        ? SchoolEscapeNetworkBootstrap.Instance.AoiStatusText
        : "Red: sin sesión";
    public MobileHudController MobileHud { get; private set; }
    public Transform CameraFollowTarget
    {
        get
        {
            PrototypeActor localActor = networkCameraTarget != null
                ? networkCameraTarget.GetComponentInParent<PrototypeActor>()
                : Player;
            bool localUnavailable = localActor == null || localActor.IsCaptive || localActor.IsEscaped ||
                                    (IsNetworkReplica && localActor == Ally && ClientBridge != null && ClientBridge.DisplayCaptive);
            if (!localUnavailable) return networkCameraTarget != null ? networkCameraTarget : localActor.transform;

            PrototypeActor spectatorTarget = localActor == Player ? (PrototypeActor)Ally : Player;
            if (spectatorTarget != null && !spectatorTarget.IsCaptive && !spectatorTarget.IsEscaped)
                return spectatorTarget.transform;
            return localActor != null ? localActor.transform : null;
        }
    }
    public bool IsNetworkReplica { get; private set; }
    public bool IsRoomTransitioning => roomTransitionInProgress;
    public bool HasActiveRescueRoom => activeRescueRoomInstance != null;
    public bool IsRescueEntranceOpen => rescueEntranceOpen;

    private readonly List<PrototypeEnemy> enemies = new List<PrototypeEnemy>();
    private readonly List<PrototypeInteractable> interactables = new List<PrototypeInteractable>();
    private readonly HashSet<PrototypeExitPanel> activatedExitPanels = new HashSet<PrototypeExitPanel>();
    private readonly PrototypeInventory inventory = new PrototypeInventory();
    private Material floorMaterial;
    private Material wallMaterial;
    private Material furnitureMaterial;
    private SchoolRoomDefinition authoredRoom;
    private RoomSequenceCatalog roomSequence;
    private GameObject activeRoomInstance;
    private GameObject activeRescueRoomInstance;
    private RescueRoomDefinition activeRescueRoom;
    private RescueEntranceAnchor activeRescueEntrance;
    private PrototypeRescueEntrance rescueEntranceInteraction;
    private RescueDoorAnchor connectedRescueDoor;
    private RescuePassageLink rescuePassageLink;
    private bool rescueEntranceOpening;
    private bool rescueEntranceOpen;
    private readonly Dictionary<PrototypeActor, int> rescueQueue = new Dictionary<PrototypeActor, int>();
    private PrototypeActor activeRescueCaptive;
    private int currentRoomIndex = -1;
    private bool roomTransitionInProgress;
    private int requiredExitPanelCount = 1;
    private readonly List<Vector3> activeEnemySpawnPoints = new List<Vector3>();
    private int normalEnemiesSpawnedThisRoom;
    private int smallEnemiesSpawnedThisRoom;
    private float spawnTimer = 10f;
    private float nextDirectorRoleUpdate;
    private float recentDirectorActivity;
    private float noSurvivorDefeatAt = -1f;
    private const float ExitOpeningDuration = 30f;
    private const float RescueEntranceOpeningDuration = 5f;
    private const float NoSurvivorGraceDuration = 4f;
    private const int NormalActiveCap = 10;
    private const int SmallActiveCap = 5;
    private const int FrenzyNormalActiveCap = 15;
    private const int FrenzySmallActiveCap = 8;
    private const int NormalRoomBudget = 15;
    private const int SmallRoomBudget = 8;
    private const float OrientationThreshold = .72f;
    private const float OrientationDominance = .18f;
    private const float OrientationStableDuration = .6f;
    private static readonly string[] OrderedRoomIds =
    {
        "salon_1", "salon_2", "salon_3",
        "cafeteria_1", "cafeteria_2", "cafeteria_3"
    };
    private bool exitOpening;
    private float exitOpeningEndsAt;
    private ScreenOrientation orientationCandidate = ScreenOrientation.LandscapeLeft;
    private float orientationCandidateSince;
    private float nextOrientationChangeTime;
    private string message = "Encuentra la llave y escapa";
    private float messageUntil = 4f;
    private string playerMessage = string.Empty;
    private float playerMessageUntil;
    private string allyMessage = string.Empty;
    private float allyMessageUntil;
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;
    private GUIStyle centerStyle;
    private Transform networkCameraTarget;

    private int CurrentNormalCap => exitOpening ? FrenzyNormalActiveCap : NormalActiveCap;
    private int CurrentSmallCap => exitOpening ? FrenzySmallActiveCap : SmallActiveCap;

    public static readonly Vector3[] EnemySpawnPoints =
    {
        new Vector3(-12.5f, 1f, 8f), new Vector3(12.5f, 1f, 8f),
        new Vector3(-12.5f, 1f, -8f), new Vector3(12.5f, 1f, -8f)
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoStart()
    {
        Debug.Log("[SchoolEscape] AutoStart");
        if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        SchoolEscapePrototype session = FindFirstObjectByType<SchoolEscapePrototype>();
        if (session == null)
            session = new GameObject("School Escape Prototype").AddComponent<SchoolEscapePrototype>();
        SchoolEscapeNetworkBootstrap.EnsureFor(session);
        BasicGameFlowUI.EnsureFor(session);
    }

    private void Awake()
    {
        MobileHud = FindFirstObjectByType<MobileHudController>();
        ConfigureMobileOrientation();
        ConfigurePerformanceBudget();
        if (Accelerometer.current != null)
            InputSystem.EnableDevice(Accelerometer.current);
        if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        BuildPrototype();
        SchoolEscapeNetworkBootstrap.EnsureFor(this);
        BasicGameFlowUI.EnsureFor(this);
    }

    private static void ConfigureMobileOrientation()
    {
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = false;
        Screen.autorotateToLandscapeRight = false;
        Screen.orientation = ScreenOrientation.LandscapeLeft;
    }

    private static void ConfigurePerformanceBudget()
    {
#if UNITY_ANDROID || UNITY_IOS
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 30;
#endif
    }

    private void OnDestroy()
    {
        if (EnhancedTouchSupport.enabled) EnhancedTouchSupport.Disable();
    }

    private void Update()
    {
        UpdateLandscapeOrientation();

        SchoolEscapeNetworkBootstrap network = SchoolEscapeNetworkBootstrap.Instance;
        if (network != null && network.IsListening && !network.GameplayActive) return;

        if (IsNetworkReplica) return;

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            Restart();

        if (Victory || Defeat || Player == null) return;

        RunTime += Time.deltaTime;
        recentDirectorActivity = Mathf.MoveTowards(recentDirectorActivity, 0f, Time.deltaTime * .08f);
        UpdateDefeatCountdown();
        if (Defeat) return;
        spawnTimer -= Time.deltaTime;
        if (Time.time >= nextDirectorRoleUpdate)
        {
            nextDirectorRoleUpdate = Time.time + .8f;
            AssignDirectorRoles();
        }

        if (spawnTimer <= 0f && CanSpawnAnyEnemy())
        {
            SpawnEnemy(ChooseSmallEnemyForNextSpawn());
            spawnTimer = exitOpening
                ? Mathf.Max(1.15f, 4.25f / DangerMultiplier)
                : Mathf.Max(3.5f, 10f / DangerMultiplier);
        }
    }

    private IEnumerable<PrototypeActor> GetParticipatingActors()
    {
        if (Player != null) yield return Player;
        if (Ally != null) yield return Ally;
    }

    private bool HasRemoteHuman =>
        SchoolEscapeNetworkBootstrap.Instance != null &&
        SchoolEscapeNetworkBootstrap.Instance.HasRemoteClient;

    private bool AnyParticipantCanAct()
    {
        if (Player != null && Player.CanAct) return true;
        return Ally != null && Ally.CanAct;
    }

    private int CountCaptives()
    {
        int count = 0;
        foreach (PrototypeActor actor in GetParticipatingActors())
            if (actor != null && actor.IsCaptive) count++;
        return count;
    }

    private void UpdateDefeatCountdown()
    {
        if (MatchResult != PrototypeMatchResult.InProgress) return;
        if (AnyParticipantCanAct())
        {
            noSurvivorDefeatAt = -1f;
            return;
        }

        if (noSurvivorDefeatAt < 0f)
        {
            noSurvivorDefeatAt = Time.time + NoSurvivorGraceDuration;
            ShowMessage($"Nadie puede moverse. Derrota en {NoSurvivorGraceDuration:0}s…", NoSurvivorGraceDuration);
            return;
        }
        if (Time.time < noSurvivorDefeatAt) return;

        foreach (PrototypeActor actor in GetParticipatingActors())
        {
            if (actor == null || actor.IsCaptive || actor.IsEscaped) continue;
            actor.Captured(ClosestEnemySpawn(actor.transform.position));
        }
        if (IsFinalRoom)
            EvaluateFinalResult();
        if (MatchResult == PrototypeMatchResult.InProgress)
        {
            MatchResult = PrototypeMatchResult.TotalDefeat;
            Victory = false;
            ShowMessage("TODO EL EQUIPO FUE RAPTADO", 999f);
        }
    }

    public void AddDirectorActivity(float amount)
    {
        recentDirectorActivity = Mathf.Clamp(recentDirectorActivity + Mathf.Max(0f, amount), 0f, 2.5f);
    }

    private string BuildMemorialText()
    {
        string hostState = Player == null ? "ausente" :
            Player.IsEscaped ? "escapó" : Player.IsCaptive ? "raptado" : "no resolvió su salida";
        string partnerState = Ally == null ? "ausente" :
            Ally.IsEscaped ? "escapó" : Ally.IsCaptive ? "raptado" : "no resolvió su salida";
        return $"Host: {hostState}\nJugador 2: {partnerState}";
    }

    private void UpdateLandscapeOrientation()
    {
#if UNITY_ANDROID || UNITY_IOS
        Accelerometer accelerometer = Accelerometer.current;
        if (accelerometer == null || Time.unscaledTime < nextOrientationChangeTime) return;

        Vector3 acceleration = accelerometer.acceleration.ReadValue();
        float horizontal = Mathf.Abs(acceleration.x);
        float vertical = Mathf.Abs(acceleration.y);
        if (horizontal < OrientationThreshold || horizontal < vertical + OrientationDominance)
        {
            orientationCandidateSince = Time.unscaledTime;
            return;
        }

        ScreenOrientation desired = acceleration.x < 0f
            ? ScreenOrientation.LandscapeRight
            : ScreenOrientation.LandscapeLeft;
        if (desired != orientationCandidate)
        {
            orientationCandidate = desired;
            orientationCandidateSince = Time.unscaledTime;
            return;
        }

        if (Screen.orientation == desired || Time.unscaledTime - orientationCandidateSince < OrientationStableDuration)
            return;

        Screen.orientation = desired;
        nextOrientationChangeTime = Time.unscaledTime + 1.2f;
#endif
    }

    private void BuildPrototype()
    {
        Debug.Log("[SchoolEscape] Construyendo prototipo");
        roomSequence = Resources.Load<RoomSequenceCatalog>("RoomSequenceCatalog");
        if (roomSequence != null && roomSequence.Count > 0)
        {
            DisableAuthoredRoomsInScene();
            LoadSequencedRoom(0);
            return;
        }

        authoredRoom = FindFirstObjectByType<SchoolRoomDefinition>();
        if (authoredRoom != null)
        {
            BuildAuthoredRoomPrototype();
            return;
        }

        foreach (Camera oldCamera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            Destroy(oldCamera.gameObject);

        foreach (Light oldLight in FindObjectsByType<Light>(FindObjectsSortMode.None))
            Destroy(oldLight.gameObject);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.16f, 0.17f, 0.21f);
        floorMaterial = MakeMaterial(new Color(0.19f, 0.22f, 0.25f));
        wallMaterial = MakeMaterial(new Color(0.40f, 0.42f, 0.44f));
        furnitureMaterial = MakeMaterial(new Color(0.28f, 0.17f, 0.10f));

        CreateBox("Floor", new Vector3(0f, -0.3f, 0f), new Vector3(30f, 0.5f, 20f), floorMaterial, true);
        CreateBox("Wall North L", new Vector3(-8f, 1.5f, 9.7f), new Vector3(14f, 3f, 0.6f), wallMaterial, true);
        CreateBox("Wall North R", new Vector3(8f, 1.5f, 9.7f), new Vector3(14f, 3f, 0.6f), wallMaterial, true);
        CreateBox("Wall South", new Vector3(0f, 1.5f, -9.7f), new Vector3(30f, 3f, 0.6f), wallMaterial, true);
        CreateBox("Wall West", new Vector3(-14.7f, 1.5f, 0f), new Vector3(0.6f, 3f, 20f), wallMaterial, true);
        CreateBox("Wall East", new Vector3(14.7f, 1.5f, 0f), new Vector3(0.6f, 3f, 20f), wallMaterial, true);

        Vector3[] coverPositions =
        {
            new Vector3(-7f, .65f, -4f), new Vector3(7f, .65f, -4f),
            new Vector3(-8f, .65f, 2f), new Vector3(7f, .65f, 3f),
            new Vector3(-2f, .65f, 4f), new Vector3(2f, .65f, -1f)
        };
        foreach (Vector3 p in coverPositions)
            CreateBox("Pupitre", p, new Vector3(3f, 1.3f, 1.4f), furnitureMaterial, true);

        CreateBox("Columna A", new Vector3(-4f, 1.3f, 0f), new Vector3(1.2f, 2.6f, 1.2f), wallMaterial, true);
        CreateBox("Columna B", new Vector3(4f, 1.3f, 0f), new Vector3(1.2f, 2.6f, 1.2f), wallMaterial, true);

        Vector3[] lockerPositions =
        {
            new Vector3(-12.7f, 1f, -6f), new Vector3(-12.7f, 1f, 6f),
            new Vector3(12.7f, 1f, -6f), new Vector3(12.7f, 1f, 6f)
        };
        foreach (Vector3 p in lockerPositions)
        {
            GameObject locker = CreateBox("Taquilla registrable", p, new Vector3(1.1f, 2f, 1.2f), MakeMaterial(new Color(0.15f, 0.31f, 0.36f)), true);
            Register(locker.AddComponent<PrototypeLocker>());
        }

        Vector3[] keySpawns =
        {
            new Vector3(-10f, .25f, -1f), new Vector3(10f, .25f, 0f),
            new Vector3(-4f, .25f, 7f), new Vector3(5f, .25f, -7f),
            new Vector3(0f, .25f, 5.5f), new Vector3(9f, .25f, 6.5f)
        };
        GameObject key = CreateBox("Llave", keySpawns[Random.Range(0, keySpawns.Length)], new Vector3(.65f, .25f, .3f), MakeMaterial(new Color(1f, .73f, .08f)), false);
        Register(key.AddComponent<PrototypeKey>());

        GameObject door = CreateBox("Salida bloqueada", new Vector3(0f, 1.4f, 9.15f), new Vector3(3.2f, 2.8f, .45f), MakeMaterial(new Color(.45f, .05f, .05f)), true);
        Register(door.AddComponent<PrototypeDoor>());

        foreach (Vector3 spawn in EnemySpawnPoints)
        {
            GameObject portal = CreateBox("Portal de la secta", new Vector3(spawn.x, .04f, spawn.z), new Vector3(2.6f, .08f, 2.6f), MakeMaterial(new Color(.38f, .02f, .42f)), false);
            portal.AddComponent<PrototypeRitual>();
        }

        Player = CreateCapsule("Jugador", new Vector3(0f, 1f, -7f), new Color(.18f, .52f, 1f)).AddComponent<PrototypePlayer>();
        Player.Initialize(this);
        Ally = CreateCapsule("Compañero", new Vector3(1.6f, 1f, -6.4f), new Color(.25f, .9f, .38f)).AddComponent<PrototypeAlly>();
        Ally.Initialize(this);

        SpawnEnemy(true);
        SpawnEnemy(false);

        GameObject cameraObject = new GameObject("Top Down Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.fieldOfView = 48f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.025f, .03f, .045f);
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<PrototypeCamera>().Initialize(this);

        GameObject lightObject = new GameObject("Moon Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(.55f, .65f, 1f);
        light.intensity = 1.25f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
    }

    private void BuildAuthoredRoomPrototype()
    {
        Debug.Log($"[SchoolEscape] Usando sala guardada: {authoredRoom.RoomId}");

        activeEnemySpawnPoints.Clear();
        for (int index = 0; index < authoredRoom.EnemySpawnCount; index++)
        {
            Transform spawn = authoredRoom.GetEnemySpawn(index);
            if (spawn != null) activeEnemySpawnPoints.Add(spawn.position + Vector3.up);
        }

        RegisterAuthoredInteractables();

        Transform playerSpawn = authoredRoom.GetPlayerSpawn(0);
        Transform allySpawn = authoredRoom.GetPlayerSpawn(1);
        Vector3 playerPosition = playerSpawn != null ? playerSpawn.position + Vector3.up : authoredRoom.transform.position + Vector3.up;
        Vector3 allyPosition = allySpawn != null ? allySpawn.position + Vector3.up : playerPosition + Vector3.right * 1.7f;

        if (Player == null)
        {
            Player = CreateCapsule("Jugador", playerPosition, new Color(.18f, .52f, 1f)).AddComponent<PrototypePlayer>();
            Player.Initialize(this);
        }
        else if (!Player.IsCaptive && !Player.IsEscaped)
        {
            TeleportActor(Player, playerPosition);
            Player.enabled = true;
        }

        if (Ally == null)
        {
            Ally = CreateCapsule("Compañero de prueba", allyPosition, new Color(.25f, .9f, .38f)).AddComponent<PrototypeAlly>();
            Ally.Initialize(this);
        }
        else if (!Ally.IsCaptive && !Ally.IsEscaped)
        {
            TeleportActor(Ally, allyPosition);
            Ally.enabled = Ally.GetComponent<NetworkClientAllyBridge>() == null;
        }

        if (ShouldPrepareRescueBranch())
            PrepareRescueBranch();

        if (activeEnemySpawnPoints.Count > 0)
        {
            SpawnEnemy(true);
            SpawnEnemy(false);
        }

        Camera camera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Top Down Camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            cameraObject.AddComponent<AudioListener>();
        }

        camera.fieldOfView = 48f;
        PrototypeCamera cameraController = camera.GetComponent<PrototypeCamera>();
        if (cameraController == null) cameraController = camera.gameObject.AddComponent<PrototypeCamera>();
        cameraController.Initialize(this);

        string objectiveMessage = requiredExitPanelCount == 1
            ? "Activa la palanca y llega a la salida"
            : $"Activa las {requiredExitPanelCount} palancas y llega a la salida";
        ShowMessage($"{RoomProgressText}: {objectiveMessage}", 5f);
    }

    private void DisableAuthoredRoomsInScene()
    {
        foreach (SchoolRoomDefinition room in FindObjectsByType<SchoolRoomDefinition>(FindObjectsSortMode.None))
        {
            if (room == null) continue;
            room.gameObject.SetActive(false);
        }
    }

    private void LoadSequencedRoom(int roomIndex)
    {
        GameObject roomPrefab = roomSequence.GetRoom(roomIndex);
        if (roomPrefab == null)
        {
            Debug.LogError($"[SchoolEscape] La sala {roomIndex + 1} no existe en el catálogo.");
            Win();
            return;
        }

        currentRoomIndex = roomIndex;
        activeRoomInstance = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity);
        activeRoomInstance.name = roomPrefab.name;
        authoredRoom = activeRoomInstance.GetComponent<SchoolRoomDefinition>();
        if (authoredRoom == null)
            authoredRoom = activeRoomInstance.GetComponentInChildren<SchoolRoomDefinition>(true);

        if (authoredRoom == null)
        {
            Debug.LogError($"[SchoolEscape] {roomPrefab.name} no tiene SchoolRoomDefinition.");
            Win();
            return;
        }

        RoomArchetype archetype = roomIndex >= 3 ? RoomArchetype.Cafeteria : RoomArchetype.Classroom;
        string roomId = roomIndex < OrderedRoomIds.Length ? OrderedRoomIds[roomIndex] : $"room_{roomIndex + 1}";
        authoredRoom.ConfigureIdentity(roomId, archetype);
        BuildAuthoredRoomPrototype();
    }

    public void ApplyNetworkRoomState(int roomIndex, bool rescueActive, bool rescueOpen)
    {
        if (!IsNetworkReplica || roomSequence == null || roomIndex < 0 || roomIndex >= roomSequence.Count) return;
        if (roomIndex != currentRoomIndex || activeRoomInstance == null)
            LoadReplicaRoom(roomIndex);

        if (rescueActive && activeRescueRoomInstance == null)
            PrepareReplicaRescueBranch();
        if (rescueActive && rescueOpen)
        {
            activeRescueEntrance?.Gate?.Unlock();
            connectedRescueDoor?.Gate?.Unlock();
            rescuePassageLink?.SetTraversable(true);
            rescueEntranceOpen = true;
        }
    }

    private void LoadReplicaRoom(int roomIndex)
    {
        if (activeRescueRoomInstance != null) Destroy(activeRescueRoomInstance);
        if (activeRoomInstance != null) Destroy(activeRoomInstance);
        activeRescueRoomInstance = null;
        activeRescueRoom = null;
        connectedRescueDoor = null;
        rescuePassageLink = null;
        rescueEntranceOpen = false;
        rescueEntranceOpening = false;

        GameObject roomPrefab = roomSequence.GetRoom(roomIndex);
        if (roomPrefab == null) return;
        currentRoomIndex = roomIndex;
        activeRoomInstance = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity);
        activeRoomInstance.name = roomPrefab.name + " (réplica)";
        authoredRoom = activeRoomInstance.GetComponent<SchoolRoomDefinition>();
        if (authoredRoom == null) authoredRoom = activeRoomInstance.GetComponentInChildren<SchoolRoomDefinition>(true);
        RegisterRescueEntrance();
        Debug.Log($"[Multiplayer] Réplica cargó {roomPrefab.name} para la sala {roomIndex + 1}.");
    }

    private void PrepareReplicaRescueBranch()
    {
        if (authoredRoom == null || activeRescueEntrance == null || roomSequence == null || roomSequence.RescueRoom == null)
            return;
        activeRescueRoomInstance = Instantiate(roomSequence.RescueRoom, Vector3.zero, Quaternion.identity);
        activeRescueRoomInstance.name = "Mapa_Rescate (réplica)";
        activeRescueRoom = activeRescueRoomInstance.GetComponent<RescueRoomDefinition>();
        if (activeRescueRoom == null) return;

        RescueDirection requiredDirection = RescueDirectionUtility.Opposite(activeRescueEntrance.Direction);
        connectedRescueDoor = activeRescueRoom.GetDoor(requiredDirection);
        if (connectedRescueDoor == null) return;
        AlignRescueRoom(activeRescueRoomInstance.transform, connectedRescueDoor.transform, activeRescueEntrance.transform);
        RefreshNavMeshSurfaces(activeRescueRoomInstance);
        ConfigureConnectedWallVisibility();

        GameObject linkObject = new GameObject("Enlace NavMesh de rescate (réplica)");
        linkObject.transform.SetParent(activeRescueRoomInstance.transform, true);
        rescuePassageLink = linkObject.AddComponent<RescuePassageLink>();
        rescuePassageLink.Configure(activeRescueEntrance.transform, activeRoomInstance.transform, activeRescueRoomInstance.transform);
    }

    private static void TeleportActor(PrototypeActor actor, Vector3 position)
    {
        CharacterController controller = actor.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        actor.transform.position = position;
        if (controller != null) controller.enabled = wasEnabled;
    }

    private void RegisterAuthoredInteractables()
    {
        RegisterChildrenAs<PrototypeLocker>(authoredRoom.RandomInteractablesRoot);
        RegisterChildrenAs<PrototypeBatteryFurniture>(authoredRoom.GuaranteedBatteryRoot);
        RegisterChildrenAs<PrototypeBandageFurniture>(authoredRoom.GuaranteedBandageRoot);

        activatedExitPanels.Clear();
        requiredExitPanelCount = 0;
        foreach (Transform exitPanel in authoredRoom.ExitPanels)
        {
            if (exitPanel == null) continue;
            PrototypeExitPanel panel = exitPanel.GetComponent<PrototypeExitPanel>();
            if (panel == null) panel = exitPanel.gameObject.AddComponent<PrototypeExitPanel>();
            Register(panel);
            requiredExitPanelCount++;
        }

        if (requiredExitPanelCount == 0 && authoredRoom.ExitPanel != null)
        {
            PrototypeExitPanel panel = authoredRoom.ExitPanel.GetComponent<PrototypeExitPanel>();
            if (panel == null) panel = authoredRoom.ExitPanel.gameObject.AddComponent<PrototypeExitPanel>();
            Register(panel);
            requiredExitPanelCount = 1;
        }

        foreach (Transform exit in authoredRoom.Exits)
        {
            if (exit == null) continue;
            PrototypeDoor door = exit.GetComponent<PrototypeDoor>();
            if (door == null) door = exit.gameObject.AddComponent<PrototypeDoor>();
            Register(door);
        }

        RegisterRescueEntrance();
    }

    private void RegisterRescueEntrance()
    {
        activeRescueEntrance = null;
        rescueEntranceInteraction = null;
        foreach (Transform child in authoredRoom.GetComponentsInChildren<Transform>(true))
        {
            if (!RescueDirectionUtility.TryParseEntrance(child.name, out RescueDirection direction)) continue;
            if (activeRescueEntrance != null)
            {
                Debug.LogWarning($"[SchoolEscape/Rescate] {authoredRoom.name} tiene más de una entrada direccional; se usará {activeRescueEntrance.name}.");
                continue;
            }

            RescueEntranceAnchor anchor = child.GetComponent<RescueEntranceAnchor>();
            if (anchor == null) anchor = child.gameObject.AddComponent<RescueEntranceAnchor>();
            RoomExitGate gate = child.GetComponent<RoomExitGate>();
            if (gate == null) gate = child.gameObject.AddComponent<RoomExitGate>();
            anchor.Configure(direction, gate);
            gate.ResetGate();

            PrototypeRescueEntrance interaction = child.GetComponent<PrototypeRescueEntrance>();
            if (interaction == null) interaction = child.gameObject.AddComponent<PrototypeRescueEntrance>();
            interaction.Configure(anchor);
            Register(interaction);
            activeRescueEntrance = anchor;
            rescueEntranceInteraction = interaction;
        }
    }

    private void RegisterChildrenAs<T>(Transform root) where T : PrototypeInteractable
    {
        if (root == null) return;
        foreach (Transform child in root)
        {
            T interactable = child.GetComponent<T>();
            if (interactable == null) interactable = child.gameObject.AddComponent<T>();
            Register(interactable);
        }
    }

    private void SpawnEnemy(bool small)
    {
        int activeOfType = small ? ActiveSmallEnemyCount : ActiveNormalEnemyCount;
        int activeCap = small ? CurrentSmallCap : CurrentNormalCap;
        int spawnedOfType = small ? smallEnemiesSpawnedThisRoom : normalEnemiesSpawnedThisRoom;
        int roomBudget = small ? SmallRoomBudget : NormalRoomBudget;
        if (activeOfType >= activeCap || spawnedOfType >= roomBudget)
            return;

        Vector3 requestedSpawn = GetRandomEnemySpawn();
        if (!TryGetNavigablePoint(requestedSpawn, out Vector3 spawnPoint, 24f))
        {
            Debug.LogWarning($"[SchoolEscape/NavMesh] No se encontró superficie navegable cerca del spawn {requestedSpawn} en {CurrentRoomId}.");
            return;
        }

        GameObject enemy = CreateCapsule(small ? "Cultista chico" : "Cultista normal", spawnPoint, small ? new Color(.95f, .25f, .15f) : new Color(.62f, .08f, .09f));
        enemy.transform.localScale = small ? new Vector3(.72f, .72f, .72f) : new Vector3(1.15f, 1.15f, 1.15f);
        NavMeshAgent agent = enemy.AddComponent<NavMeshAgent>();
        agent.radius = small ? .32f : .43f;
        agent.height = small ? 1.45f : 2.15f;
        agent.baseOffset = small ? .72f : 1.05f;
        agent.acceleration = 18f;
        agent.angularSpeed = 720f;
        agent.stoppingDistance = .82f;
        agent.autoBraking = true;
        PrototypeEnemy controller = enemy.AddComponent<PrototypeEnemy>();
        controller.IsSmall = small;
        controller.Initialize(this);
        enemies.Add(controller);
        if (small) smallEnemiesSpawnedThisRoom++;
        else normalEnemiesSpawnedThisRoom++;
        AssignDirectorRoles();
    }

    public bool TryGetNavigablePoint(Vector3 desiredPosition, out Vector3 navigablePosition, float maximumDistance = 12f)
    {
        float[] searchRadii = { 1.5f, 4f, 8f, 12f, 24f };
        foreach (float radius in searchRadii)
        {
            if (radius > maximumDistance) break;
            if (!NavMesh.SamplePosition(desiredPosition, out NavMeshHit hit, radius, NavMesh.AllAreas)) continue;
            navigablePosition = hit.position;
            return true;
        }

        if (maximumDistance > 0f && NavMesh.SamplePosition(desiredPosition, out NavMeshHit finalHit, maximumDistance, NavMesh.AllAreas))
        {
            navigablePosition = finalHit.position;
            return true;
        }

        navigablePosition = desiredPosition;
        return false;
    }

    private int CountEnemies(bool small)
    {
        int count = 0;
        foreach (PrototypeEnemy enemy in enemies)
            if (enemy != null && enemy.IsSmall == small) count++;
        return count;
    }

    private bool CanSpawnAnyEnemy()
    {
        bool normalAvailable = ActiveNormalEnemyCount < CurrentNormalCap && normalEnemiesSpawnedThisRoom < NormalRoomBudget;
        bool smallAvailable = ActiveSmallEnemyCount < CurrentSmallCap && smallEnemiesSpawnedThisRoom < SmallRoomBudget;
        return normalAvailable || smallAvailable;
    }

    private bool ChooseSmallEnemyForNextSpawn()
    {
        bool normalAvailable = ActiveNormalEnemyCount < CurrentNormalCap && normalEnemiesSpawnedThisRoom < NormalRoomBudget;
        bool smallAvailable = ActiveSmallEnemyCount < CurrentSmallCap && smallEnemiesSpawnedThisRoom < SmallRoomBudget;
        if (!normalAvailable) return true;
        if (!smallAvailable) return false;

        float normalFill = (float)ActiveNormalEnemyCount / CurrentNormalCap;
        float smallFill = (float)ActiveSmallEnemyCount / CurrentSmallCap;
        if (Mathf.Abs(normalFill - smallFill) > .12f)
            return smallFill < normalFill;
        return Random.value < .34f;
    }

    private void AssignDirectorRoles()
    {
        List<PrototypeEnemy> available = new List<PrototypeEnemy>();
        foreach (PrototypeEnemy enemy in enemies)
            if (enemy != null) available.Add(enemy);
        if (available.Count == 0) return;

        List<PrototypeActor> fragileTargets = new List<PrototypeActor>();
        if (Player != null && !Player.IsCaptive && Player.Health <= 1) fragileTargets.Add(Player);
        if (Ally != null && !Ally.IsCaptive && Ally.Health <= 1) fragileTargets.Add(Ally);

        foreach (PrototypeEnemy enemy in available)
            enemy.ConfigureRole(EnemyRole.Chaser, null);

        int assigned = 0;
        int kidnapperCount = Mathf.Min(2, fragileTargets.Count);
        for (int targetIndex = 0; targetIndex < kidnapperCount; targetIndex++)
        {
            int candidate = FindRoleCandidate(available, assigned, true);
            Swap(available, assigned, candidate);
            available[assigned].ConfigureRole(EnemyRole.Kidnapper, fragileTargets[targetIndex]);
            assigned++;
        }

        int remaining = available.Count - assigned;
        int cutterCount = remaining >= 5 ? (exitOpening && remaining >= 10 ? 3 : Mathf.Min(2, remaining / 5)) : 0;
        for (int index = 0; index < cutterCount; index++)
            available[assigned++].ConfigureRole(EnemyRole.Cutter, null);

        remaining = available.Count - assigned;
        int ambusherCount = remaining >= 4 ? (exitOpening && remaining >= 9 ? 3 : Mathf.Min(2, remaining / 4)) : 0;
        for (int index = 0; index < ambusherCount; index++)
            available[assigned++].ConfigureRole(EnemyRole.Ambusher, null);
    }

    private static int FindRoleCandidate(List<PrototypeEnemy> candidates, int startIndex, bool preferSmall)
    {
        if (!preferSmall) return startIndex;
        for (int index = startIndex; index < candidates.Count; index++)
            if (candidates[index].IsSmall) return index;
        return startIndex;
    }

    private static void Swap<T>(List<T> list, int left, int right)
    {
        if (left == right) return;
        T value = list[left];
        list[left] = list[right];
        list[right] = value;
    }

    public void UnregisterEnemy(PrototypeEnemy enemy) => enemies.Remove(enemy);
    public void Register(PrototypeInteractable interactable)
    {
        if (interactable == null) return;
        interactable.Initialize(this);
        interactables.Add(interactable);
    }

    public bool CanUseRescueEntrance(PrototypeRescueEntrance entrance)
    {
        return entrance != null && entrance == rescueEntranceInteraction && activeRescueCaptive != null &&
               activeRescueCaptive.IsCaptive &&
               activeRescueRoom != null && !rescueEntranceOpening && !rescueEntranceOpen;
    }

    public void NotifyActorCaptured(PrototypeActor actor)
    {
        if (actor == null) return;
        if (currentRoomIndex < TotalRooms - 1)
            rescueQueue[actor] = currentRoomIndex;
        foreach (PrototypeEnemy enemy in enemies)
            enemy?.ForgetTarget(actor);
        AssignDirectorRoles();
        AddDirectorActivity(.35f);
        ShowMessage("Jugador cautivo. La zona de rescate aparecerá desde la siguiente sala.", 4f);
    }

    public void NotifyActorRescued(PrototypeActor actor)
    {
        if (actor == null) return;
        rescueQueue.Remove(actor);
        if (activeRescueCaptive == actor) activeRescueCaptive = null;
        noSurvivorDefeatAt = -1f;
        AddDirectorActivity(.55f);
        ShowMessage("Compañero rescatado con 1 corazón. Regresa por la misma puerta.", 4f);
    }

    private bool ShouldPrepareRescueBranch()
    {
        activeRescueCaptive = null;
        foreach (KeyValuePair<PrototypeActor, int> entry in rescueQueue)
        {
            if (entry.Key == null || !entry.Key.IsCaptive || currentRoomIndex <= entry.Value) continue;
            activeRescueCaptive = entry.Key;
            return true;
        }
        return false;
    }

    public void BeginRescueEntranceOpening(PrototypeRescueEntrance entrance)
    {
        if (!CanUseRescueEntrance(entrance)) return;
        StartCoroutine(OpenRescueEntranceAfterDelay());
    }

    private void PrepareRescueBranch()
    {
        if (activeRescueRoom != null || authoredRoom == null || activeRescueEntrance == null || roomSequence == null || roomSequence.RescueRoom == null)
            return;

        activeRescueRoomInstance = Instantiate(roomSequence.RescueRoom, Vector3.zero, Quaternion.identity);
        activeRescueRoomInstance.name = "Mapa_Rescate (conectado)";
        activeRescueRoom = activeRescueRoomInstance.GetComponent<RescueRoomDefinition>();
        if (activeRescueRoom == null)
        {
            Debug.LogError("[SchoolEscape/Rescate] Mapa_Rescate no tiene RescueRoomDefinition.");
            Destroy(activeRescueRoomInstance);
            activeRescueRoomInstance = null;
            return;
        }

        RescueDirection requiredDirection = RescueDirectionUtility.Opposite(activeRescueEntrance.Direction);
        connectedRescueDoor = activeRescueRoom.GetDoor(requiredDirection);
        if (connectedRescueDoor == null)
        {
            Debug.LogError($"[SchoolEscape/Rescate] Falta la puerta {requiredDirection} en Mapa_Rescate.");
            Destroy(activeRescueRoomInstance);
            activeRescueRoomInstance = null;
            activeRescueRoom = null;
            return;
        }

        AlignRescueRoom(activeRescueRoomInstance.transform, connectedRescueDoor.transform, activeRescueEntrance.transform);
        RefreshNavMeshSurfaces(activeRescueRoomInstance);
        foreach (RescueDoorAnchor door in activeRescueRoom.Doors)
        {
            if (door == null || door.Gate == null) continue;
            door.Gate.ResetGate();
        }
        ConfigureConnectedWallVisibility();

        GameObject linkObject = new GameObject("Enlace NavMesh de rescate");
        linkObject.transform.SetParent(activeRescueRoomInstance.transform, true);
        rescuePassageLink = linkObject.AddComponent<RescuePassageLink>();
        Transform regularRoomRoot = activeRoomInstance != null ? activeRoomInstance.transform : authoredRoom.transform;
        rescuePassageLink.Configure(activeRescueEntrance.transform, regularRoomRoot, activeRescueRoomInstance.transform);

        if (activeRescueCaptive != null && activeRescueCaptive.IsCaptive && activeRescueRoom.CaptiveSpawn != null)
            activeRescueCaptive.PlaceCaptiveForRescue(activeRescueRoom.CaptiveSpawn.position + Vector3.up);

        ShowMessage("Tu compañero está en la zona de rescate. Busca la entrada marcada.", 4f);
    }

    private void AlignRescueRoom(Transform rescueRoot, Transform rescueDoor, Transform entrance)
    {
        Transform regularRoot = activeRoomInstance != null ? activeRoomInstance.transform : authoredRoom.transform;
        rescueRoot.rotation = regularRoot.rotation;
        Vector3 entrancePoint = activeRescueEntrance != null && activeRescueEntrance.Gate != null
            ? activeRescueEntrance.Gate.ConnectionPoint
            : entrance.position;
        Vector3 rescuePoint = connectedRescueDoor != null && connectedRescueDoor.Gate != null
            ? connectedRescueDoor.Gate.ConnectionPoint
            : rescueDoor.position;
        rescueRoot.position += entrancePoint - rescuePoint;
        Physics.SyncTransforms();

        rescuePoint = connectedRescueDoor != null && connectedRescueDoor.Gate != null
            ? connectedRescueDoor.Gate.ConnectionPoint
            : rescueDoor.position;
        float residual = Vector3.Distance(rescuePoint, entrancePoint);
        Debug.Log($"[SchoolEscape/Rescate] {activeRescueEntrance.Direction} ↔ {connectedRescueDoor.Direction}; paredes superpuestas con error {residual:0.0000} m.");
    }

    private void ConfigureConnectedWallVisibility()
    {
        if (activeRescueEntrance == null || connectedRescueDoor == null) return;

        activeRescueEntrance.Gate?.SetConnectionGroupVisible(false);
        Debug.Log("[SchoolEscape/Rescate] Entrada y paredes adyacentes del mapa actual ocultas; se conserva la cara de Mapa_Rescate.");
    }

    private static void RefreshNavMeshSurfaces(GameObject roomInstance)
    {
        if (roomInstance == null) return;
        foreach (NavMeshSurface surface in roomInstance.GetComponentsInChildren<NavMeshSurface>(true))
        {
            if (surface == null || surface.navMeshData == null) continue;
            surface.RemoveData();
            surface.AddData();
        }
    }

    private IEnumerator OpenRescueEntranceAfterDelay()
    {
        rescueEntranceOpening = true;
        activeRescueEntrance.Gate?.BeginOpening();
        connectedRescueDoor?.Gate?.BeginOpening();
        float endTime = Time.time + RescueEntranceOpeningDuration;
        int previousSecond = -1;
        while (Time.time < endTime)
        {
            int remaining = Mathf.CeilToInt(endTime - Time.time);
            if (remaining != previousSecond)
            {
                previousSecond = remaining;
                ShowMessage($"Abriendo acceso de rescate: {remaining}s", 1.05f);
            }
            yield return null;
        }

        activeRescueEntrance.Gate?.Unlock();
        connectedRescueDoor?.Gate?.Unlock();
        rescuePassageLink?.SetTraversable(true);
        rescueEntranceOpening = false;
        rescueEntranceOpen = true;
        ShowMessage("Acceso de rescate abierto.", 3f);
    }

    private Vector3 GetRandomEnemySpawn()
    {
        if (activeEnemySpawnPoints.Count > 0)
            return activeEnemySpawnPoints[Random.Range(0, activeEnemySpawnPoints.Count)];
        return EnemySpawnPoints[Random.Range(0, EnemySpawnPoints.Length)];
    }

    public Vector3 ClosestEnemySpawn(Vector3 position)
    {
        IReadOnlyList<Vector3> spawnPoints = activeEnemySpawnPoints.Count > 0 ? activeEnemySpawnPoints : EnemySpawnPoints;
        Vector3 closest = spawnPoints[0];
        float bestDistance = float.MaxValue;
        foreach (Vector3 spawn in spawnPoints)
        {
            float distance = (spawn - position).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            closest = spawn;
        }
        return closest;
    }

    public PrototypeInteractable ClosestInteractable(Vector3 position, float maxDistance)
    {
        PrototypeInteractable best = null;
        float bestDistance = maxDistance;
        foreach (PrototypeInteractable item in interactables)
        {
            if (item == null || !item.Available) continue;
            float distance = item.DistanceOnFloorFrom(position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = item;
            }
        }

        foreach (PrototypeActor actor in GetParticipatingActors())
        {
            if (actor == null || actor.RescueInteraction == null || !actor.RescueInteraction.Available) continue;
            Vector3 actorPosition = actor.transform.position;
            actorPosition.y = position.y;
            float distance = Vector3.Distance(position, actorPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = actor.RescueInteraction;
            }
        }
        return best;
    }

    public void CollectKey()
    {
        HasKey = true;
        ShowMessage("Llave encontrada. Ve a la salida.", 4f);
    }

    public void UnlockExit()
    {
        exitOpening = false;
        HasKey = true;
        if (authoredRoom != null)
        {
            authoredRoom.GetComponent<RoomObjectiveController>()?.UnlockImmediately();
            foreach (Transform exit in authoredRoom.Exits)
                exit?.GetComponent<RoomExitGate>()?.Unlock();
        }
        ShowMessage("La puerta terminó de abrirse. Cruza la salida.", 4f);
    }

    public void ActivateExitPanel(PrototypeExitPanel panel)
    {
        if (panel == null || !activatedExitPanels.Add(panel)) return;

        int required = Mathf.Max(1, requiredExitPanelCount);
        if (activatedExitPanels.Count < required)
        {
            ShowMessage($"Palancas activadas: {activatedExitPanels.Count}/{required}", 3f);
            return;
        }

        BeginExitOpening();
    }

    private void BeginExitOpening()
    {
        if (HasKey || exitOpening) return;

        exitOpening = true;
        exitOpeningEndsAt = Time.time + ExitOpeningDuration;
        if (authoredRoom != null)
        {
            foreach (Transform exit in authoredRoom.Exits)
                exit?.GetComponent<RoomExitGate>()?.BeginOpening();
        }

        ShowMessage($"Palancas completas. La puerta se abrirá en {ExitOpeningDuration:0} segundos. ¡Resiste!", 3f);
        StartCoroutine(CompleteExitOpeningAfterDelay());
    }

    private IEnumerator CompleteExitOpeningAfterDelay()
    {
        int previousSecond = -1;
        while (Time.time < exitOpeningEndsAt)
        {
            int remaining = Mathf.CeilToInt(exitOpeningEndsAt - Time.time);
            if (remaining != previousSecond)
            {
                previousSecond = remaining;
                ShowMessage($"Puerta abriéndose: {remaining}s. Peligro aumentado.", 1.05f);
            }
            yield return null;
        }

        UnlockExit();
    }

    public void AddBattery(PrototypeActor actor = null)
    {
        inventory.AddBattery();
        ShowPersonalMessage(actor != null ? actor : Player, "Pila recogida", 2f);
    }

    public void AddBandage(PrototypeActor actor = null)
    {
        inventory.AddBandage();
        ShowPersonalMessage(actor != null ? actor : Player, "Venda recogida", 2f);
    }

    public bool UseBattery(PrototypeActor actor = null)
    {
        if (!inventory.TryUseBattery())
        {
            ShowPersonalMessage(actor != null ? actor : Player, "Sin pilas", 2f);
            return false;
        }
        return true;
    }

    public bool UseBandage()
    {
        if (IsNetworkReplica && ClientBridge != null)
        {
            ClientBridge.RequestBandage();
            return true;
        }
        return UseBandageFor(Player);
    }

    public bool UseBandageFor(PrototypeActor actor)
    {
        if (Bandages <= 0)
        {
            ShowPersonalMessage(actor, "No tienes vendas", 2f);
            return false;
        }
        if (actor == null || actor.IsCaptive || actor.IsDown || actor.Dragger != null)
        {
            ShowPersonalMessage(actor, "No puedes usar una venda en este estado", 2f);
            return false;
        }
        if (actor.Health >= 3)
        {
            ShowPersonalMessage(actor, "Tu vida ya está completa", 2f);
            return false;
        }
        if (!inventory.TryUseBandage()) return false;
        actor.HealFull();
        ShowPersonalMessage(actor, "Salud restaurada", 2f);
        return true;
    }

    public void SetLocalInteractHeld(bool held)
    {
        if (IsNetworkReplica && ClientBridge != null)
            ClientBridge.SetInteractHeld(held);
        else if (held)
            Player?.PulseInteract();
    }

    public void RequestLocalFlash()
    {
        if (IsNetworkReplica && ClientBridge != null)
            ClientBridge.RequestFlash();
        else
            Player?.RequestFlash();
    }

    public bool FireFlashFor(PrototypeActor actor, Vector3 requestedDirection)
    {
        return FireFlashInternal(actor, requestedDirection, null, 0d, 0d);
    }

    public bool FireCompensatedFlashFor(
        PrototypeActor actor,
        Vector3 requestedDirection,
        SchoolEscape.Multiplayer.ServerRewindHistory history,
        double observedServerTime,
        double currentServerTime)
    {
        return FireFlashInternal(actor, requestedDirection, history, observedServerTime, currentServerTime);
    }

    private bool FireFlashInternal(
        PrototypeActor actor,
        Vector3 requestedDirection,
        SchoolEscape.Multiplayer.ServerRewindHistory history,
        double observedServerTime,
        double currentServerTime)
    {
        if (actor == null || actor.IsDown || actor.IsCaptive || actor.Dragger != null) return false;

        Vector3 direction = requestedDirection;
        if (direction.sqrMagnitude < .01f)
        {
            direction = actor.transform.forward;
            float nearestDistance = 7f;
            foreach (PrototypeEnemy enemy in enemies)
            {
                if (enemy == null) continue;
                Vector3 delta = enemy.transform.position - actor.transform.position;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    direction = delta.normalized;
                }
            }
        }

        direction.y = 0f;
        if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
        direction.Normalize();

        List<PrototypeEnemy> affected = null;
        int currentStateHits = 0;
        if (history != null && !history.TryEvaluateFlash(
                this,
                actor,
                direction,
                observedServerTime,
                currentServerTime,
                out affected,
                out currentStateHits,
                out string rejectionReason))
        {
            ShowPersonalMessage(actor, $"Flash rechazado: {rejectionReason}", 2f);
            return false;
        }

        if (!UseBattery(actor)) return false;
        AddDirectorActivity(.16f);

        if (affected == null)
        {
            affected = new List<PrototypeEnemy>();
            foreach (PrototypeEnemy enemy in enemies.ToArray())
            {
                if (enemy == null) continue;
                Vector3 delta = enemy.transform.position - actor.transform.position;
                delta.y = 0f;
                if (delta.magnitude <= 7f && Vector3.Angle(direction, delta) <= 35f)
                    affected.Add(enemy);
            }
        }

        int affectedEnemies = 0;
        foreach (PrototypeEnemy enemy in affected)
        {
            if (enemy == null) continue;
            enemy.HitByFlash();
            affectedEnemies++;
        }

        string comparison = history != null ? $" · sin rewind: {currentStateHits}" : string.Empty;
        ShowPersonalMessage(actor, (affectedEnemies == 1
            ? "Flash: 1 cultista afectado"
            : $"Flash: {affectedEnemies} cultistas afectados") + comparison, 1.5f);

        GameObject flash = CreateBox(
            "Destello remoto",
            actor.transform.position + direction * 2.4f + Vector3.up * .15f,
            new Vector3(2.7f, .08f, 4.6f),
            MakeMaterial(new Color(1f, 1f, .7f)),
            false);
        flash.transform.rotation = Quaternion.LookRotation(direction);
        Destroy(flash, .12f);
        return true;
    }

    public void CompleteCurrentRoom()
    {
        if (Victory || Defeat || roomTransitionInProgress) return;

        int nextRoomIndex = currentRoomIndex + 1;
        if (roomSequence == null || nextRoomIndex >= roomSequence.Count)
        {
            Win();
            return;
        }

        StartCoroutine(TransitionToRoom(nextRoomIndex));
    }

    public void ActorReachedExit(PrototypeActor actor)
    {
        if (actor == null || !actor.CanAct || Victory || Defeat || roomTransitionInProgress) return;
        bool finalRoom = roomSequence == null || currentRoomIndex + 1 >= roomSequence.Count;
        if (!finalRoom)
        {
            CompleteCurrentRoom();
            return;
        }

        actor.MarkEscaped();
        EvaluateFinalResult();
    }

    private void EvaluateFinalResult()
    {
        int participantCount = HasRemoteHuman ? 2 : 1;
        int escapedCount = 0;
        int resolvedCount = 0;
        foreach (PrototypeActor actor in GetParticipatingActors())
        {
            if (actor == null || (!HasRemoteHuman && actor == Ally)) continue;
            if (actor.IsEscaped) escapedCount++;
            if (actor.IsEscaped || actor.IsCaptive) resolvedCount++;
        }
        if (resolvedCount < participantCount) return;

        if (escapedCount <= 0)
        {
            MatchResult = PrototypeMatchResult.TotalDefeat;
            Victory = false;
        }
        else
        {
            MatchResult = escapedCount == participantCount
                ? PrototypeMatchResult.PerfectEscape
                : PrototypeMatchResult.PartialEscape;
            Victory = true;
        }
        ShowMessage(MatchResult == PrototypeMatchResult.PerfectEscape
            ? "ESCAPE PERFECTO"
            : MatchResult == PrototypeMatchResult.PartialEscape
                ? "ESCAPE PARCIAL"
                : "TODO EL EQUIPO FUE RAPTADO", 999f);
    }

    private IEnumerator TransitionToRoom(int nextRoomIndex)
    {
        roomTransitionInProgress = true;
        ShowMessage($"{RoomProgressText} completada", 2f);

        foreach (PrototypeActor actor in GetParticipatingActors())
        {
            if (actor == null || actor.IsCaptive || actor.IsEscaped) continue;
            if (actor.Dragger != null || actor.IsDown)
                actor.Captured(ClosestEnemySpawn(actor.transform.position));
        }
        if (Player != null) Player.enabled = false;
        if (Ally != null) Ally.enabled = false;

        foreach (PrototypeEnemy enemy in enemies.ToArray())
            if (enemy != null) Destroy(enemy.gameObject);
        enemies.Clear();
        interactables.Clear();
        activatedExitPanels.Clear();
        activeEnemySpawnPoints.Clear();

        if (activeRescueRoomInstance != null) Destroy(activeRescueRoomInstance);
        activeRescueRoomInstance = null;
        activeRescueRoom = null;
        activeRescueEntrance = null;
        rescueEntranceInteraction = null;
        connectedRescueDoor = null;
        rescuePassageLink = null;
        activeRescueCaptive = null;
        rescueEntranceOpening = false;
        rescueEntranceOpen = false;

        HasKey = false;
        exitOpening = false;
        exitOpeningEndsAt = 0f;
        requiredExitPanelCount = 1;
        normalEnemiesSpawnedThisRoom = 0;
        smallEnemiesSpawnedThisRoom = 0;
        spawnTimer = 10f;

        GameObject previousRoom = activeRoomInstance != null
            ? activeRoomInstance
            : authoredRoom != null ? authoredRoom.gameObject : null;
        authoredRoom = null;
        activeRoomInstance = null;
        if (previousRoom != null)
        {
            previousRoom.SetActive(false);
            Destroy(previousRoom);
        }

        yield return new WaitForSeconds(.45f);

        LoadSequencedRoom(nextRoomIndex);
        if (Player != null && !Player.IsCaptive && !Player.IsEscaped) Player.enabled = true;
        if (Ally != null && !Ally.IsCaptive && !Ally.IsEscaped)
            Ally.enabled = Ally.GetComponent<NetworkClientAllyBridge>() == null;
        roomTransitionInProgress = false;
    }

    public void Win()
    {
        MatchResult = PrototypeMatchResult.PerfectEscape;
        Victory = true;
        ShowMessage("ESCAPASTE DEL INSTITUTO", 999f);
    }

    public void ShowMessage(string text, float duration)
    {
        message = text;
        messageUntil = Time.time + duration;
    }

    public void ShowPersonalMessage(PrototypeActor actor, string text, float duration)
    {
        if (actor == Ally)
        {
            allyMessage = text;
            allyMessageUntil = Time.time + duration;
            return;
        }
        if (actor != Player) return;
        playerMessage = text;
        playerMessageUntil = Time.time + duration;
    }

    public string CurrentPersonalAnnouncementFor(PrototypeActor actor)
    {
        if (actor == Ally)
            return Time.time < allyMessageUntil ? allyMessage : string.Empty;
        if (actor == Player)
            return Time.time < playerMessageUntil ? playerMessage : string.Empty;
        return string.Empty;
    }

    public Vector3 GetNetworkPlayerSpawnPosition(bool allySlot)
    {
        Transform spawn = authoredRoom != null ? authoredRoom.GetPlayerSpawn(allySlot ? 1 : 0) : null;
        if (spawn != null) return spawn.position + Vector3.up;
        if (allySlot && Ally != null) return Ally.transform.position;
        if (!allySlot && Player != null) return Player.transform.position;
        Vector3 origin = authoredRoom != null ? authoredRoom.transform.position : Vector3.zero;
        return origin + Vector3.up + (allySlot ? Vector3.right * 1.7f : Vector3.zero);
    }

    public void AttachNetworkHostPlayer(PrototypePlayer networkPlayer, bool localAuthority)
    {
        if (networkPlayer == null) return;
        if (!localAuthority) EnterNetworkReplicaMode();

        PrototypePlayer previous = Player;
        Player = networkPlayer;
        Player.Initialize(this);
        Player.enabled = localAuthority;
        if (previous != null && previous != networkPlayer) Destroy(previous.gameObject);
        if (localAuthority) networkCameraTarget = Player.transform;
    }

    public void AttachNetworkClientAlly(PrototypeAlly networkAlly, bool serverAuthority, bool localAuthority)
    {
        if (networkAlly == null) return;
        if (!serverAuthority) EnterNetworkReplicaMode();
        PrototypeAlly previous = Ally;
        Ally = networkAlly;
        Ally.Initialize(this);
        Ally.enabled = false;
        if (previous != null && previous != networkAlly) Destroy(previous.gameObject);
        if (localAuthority) networkCameraTarget = Ally.transform;
    }

    public void SetNetworkCameraTarget(Transform target)
    {
        if (target != null) networkCameraTarget = target;
    }

    public void RestoreSoloAllyAfterDisconnect()
    {
        if (IsNetworkReplica || authoredRoom == null) return;
        Vector3 position = GetNetworkPlayerSpawnPosition(true);
        if (Ally != null) Destroy(Ally.gameObject);
        Ally = CreateCapsule("Compañero", position, new Color(.25f, .9f, .38f)).AddComponent<PrototypeAlly>();
        Ally.Initialize(this);
        ShowMessage("Jugador 2 desconectado: vuelve el aliado IA", 4f);
    }

    private void EnterNetworkReplicaMode()
    {
        if (IsNetworkReplica) return;
        IsNetworkReplica = true;
        enabled = true;

        foreach (PrototypeEnemy enemy in enemies.ToArray())
            if (enemy != null) Destroy(enemy.gameObject);
        enemies.Clear();

        if (Player != null) Destroy(Player.gameObject);
        if (Ally != null) Destroy(Ally.gameObject);
        Player = null;
        Ally = null;
    }

    public IEnumerator ResetForNetworkMatch(bool forceLocal = false)
    {
        if (forceLocal) IsNetworkReplica = false;
        bool rebuildAsReplica = IsNetworkReplica;
        foreach (PrototypeEnemy enemy in enemies.ToArray())
            if (enemy != null) Destroy(enemy.gameObject);
        enemies.Clear();
        interactables.Clear();
        activatedExitPanels.Clear();
        activeEnemySpawnPoints.Clear();

        if (Player != null) Destroy(Player.gameObject);
        if (Ally != null) Destroy(Ally.gameObject);
        Player = null;
        Ally = null;
        networkCameraTarget = null;

        if (activeRescueRoomInstance != null) Destroy(activeRescueRoomInstance);
        if (activeRoomInstance != null) Destroy(activeRoomInstance);
        activeRoomInstance = null;
        authoredRoom = null;
        activeRescueRoomInstance = null;
        activeRescueRoom = null;
        activeRescueEntrance = null;
        rescueEntranceInteraction = null;
        connectedRescueDoor = null;
        rescuePassageLink = null;
        activeRescueCaptive = null;
        rescueQueue.Clear();

        HasKey = false;
        exitOpening = false;
        exitOpeningEndsAt = 0f;
        Victory = false;
        MatchResult = PrototypeMatchResult.InProgress;
        roomTransitionInProgress = false;
        currentRoomIndex = -1;
        rescueEntranceOpening = false;
        rescueEntranceOpen = false;
        noSurvivorDefeatAt = -1f;
        recentDirectorActivity = 0f;
        inventory.Reset();
        RunTime = 0f;
        normalEnemiesSpawnedThisRoom = 0;
        smallEnemiesSpawnedThisRoom = 0;
        spawnTimer = 10f;
        message = "Encuentra la llave y escapa";
        messageUntil = Time.time + 4f;
        playerMessage = allyMessage = string.Empty;
        playerMessageUntil = allyMessageUntil = 0f;

        yield return null;
        roomSequence = Resources.Load<RoomSequenceCatalog>("RoomSequenceCatalog");
        if (rebuildAsReplica && roomSequence != null && roomSequence.Count > 0)
            LoadReplicaRoom(0);
        else
            BuildPrototype();
    }

    public void Restart()
    {
        int buildIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
        if (buildIndex >= 0)
            UnityEngine.SceneManagement.SceneManager.LoadScene(buildIndex);
        else
            StartCoroutine(RestartInUnsavedScene());
    }

    private IEnumerator RestartInUnsavedScene()
    {
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (root != gameObject) Destroy(root);
        yield return null;

        enemies.Clear();
        interactables.Clear();
        activatedExitPanels.Clear();
        activeEnemySpawnPoints.Clear();
        HasKey = false;
        exitOpening = false;
        exitOpeningEndsAt = 0f;
        Victory = false;
        MatchResult = PrototypeMatchResult.InProgress;
        roomTransitionInProgress = false;
        currentRoomIndex = -1;
        activeRoomInstance = null;
        authoredRoom = null;
        if (activeRescueRoomInstance != null) Destroy(activeRescueRoomInstance);
        activeRescueRoomInstance = null;
        activeRescueRoom = null;
        activeRescueEntrance = null;
        rescueEntranceInteraction = null;
        connectedRescueDoor = null;
        rescuePassageLink = null;
        rescueEntranceOpening = false;
        rescueEntranceOpen = false;
        rescueQueue.Clear();
        activeRescueCaptive = null;
        noSurvivorDefeatAt = -1f;
        recentDirectorActivity = 0f;
        inventory.Reset();
        RunTime = 0f;
        normalEnemiesSpawnedThisRoom = 0;
        smallEnemiesSpawnedThisRoom = 0;
        spawnTimer = 10f;
        message = "Encuentra la llave y escapa";
        messageUntil = Time.time + 4f;
        playerMessage = string.Empty;
        playerMessageUntil = 0f;
        allyMessage = string.Empty;
        allyMessageUntil = 0f;
        BuildPrototype();
    }

    private void OnGUI()
    {
        if (BasicGameFlowUI.IsMenuVisible) return;
        if (MobileHud != null && MobileHud.isActiveAndEnabled) return;
        EnsureStyles();
        float scale = Mathf.Clamp(Screen.width / 1000f, .72f, 1.35f);
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float w = Screen.width / scale;
        float h = Screen.height / scale;

        GUI.Box(new Rect(18, 16, 390, 166), "");
        GUI.Label(new Rect(32, 25, 290, 32), $"SCHOOL ESCAPE: {DisplayRoomProgressText}", titleStyle);
        GUI.Label(new Rect(32, 55, 365, 122), $"Vida: {Hearts(DisplayHealth)}   Pilas: {DisplayBatteries}/3   Venda: {(DisplayBandages > 0 ? "Sí" : "No")}\nSalida: {DisplayExitStatusText}   Peligro: x{DisplayDangerMultiplier:0.0}\n{EnemyCounterText}\n{NetworkAoiStatusText}\n{DisplayInteractionStatus}", bodyStyle);

        if (Ally != null)
        {
            string allyState = Ally.IsCaptive ? "CAUTIVO - el peligro aumenta" : Ally.IsDown ? "CAÍDO - mantén E para rescatar" : Hearts(Ally.Health);
            GUI.Label(new Rect(32, 166, 520, 30), "Compañero: " + allyState, bodyStyle);
        }

        if (!BasicGameFlowUI.HasAnnouncementPresenter && !string.IsNullOrEmpty(DisplayAnnouncementText))
            GUI.Label(new Rect(w * .25f, 20, w * .5f, 46), DisplayAnnouncementText, centerStyle);

        GUI.Label(new Rect(20, h - 112, 650, 92), "WASD: mover  |  Espacio: flash automático\nFLASH: clic corto = autoapuntar  |  Mantén y arrastra = apuntar, suelta = disparar\nE: interactuar/canalizar  |  B: usar venda  |  R: reiniciar", bodyStyle);

        if (IsNetworkReplica)
        {
            if (GUI.Button(new Rect(w - 155, h - 145, 130, 55), "FLASH")) RequestLocalFlash();
        }
        else
            GUI.Box(new Rect(w - 155, h - 145, 130, 55), Player != null && Player.IsAimingFlash ? "APUNTANDO..." : "FLASH");
        SetLocalInteractHeld(GUI.RepeatButton(new Rect(w - 155, h - 82, 130, 55), "INTERACTUAR"));
        if (GUI.Button(new Rect(w - 295, h - 82, 125, 55), "VENDA")) UseBandage();

        GUI.Label(new Rect(30, h - 190, 175, 36), "Joystick táctil", centerStyle);
        GUI.Box(new Rect(65, h - 155, 95, 95), "+");

        if (DisplayMatchResult != PrototypeMatchResult.InProgress)
        {
            GUI.Box(new Rect(w * .22f, h * .34f, w * .56f, 190), "");
            string resultTitle = DisplayMatchResult == PrototypeMatchResult.PerfectEscape
                ? "ESCAPE PERFECTO"
                : DisplayMatchResult == PrototypeMatchResult.PartialEscape
                    ? "ESCAPE PARCIAL"
                    : "DERROTA TOTAL";
            GUI.Label(new Rect(w * .24f, h * .36f, w * .52f, 70), resultTitle, centerStyle);
            GUI.Label(new Rect(w * .30f, h * .44f, w * .40f, 60), MemorialText, bodyStyle);
            if (GUI.Button(new Rect(w * .38f, h * .51f, w * .24f, 52), "REINICIAR")) Restart();
        }
    }

    private void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = new Color(.9f, .94f, 1f) } };
        centerStyle = new GUIStyle(bodyStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true };
    }

    private static string Hearts(int amount) => amount <= 0 ? "♡♡♡" : new string('♥', amount) + new string('♡', 3 - amount);

    public Rect FlashButtonScreenRect()
    {
        if (MobileHud != null)
            return MobileHud.GetFlashButtonScreenRect();
        float scale = Mathf.Clamp(Screen.width / 1000f, .72f, 1.35f);
        return new Rect(Screen.width - 155f * scale, Screen.height - 145f * scale, 130f * scale, 55f * scale);
    }

    public static Material MakeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    public static GameObject CreateBox(string name, Vector3 position, Vector3 scale, Material material, bool collider)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material = material;
        if (!collider) Destroy(go.GetComponent<Collider>());
        return go;
    }

    public static GameObject CreateCapsule(string name, Vector3 position, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position = position;
        Destroy(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().material = MakeMaterial(color);
        CharacterController controller = go.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = .45f;
        controller.center = Vector3.zero;
        return go;
    }
}

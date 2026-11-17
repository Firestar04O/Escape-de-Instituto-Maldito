using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using SchoolEscape.Rooms;
using SchoolEscape.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class SchoolEscapePrototype : MonoBehaviour
{
    public PrototypePlayer Player { get; private set; }
    public PrototypeAlly Ally { get; private set; }

    public bool HasKey { get; private set; }
    public bool Victory { get; private set; }
    public int Batteries => inventory.Batteries;
    public int Bandages => inventory.Bandages;
    public float RunTime { get; private set; }
    public float DangerMultiplier => 1f + RunTime / 80f + (Ally != null && Ally.IsCaptive ? 1.5f : 0f) + (exitOpening ? 1.25f : 0f);
    public bool CanUseBandage => Bandages > 0 && Player != null && !Player.IsCaptive && !Player.IsDown && Player.Dragger == null && Player.Health < 3;
    public bool IsExitOpening => exitOpening;
    public float ExitOpeningRemaining => exitOpening ? Mathf.Max(0f, exitOpeningEndsAt - Time.time) : 0f;
    public string ExitStatusText => HasKey ? "ABIERTA" : exitOpening ? $"ABRIENDO {Mathf.CeilToInt(ExitOpeningRemaining)}s" : "bloqueada";
    public MobileHudController MobileHud { get; private set; }

    private readonly List<PrototypeEnemy> enemies = new List<PrototypeEnemy>();
    private readonly List<PrototypeInteractable> interactables = new List<PrototypeInteractable>();
    private readonly HashSet<PrototypeExitPanel> activatedExitPanels = new HashSet<PrototypeExitPanel>();
    private readonly PrototypeInventory inventory = new PrototypeInventory();
    private Material floorMaterial;
    private Material wallMaterial;
    private Material furnitureMaterial;
    private SchoolRoomDefinition authoredRoom;
    private int requiredExitPanelCount = 1;
    private readonly List<Vector3> activeEnemySpawnPoints = new List<Vector3>();
    private float spawnTimer = 10f;
    private const float ExitOpeningDuration = 8f;
    private const float OrientationThreshold = .72f;
    private const float OrientationDominance = .18f;
    private const float OrientationStableDuration = .6f;
    private bool exitOpening;
    private float exitOpeningEndsAt;
    private ScreenOrientation orientationCandidate = ScreenOrientation.LandscapeLeft;
    private float orientationCandidateSince;
    private float nextOrientationChangeTime;
    private string message = "Encuentra la llave y escapa";
    private float messageUntil = 4f;
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;
    private GUIStyle centerStyle;

    public static readonly Vector3[] EnemySpawnPoints =
    {
        new Vector3(-12.5f, 1f, 8f), new Vector3(12.5f, 1f, 8f),
        new Vector3(-12.5f, 1f, -8f), new Vector3(12.5f, 1f, -8f)
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoStart()
    {
        Debug.Log("[SchoolEscape] AutoStart");
        if (FindFirstObjectByType<SchoolEscapePrototype>() == null)
            new GameObject("School Escape Prototype").AddComponent<SchoolEscapePrototype>();
    }

    private void Awake()
    {
        MobileHud = FindFirstObjectByType<MobileHudController>();
        ConfigureMobileOrientation();
        ConfigurePerformanceBudget();
        if (Accelerometer.current != null)
            InputSystem.EnableDevice(Accelerometer.current);
        EnhancedTouchSupport.Enable();
        BuildPrototype();
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
        EnhancedTouchSupport.Disable();
    }

    private void Update()
    {
        UpdateLandscapeOrientation();

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            Restart();

        if (Victory || Player == null || Player.IsCaptive) return;

        RunTime += Time.deltaTime;
        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f && enemies.Count < 10)
        {
            SpawnEnemy(Random.value < 0.58f);
            spawnTimer = Mathf.Max(3.5f, 10f / DangerMultiplier);
        }
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

        Player = CreateCapsule("Jugador", playerPosition, new Color(.18f, .52f, 1f)).AddComponent<PrototypePlayer>();
        Player.Initialize(this);
        Ally = CreateCapsule("Compañero de prueba", allyPosition, new Color(.25f, .9f, .38f)).AddComponent<PrototypeAlly>();
        Ally.Initialize(this);

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
        ShowMessage(objectiveMessage, 5f);
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
        Vector3 spawnPoint = GetRandomEnemySpawn();
        GameObject enemy = CreateCapsule(small ? "Cultista chico" : "Cultista normal", spawnPoint, small ? new Color(.95f, .25f, .15f) : new Color(.62f, .08f, .09f));
        enemy.transform.localScale = small ? new Vector3(.72f, .72f, .72f) : new Vector3(1.15f, 1.15f, 1.15f);
        PrototypeEnemy controller = enemy.AddComponent<PrototypeEnemy>();
        controller.Initialize(this);
        controller.IsSmall = small;
        enemies.Add(controller);
    }

    public void UnregisterEnemy(PrototypeEnemy enemy) => enemies.Remove(enemy);
    public void Register(PrototypeInteractable interactable)
    {
        if (interactable == null) return;
        interactable.Initialize(this);
        interactables.Add(interactable);
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

        if (Ally != null && Ally.RescueInteraction != null && Ally.RescueInteraction.Available)
        {
            Vector3 allyPosition = Ally.transform.position;
            allyPosition.y = position.y;
            float distance = Vector3.Distance(position, allyPosition);
            if (distance < bestDistance)
                return Ally.RescueInteraction;
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

    public void AddBattery()
    {
        inventory.AddBattery();
        ShowMessage("Pila recogida", 2f);
    }

    public void AddBandage()
    {
        inventory.AddBandage();
        ShowMessage("Venda recogida", 2f);
    }

    public bool UseBattery()
    {
        if (!inventory.TryUseBattery())
        {
            ShowMessage("Sin pilas", 2f);
            return false;
        }
        return true;
    }

    public bool UseBandage()
    {
        if (Bandages <= 0)
        {
            ShowMessage("No tienes vendas", 2f);
            return false;
        }
        if (Player == null || Player.IsCaptive || Player.IsDown || Player.Dragger != null)
        {
            ShowMessage("No puedes usar una venda en este estado", 2f);
            return false;
        }
        if (Player.Health >= 3)
        {
            ShowMessage("Tu vida ya está completa", 2f);
            return false;
        }
        if (!inventory.TryUseBandage()) return false;
        Player.HealFull();
        ShowMessage("Salud restaurada", 2f);
        return true;
    }

    public void Win()
    {
        Victory = true;
        ShowMessage("ESCAPASTE DEL INSTITUTO", 999f);
    }

    public void ShowMessage(string text, float duration)
    {
        message = text;
        messageUntil = Time.time + duration;
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
        HasKey = false;
        exitOpening = false;
        exitOpeningEndsAt = 0f;
        Victory = false;
        inventory.Reset();
        RunTime = 0f;
        spawnTimer = 10f;
        message = "Encuentra la llave y escapa";
        messageUntil = Time.time + 4f;
        BuildPrototype();
    }

    private void OnGUI()
    {
        if (MobileHud != null && MobileHud.isActiveAndEnabled) return;
        EnsureStyles();
        float scale = Mathf.Clamp(Screen.width / 1000f, .72f, 1.35f);
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float w = Screen.width / scale;
        float h = Screen.height / scale;

        GUI.Box(new Rect(18, 16, 310, 118), "");
        GUI.Label(new Rect(32, 25, 290, 32), "SCHOOL ESCAPE: CULT NIGHT", titleStyle);
        GUI.Label(new Rect(32, 55, 280, 74), $"Vida: {Hearts(Player != null ? Player.Health : 0)}   Pilas: {Batteries}/3   Venda: {(Bandages > 0 ? "Sí" : "No")}\nSalida: {ExitStatusText}   Peligro: x{DangerMultiplier:0.0}", bodyStyle);

        if (Ally != null)
        {
            string allyState = Ally.IsCaptive ? "CAUTIVO - el peligro aumenta" : Ally.IsDown ? "CAÍDO - mantén E para rescatar" : Hearts(Ally.Health);
            GUI.Label(new Rect(32, 136, 520, 30), "Compañero: " + allyState, bodyStyle);
        }

        if (Time.time < messageUntil)
            GUI.Label(new Rect(w * .25f, 20, w * .5f, 46), message, centerStyle);

        GUI.Label(new Rect(20, h - 112, 650, 92), "WASD: mover  |  Espacio: flash automático\nFLASH: clic corto = autoapuntar  |  Mantén y arrastra = apuntar, suelta = disparar\nE: interactuar/canalizar  |  B: usar venda  |  R: reiniciar", bodyStyle);

        GUI.Box(new Rect(w - 155, h - 145, 130, 55), Player != null && Player.IsAimingFlash ? "APUNTANDO..." : "FLASH");
        if (GUI.RepeatButton(new Rect(w - 155, h - 82, 130, 55), "INTERACTUAR")) Player?.PulseInteract();
        if (GUI.Button(new Rect(w - 295, h - 82, 125, 55), "VENDA")) UseBandage();

        GUI.Label(new Rect(30, h - 190, 175, 36), "Joystick táctil", centerStyle);
        GUI.Box(new Rect(65, h - 155, 95, 95), "+");

        if (Victory || (Player != null && Player.IsCaptive))
        {
            GUI.Box(new Rect(w * .22f, h * .34f, w * .56f, 190), "");
            GUI.Label(new Rect(w * .24f, h * .38f, w * .52f, 70), Victory ? "ESCAPASTE" : "HAS SIDO RAPTADO", centerStyle);
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

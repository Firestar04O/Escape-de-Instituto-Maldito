using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class SchoolEscapePrototype : MonoBehaviour
{
    public static SchoolEscapePrototype I { get; private set; }

    public PrototypePlayer Player { get; private set; }
    public PrototypeAlly Ally { get; private set; }

    public bool HasKey { get; private set; }
    public bool Victory { get; private set; }
    public int Batteries { get; private set; } = 2;
    public int Bandages { get; private set; }
    public float RunTime { get; private set; }
    public float DangerMultiplier => 1f + RunTime / 80f + (Ally != null && Ally.IsCaptive ? 1.5f : 0f);

    private readonly List<PrototypeEnemy> enemies = new List<PrototypeEnemy>();
    private readonly List<PrototypeInteractable> interactables = new List<PrototypeInteractable>();
    private Material floorMaterial;
    private Material wallMaterial;
    private Material furnitureMaterial;
    private float spawnTimer = 10f;
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
        I = this;
        EnhancedTouchSupport.Enable();
        BuildPrototype();
    }

    private void OnDestroy()
    {
        if (I == this) I = null;
        EnhancedTouchSupport.Disable();
    }

    private void Update()
    {
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

    private void BuildPrototype()
    {
        Debug.Log("[SchoolEscape] Construyendo prototipo");
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
        Ally = CreateCapsule("Compañero", new Vector3(1.6f, 1f, -6.4f), new Color(.25f, .9f, .38f)).AddComponent<PrototypeAlly>();

        SpawnEnemy(true);
        SpawnEnemy(false);

        GameObject cameraObject = new GameObject("Top Down Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.fieldOfView = 48f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.025f, .03f, .045f);
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<PrototypeCamera>();

        GameObject lightObject = new GameObject("Moon Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(.55f, .65f, 1f);
        light.intensity = 1.25f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
    }

    private void SpawnEnemy(bool small)
    {
        GameObject enemy = CreateCapsule(small ? "Cultista chico" : "Cultista normal", EnemySpawnPoints[Random.Range(0, EnemySpawnPoints.Length)], small ? new Color(.95f, .25f, .15f) : new Color(.62f, .08f, .09f));
        enemy.transform.localScale = small ? new Vector3(.72f, .72f, .72f) : new Vector3(1.15f, 1.15f, 1.15f);
        PrototypeEnemy controller = enemy.AddComponent<PrototypeEnemy>();
        controller.IsSmall = small;
        enemies.Add(controller);
    }

    public void UnregisterEnemy(PrototypeEnemy enemy) => enemies.Remove(enemy);
    public void Register(PrototypeInteractable interactable) => interactables.Add(interactable);

    public static Vector3 ClosestEnemySpawn(Vector3 position)
    {
        Vector3 closest = EnemySpawnPoints[0];
        float bestDistance = float.MaxValue;
        foreach (Vector3 spawn in EnemySpawnPoints)
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
            float distance = Vector3.Distance(position, item.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = item;
            }
        }

        if (Ally != null && (Ally.IsDown || Ally.IsCaptive))
        {
            float distance = Vector3.Distance(position, Ally.transform.position);
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

    public void AddBattery()
    {
        Batteries = Mathf.Min(3, Batteries + 1);
        ShowMessage("Pila recogida", 2f);
    }

    public void AddBandage()
    {
        Bandages = 1;
        ShowMessage("Venda recogida", 2f);
    }

    public bool UseBattery()
    {
        if (Batteries <= 0)
        {
            ShowMessage("Sin pilas", 2f);
            return false;
        }
        Batteries--;
        return true;
    }

    public bool UseBandage()
    {
        if (Bandages <= 0 || Player == null || Player.Health >= 3) return false;
        Bandages--;
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
        Victory = false;
        Batteries = 2;
        Bandages = 0;
        RunTime = 0f;
        spawnTimer = 10f;
        message = "Encuentra la llave y escapa";
        messageUntil = Time.time + 4f;
        BuildPrototype();
    }

    private void OnGUI()
    {
        EnsureStyles();
        float scale = Mathf.Clamp(Screen.width / 1000f, .72f, 1.35f);
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float w = Screen.width / scale;
        float h = Screen.height / scale;

        GUI.Box(new Rect(18, 16, 310, 118), "");
        GUI.Label(new Rect(32, 25, 290, 32), "SCHOOL ESCAPE: CULT NIGHT", titleStyle);
        GUI.Label(new Rect(32, 55, 280, 74), $"Vida: {Hearts(Player != null ? Player.Health : 0)}   Pilas: {Batteries}/3   Venda: {(Bandages > 0 ? "Sí" : "No")}\nLlave: {(HasKey ? "OBTENIDA" : "pendiente")}   Peligro: x{DangerMultiplier:0.0}", bodyStyle);

        if (Ally != null)
        {
            string allyState = Ally.IsCaptive ? "CAUTIVO - el peligro aumenta" : Ally.IsDown ? "CAÍDO - mantén E para rescatar" : Hearts(Ally.Health);
            GUI.Label(new Rect(32, 136, 520, 30), "Compañero: " + allyState, bodyStyle);
        }

        if (Time.time < messageUntil)
            GUI.Label(new Rect(w * .25f, 20, w * .5f, 46), message, centerStyle);

        GUI.Label(new Rect(20, h - 112, 650, 92), "WASD: mover  |  Espacio: flash automático\nFLASH: clic corto = autoapuntar  |  Mantén y arrastra = apuntar, suelta = disparar\nE: interactuar/canalizar  |  B: usar venda  |  R: reiniciar", bodyStyle);

        GUI.Box(new Rect(w - 155, h - 145, 130, 55), Player != null && Player.IsAimingFlash ? "APUNTANDO..." : "FLASH");
        if (GUI.Button(new Rect(w - 155, h - 82, 130, 55), "INTERACTUAR")) Player?.PulseInteract();
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

    public static Rect FlashButtonScreenRect()
    {
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

public abstract class PrototypeActor : MonoBehaviour
{
    public int Health { get; protected set; } = 3;
    public bool IsDown => Health <= 0;
    public bool IsCaptive { get; protected set; }
    public PrototypeEnemy Dragger { get; private set; }
    public bool CanBeDragged => IsDown && !IsCaptive && Dragger == null;
    protected CharacterController Controller;
    private float nextDamageTime;

    protected virtual void Awake() => Controller = GetComponent<CharacterController>();

    public virtual void Damage(int amount)
    {
        if (Time.time < nextDamageTime || IsDown) return;
        nextDamageTime = Time.time + 1f;
        Health = Mathf.Max(0, Health - amount);
        if (IsDown) OnDowned();
    }

    protected virtual void OnDowned() { }
    public void HealFull() => Health = 3;

    public virtual bool StartDrag(PrototypeEnemy enemy)
    {
        if (!CanBeDragged) return false;
        Dragger = enemy;
        if (Controller != null) Controller.enabled = false;
        return true;
    }

    public void DragTo(Vector3 position)
    {
        transform.position = position + Vector3.back * .8f;
    }

    public virtual void Captured(Vector3 spawnPosition)
    {
        IsCaptive = true;
        Dragger = null;
        transform.position = spawnPosition + Vector3.up * .2f;
    }

    public void ReleaseFromDrag()
    {
        Dragger = null;
        if (Controller != null) Controller.enabled = true;
    }

    public virtual void Rescue()
    {
        IsCaptive = false;
        ReleaseFromDrag();
        Health = 1;
    }

    protected void Move(Vector3 velocity)
    {
        if (Controller != null && Controller.enabled)
            Controller.Move(velocity * Time.deltaTime);
    }
}

public class PrototypePlayer : PrototypeActor
{
    public Vector3 Facing { get; private set; } = Vector3.forward;
    public bool IsAimingFlash => aimingFlash;
    private const float Speed = 5.8f;
    private bool interactPulse;
    private float flashCooldown;
    private PrototypeInteractable currentInteraction;
    private float interactionProgress;
    private int moveFinger = -1;
    private bool aimingFlash;
    private bool flashUsesMouse;
    private bool manualFlashAim;
    private int flashFinger = -1;
    private Vector2 flashPressPosition;
    private Vector2 flashCurrentPosition;
    private GameObject aimIndicator;

    private void Update()
    {
        if (SchoolEscapePrototype.I == null || IsDown || SchoolEscapePrototype.I.Victory) return;

        Vector2 move = ReadMove();
        Vector3 worldMove = new Vector3(move.x, 0f, move.y);
        if (worldMove.sqrMagnitude > 1f) worldMove.Normalize();
        if (worldMove.sqrMagnitude > .02f) Facing = worldMove.normalized;
        Move(worldMove * Speed);

        UpdateFlashButtonInput();
        if (!aimingFlash) ReadAim();
        if (transform.forward != Facing)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(Facing), Time.deltaTime * 14f);

        flashCooldown -= Time.deltaTime;
        bool keyboardFlash = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        if (keyboardFlash) FireAutoFlash();

        if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
            SchoolEscapePrototype.I.UseBandage();

        bool interacting = interactPulse || (Keyboard.current != null && Keyboard.current.eKey.isPressed);
        interactPulse = false;
        UpdateInteraction(interacting);
    }

    public void RequestFlash() => FireAutoFlash();
    public void PulseInteract() => interactPulse = true;

    private Vector2 ReadMove()
    {
        Vector2 value = Vector2.zero;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed) value.x -= 1f;
            if (Keyboard.current.dKey.isPressed) value.x += 1f;
            if (Keyboard.current.sKey.isPressed) value.y -= 1f;
            if (Keyboard.current.wKey.isPressed) value.y += 1f;
        }

        Vector2 center = new Vector2(112f, 108f);
        foreach (Touch touch in Touch.activeTouches)
        {
            Vector2 p = touch.screenPosition;
            if ((moveFinger < 0 && p.x < Screen.width * .42f && p.y < Screen.height * .42f) || touch.finger.index == moveFinger)
            {
                moveFinger = touch.finger.index;
                value = Vector2.ClampMagnitude((p - center) / 65f, 1f);
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended || touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                    moveFinger = -1;
                break;
            }
        }
        if (Touch.activeTouches.Count == 0) moveFinger = -1;
        return value;
    }

    private void ReadAim()
    {
        Camera camera = Camera.main;
        if (camera == null || Mouse.current == null) return;
        Ray ray = camera.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane plane = new Plane(Vector3.up, transform.position);
        if (plane.Raycast(ray, out float enter))
        {
            Vector3 direction = ray.GetPoint(enter) - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > .1f) Facing = direction.normalized;
        }
    }

    private void UpdateFlashButtonInput()
    {
        Rect button = SchoolEscapePrototype.FlashButtonScreenRect();
        Mouse mouse = Mouse.current;

        if (!aimingFlash && mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 position = mouse.position.ReadValue();
            if (button.Contains(ToTopLeft(position))) BeginFlashAim(position, true, -1);
        }

        if (aimingFlash && flashUsesMouse && mouse != null)
        {
            UpdateFlashAim(mouse.position.ReadValue());
            if (mouse.leftButton.wasReleasedThisFrame) EndFlashAim();
            return;
        }

        foreach (Touch touch in Touch.activeTouches)
        {
            if (!aimingFlash && touch.phase == UnityEngine.InputSystem.TouchPhase.Began && button.Contains(ToTopLeft(touch.screenPosition)))
            {
                BeginFlashAim(touch.screenPosition, false, touch.finger.index);
                break;
            }

            if (aimingFlash && !flashUsesMouse && touch.finger.index == flashFinger)
            {
                UpdateFlashAim(touch.screenPosition);
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended || touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                    EndFlashAim();
                break;
            }
        }
    }

    private static Vector2 ToTopLeft(Vector2 bottomLeftPosition) => new Vector2(bottomLeftPosition.x, Screen.height - bottomLeftPosition.y);

    private void BeginFlashAim(Vector2 screenPosition, bool mouse, int finger)
    {
        aimingFlash = true;
        flashUsesMouse = mouse;
        flashFinger = finger;
        flashPressPosition = screenPosition;
        flashCurrentPosition = screenPosition;
        manualFlashAim = false;
    }

    private void UpdateFlashAim(Vector2 screenPosition)
    {
        flashCurrentPosition = screenPosition;
        Vector2 drag = flashCurrentPosition - flashPressPosition;
        if (drag.magnitude < 24f) return;

        manualFlashAim = true;
        Vector3 direction = new Vector3(drag.x, 0f, drag.y).normalized;
        Facing = direction;
        ShowAimIndicator(direction);
    }

    private void EndFlashAim()
    {
        if (manualFlashAim) FireFlash(Facing);
        else FireAutoFlash();

        aimingFlash = false;
        flashUsesMouse = false;
        manualFlashAim = false;
        flashFinger = -1;
        if (aimIndicator != null) Destroy(aimIndicator);
    }

    private void FireAutoFlash()
    {
        Vector3 direction = Facing;
        float nearestDistance = 7f;
        foreach (PrototypeEnemy enemy in FindObjectsByType<PrototypeEnemy>(FindObjectsSortMode.None))
        {
            Vector3 delta = enemy.transform.position - transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                direction = delta.normalized;
            }
        }
        FireFlash(direction);
    }

    private void FireFlash(Vector3 direction)
    {
        if (flashCooldown > 0f || !SchoolEscapePrototype.I.UseBattery()) return;
        flashCooldown = .25f;
        direction.y = 0f;
        if (direction.sqrMagnitude < .01f) direction = Facing;
        Facing = direction.normalized;

        foreach (PrototypeEnemy enemy in FindObjectsByType<PrototypeEnemy>(FindObjectsSortMode.None))
        {
            Vector3 delta = enemy.transform.position - transform.position;
            delta.y = 0f;
            if (delta.magnitude <= 7f && Vector3.Angle(direction, delta) <= 35f)
                enemy.HitByFlash();
        }

        GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Cube);
        flash.name = "Destello";
        flash.transform.position = transform.position + direction * 2.4f + Vector3.up * .15f;
        flash.transform.rotation = Quaternion.LookRotation(direction);
        flash.transform.localScale = new Vector3(2.7f, .08f, 4.6f);
        Destroy(flash.GetComponent<Collider>());
        flash.GetComponent<Renderer>().material = SchoolEscapePrototype.MakeMaterial(new Color(1f, 1f, .7f));
        Destroy(flash, .12f);
    }

    private void ShowAimIndicator(Vector3 direction)
    {
        if (aimIndicator == null)
        {
            aimIndicator = new GameObject("Indicador de cono del flash");
            CreateIndicatorRay(aimIndicator.transform, 0f);
            CreateIndicatorRay(aimIndicator.transform, -35f);
            CreateIndicatorRay(aimIndicator.transform, 35f);
        }
        aimIndicator.transform.position = transform.position;
        aimIndicator.transform.rotation = Quaternion.LookRotation(direction);
    }

    private void CreateIndicatorRay(Transform parent, float angle)
    {
        GameObject ray = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ray.name = "Borde del cono";
        Destroy(ray.GetComponent<Collider>());
        ray.transform.SetParent(parent, false);
        ray.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
        float radians = angle * Mathf.Deg2Rad;
        ray.transform.localPosition = new Vector3(Mathf.Sin(radians) * 2.5f, .12f, Mathf.Cos(radians) * 2.5f);
        ray.transform.localScale = new Vector3(.08f, .04f, 5f);
        ray.GetComponent<Renderer>().material = SchoolEscapePrototype.MakeMaterial(new Color(1f, .82f, .16f));
    }

    private void OnDisable()
    {
        if (aimIndicator != null) Destroy(aimIndicator);
    }

    private void UpdateInteraction(bool pressed)
    {
        PrototypeInteractable closest = SchoolEscapePrototype.I.ClosestInteractable(transform.position, 2.2f);
        if (!pressed || closest == null)
        {
            interactionProgress = 0f;
            currentInteraction = null;
            return;
        }

        if (currentInteraction != closest)
        {
            currentInteraction = closest;
            interactionProgress = 0f;
        }
        interactionProgress += Time.deltaTime;
        SchoolEscapePrototype.I.ShowMessage($"{closest.Prompt} {Mathf.Min(100f, interactionProgress / closest.Duration * 100f):0}%", .15f);
        if (interactionProgress >= closest.Duration)
        {
            closest.Complete(this);
            interactionProgress = 0f;
            currentInteraction = null;
        }
    }

    protected override void OnDowned()
    {
        SchoolEscapePrototype.I.ShowMessage("Has caído. Los cultistas intentarán raptarte.", 4f);
    }

    public override void Captured(Vector3 spawnPosition)
    {
        base.Captured(spawnPosition);
        SchoolEscapePrototype.I.ShowMessage("Te llevaron a un portal de la secta", 999f);
    }
}

public class PrototypeAlly : PrototypeActor
{
    public PrototypeRescue RescueInteraction { get; private set; }
    private Vector3 followOffset = new Vector3(1.7f, 0f, -.7f);

    protected override void Awake()
    {
        base.Awake();
        RescueInteraction = gameObject.AddComponent<PrototypeRescue>();
        RescueInteraction.enabled = false;
    }

    private void Update()
    {
        if (SchoolEscapePrototype.I == null || SchoolEscapePrototype.I.Victory || IsDown || IsCaptive || Dragger != null) return;
        PrototypePlayer player = SchoolEscapePrototype.I.Player;
        if (player == null) return;
        Vector3 target = player.transform.position + followOffset;
        Vector3 delta = target - transform.position;
        delta.y = 0f;
        if (delta.magnitude > 1.2f) Move(delta.normalized * 4.3f);
    }

    protected override void OnDowned()
    {
        RescueInteraction.enabled = true;
        SchoolEscapePrototype.I.ShowMessage("Tu compañero está caído", 3f);
    }

    public override void Captured(Vector3 spawnPosition)
    {
        base.Captured(spawnPosition);
        SchoolEscapePrototype.I.ShowMessage("Compañero cautivo: el peligro aumenta", 4f);
    }

    public override void Rescue()
    {
        base.Rescue();
        RescueInteraction.enabled = false;
        SchoolEscapePrototype.I.ShowMessage("Compañero rescatado", 3f);
    }
}

public class PrototypeEnemy : MonoBehaviour
{
    public bool IsSmall;
    private CharacterController controller;
    private float stunnedUntil;
    private float nextHit;
    private PrototypeActor dragTarget;

    private void Awake() => controller = GetComponent<CharacterController>();

    private void Update()
    {
        if (SchoolEscapePrototype.I == null || SchoolEscapePrototype.I.Victory || Time.time < stunnedUntil) return;
        PrototypePlayer player = SchoolEscapePrototype.I.Player;
        PrototypeAlly ally = SchoolEscapePrototype.I.Ally;
        if (player == null || ally == null) return;

        if (dragTarget != null)
        {
            Vector3 destination = SchoolEscapePrototype.ClosestEnemySpawn(transform.position);
            Vector3 delta = destination - transform.position;
            delta.y = 0f;
            float draggingSpeed = (IsSmall ? 3.35f : 2.55f) * .5f;
            controller.Move(delta.normalized * draggingSpeed * Time.deltaTime);
            dragTarget.DragTo(transform.position);
            if (delta.magnitude < 1f)
            {
                PrototypeActor captured = dragTarget;
                dragTarget = null;
                captured.Captured(destination);
            }
            return;
        }

        PrototypeActor target = ChooseTarget(player, ally);
        if (target == null) return;
        Vector3 direction = target.transform.position - transform.position;
        direction.y = 0f;
        float speed = IsSmall ? 3.35f : 2.55f;
        controller.Move(direction.normalized * speed * Time.deltaTime);
        if (direction.sqrMagnitude > .1f) transform.rotation = Quaternion.LookRotation(direction);

        if (direction.magnitude < 1.05f && Time.time > nextHit)
        {
            nextHit = Time.time + 1.25f;
            if (target.IsDown && target.StartDrag(this))
                dragTarget = target;
            else
                target.Damage(1);
        }
    }

    private PrototypeActor ChooseTarget(PrototypePlayer player, PrototypeAlly ally)
    {
        PrototypeActor downedPlayer = player.CanBeDragged ? player : null;
        PrototypeActor downedAlly = ally.CanBeDragged ? ally : null;
        if (downedPlayer != null && downedAlly != null)
            return Vector3.Distance(transform.position, player.transform.position) <= Vector3.Distance(transform.position, ally.transform.position) ? player : ally;
        if (downedPlayer != null) return downedPlayer;
        if (downedAlly != null) return downedAlly;

        bool playerAvailable = !player.IsDown && !player.IsCaptive;
        bool allyAvailable = !ally.IsDown && !ally.IsCaptive;
        if (!playerAvailable) return allyAvailable ? ally : null;
        if (!allyAvailable) return player;
        return Vector3.Distance(transform.position, player.transform.position) <= Vector3.Distance(transform.position, ally.transform.position) ? player : ally;
    }

    public void HitByFlash()
    {
        if (dragTarget != null)
        {
            dragTarget.ReleaseFromDrag();
            dragTarget = null;
        }
        if (IsSmall)
        {
            SchoolEscapePrototype.I.UnregisterEnemy(this);
            Destroy(gameObject);
        }
        else
        {
            stunnedUntil = Time.time + 1.2f;
            SchoolEscapePrototype.I.ShowMessage("Cultista aturdido 1.2 s", 1.2f);
        }
    }
}

public abstract class PrototypeInteractable : MonoBehaviour
{
    public virtual bool Available => enabled && gameObject.activeInHierarchy;
    public abstract float Duration { get; }
    public abstract string Prompt { get; }
    public abstract void Complete(PrototypePlayer player);
}

public class PrototypeLocker : PrototypeInteractable
{
    private bool searched;
    public override bool Available => base.Available && !searched;
    public override float Duration => 1.5f;
    public override string Prompt => "Registrando taquilla";

    public override void Complete(PrototypePlayer player)
    {
        if (searched) return;
        searched = true;
        GetComponent<Renderer>().material.color = new Color(.09f, .12f, .13f);
        if (Random.value < .67f) SchoolEscapePrototype.I.AddBattery();
        else SchoolEscapePrototype.I.AddBandage();
    }
}

public class PrototypeKey : PrototypeInteractable
{
    public override float Duration => 1.5f;
    public override string Prompt => "Recogiendo llave";
    public override void Complete(PrototypePlayer player)
    {
        SchoolEscapePrototype.I.CollectKey();
        gameObject.SetActive(false);
    }
}

public class PrototypeDoor : PrototypeInteractable
{
    public override float Duration => .25f;
    public override string Prompt => SchoolEscapePrototype.I.HasKey ? "Abriendo salida" : "Puerta bloqueada";
    public override void Complete(PrototypePlayer player)
    {
        if (!SchoolEscapePrototype.I.HasKey)
        {
            SchoolEscapePrototype.I.ShowMessage("Necesitas encontrar la llave", 2f);
            return;
        }
        GetComponent<Renderer>().material.color = new Color(.08f, .62f, .22f);
        SchoolEscapePrototype.I.Win();
    }
}

public class PrototypeRescue : PrototypeInteractable
{
    public override bool Available
    {
        get
        {
            PrototypeAlly ally = GetComponent<PrototypeAlly>();
            return ally != null && (ally.IsDown || ally.IsCaptive);
        }
    }
    public override float Duration => 2.5f;
    public override string Prompt => "Rescatando compañero";
    public override void Complete(PrototypePlayer player) => GetComponent<PrototypeAlly>().Rescue();
}

public class PrototypeRitual : MonoBehaviour
{
    private void Update() => transform.Rotate(0f, 25f * Time.deltaTime, 0f);
}

public class PrototypeCamera : MonoBehaviour
{
    private Vector3 velocity;
    private void LateUpdate()
    {
        if (SchoolEscapePrototype.I == null || SchoolEscapePrototype.I.Player == null) return;
        Vector3 target = SchoolEscapePrototype.I.Player.transform.position + new Vector3(0f, 17f, -10f);
        transform.position = Vector3.SmoothDamp(transform.position, target, ref velocity, .18f);
        transform.rotation = Quaternion.Euler(59f, 0f, 0f);
    }
}

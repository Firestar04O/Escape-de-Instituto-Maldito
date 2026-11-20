using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace SchoolEscape.Multiplayer
{
    [RequireComponent(typeof(NetworkObject), typeof(PrototypeAlly))]
    public sealed class NetworkClientAllyBridge : NetworkBehaviour
    {
        private const float Speed = 5.8f;
        private const float TickDelta = 1f / 30f;
        private const int InputHistorySize = 256;
        private const int MaximumServerCatchUpTicks = 8;
        private const float ReconciliationThreshold = .08f;
        private const float VisualCorrectionSharpness = 14f;
        private const float FlashAimThreshold = 48f;
        private const float FlashCancelRadius = 32f;
        private const uint MaximumAcceptedTickAdvance = 12;
        private const float MaximumInputMagnitude = 1.05f;
        private const float BandageCooldown = .5f;
        private const float FlashCooldown = .25f;
        private const double MaximumFlashRewindSeconds = .75d;
        private const double FlashFutureToleranceSeconds = .1d;

        public static NetworkClientAllyBridge LocalOwner { get; private set; }

        private readonly NetworkVariable<int> syncedHealth = new NetworkVariable<int>(3);
        private readonly NetworkVariable<int> syncedBatteries = new NetworkVariable<int>();
        private readonly NetworkVariable<int> syncedBandages = new NetworkVariable<int>();
        private readonly NetworkVariable<float> syncedDanger = new NetworkVariable<float>(1f);
        private readonly NetworkVariable<float> syncedRunTime = new NetworkVariable<float>();
        private readonly NetworkVariable<bool> syncedVictory = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> syncedDefeat = new NetworkVariable<bool>();
        private readonly NetworkVariable<int> syncedMatchResult = new NetworkVariable<int>();
        private readonly NetworkVariable<bool> syncedCaptive = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> syncedEscaped = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> syncedVisible = new NetworkVariable<bool>(true);
        private readonly NetworkVariable<int> syncedRoomIndex = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<bool> syncedRescueActive = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> syncedRescueOpen = new NetworkVariable<bool>();
        private readonly NetworkVariable<FixedString64Bytes> syncedRoom = new NetworkVariable<FixedString64Bytes>();
        private readonly NetworkVariable<FixedString64Bytes> syncedExit = new NetworkVariable<FixedString64Bytes>();
        private readonly NetworkVariable<FixedString128Bytes> syncedAnnouncement = new NetworkVariable<FixedString128Bytes>();
        private readonly NetworkVariable<FixedString128Bytes> syncedPersonalAnnouncement = new NetworkVariable<FixedString128Bytes>(
            default, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString128Bytes> syncedInteraction = new NetworkVariable<FixedString128Bytes>(
            default, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector3> syncedAuthoritativePosition = new NetworkVariable<Vector3>(
            default, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<uint> syncedLastProcessedTick = new NetworkVariable<uint>(
            default, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        private struct PredictedInput
        {
            public uint Tick;
            public Vector2 Move;
            public Vector3 PredictedPosition;
            public bool Valid;
        }

        private PrototypeAlly ally;
        private SchoolEscapePrototype session;
        private PrototypeInteractable currentInteraction;
        private readonly PredictedInput[] inputHistory = new PredictedInput[InputHistorySize];
        private float interactionProgress;
        private float nextFlashTime;
        private float nextBandageTime;
        private uint localInputTick;
        private uint lastServerProcessedTick;
        private uint lastReconciledTick;
        private int correctionCount;
        private float lastCorrectionDistance;
        private float maximumCorrectionDistance;
        private int moveFinger = -1;
        private bool uiInteractHeld;
        private bool serverInteractHeld;
        private bool aimingFlash;
        private bool flashUsesMouse;
        private bool manualFlashAim;
        private bool flashWasDragged;
        private int flashFinger = -1;
        private Vector2 flashPressPosition;
        private Vector3 flashAimDirection = Vector3.forward;
        private GameObject aimIndicator;
        private Transform predictedPresentation;
        private Renderer rootRenderer;
        private Renderer predictedRenderer;
        private NetworkTransform networkTransform;

        public int DisplayHealth => syncedHealth.Value;
        public int DisplayBatteries => syncedBatteries.Value;
        public int DisplayBandages => syncedBandages.Value;
        public float DisplayDanger => syncedDanger.Value;
        public float DisplayRunTime => syncedRunTime.Value;
        public bool DisplayVictory => syncedVictory.Value;
        public bool DisplayDefeat => syncedDefeat.Value;
        public bool DisplayCaptive => syncedCaptive.Value;
        public PrototypeMatchResult DisplayMatchResult => (PrototypeMatchResult)syncedMatchResult.Value;
        public string DisplayRoom => syncedRoom.Value.IsEmpty ? "Sala" : syncedRoom.Value.ToString();
        public string DisplayExit => syncedExit.Value.IsEmpty ? "bloqueada" : syncedExit.Value.ToString();
        public string DisplayAnnouncement => !syncedPersonalAnnouncement.Value.IsEmpty
            ? syncedPersonalAnnouncement.Value.ToString()
            : syncedAnnouncement.Value.IsEmpty ? string.Empty : syncedAnnouncement.Value.ToString();
        public string InteractionStatus => syncedInteraction.Value.IsEmpty ? "Sin objetivo cercano" : syncedInteraction.Value.ToString();
        public uint PredictionTick => localInputTick;
        public uint LastAcknowledgedTick => syncedLastProcessedTick.Value;
        public int PendingPredictionCount => syncedLastProcessedTick.Value >= localInputTick
            ? 0
            : localInputTick - syncedLastProcessedTick.Value > InputHistorySize
                ? InputHistorySize
                : (int)(localInputTick - syncedLastProcessedTick.Value);
        public int CorrectionCount => correctionCount;
        public float LastCorrectionDistance => lastCorrectionDistance;
        public float MaximumCorrectionDistance => maximumCorrectionDistance;

        public override void OnNetworkSpawn()
        {
            ally = GetComponent<PrototypeAlly>();
            session = FindFirstObjectByType<SchoolEscapePrototype>();
            ally.enabled = false;
            networkTransform = GetComponent<NetworkTransform>();
            if (IsServer)
                syncedAuthoritativePosition.Value = transform.position;
            if (IsOwner && IsClient)
            {
                LocalOwner = this;
                if (!IsServer && networkTransform != null) networkTransform.enabled = false;
                CreatePredictedPresentation();
            }
            if (session != null) session.AttachNetworkClientAlly(ally, IsOwner);
            if (predictedPresentation != null) session?.SetNetworkCameraTarget(predictedPresentation);
            if (NetworkManager != null)
                NetworkManager.NetworkTickSystem.Tick += OnNetworkTick;
        }

        public override void OnNetworkDespawn()
        {
            if (LocalOwner == this) LocalOwner = null;
            if (NetworkManager != null)
                NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;
            if (aimIndicator != null) Destroy(aimIndicator);
        }

        private void Update()
        {
            if (IsOwner && IsClient)
            {
                UpdateFlashButtonInput();
                if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
                    RequestBandage();
                if (!aimingFlash && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                    RequestFlash();

                ally?.ApplyReplicatedState(
                    syncedHealth.Value,
                    syncedCaptive.Value,
                    syncedEscaped.Value,
                    syncedVisible.Value);
                session?.ApplyNetworkRoomState(
                    syncedRoomIndex.Value,
                    syncedRescueActive.Value,
                    syncedRescueOpen.Value);
                ApplyPresentationVisibility();
                ReconcileIfNeeded();
                SmoothVisualCorrection();
            }

            if (!IsServer || ally == null || session == null) return;

            SyncHudState();
            if (session.IsRoomTransitioning)
            {
                UpdateServerInteraction(false);
                return;
            }
            if (ally.IsDown || ally.IsCaptive || ally.Dragger != null)
            {
                UpdateServerInteraction(false);
                return;
            }
            UpdateServerInteraction(serverInteractHeld);
        }

        private void OnNetworkTick()
        {
            if (!IsOwner || !IsClient || ally == null) return;

            bool keyboardInteract = Keyboard.current != null && Keyboard.current.eKey.isPressed;
            Vector2 input = Vector2.ClampMagnitude(ReadMove(), 1f);
            uint tick = ++localInputTick;
            SimulateMovement(input);

            int slot = (int)(tick % InputHistorySize);
            inputHistory[slot] = new PredictedInput
            {
                Tick = tick,
                Move = input,
                PredictedPosition = ally.transform.position,
                Valid = true
            };
            SubmitInputRpc(tick, input, uiInteractHeld || keyboardInteract);
        }

        public void SetInteractHeld(bool held) => uiInteractHeld = held;

        public void RequestBandage()
        {
            if (IsOwner && IsClient) UseBandageRpc();
        }

        public void RequestFlash()
        {
            if (IsOwner && IsClient) SendFlashRequest(Vector3.zero);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        private void SubmitInputRpc(uint tick, Vector2 input, bool interactHeld)
        {
            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.Instance;
            if (!IsFinite(input) || input.sqrMagnitude > MaximumInputMagnitude * MaximumInputMagnitude)
            {
                bootstrap?.ReportSecurityViolation(OwnerClientId, true,
                    $"input imposible tick={tick}, magnitud={input.magnitude:0.00}", true);
                return;
            }
            if (tick <= lastServerProcessedTick)
            {
                bootstrap?.ReportSecurityViolation(OwnerClientId, true,
                    $"tick repetido/atrasado {tick} <= {lastServerProcessedTick}", false);
                return;
            }

            uint missingTicks = tick - lastServerProcessedTick;
            if (missingTicks > MaximumAcceptedTickAdvance)
            {
                bootstrap?.ReportSecurityViolation(OwnerClientId, true,
                    $"tick futuro {tick}; salto solicitado={missingTicks}, máximo={MaximumAcceptedTickAdvance}", true);
                return;
            }

            serverInteractHeld = interactHeld;
            Vector2 clampedInput = Vector2.ClampMagnitude(input, 1f);
            int simulatedTicks = missingTicks > MaximumServerCatchUpTicks
                ? MaximumServerCatchUpTicks
                : (int)missingTicks;
            Vector3 positionBeforeSimulation = ally.transform.position;
            for (int index = 0; index < simulatedTicks; index++)
            {
                SimulateMovement(clampedInput);
            }

            float maximumDisplacement = Speed * TickDelta * simulatedTicks + .05f;
            float displacement = Vector3.Distance(positionBeforeSimulation, ally.transform.position);
            if (displacement > maximumDisplacement)
            {
                ally.SetNetworkPosition(positionBeforeSimulation);
                bootstrap?.ReportSecurityViolation(OwnerClientId, true,
                    $"desplazamiento imposible {displacement:0.000} m > {maximumDisplacement:0.000} m", true);
                return;
            }
            lastServerProcessedTick = tick;

            syncedAuthoritativePosition.Value = ally.transform.position;
            syncedLastProcessedTick.Value = lastServerProcessedTick;
        }

        private void SimulateMovement(Vector2 input)
        {
            if (ally == null || session == null || !ally.CanAct || session.IsRoomTransitioning) return;
            Vector3 direction = new Vector3(input.x, 0f, input.y);
            if (direction.sqrMagnitude > 1f) direction.Normalize();
            ally.ApplyNetworkMovement(direction * Speed, TickDelta);
        }

        private void ReconcileIfNeeded()
        {
            uint acknowledgedTick = syncedLastProcessedTick.Value;
            if (acknowledgedTick == 0 || acknowledgedTick == lastReconciledTick) return;
            lastReconciledTick = acknowledgedTick;

            int acknowledgedSlot = (int)(acknowledgedTick % InputHistorySize);
            PredictedInput acknowledged = inputHistory[acknowledgedSlot];
            if (!acknowledged.Valid || acknowledged.Tick != acknowledgedTick) return;

            float error = Vector3.Distance(acknowledged.PredictedPosition, syncedAuthoritativePosition.Value);
            lastCorrectionDistance = error;
            maximumCorrectionDistance = Mathf.Max(maximumCorrectionDistance, error);

            uint oldestStoredTick = localInputTick >= InputHistorySize ? localInputTick - InputHistorySize + 1 : 1;
            uint newestConfirmedTick = acknowledgedTick < localInputTick ? acknowledgedTick : localInputTick;
            for (uint tick = oldestStoredTick; tick <= newestConfirmedTick; tick++)
            {
                int slot = (int)(tick % InputHistorySize);
                if (inputHistory[slot].Tick == tick) inputHistory[slot].Valid = false;
            }

            if (error <= ReconciliationThreshold) return;

            correctionCount++;
            Vector3 oldVisualPosition = predictedPresentation != null
                ? predictedPresentation.position
                : ally.transform.position;
            ally.SetNetworkPosition(syncedAuthoritativePosition.Value);

            uint firstPendingTick = acknowledgedTick + 1;
            if (firstPendingTick < oldestStoredTick) firstPendingTick = oldestStoredTick;
            for (uint tick = firstPendingTick; tick <= localInputTick; tick++)
            {
                int slot = (int)(tick % InputHistorySize);
                PredictedInput pending = inputHistory[slot];
                if (!pending.Valid || pending.Tick != tick) continue;
                SimulateMovement(pending.Move);
                pending.PredictedPosition = ally.transform.position;
                inputHistory[slot] = pending;
            }

            if (predictedPresentation != null) predictedPresentation.position = oldVisualPosition;
        }

        private void CreatePredictedPresentation()
        {
            MeshFilter sourceFilter = GetComponent<MeshFilter>();
            rootRenderer = GetComponent<Renderer>();
            if (sourceFilter == null || rootRenderer == null) return;

            GameObject presentation = new GameObject("Predicted Presentation");
            presentation.transform.SetParent(transform, false);
            MeshFilter targetFilter = presentation.AddComponent<MeshFilter>();
            targetFilter.sharedMesh = sourceFilter.sharedMesh;
            MeshRenderer targetRenderer = presentation.AddComponent<MeshRenderer>();
            targetRenderer.sharedMaterials = rootRenderer.sharedMaterials;
            targetRenderer.shadowCastingMode = rootRenderer.shadowCastingMode;
            targetRenderer.receiveShadows = rootRenderer.receiveShadows;
            predictedPresentation = presentation.transform;
            predictedRenderer = targetRenderer;
            rootRenderer.enabled = false;
        }

        private void ApplyPresentationVisibility()
        {
            if (predictedRenderer != null) predictedRenderer.enabled = syncedVisible.Value;
            if (rootRenderer != null) rootRenderer.enabled = false;
        }

        private void SmoothVisualCorrection()
        {
            if (predictedPresentation == null) return;
            float blend = 1f - Mathf.Exp(-VisualCorrectionSharpness * Time.unscaledDeltaTime);
            predictedPresentation.localPosition = Vector3.Lerp(predictedPresentation.localPosition, Vector3.zero, blend);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UseBandageRpc()
        {
            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.Instance;
            if (session == null || ally == null || !ally.CanAct || session.Bandages <= 0 || ally.Health >= 3)
            {
                bootstrap?.ReportSecurityViolation(OwnerClientId, false,
                    "venda rechazada por estado, inventario o vida", false);
                return;
            }
            if (Time.unscaledTime < nextBandageTime)
            {
                bootstrap?.ReportSecurityViolation(OwnerClientId, false,
                    "venda solicitada antes del cooldown del servidor", true);
                return;
            }

            nextBandageTime = Time.unscaledTime + BandageCooldown;
            session.UseBandageFor(ally);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UseFlashRpc(Vector3 requestedDirection, double observedServerTime)
        {
            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.Instance;
            if (bootstrap == null) return;
            if (!IsFinite(requestedDirection) || requestedDirection.sqrMagnitude > MaximumInputMagnitude * MaximumInputMagnitude)
            {
                bootstrap.ReportSecurityViolation(OwnerClientId, false,
                    "flash rechazado por dirección imposible", true);
                return;
            }
            if (session == null || ally == null || !ally.CanAct)
            {
                bootstrap.ReportSecurityViolation(OwnerClientId, false,
                    "flash rechazado por estado del jugador", false);
                return;
            }
            if (Time.unscaledTime < nextFlashTime)
            {
                bootstrap.ReportSecurityViolation(OwnerClientId, false,
                    "flash solicitado antes del cooldown del servidor", true);
                return;
            }

            double serverTime = NetworkManager.ServerTime.Time;
            double requestAge = serverTime - observedServerTime;
            if (double.IsNaN(observedServerTime) || double.IsInfinity(observedServerTime) ||
                requestAge < -FlashFutureToleranceSeconds || requestAge > MaximumFlashRewindSeconds)
            {
                bootstrap.ReportSecurityViolation(OwnerClientId, false,
                    $"flash rechazado por tiempo inválido ({requestAge * 1000d:0} ms)", true);
                return;
            }

            nextFlashTime = Time.unscaledTime + FlashCooldown;
            session.FireCompensatedFlashFor(ally, requestedDirection, bootstrap.RewindHistory, observedServerTime, serverTime);
        }

        private static bool IsFinite(Vector2 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y);

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private void SendFlashRequest(Vector3 requestedDirection)
        {
            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.Instance;
            if (bootstrap == null) return;
            Vector3 direction = requestedDirection;
            if (direction.sqrMagnitude < .01f)
                bootstrap.TryGetClosestRemoteEnemyDirection(ally.transform.position, 7f, out direction);
            if (direction.sqrMagnitude < .01f) direction = ally.transform.forward;
            UseFlashRpc(direction.normalized, bootstrap.ObservedServerTime);
        }

        private void UpdateServerInteraction(bool pressed)
        {
            PrototypeInteractable closest = session.ClosestInteractable(ally.transform.position, 2.8f);
            if (closest == null)
            {
                interactionProgress = 0f;
                currentInteraction = null;
                SetInteractionStatus(pressed ? "Botón detectado, sin objeto cercano" : "Sin objetivo cercano");
                if (pressed) session.ShowPersonalMessage(ally, "Botón detectado, sin objeto cercano", .2f);
                return;
            }

            if (!pressed)
            {
                interactionProgress = 0f;
                currentInteraction = null;
                SetInteractionStatus($"Cerca: {closest.Prompt}");
                return;
            }

            if (currentInteraction != closest)
            {
                currentInteraction = closest;
                interactionProgress = 0f;
            }

            interactionProgress += Time.deltaTime;
            float percent = Mathf.Min(100f, interactionProgress / closest.Duration * 100f);
            string progressText = $"{closest.Prompt} {percent:0}%";
            SetInteractionStatus(progressText);
            session.ShowPersonalMessage(ally, progressText, .2f);
            if (interactionProgress < closest.Duration) return;

            closest.Complete(ally);
            interactionProgress = 0f;
            currentInteraction = null;
        }

        private void SyncHudState()
        {
            syncedHealth.Value = ally != null ? ally.Health : 0;
            syncedBatteries.Value = session.Batteries;
            syncedBandages.Value = session.Bandages;
            syncedDanger.Value = session.DangerMultiplier;
            syncedRunTime.Value = session.RunTime;
            syncedVictory.Value = session.Victory;
            syncedDefeat.Value = session.Defeat;
            syncedMatchResult.Value = (int)session.MatchResult;
            syncedCaptive.Value = ally != null && ally.IsCaptive;
            syncedEscaped.Value = ally != null && ally.IsEscaped;
            syncedVisible.Value = ally != null && ally.PresentationVisible;
            syncedRoomIndex.Value = session.CurrentRoomIndex;
            syncedRescueActive.Value = session.HasActiveRescueRoom;
            syncedRescueOpen.Value = session.IsRescueEntranceOpen;
            SetFixed(syncedRoom, session.RoomProgressText);
            SetFixed(syncedExit, session.ExitStatusText);
            SetFixed(syncedAnnouncement, session.CurrentAnnouncementText);
            SetFixed(syncedPersonalAnnouncement, session.CurrentPersonalAnnouncementFor(ally));
        }

        private void SetInteractionStatus(string value)
        {
            FixedString128Bytes next = new FixedString128Bytes(value);
            if (!syncedInteraction.Value.Equals(next)) syncedInteraction.Value = next;
        }

        private static void SetFixed(NetworkVariable<FixedString64Bytes> target, string value)
        {
            FixedString64Bytes next = new FixedString64Bytes(value);
            if (!target.Value.Equals(next)) target.Value = next;
        }

        private static void SetFixed(NetworkVariable<FixedString128Bytes> target, string value)
        {
            FixedString128Bytes next = new FixedString128Bytes(value);
            if (!target.Value.Equals(next)) target.Value = next;
        }

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

            Vector2 center = session != null && session.MobileHud != null
                ? session.MobileHud.MovementCenterScreen
                : new Vector2(112f, 108f);
            float radius = session != null && session.MobileHud != null
                ? session.MobileHud.MovementRadiusScreen
                : 65f;

            if (EnhancedTouchSupport.enabled)
            {
                foreach (Touch touch in Touch.activeTouches)
                {
                    Vector2 point = touch.screenPosition;
                    if ((moveFinger < 0 && point.x < Screen.width * .42f && point.y < Screen.height * .42f) || touch.finger.index == moveFinger)
                    {
                        moveFinger = touch.finger.index;
                        value = Vector2.ClampMagnitude((point - center) / radius, 1f);
                        if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended || touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                            moveFinger = -1;
                        break;
                    }
                }
                if (Touch.activeTouches.Count == 0) moveFinger = -1;
            }

            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                Vector2 point = Mouse.current.position.ReadValue();
                if (point.x < Screen.width * .42f && point.y < Screen.height * .42f)
                    value = Vector2.ClampMagnitude((point - center) / radius, 1f);
            }

            return value;
        }

        private void UpdateFlashButtonInput()
        {
            if (session == null || ally == null) return;
            Rect button = session.FlashButtonScreenRect();
            Mouse mouse = Mouse.current;

            if (!aimingFlash && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 point = mouse.position.ReadValue();
                if (button.Contains(ToTopLeft(point)))
                    BeginFlashAim(point, true, -1);
            }

            if (aimingFlash && flashUsesMouse && mouse != null)
            {
                UpdateFlashAim(mouse.position.ReadValue());
                if (mouse.leftButton.wasReleasedThisFrame) EndFlashAim();
                return;
            }

            if (!EnhancedTouchSupport.enabled) return;
            foreach (Touch touch in Touch.activeTouches)
            {
                if (!aimingFlash && touch.phase == UnityEngine.InputSystem.TouchPhase.Began &&
                    button.Contains(ToTopLeft(touch.screenPosition)))
                {
                    BeginFlashAim(touch.screenPosition, false, touch.finger.index);
                    break;
                }

                if (aimingFlash && !flashUsesMouse && touch.finger.index == flashFinger)
                {
                    UpdateFlashAim(touch.screenPosition);
                    if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                        touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                        EndFlashAim();
                    break;
                }
            }
        }

        private static Vector2 ToTopLeft(Vector2 bottomLeftPosition) =>
            new Vector2(bottomLeftPosition.x, Screen.height - bottomLeftPosition.y);

        private void BeginFlashAim(Vector2 screenPosition, bool mouse, int finger)
        {
            aimingFlash = true;
            flashUsesMouse = mouse;
            flashFinger = finger;
            flashPressPosition = screenPosition;
            flashAimDirection = ally != null ? ally.transform.forward : Vector3.forward;
            manualFlashAim = false;
            flashWasDragged = false;
        }

        private void UpdateFlashAim(Vector2 screenPosition)
        {
            Vector2 drag = screenPosition - flashPressPosition;
            float distance = drag.magnitude;
            if (distance >= FlashAimThreshold) flashWasDragged = true;

            if (flashWasDragged && distance <= FlashCancelRadius)
            {
                manualFlashAim = false;
                if (aimIndicator != null) aimIndicator.SetActive(false);
                return;
            }
            if (!flashWasDragged) return;

            manualFlashAim = true;
            flashAimDirection = new Vector3(drag.x, 0f, drag.y).normalized;
            ShowAimIndicator(flashAimDirection);
        }

        private void EndFlashAim()
        {
            if (manualFlashAim)
                SendFlashRequest(flashAimDirection);
            else if (!flashWasDragged)
                RequestFlash();

            aimingFlash = false;
            flashUsesMouse = false;
            manualFlashAim = false;
            flashWasDragged = false;
            flashFinger = -1;
            if (aimIndicator != null) Destroy(aimIndicator);
        }

        private void ShowAimIndicator(Vector3 direction)
        {
            if (aimIndicator == null)
            {
                aimIndicator = new GameObject("Indicador de cono del flash (cliente)");
                CreateIndicatorRay(aimIndicator.transform, 0f);
                CreateIndicatorRay(aimIndicator.transform, -35f);
                CreateIndicatorRay(aimIndicator.transform, 35f);
            }

            aimIndicator.SetActive(true);
            aimIndicator.transform.position = ally.transform.position;
            aimIndicator.transform.rotation = Quaternion.LookRotation(direction);
        }

        private static void CreateIndicatorRay(Transform parent, float angle)
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
    }
}

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace SchoolEscape.UI
{
    [DisallowMultipleComponent]
    public sealed class MobileHudController : MonoBehaviour
    {
        [SerializeField] private SchoolEscapePrototype session;
        [SerializeField] private RectTransform movementJoystick;
        [SerializeField] private RectTransform flashButton;
        [SerializeField] private MobileHoldButton interactButton;
        [SerializeField] private Button bandageButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Text allyText;
        private Button boundBandageButton;
        private Button boundFlashButton;

        public Vector2 MovementCenterScreen
        {
            get
            {
                if (movementJoystick == null) return new Vector2(112f, 108f);
                return RectTransformUtility.WorldToScreenPoint(null, movementJoystick.position);
            }
        }

        public float MovementRadiusScreen
        {
            get
            {
                if (movementJoystick == null) return 65f;
                Vector3[] corners = new Vector3[4];
                movementJoystick.GetWorldCorners(corners);
                Vector2 left = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
                Vector2 right = RectTransformUtility.WorldToScreenPoint(null, corners[3]);
                return Mathf.Max(24f, Vector2.Distance(left, right) * .42f);
            }
        }

        public void Configure(
            SchoolEscapePrototype gameSession,
            RectTransform joystick,
            RectTransform flash,
            MobileHoldButton interact,
            Button bandage,
            Text status,
            Text ally)
        {
            session = gameSession;
            movementJoystick = joystick;
            flashButton = flash;
            interactButton = interact;
            bandageButton = bandage;
            statusText = status;
            allyText = ally;
            BindButtons();
        }

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SchoolEscapePrototype>();
            BindButtons();
        }

        private void OnDestroy()
        {
            if (boundBandageButton != null) boundBandageButton.onClick.RemoveListener(UseBandage);
            if (boundFlashButton != null) boundFlashButton.onClick.RemoveListener(UseFlash);
        }

        private void Update()
        {
            if (session == null) session = FindFirstObjectByType<SchoolEscapePrototype>();
            if (session == null) return;

            session.SetLocalInteractHeld(IsInteractHeld());

            if (statusText != null)
            {
                statusText.text = $"{session.DisplayRoomProgressText}   Vida {session.DisplayHealth}/3   Pilas {session.DisplayBatteries}/3   Venda {(session.DisplayBandages > 0 ? "Sí" : "No")}\n" +
                                  $"Salida {session.DisplayExitStatusText}   Peligro x{session.DisplayDangerMultiplier:0.0}\n" +
                                  session.EnemyCounterText +
                                  $"\n{session.NetworkAoiStatusText}" +
                                  $"\n{session.DisplayInteractionStatus}";
            }

            if (allyText != null && session.Ally != null)
            {
                string state = session.Ally.IsCaptive ? "RAPTADO" : session.Ally.IsDown ? "VULNERABLE" : $"{session.Ally.Health}/3";
                allyText.text = "Compañero " + state;
            }

            if (bandageButton != null)
                bandageButton.interactable = session.DisplayBandages > 0 && session.DisplayHealth < 3;
        }

        public Rect GetFlashButtonScreenRect()
        {
            if (flashButton == null) return new Rect(Screen.width - 155f, 90f, 130f, 55f);
            Vector3[] corners = new Vector3[4];
            flashButton.GetWorldCorners(corners);
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            return Rect.MinMaxRect(bottomLeft.x, Screen.height - topRight.y, topRight.x, Screen.height - bottomLeft.y);
        }

        private void UseBandage() => session?.UseBandage();
        private void UseFlash() => session?.RequestLocalFlash();

        private void BindButtons()
        {
            if (boundBandageButton != bandageButton)
            {
                if (boundBandageButton != null) boundBandageButton.onClick.RemoveListener(UseBandage);
                boundBandageButton = bandageButton;
                if (boundBandageButton != null) boundBandageButton.onClick.AddListener(UseBandage);
            }

            Button flash = flashButton != null ? flashButton.GetComponent<Button>() : null;
            if (boundFlashButton == flash) return;
            if (boundFlashButton != null) boundFlashButton.onClick.RemoveListener(UseFlash);
            boundFlashButton = flash;
            if (boundFlashButton != null) boundFlashButton.onClick.AddListener(UseFlash);
        }

        private bool IsInteractHeld()
        {
            if (interactButton == null) return false;
            if (interactButton.IsHeld) return true;

            RectTransform rectTransform = (RectTransform)interactButton.transform;
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            Rect screenRect = Rect.MinMaxRect(bottomLeft.x, bottomLeft.y, topRight.x, topRight.y);

            if (EnhancedTouchSupport.enabled)
            foreach (Touch touch in Touch.activeTouches)
            {
                if (touch.phase != UnityEngine.InputSystem.TouchPhase.Ended &&
                    touch.phase != UnityEngine.InputSystem.TouchPhase.Canceled &&
                    screenRect.Contains(touch.screenPosition))
                    return true;
            }

            return Mouse.current != null &&
                   Mouse.current.leftButton.isPressed &&
                   screenRect.Contains(Mouse.current.position.ReadValue());
        }
    }
}

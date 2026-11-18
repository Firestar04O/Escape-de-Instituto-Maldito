using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class PrototypePlayer : PrototypeActor
{
    public Vector3 Facing { get; private set; } = Vector3.forward;
    public bool IsAimingFlash => aimingFlash;
    public string InteractionStatus { get; private set; } = "Sin objetivo cercano";
    private const float Speed = 5.8f;
    private const float MovementDeadZone = .08f;
    private bool interactPulse;
    private float flashCooldown;
    private PrototypeInteractable currentInteraction;
    private float interactionProgress;
    private int moveFinger = -1;
    private bool aimingFlash;
    private bool flashUsesMouse;
    private bool manualFlashAim;
    private bool flashWasDragged;
    private int flashFinger = -1;
    private Vector2 flashPressPosition;
    private Vector2 flashCurrentPosition;
    private Vector3 flashAimDirection = Vector3.forward;
    private GameObject aimIndicator;

    private const float FlashAimThreshold = 48f;
    private const float FlashCancelRadius = 32f;

    private void Update()
    {
        if (Session == null || IsDown || Session.Victory) return;

        Vector2 move = ReadMove();
        Vector3 worldMove = new Vector3(move.x, 0f, move.y);
        if (worldMove.sqrMagnitude > MovementDeadZone * MovementDeadZone)
        {
            worldMove.Normalize();
            Facing = worldMove;
        }
        else
        {
            worldMove = Vector3.zero;
        }
        Move(worldMove * Speed);

        UpdateFlashButtonInput();
        if (!aimingFlash) ReadAim();
        if (aimingFlash && manualFlashAim)
            transform.rotation = Quaternion.LookRotation(Facing);
        else if (transform.forward != Facing)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(Facing), Time.deltaTime * 14f);
        RefreshAimIndicator();

        flashCooldown -= Time.deltaTime;
        bool keyboardFlash = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        if (keyboardFlash) FireAutoFlash();

        if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
            Session.UseBandage();

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

        Vector2 center = Session.MobileHud != null
            ? Session.MobileHud.MovementCenterScreen
            : new Vector2(112f, 108f);
        float joystickRadius = Session.MobileHud != null
            ? Session.MobileHud.MovementRadiusScreen
            : 65f;
        if (!EnhancedTouchSupport.enabled) return value;
        foreach (Touch touch in Touch.activeTouches)
        {
            Vector2 p = touch.screenPosition;
            if ((moveFinger < 0 && p.x < Screen.width * .42f && p.y < Screen.height * .42f) || touch.finger.index == moveFinger)
            {
                moveFinger = touch.finger.index;
                value = Vector2.ClampMagnitude((p - center) / joystickRadius, 1f);
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
        Rect button = Session.FlashButtonScreenRect();
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
        flashWasDragged = false;
        flashAimDirection = Facing;
    }

    private void UpdateFlashAim(Vector2 screenPosition)
    {
        flashCurrentPosition = screenPosition;
        Vector2 drag = flashCurrentPosition - flashPressPosition;
        float distance = drag.magnitude;
        if (distance >= FlashAimThreshold)
            flashWasDragged = true;

        if (flashWasDragged && distance <= FlashCancelRadius)
        {
            manualFlashAim = false;
            if (aimIndicator != null) aimIndicator.SetActive(false);
            return;
        }

        if (!flashWasDragged) return;

        manualFlashAim = true;
        Vector3 direction = new Vector3(drag.x, 0f, drag.y).normalized;
        flashAimDirection = direction;
        Facing = direction;
        ShowAimIndicator(direction);
    }

    private void EndFlashAim()
    {
        if (manualFlashAim) FireFlash(flashAimDirection);
        else if (!flashWasDragged) FireAutoFlash();

        aimingFlash = false;
        flashUsesMouse = false;
        manualFlashAim = false;
        flashWasDragged = false;
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
        if (flashCooldown > 0f || !Session.UseBattery()) return;
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
        aimIndicator.SetActive(true);
        flashAimDirection = direction;
        aimIndicator.transform.position = transform.position;
        aimIndicator.transform.rotation = Quaternion.LookRotation(direction);
    }

    private void RefreshAimIndicator()
    {
        if (!aimingFlash || !manualFlashAim || aimIndicator == null) return;
        aimIndicator.transform.position = transform.position;
        aimIndicator.transform.rotation = Quaternion.LookRotation(flashAimDirection);
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
        PrototypeInteractable closest = Session.ClosestInteractable(transform.position, 2.8f);
        if (closest == null)
        {
            interactionProgress = 0f;
            currentInteraction = null;
            InteractionStatus = pressed ? "Botón detectado, sin objeto cercano" : "Sin objetivo cercano";
            return;
        }

        if (!pressed)
        {
            interactionProgress = 0f;
            currentInteraction = null;
            InteractionStatus = $"Cerca: {closest.Prompt}";
            return;
        }

        if (currentInteraction != closest)
        {
            currentInteraction = closest;
            interactionProgress = 0f;
        }
        interactionProgress += Time.deltaTime;
        float percent = Mathf.Min(100f, interactionProgress / closest.Duration * 100f);
        InteractionStatus = $"{closest.Prompt} {percent:0}%";
        Session.ShowMessage(InteractionStatus, .15f);
        if (interactionProgress >= closest.Duration)
        {
            closest.Complete(this);
            interactionProgress = 0f;
            currentInteraction = null;
        }
    }

    protected override void OnDowned()
    {
        Session.ShowMessage("Has caído. Los cultistas intentarán raptarte.", 4f);
    }

    public override void Captured(Vector3 spawnPosition)
    {
        base.Captured(spawnPosition);
        Session.ShowMessage("Te llevaron a un portal de la secta", 999f);
    }
}


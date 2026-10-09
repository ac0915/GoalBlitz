using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 4f;
    public float turnAccel = 55f;
    public float recoverAccel = 18f;
    public float holdSpeedScale = 0.5f;
    public float holdSlowdown = 7f;

    [Header("Dash")]
    public float dashSpeed = 11f;
    public float dashTime = 0.12f;
    public float dashCooldown = 1.2f;

    [Header("Ball Control")]
    public float kickRange = 0.75f;
    public float kickFromSpeed = 1.25f;
    public float holdKickBonus = 5f;
    public float minKick = 1.6f;
    public float chargeSeconds = 1.75f;
    public float powerShotForce = 28f;

    [Header("Dash Kick")]
    public float dashKickMemoryTime = 0.45f;

    [Header("Charge Visuals")]
    public Color chargeColor = new Color(1f, 0.85f, 0.2f);

    [Header("Mobile")]
    public bool useMobileInput;

    [Header("Team")]
    [Tooltip("1 = Blue, 2 = Red. Used online to bind control to lobby team.")]
    public int teamId;

    // When false, this avatar is driven by network sync (other player's character).
    private bool networkInputEnabled = true;

    private Rigidbody2D rb;
    private CircleCollider2D playerCol;
    private Vector2 input;
    private Vector2 lastDir = Vector2.right;

    private float dashUntil;
    private float dashReady;

    private bool kickHeld;
    private bool kickHeldPrev;
    private bool kickPressed;

    private Rigidbody2D nearBall;
    private SpriteRenderer ballSr;
    private Color ballOrig = Color.white;

    private float contactTime;
    private bool charged;
    private bool chargedSfxPlayed;

    private float storedKickSpeed;
    private float storedKickSpeedUntil;

    private AudioSource audioSrc;

    private bool inputLocked;

    // Mouse press that started on the joystick/HUD must not count as kick-hold.
    private static bool mousePressStartedOnUi;

    public void SetKickHeld(bool held)
    {
        kickHeld = held;
    }

    public void SetInputLocked(bool locked)
    {
        inputLocked = locked;

        if (locked)
        {
            input = Vector2.zero;
            kickHeld = false;
            kickPressed = false;
            kickHeldPrev = false;

            dashUntil = 0f;
            storedKickSpeed = 0f;
            storedKickSpeedUntil = 0f;

            if (rb != null)
            {
                rb.velocity = Vector2.zero;
            }

            ResetCharge(true);
        }
    }

    public void SetNetworkInputEnabled(bool enabled)
    {
        networkInputEnabled = enabled;

        if (!enabled)
        {
            input = Vector2.zero;
            kickHeld = false;
            kickPressed = false;
            kickHeldPrev = false;
        }
    }

    public bool HasNetworkInput => networkInputEnabled;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerCol = GetComponent<CircleCollider2D>();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.mass = 1.4f;

        PhysicsMaterial2D playerMaterial = new PhysicsMaterial2D("PlayerSoft")
        {
            friction = 0.35f,
            bounciness = 0f
        };

        playerCol.sharedMaterial = playerMaterial;

        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
    }

    private void Update()
    {
        if (inputLocked || !networkInputEnabled)
        {
            input = Vector2.zero;
            kickHeld = false;
            kickPressed = false;
            kickHeldPrev = false;

            if (useMobileInput)
            {
                MobileInputBridge.DashPressed = false;
                MobileInputBridge.KickPressed = false;
            }

            return;
        }

        UpdateMousePressTracking();

        Vector2 keyboard = ReadKeyboardMove();

        if (keyboard.sqrMagnitude > 1f)
        {
            keyboard.Normalize();
        }

        Vector2 mobile = Vector2.zero;

        if (useMobileInput)
        {
            mobile = MobileInputBridge.Move;

            if (mobile.sqrMagnitude > 1f)
            {
                mobile.Normalize();
            }
        }

        input = mobile.sqrMagnitude > 0.01f ? mobile : keyboard;

        if (input.sqrMagnitude > 0.01f)
        {
            lastDir = input.normalized;
        }

        bool keyDown = Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
        // Mouse left is used to drag the joystick. Never treat it as kick when
        // this player uses mobile input, and never when the press began on UI.
        bool mouseDown = !useMobileInput
            && Mouse.current != null
            && Mouse.current.leftButton.isPressed
            && !mousePressStartedOnUi;
        bool mobileDown = useMobileInput && MobileInputBridge.KickHeld;

        kickHeldPrev = kickHeld;
        kickHeld = keyDown || mouseDown || mobileDown;
        kickPressed = kickHeld && !kickHeldPrev;

        if (useMobileInput && MobileInputBridge.KickPressed)
        {
            kickPressed = true;
            MobileInputBridge.KickPressed = false;
        }

        if (useMobileInput && MobileInputBridge.DashPressed)
        {
            TryDash();
            MobileInputBridge.DashPressed = false;
        }
        else if (Keyboard.current != null && Keyboard.current.leftShiftKey.wasPressedThisFrame)
        {
            TryDash();
        }
    }

    private void FixedUpdate()
    {
        if (inputLocked || !networkInputEnabled)
        {
            return;
        }

        // Movement
        Vector2 desired = input * moveSpeed;
        if (kickHeld)
        {
            desired *= holdSpeedScale;
        }

        float accel = input.sqrMagnitude > 0.01f ? turnAccel : recoverAccel;
        if (kickHeld)
        {
            accel = holdSlowdown;
        }

        rb.velocity = Vector2.MoveTowards(rb.velocity, desired, accel * Time.fixedDeltaTime);

        // Dash override
        if (Time.time < dashUntil)
        {
            rb.velocity = lastDir * dashSpeed;
        }

        // Ball hold / kick
        UpdateNearBall();
        HandleKick();
    }

    private void UpdateMousePressTracking()
    {
        if (Mouse.current == null)
        {
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            mousePressStartedOnUi = PointerIsOverUi();
        }

        if (!Mouse.current.leftButton.isPressed)
        {
            mousePressStartedOnUi = false;
        }
    }

    private static bool PointerIsOverUi()
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        return EventSystem.current.IsPointerOverGameObject();
    }

    private static Vector2 ReadKeyboardMove()
    {
        if (Keyboard.current == null)
        {
            return Vector2.zero;
        }

        Vector2 move = Vector2.zero;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) move.y += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) move.y -= 1f;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) move.x -= 1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) move.x += 1f;
        return move;
    }

    private void TryDash()
    {
        if (Time.time < dashReady)
        {
            return;
        }

        dashUntil = Time.time + dashTime;
        dashReady = Time.time + dashCooldown;
        storedKickSpeed = dashSpeed;
        storedKickSpeedUntil = Time.time + dashKickMemoryTime;
    }

    private void UpdateNearBall()
    {
        nearBall = null;
        Collider2D[] hits = Physics2D.OverlapCircleAll(rb.position, kickRange);
        float best = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Rigidbody2D other = hits[i].attachedRigidbody;
            if (other == null || other == rb)
            {
                continue;
            }

            if (!other.CompareTag("Ball") && other.gameObject.name.IndexOf("Ball", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            float d = (other.position - rb.position).sqrMagnitude;
            if (d < best)
            {
                best = d;
                nearBall = other;
            }
        }

        if (nearBall != null)
        {
            if (ballSr == null || ballSr.gameObject != nearBall.gameObject)
            {
                ballSr = nearBall.GetComponent<SpriteRenderer>();
                if (ballSr != null)
                {
                    ballOrig = ballSr.color;
                }
            }
        }
    }

    private void HandleKick()
    {
        if (nearBall == null)
        {
            ResetCharge(true);
            return;
        }

        if (kickHeld)
        {
            contactTime += Time.fixedDeltaTime;
            if (contactTime >= chargeSeconds)
            {
                charged = true;
                if (ballSr != null)
                {
                    ballSr.color = chargeColor;
                }
            }
        }

        if (kickPressed || (!kickHeld && kickHeldPrev))
        {
            DoKick();
        }
    }

    private void DoKick()
    {
        if (nearBall == null)
        {
            return;
        }

        Vector2 kickDir = KickDirection();
        float force = minKick + kickFromSpeed * rb.velocity.magnitude;

        if (Time.time < storedKickSpeedUntil)
        {
            force += storedKickSpeed;
        }

        if (kickHeldPrev)
        {
            force += holdKickBonus * Mathf.Clamp01(contactTime / chargeSeconds);
        }

        if (charged)
        {
            force = powerShotForce;
        }

        nearBall.velocity = kickDir * force;
        ResetCharge(true);
    }

    private Vector2 KickDirection()
    {
        if (nearBall == null)
        {
            return lastDir;
        }

        Vector2 toBall = nearBall.position - rb.position;
        if (toBall.sqrMagnitude > 0.0001f)
        {
            return toBall.normalized;
        }

        return lastDir;
    }

    private void ResetCharge(bool restoreColor)
    {
        contactTime = 0f;
        charged = false;
        chargedSfxPlayed = false;

        if (restoreColor && ballSr != null)
        {
            ballSr.color = ballOrig;
        }
    }
}
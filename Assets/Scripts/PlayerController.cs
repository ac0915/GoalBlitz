using System.Collections;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class PlayerController : NetworkBehaviour
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

    [Networked] public NetworkBool NetInputLocked { get; set; }

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

    private bool IsNetworked => Object != null && Object.IsValid;

    public void SetKickHeld(bool held)
    {
        kickHeld = held;
    }

    public void SetInputLocked(bool locked)
    {
        if (IsNetworked && Object.HasStateAuthority)
        {
            NetInputLocked = locked;
        }

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
        if (IsNetworked)
        {
            return;
        }

        OfflineUpdate();
    }

    private void FixedUpdate()
    {
        if (IsNetworked)
        {
            return;
        }

        OfflineFixedUpdate();
    }

    public override void FixedUpdateNetwork()
    {
        if (NetInputLocked || inputLocked)
        {
            if (rb != null)
            {
                rb.velocity = Vector2.zero;
            }
            return;
        }

        if (!GetInput(out GoalBlitzPlayerInput playerInput))
        {
            return;
        }

        input = playerInput.Move;
        if (input.sqrMagnitude > 0.01f)
        {
            lastDir = input.normalized;
        }

        kickPressed = playerInput.KickPressed;
        kickHeld = playerInput.KickHeld;

        float simTime = (float)Runner.SimulationTime;

        if (playerInput.DashPressed && simTime >= dashReady)
        {
            dashUntil = simTime + dashTime;
            dashReady = simTime + dashCooldown;
            storedKickSpeed = dashSpeed;
            storedKickSpeedUntil = simTime + dashTime + dashKickMemoryTime;
        }

        ApplyMovement(simTime, Runner.DeltaTime);
        ApplyBallControl(Runner.DeltaTime, simTime);
    }

    private void OfflineUpdate()
    {
        if (inputLocked)
        {
            input = Vector2.zero;
            kickHeld = false;
            kickPressed = false;
            kickHeldPrev = false;
            return;
        }

        input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        if (input.sqrMagnitude > 0.01f)
        {
            lastDir = input.normalized;
        }

        bool down = Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0);
        kickPressed = down && !kickHeldPrev;
        kickHeld = down;
        kickHeldPrev = kickHeld;

        if (Time.time >= dashReady && Input.GetKeyDown(KeyCode.LeftShift))
        {
            dashUntil = Time.time + dashTime;
            dashReady = Time.time + dashCooldown;
            storedKickSpeed = dashSpeed;
            storedKickSpeedUntil = Time.time + dashTime + dashKickMemoryTime;
        }

        nearBall = FindNearBall();
        if (nearBall == null)
        {
            ResetCharge(true);
            return;
        }

        SetBallRenderer(nearBall);
        contactTime += Time.deltaTime;

        if (!charged && contactTime >= chargeSeconds)
        {
            charged = true;
            if (!chargedSfxPlayed)
            {
                chargedSfxPlayed = true;
                PlayChargeReady(nearBall);
            }
        }

        UpdateChargeColor();

        if (kickPressed)
        {
            if (charged) PowerKick(nearBall, Time.time);
            else NormalKick(nearBall, Time.time);
        }
    }

    private void OfflineFixedUpdate()
    {
        if (inputLocked)
        {
            return;
        }

        ApplyMovement(Time.time, Time.fixedDeltaTime);
    }

    private void ApplyMovement(float timeNow, float deltaTime)
    {
        if (timeNow < dashUntil)
        {
            rb.velocity = lastDir * dashSpeed;
            return;
        }

        float targetSpeed = kickHeld ? moveSpeed * holdSpeedScale : moveSpeed;
        Vector2 targetVelocity = input * targetSpeed;
        float acceleration = kickHeld ? holdSlowdown : recoverAccel;

        if (!kickHeld && targetVelocity.sqrMagnitude > 0.01f && Vector2.Dot(rb.velocity, targetVelocity) < 0f)
        {
            acceleration = turnAccel;
        }

        rb.velocity = Vector2.MoveTowards(rb.velocity, targetVelocity, acceleration * deltaTime);
    }

    private void ApplyBallControl(float deltaTime, float timeNow)
    {
        nearBall = FindNearBall();
        if (nearBall == null)
        {
            ResetCharge(true);
            return;
        }

        SetBallRenderer(nearBall);
        contactTime += deltaTime;

        if (!charged && contactTime >= chargeSeconds)
        {
            charged = true;
            if (!chargedSfxPlayed)
            {
                chargedSfxPlayed = true;
                PlayChargeReady(nearBall);
            }
        }

        UpdateChargeColor();

        if (kickPressed)
        {
            if (charged) PowerKick(nearBall, timeNow);
            else NormalKick(nearBall, timeNow);
        }
    }

    private Rigidbody2D FindNearBall()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(rb.position, kickRange);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit != null && hit.CompareTag("Ball") && hit.attachedRigidbody != null)
            {
                return hit.attachedRigidbody;
            }
        }
        return null;
    }

    private void SetBallRenderer(Rigidbody2D ball)
    {
        SpriteRenderer renderer = ball.GetComponent<SpriteRenderer>();
        if (renderer == null) return;
        if (ballSr != renderer)
        {
            ballSr = renderer;
            ballOrig = ballSr.color;
        }
    }

    private void UpdateChargeColor()
    {
        if (ballSr == null) return;
        float progress = Mathf.Clamp01(contactTime / chargeSeconds);
        ballSr.color = charged ? Color.white : Color.Lerp(ballOrig, chargeColor, progress);
    }

    private void NormalKick(Rigidbody2D ball, float timeNow)
    {
        Vector2 direction = KickDirection(ball);
        float speedForKick = rb.velocity.magnitude;
        if (timeNow <= storedKickSpeedUntil)
        {
            speedForKick = Mathf.Max(speedForKick, storedKickSpeed);
        }

        float power = speedForKick * kickFromSpeed + holdKickBonus;
        if (power < minKick) power = minKick;

        ball.velocity = direction * power;
        ClearDashKickMemory();
        ResetCharge(true);
    }

    private void PowerKick(Rigidbody2D ball, float timeNow)
    {
        Vector2 direction = KickDirection(ball);
        ball.velocity = direction * powerShotForce;
        PlayKickBoom();
        SpawnFlash(ball.position, new Color(1f, 0.9f, 0.3f), 2.4f);
        ClearDashKickMemory();
        ResetCharge(true);
    }

    private Vector2 KickDirection(Rigidbody2D ball)
    {
        Vector2 direction = ball.position - rb.position;
        if (direction.sqrMagnitude > 0.001f) return direction.normalized;
        if (lastDir.sqrMagnitude > 0.01f) return lastDir.normalized;
        return Vector2.right;
    }

    private void ClearDashKickMemory()
    {
        storedKickSpeed = 0f;
        storedKickSpeedUntil = 0f;
    }

    private void PlayChargeReady(Rigidbody2D ball)
    {
        PlayBeep(880f, 0.12f, 0.5f);
        PlayBeep(1320f, 0.18f, 0.55f);
        SpawnFlash(ball.position, chargeColor, 1.8f);
        if (ballSr != null) StartCoroutine(Pulse(ballSr.transform));
    }

    private void PlayKickBoom()
    {
        PlayBeep(180f, 0.08f, 0.6f);
        PlayBeep(420f, 0.12f, 0.4f);
    }

    private void PlayBeep(float frequency, float duration, float volume)
    {
        const int sampleRate = 22050;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] data = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float time = i / (float)sampleRate;
            float fadeOut = 1f - i / (float)sampleCount;
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * time) * fadeOut * volume;
        }

        AudioClip clip = AudioClip.Create("GoalBlitzBeep", sampleCount, 1, sampleRate, false);
        clip.SetData(data, 0);
        audioSrc.PlayOneShot(clip);
    }

    private void SpawnFlash(Vector2 position, Color color, float size)
    {
        GameObject flash = new GameObject("ChargeFlash");
        flash.transform.position = position;
        SpriteRenderer renderer = flash.AddComponent<SpriteRenderer>();
        renderer.sprite = ballSr != null ? ballSr.sprite : null;
        renderer.color = new Color(color.r, color.g, color.b, 0.85f);
        renderer.sortingOrder = 20;
        StartCoroutine(FlashAnimation(flash.transform, renderer, size));
    }

    private IEnumerator FlashAnimation(Transform flashTransform, SpriteRenderer flashRenderer, float size)
    {
        float time = 0f;
        const float duration = 0.35f;
        while (time < duration)
        {
            time += Time.deltaTime;
            float progress = time / duration;
            flashTransform.localScale = Vector3.one * Mathf.Lerp(0.4f, size, progress);
            Color color = flashRenderer.color;
            color.a = 0.85f * (1f - progress);
            flashRenderer.color = color;
            yield return null;
        }
        Destroy(flashTransform.gameObject);
    }

    private IEnumerator Pulse(Transform target)
    {
        Vector3 originalScale = target.localScale;
        float time = 0f;
        while (time < 0.2f)
        {
            time += Time.deltaTime;
            target.localScale = originalScale * Mathf.Lerp(1f, 1.35f, time / 0.2f);
            yield return null;
        }
        time = 0f;
        while (time < 0.2f)
        {
            time += Time.deltaTime;
            target.localScale = originalScale * Mathf.Lerp(1.35f, 1f, time / 0.2f);
            yield return null;
        }
        target.localScale = originalScale;
    }

    private void ResetCharge(bool resetColor)
    {
        contactTime = 0f;
        charged = false;
        chargedSfxPlayed = false;
        if (resetColor && ballSr != null)
        {
            ballSr.color = ballOrig;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, kickRange);
    }
}

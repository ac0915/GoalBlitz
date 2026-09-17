using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 4f;
    public float accel = 36f;
    public float turnAccel = 55f;
    public float recoverAccel = 18f;
    public float holdSpeedScale = 0.5f;
    public float dashSpeed = 11f;
    public float dashTime = 0.12f;
    public float dashCooldown = 1.2f;
    public float kickFromSpeed = 1.25f;
    public float holdKickBonus = 5f;
    public float minKick = 1.6f;
    public float chargeSeconds = 1.75f;
    public float powerShotForce = 28f;
    public float chargeRangePad = 0f;
    public float chargeGrace = 0.45f;
    public float forwardPowerMul = 1.75f;
    public float sameDirDot = 0.92f;
    public Color chargeColor = new Color(1f, 0.85f, 0.2f);

    Rigidbody2D rb;
    CircleCollider2D playerCol;
    Vector2 input;
    Vector2 lastDir = Vector2.right;
    float dashUntil;
    float dashReady;
    bool kickHeld;
    bool kickHeldPrev;
    bool kickPressed;

    float contactTime;
    bool charged;
    bool chargedSfxPlayed;
    SpriteRenderer ballSr;
    Color ballOrig = Color.white;
    AudioSource audioSrc;
    Rigidbody2D nearBall;
    Rigidbody2D lastBall;
    float lastTouch = -99f;
    float lostNearAt = -999f;

    public void SetKickHeld(bool held) { kickHeld = held; }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerCol = GetComponent<CircleCollider2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.mass = 1.4f;

        var sr = GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null && playerCol != null)
        {
            playerCol.offset = sr.sprite.bounds.center;
            playerCol.radius = sr.sprite.bounds.extents.x;
        }

        var mat = new PhysicsMaterial2D("PlayerSoft") { friction = 0.35f, bounciness = 0f };
        if (playerCol != null) playerCol.sharedMaterial = mat;

        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
    }

    void Update()
    {
        input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude > 1f) input.Normalize();
        if (input.sqrMagnitude > 0.01f) lastDir = input.normalized;

        bool down = Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0);
        kickPressed = down && !kickHeldPrev;
        kickHeld = down;
        if (kickHeld && !kickHeldPrev)
            rb.velocity *= holdSpeedScale;
        kickHeldPrev = kickHeld;

        if (Time.time >= dashReady && Input.GetKeyDown(KeyCode.LeftShift))
        {
            dashUntil = Time.time + dashTime;
            dashReady = Time.time + dashCooldown;
        }

        nearBall = FindNearBall();
        if (nearBall == null && lastBall != null && Time.time - lastTouch < 0.12f)
            nearBall = lastBall;

        if (nearBall != null)
        {
            lastBall = nearBall;
            lostNearAt = -1f;

            if (ballSr == null)
            {
                ballSr = nearBall.GetComponent<SpriteRenderer>();
                if (ballSr != null) ballOrig = ballSr.color;
            }

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

            if (kickPressed)
            {
                if (charged) PowerKick(nearBall);
                else NormalKick(nearBall);
            }
        }
        else
        {
            if (lostNearAt < 0f) lostNearAt = Time.time;
            if (Time.time - lostNearAt <= chargeGrace)
            {
                if (kickPressed && lastBall != null)
                {
                    if (charged) PowerKick(lastBall);
                    else NormalKick(lastBall);
                }
            }
            else ResetCharge(true);
        }

        if (ballSr != null && (nearBall != null || Time.time - lostNearAt <= chargeGrace))
        {
            float t = Mathf.Clamp01(contactTime / chargeSeconds);
            ballSr.color = charged ? Color.white : Color.Lerp(ballOrig, chargeColor, t);
        }
    }

    Rigidbody2D FindNearBall()
    {
        Vector2 pos = rb.position;
        float r = 0.5f;
        if (playerCol != null)
        {
            pos += playerCol.offset;
            r = playerCol.radius * Mathf.Abs(transform.lossyScale.x) + chargeRangePad + 0.02f;
        }
        var hits = Physics2D.OverlapCircleAll(pos, r);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] != null && hits[i].CompareTag("Ball") && hits[i].attachedRigidbody != null)
                return hits[i].attachedRigidbody;
        }
        return null;
    }

    bool SameDirectionAsBall(Rigidbody2D ball)
    {
        Vector2 p = rb.velocity;
        Vector2 b = ball.velocity;
        if (p.sqrMagnitude < 0.25f || b.sqrMagnitude < 0.25f)
            return false;
        return Vector2.Dot(p.normalized, b.normalized) >= sameDirDot;
    }

    float ShotPower(Rigidbody2D ball, float basePower)
    {
        if (SameDirectionAsBall(ball))
            return basePower * forwardPowerMul;
        return basePower;
    }

    void FixedUpdate()
    {
        if (Time.time < dashUntil)
        {
            rb.velocity = lastDir * dashSpeed;
            return;
        }

        float speed = kickHeld ? moveSpeed * holdSpeedScale : moveSpeed;
        Vector2 target = input * speed;
        float a = kickHeld ? 80f : recoverAccel;
        if (!kickHeld && target.sqrMagnitude > 0.01f && Vector2.Dot(rb.velocity, target) < 0f)
            a = turnAccel;
        rb.velocity = Vector2.MoveTowards(rb.velocity, target, a * Time.fixedDeltaTime);
    }

    void OnCollisionStay2D(Collision2D col)
    {
        if (!col.collider.CompareTag("Ball")) return;
        if (col.rigidbody == null) return;
        lastBall = col.rigidbody;
        lastTouch = Time.time;
    }

    void NormalKick(Rigidbody2D ball)
    {
        Vector2 dir = DirTo(ball);
        float power = rb.velocity.magnitude * kickFromSpeed + holdKickBonus;
        if (power < minKick) power = minKick;
        ball.velocity = dir * ShotPower(ball, power);
        ResetCharge(true);
    }

    void PowerKick(Rigidbody2D ball)
    {
        Vector2 dir = DirTo(ball);
        ball.velocity = dir * ShotPower(ball, powerShotForce);
        PlayKickBoom();
        SpawnFlash(ball.position, new Color(1f, 0.9f, 0.3f), 2.4f);
        ResetCharge(true);
    }

    Vector2 DirTo(Rigidbody2D ball)
    {
        if (rb.velocity.sqrMagnitude > 0.05f) return rb.velocity.normalized;
        if (lastDir.sqrMagnitude > 0.01f) return lastDir;
        return ((Vector2)ball.position - rb.position).normalized;
    }

    void PlayChargeReady(Rigidbody2D ball)
    {
        PlayBeep(880f, 0.12f, 0.5f);
        PlayBeep(1320f, 0.18f, 0.55f);
        SpawnFlash(ball.position, chargeColor, 1.8f);
        if (ballSr != null)
            StartCoroutine(Pulse(ballSr.transform));
    }

    void PlayKickBoom()
    {
        PlayBeep(180f, 0.08f, 0.6f);
        PlayBeep(420f, 0.12f, 0.4f);
    }

    void PlayBeep(float freq, float dur, float vol)
    {
        int rate = 22050;
        int n = Mathf.CeilToInt(rate * dur);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * (1f - i / (float)n) * vol;
        }
        var clip = AudioClip.Create("beep", n, 1, rate, false);
        clip.SetData(data, 0);
        audioSrc.PlayOneShot(clip);
    }

    void SpawnFlash(Vector2 pos, Color c, float size)
    {
        var go = new GameObject("ChargeFlash");
        go.transform.position = pos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = ballSr != null ? ballSr.sprite : null;
        sr.color = new Color(c.r, c.g, c.b, 0.85f);
        sr.sortingOrder = 20;
        StartCoroutine(FlashAnim(go.transform, sr, size));
    }

    IEnumerator FlashAnim(Transform tr, SpriteRenderer sr, float size)
    {
        float t = 0f;
        while (t < 0.35f)
        {
            t += Time.deltaTime;
            float k = t / 0.35f;
            tr.localScale = Vector3.one * Mathf.Lerp(0.4f, size, k);
            var col = sr.color;
            col.a = 0.85f * (1f - k);
            sr.color = col;
            yield return null;
        }
        Destroy(tr.gameObject);
    }

    IEnumerator Pulse(Transform tr)
    {
        Vector3 a = tr.localScale;
        float t = 0f;
        while (t < 0.2f)
        {
            t += Time.deltaTime;
            tr.localScale = a * Mathf.Lerp(1f, 1.35f, t / 0.2f);
            yield return null;
        }
        t = 0f;
        while (t < 0.2f)
        {
            t += Time.deltaTime;
            tr.localScale = a * Mathf.Lerp(1.35f, 1f, t / 0.2f);
            yield return null;
        }
        tr.localScale = a;
    }

    void ResetCharge(bool resetColor)
    {
        contactTime = 0f;
        charged = false;
        chargedSfxPlayed = false;
        lostNearAt = -999f;
        if (resetColor && ballSr != null)
            ballSr.color = ballOrig;
    }
}
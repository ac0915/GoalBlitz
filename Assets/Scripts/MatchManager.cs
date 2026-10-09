using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class MatchManager : MonoBehaviour
{
    public enum Team
    {
        Blue,
        Red
    }

    private enum MatchState
    {
        Countdown,
        Playing,
        GoalSequence,
        Replay,
        MatchOver
    }

    [Header("Scene Objects")]
    public GameObject playerBlue;
    public GameObject playerRed;
    public GameObject ball;

    [Header("Kickoff Positions")]
    public Vector2 blueKickoffPosition = new Vector2(-4f, 0f);
    public Vector2 redKickoffPosition = new Vector2(4f, 0f);
    public Vector2 ballKickoffPosition = Vector2.zero;

    [Header("Match Rules")]
    public int winningScore = 3;
    public float countdownSeconds = 3f;

    [Header("Goal Celebration")]
    public float goalCelebrationSeconds = 2f;
    public float goalTextPulseSpeed = 12f;

    [Header("Replay")]
    public float replayBeforeSeconds = 3f;
    public float replayAfterSeconds = 3f;
    public float replayPlaybackSpeed = 1f;
    public float replayGoalShowSeconds = 0.8f;
    public float replaySlowSpeed = 0.5f;

    public static MatchManager Instance { get; private set; }

    private PlayerController blueController;
    private PlayerController redController;

    private Rigidbody2D blueRb;
    private Rigidbody2D redRb;
    private Rigidbody2D ballRb;

    private Sprite ballSprite;

    private int blueScore;
    private int redScore;

    private MatchState state;
    private bool skipReplayRequested;
    private float countdownRemaining;

    private Vector2 lastGoalPosition;
    private float replayProgress;
    private int goalFrameIndex;

    private readonly List<ReplayFrame> replayFrames =
        new List<ReplayFrame>();

    private struct ReplayFrame
    {
        public Vector2 bluePosition;
        public Vector2 redPosition;
        public Vector2 ballPosition;

        public ReplayFrame(
            Vector2 bluePosition,
            Vector2 redPosition,
            Vector2 ballPosition)
        {
            this.bluePosition = bluePosition;
            this.redPosition = redPosition;
            this.ballPosition = ballPosition;
        }
    }

    private void Awake()
    {
        Instance = this;

        blueController = playerBlue.GetComponent<PlayerController>();
        redController = playerRed.GetComponent<PlayerController>();

        blueRb = playerBlue.GetComponent<Rigidbody2D>();
        redRb = playerRed.GetComponent<Rigidbody2D>();
        ballRb = ball.GetComponent<Rigidbody2D>();

        SpriteRenderer ballRenderer =
            ball.GetComponent<SpriteRenderer>();

        if (ballRenderer != null)
        {
            ballSprite = ballRenderer.sprite;
        }
    }

    private void Start()
    {
        blueScore = 0;
        redScore = 0;

        StartCoroutine(StartRound());
    }

    private void FixedUpdate()
    {
        if (state == MatchState.Playing)
        {
            RecordReplayFrame(true);
        }
    }

    private void Update()
    {
        if (state == MatchState.MatchOver &&
            Keyboard.current != null &&
            Keyboard.current.rKey.wasPressedThisFrame)
        {
            blueScore = 0;
            redScore = 0;

            replayFrames.Clear();

            StartCoroutine(StartRound());
        }
    }

    public void RegisterGoal(
        Team scoringTeam,
        Vector2 goalPosition)
    {
        if (state != MatchState.Playing)
        {
            return;
        }

        lastGoalPosition = goalPosition;

        StartCoroutine(GoalSequence(scoringTeam));
    }

    public void SkipReplay()
    {
        if (state == MatchState.Replay)
        {
            skipReplayRequested = true;
        }
    }

    private IEnumerator GoalSequence(Team scoringTeam)
    {
        state = MatchState.GoalSequence;

        if (scoringTeam == Team.Blue)
        {
            blueScore++;
        }
        else
        {
            redScore++;
        }

        StartCoroutine(
            BlackHoleGoalEffect(lastGoalPosition)
        );

        goalFrameIndex = replayFrames.Count;
        int afterFrames = Mathf.CeilToInt(
            Mathf.Max(0.1f, replayAfterSeconds) / Time.fixedDeltaTime
        );

        for (int n = 0; n < afterFrames; n++)
        {
            yield return new WaitForFixedUpdate();
            RecordReplayFrame(false);
        }

        LockPlayers(true);
        StopAndFreezePhysics(true);

        if (blueScore >= winningScore ||
            redScore >= winningScore)
        {
            state = MatchState.MatchOver;
            yield break;
        }

        yield return StartCoroutine(PlayReplay());

        ResetAllObjects();

        StartCoroutine(StartRound());
    }

    private IEnumerator StartRound()
    {
        state = MatchState.Countdown;

        skipReplayRequested = false;

        StopAndFreezePhysics(false);
        ResetAllObjects();
        LockPlayers(true);

        countdownRemaining = countdownSeconds;

        while (countdownRemaining > 0f)
        {
            yield return null;

            countdownRemaining -= Time.unscaledDeltaTime;
        }

        state = MatchState.Playing;

        replayFrames.Clear();

        LockPlayers(false);
    }

    private IEnumerator PlayReplay()
    {
        if (replayFrames.Count < 2)
        {
            yield break;
        }

        state = MatchState.Replay;

        skipReplayRequested = false;
        replayProgress = 0f;

        StopAndFreezePhysics(true);

        for (int i = 0; i < replayFrames.Count; i++)
        {
            if (skipReplayRequested)
            {
                break;
            }

            replayProgress =
                i / (float)(replayFrames.Count - 1);

            ReplayFrame frame = replayFrames[i];

            playerBlue.transform.position =
                frame.bluePosition;

            playerRed.transform.position =
                frame.redPosition;

            ball.transform.position =
                frame.ballPosition;

            int oneSecondFrames = Mathf.Max(
                1,
                Mathf.RoundToInt(1f / Time.fixedDeltaTime)
            );
            int slowStart = Mathf.Max(0, goalFrameIndex - oneSecondFrames);
            int slowEnd = Mathf.Min(
                replayFrames.Count,
                goalFrameIndex + oneSecondFrames
            );
            float speed = i >= slowStart && i < slowEnd
                ? replaySlowSpeed
                : 1f;

            yield return new WaitForSecondsRealtime(
                Time.fixedDeltaTime / Mathf.Max(0.05f, speed)
            );
        }
    }

    private void RecordReplayFrame(bool trimToBeforeGoal)
    {
        replayFrames.Add(
            new ReplayFrame(
                blueRb.position,
                redRb.position,
                ballRb.position
            )
        );

        if (!trimToBeforeGoal)
        {
            return;
        }

        int maximumFrames = Mathf.CeilToInt(
            Mathf.Max(0.1f, replayBeforeSeconds) / Time.fixedDeltaTime
        );

        while (replayFrames.Count > maximumFrames)
        {
            replayFrames.RemoveAt(0);
        }
    }

    private void ResetAllObjects()
    {
        playerBlue.transform.position = blueKickoffPosition;
        playerRed.transform.position = redKickoffPosition;
        ball.transform.position = ballKickoffPosition;

        blueRb.velocity = Vector2.zero;
        redRb.velocity = Vector2.zero;
        ballRb.velocity = Vector2.zero;

        blueRb.angularVelocity = 0f;
        redRb.angularVelocity = 0f;
        ballRb.angularVelocity = 0f;
    }

    private void LockPlayers(bool locked)
    {
        blueController.SetInputLocked(locked);
        redController.SetInputLocked(locked);
    }

    private void StopAndFreezePhysics(bool freeze)
    {
        blueRb.velocity = Vector2.zero;
        redRb.velocity = Vector2.zero;
        ballRb.velocity = Vector2.zero;

        blueRb.angularVelocity = 0f;
        redRb.angularVelocity = 0f;
        ballRb.angularVelocity = 0f;

        blueRb.simulated = !freeze;
        redRb.simulated = !freeze;
        ballRb.simulated = !freeze;
    }

    private IEnumerator BlackHoleGoalEffect(Vector2 position)
    {
        if (ballSprite == null)
        {
            yield break;
        }

        GameObject root = new GameObject("GoalBlackHoleEffect");
        root.transform.position = position;

        SpriteRenderer core = CreateEffectSprite(
            "BlackHoleCore",
            root.transform,
            Color.black,
            70
        );

        SpriteRenderer glow = CreateEffectSprite(
            "BlackHoleGlow",
            root.transform,
            new Color(0.1f, 0.85f, 1f, 0.8f),
            69
        );

        SpriteRenderer purpleRing = CreateEffectSprite(
            "PurpleEnergyRing",
            root.transform,
            new Color(0.55f, 0.08f, 1f, 0.9f),
            68
        );

        SpriteRenderer cyanRing = CreateEffectSprite(
            "CyanEnergyRing",
            root.transform,
            new Color(0.05f, 0.85f, 1f, 0.8f),
            71
        );

        SpriteRenderer shockwaveOne = CreateEffectSprite(
            "ShockwaveOne",
            root.transform,
            new Color(0.8f, 0.25f, 1f, 0.65f),
            66
        );

        SpriteRenderer shockwaveTwo = CreateEffectSprite(
            "ShockwaveTwo",
            root.transform,
            new Color(0.1f, 0.9f, 1f, 0.55f),
            65
        );

        List<SpriteRenderer> particles =
            new List<SpriteRenderer>();

        List<float> particleAngles =
            new List<float>();

        for (int i = 0; i < 10; i++)
        {
            GameObject particle =
                new GameObject("BlackHoleParticle");

            particle.transform.parent = root.transform;

            SpriteRenderer particleRenderer =
                particle.AddComponent<SpriteRenderer>();

            particleRenderer.sprite = ballSprite;
            particleRenderer.color =
                i % 2 == 0
                    ? new Color(0.2f, 0.9f, 1f, 1f)
                    : new Color(0.75f, 0.15f, 1f, 1f);

            particleRenderer.sortingOrder = 72;

            particles.Add(particleRenderer);

            particleAngles.Add(i * 36f);
        }

        float time = 0f;
        float duration = goalCelebrationSeconds;

        while (time < duration)
        {
            time += Time.unscaledDeltaTime;

            float progress =
                Mathf.Clamp01(time / duration);

            float pulse =
                1f + Mathf.Sin(time * 18f) * 0.13f;

            float fadeOut =
                1f - Mathf.SmoothStep(0.55f, 1f, progress);

            core.transform.localScale =
                Vector3.one *
                Mathf.Lerp(0.25f, 2.4f, progress) *
                pulse;

            glow.transform.localScale =
                Vector3.one *
                Mathf.Lerp(0.35f, 3.2f, progress) *
                pulse;

            purpleRing.transform.localScale =
                Vector3.one *
                Mathf.Lerp(0.55f, 4.5f, progress);

            cyanRing.transform.localScale =
                Vector3.one *
                Mathf.Lerp(0.4f, 3.7f, progress) *
                pulse;

            shockwaveOne.transform.localScale =
                Vector3.one *
                Mathf.Lerp(0.2f, 7f, progress);

            shockwaveTwo.transform.localScale =
                Vector3.one *
                Mathf.Lerp(0.1f, 5.7f, progress);

            purpleRing.transform.Rotate(
                0f,
                0f,
                -460f * Time.unscaledDeltaTime
            );

            cyanRing.transform.Rotate(
                0f,
                0f,
                680f * Time.unscaledDeltaTime
            );

            glow.transform.Rotate(
                0f,
                0f,
                -130f * Time.unscaledDeltaTime
            );

            for (int i = 0; i < particles.Count; i++)
            {
                float angle =
                    particleAngles[i] +
                    time * (220f + i * 16f);

                float radius =
                    Mathf.Lerp(0.25f, 2.9f, progress) +
                    Mathf.Sin(time * 7f + i) * 0.15f;

                float radians =
                    angle * Mathf.Deg2Rad;

                Vector2 offset = new Vector2(
                    Mathf.Cos(radians),
                    Mathf.Sin(radians)
                ) * radius;

                particles[i].transform.localPosition = offset;

                float particleScale =
                    Mathf.Lerp(0.12f, 0.38f, progress);

                particles[i].transform.localScale =
                    Vector3.one * particleScale;

                Color particleColor = particles[i].color;
                particleColor.a = fadeOut;
                particles[i].color = particleColor;
            }

            SetAlpha(core, 0.98f * fadeOut);
            SetAlpha(glow, 0.7f * fadeOut);
            SetAlpha(purpleRing, 0.9f * fadeOut);
            SetAlpha(cyanRing, 0.85f * fadeOut);
            SetAlpha(shockwaveOne, 0.55f * fadeOut);
            SetAlpha(shockwaveTwo, 0.45f * fadeOut);

            yield return null;
        }

        Destroy(root);
    }

    private SpriteRenderer CreateEffectSprite(
        string objectName,
        Transform parent,
        Color color,
        int sortingOrder)
    {
        GameObject effectObject =
            new GameObject(objectName);

        effectObject.transform.parent = parent;
        effectObject.transform.localPosition = Vector3.zero;

        SpriteRenderer renderer =
            effectObject.AddComponent<SpriteRenderer>();

        renderer.sprite = ballSprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;

        return renderer;
    }

    private void SetAlpha(
        SpriteRenderer renderer,
        float alpha)
    {
        Color color = renderer.color;
        color.a = alpha;
        renderer.color = color;
    }

    private void OnGUI()
    {
        GUIStyle scoreStyle =
            new GUIStyle(GUI.skin.label);

        scoreStyle.alignment =
            TextAnchor.UpperCenter;

        scoreStyle.fontSize = 34;
        scoreStyle.fontStyle = FontStyle.Bold;
        scoreStyle.richText = true;
        scoreStyle.normal.textColor = Color.white;

        GUIStyle messageStyle =
            new GUIStyle(GUI.skin.label);

        messageStyle.alignment =
            TextAnchor.MiddleCenter;

        messageStyle.fontSize = 72;
        messageStyle.fontStyle = FontStyle.Bold;
        messageStyle.richText = true;
        messageStyle.normal.textColor = Color.white;

        GUIStyle smallMessageStyle =
            new GUIStyle(GUI.skin.label);

        smallMessageStyle.alignment =
            TextAnchor.MiddleCenter;

        smallMessageStyle.fontSize = 30;
        smallMessageStyle.fontStyle = FontStyle.Bold;
        smallMessageStyle.richText = true;
        smallMessageStyle.normal.textColor = Color.white;

        float screenWidth = Screen.width;
        float screenHeight = Screen.height;

        string scoreText =
            "<color=#4DA6FFFF>BLUE</color>  " +
            blueScore +
            "  -  " +
            redScore +
            "  <color=#FF5555FF>RED</color>";

        GUI.Label(
            new Rect(0f, 22f, screenWidth, 50f),
            scoreText,
            scoreStyle
        );

        if (state == MatchState.Countdown)
        {
            int number = Mathf.Clamp(
                Mathf.CeilToInt(countdownRemaining),
                1,
                99
            );

            GUI.Label(
                new Rect(
                    0f,
                    screenHeight * 0.40f,
                    screenWidth,
                    120f
                ),
                number.ToString(),
                messageStyle
            );
        }

        if (state == MatchState.GoalSequence)
        {
            DrawGoalText(
                screenWidth,
                screenHeight,
                94f
            );
        }

        if (state == MatchState.Replay)
        {
            GUI.Label(
                new Rect(
                    0f,
                    screenHeight * 0.05f,
                    screenWidth,
                    50f
                ),
                "<color=#7BE8FFFF>REPLAY</color>",
                smallMessageStyle
            );

            float showGoalAt =
                1f - (
                    replayGoalShowSeconds /
                    Mathf.Max(0.1f, replayBeforeSeconds + replayAfterSeconds)
                );

            if (replayProgress >= showGoalAt)
            {
                DrawGoalText(
                    screenWidth,
                    screenHeight,
                    64f
                );
            }

            if (GUI.Button(
                new Rect(
                    screenWidth * 0.5f - 85f,
                    screenHeight - 90f,
                    170f,
                    48f
                ),
                "Skip Replay"
            ))
            {
                SkipReplay();
            }
        }

        if (state == MatchState.MatchOver)
        {
            string winner = blueScore >= winningScore
                ? "<color=#4DA6FFFF>BLUE WINS!</color>"
                : "<color=#FF5555FF>RED WINS!</color>";

            GUI.Label(
                new Rect(
                    0f,
                    screenHeight * 0.35f,
                    screenWidth,
                    110f
                ),
                winner,
                messageStyle
            );

            GUI.Label(
                new Rect(
                    0f,
                    screenHeight * 0.52f,
                    screenWidth,
                    50f
                ),
                "Press R to play again",
                smallMessageStyle
            );
        }
    }

    private void DrawGoalText(
        float screenWidth,
        float screenHeight,
        float baseFontSize)
    {
        float pulse =
            1f +
            Mathf.Sin(
                Time.realtimeSinceStartup *
                goalTextPulseSpeed
            ) * 0.12f;

        GUIStyle goalStyle =
            new GUIStyle(GUI.skin.label);

        goalStyle.alignment =
            TextAnchor.MiddleCenter;

        goalStyle.fontSize =
            Mathf.RoundToInt(baseFontSize * pulse);

        goalStyle.fontStyle = FontStyle.Bold;
        goalStyle.richText = true;

        goalStyle.normal.textColor =
            new Color(1f, 0.86f, 0.12f);

        GUI.Label(
            new Rect(
                0f,
                screenHeight * 0.37f,
                screenWidth,
                140f
            ),
            "<color=#FFE23BFF>GOAL!</color>",
            goalStyle
        );
    }
}
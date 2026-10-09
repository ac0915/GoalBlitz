using Fusion;
using UnityEngine;

/// <summary>
/// Local player controls only their lobby team. The other avatar and the ball
/// are rendered from the host's snapshots, a few milliseconds behind, so motion
/// stays smooth instead of hitching every physics step.
/// </summary>
public class PitchTeamBinder : MonoBehaviour
{
    private struct Snapshot
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Time;
        public bool Valid;
    }

    [SerializeField] private PlayerController bluePlayer;
    [SerializeField] private PlayerController redPlayer;

    private NetworkRunner runner;
    private GoalBlitzLobbyState lobbyState;
    private bool boundOnce;
    private float nextRttLog;
    private Rigidbody2D ballBody;

    private Snapshot blueA;
    private Snapshot blueB;
    private Snapshot redA;
    private Snapshot redB;
    private Snapshot ballA;
    private Snapshot ballB;

    private const float RenderDelay = 0.045f;

    private void Start()
    {
        ResolvePlayers();
    }

    private void Update()
    {
        ResolveNetwork();
        if (!IsOnline())
        {
            return;
        }

        int localTeam = lobbyState.GetTeamForPlayer(runner.LocalPlayer);
        bool localIsBlue = localTeam == GoalBlitzLobbyState.BlueTeam;
        bool localIsRed = localTeam == GoalBlitzLobbyState.RedTeam;

        if (!localIsBlue)
        {
            RenderSnapshot(bluePlayer, blueA, blueB);
        }

        if (!localIsRed)
        {
            RenderSnapshot(redPlayer, redA, redB);
        }

        if (!lobbyState.HasStateAuthority)
        {
            RenderBall(ballA, ballB);
        }
    }

    private void FixedUpdate()
    {
        ResolvePlayers();
        ResolveNetwork();

        if (runner != null && runner.IsRunning && !IsLobbyAlive())
        {
            SetInput(bluePlayer, false);
            SetInput(redPlayer, false);
            return;
        }

        if (!IsOnline())
        {
            SetInput(bluePlayer, true);
            SetInput(redPlayer, true);
            SetSimulated(bluePlayer, true);
            SetSimulated(redPlayer, true);
            SetBallSimulated(true);
            return;
        }

        int localTeam = lobbyState.GetTeamForPlayer(runner.LocalPlayer);
        bool localIsBlue = localTeam == GoalBlitzLobbyState.BlueTeam;
        bool localIsRed = localTeam == GoalBlitzLobbyState.RedTeam;

        if (bluePlayer != null)
        {
            bluePlayer.teamId = GoalBlitzLobbyState.BlueTeam;
            SetInput(bluePlayer, localIsBlue);
            bluePlayer.useMobileInput = localIsBlue;
            SetSimulated(bluePlayer, localIsBlue);
        }

        if (redPlayer != null)
        {
            redPlayer.teamId = GoalBlitzLobbyState.RedTeam;
            SetInput(redPlayer, localIsRed);
            redPlayer.useMobileInput = localIsRed;
            SetSimulated(redPlayer, localIsRed);
        }

        SetBallSimulated(lobbyState.HasStateAuthority);

        if (!boundOnce)
        {
            boundOnce = true;
            Debug.Log(
                "PitchTeamBinder: shared match. Local team=" + localTeam +
                " (1=Blue, 2=Red). Blue input=" + localIsBlue +
                ", Red input=" + localIsRed,
                this
            );
        }

        if (Time.unscaledTime >= nextRttLog)
        {
            nextRttLog = Time.unscaledTime + 2f;
            double rttMs = runner.GetPlayerRtt(runner.LocalPlayer) * 1000.0;
            Debug.Log("PitchTeamBinder: RTT " + rttMs.ToString("0") + " ms");
        }

        if (localIsBlue && bluePlayer != null)
        {
            Report(bluePlayer, GoalBlitzLobbyState.BlueTeam);
        }
        else if (localIsRed && redPlayer != null)
        {
            Report(redPlayer, GoalBlitzLobbyState.RedTeam);
        }

        if (!localIsBlue)
        {
            Push(ref blueA, ref blueB, lobbyState.BluePosition, lobbyState.BlueVelocity);
        }

        if (!localIsRed)
        {
            Push(ref redA, ref redB, lobbyState.RedPosition, lobbyState.RedVelocity);
        }

        if (!lobbyState.HasStateAuthority && lobbyState.BallTick != 0)
        {
            Push(ref ballA, ref ballB, lobbyState.BallPosition, lobbyState.BallVelocity);
        }
    }

    private void Report(PlayerController controller, int team)
    {
        Rigidbody2D rb = controller.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            return;
        }

        lobbyState.RPC_ReportPlayerTransform(team, rb.position, rb.velocity);
    }

    private static void Push(ref Snapshot older, ref Snapshot newer, Vector2 position, Vector2 velocity)
    {
        if (position.sqrMagnitude < 0.0001f && velocity.sqrMagnitude < 0.0001f && !newer.Valid)
        {
            return;
        }

        if (newer.Valid &&
            (newer.Position - position).sqrMagnitude < 0.000001f &&
            (newer.Velocity - velocity).sqrMagnitude < 0.000001f)
        {
            return;
        }

        older = newer;
        newer.Position = position;
        newer.Velocity = velocity;
        newer.Time = Time.time;
        newer.Valid = true;
    }

    private static void RenderSnapshot(PlayerController controller, Snapshot older, Snapshot newer)
    {
        if (controller == null || !newer.Valid)
        {
            return;
        }

        Rigidbody2D rb = controller.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            return;
        }

        rb.position = Sample(older, newer);
        rb.velocity = newer.Velocity;
    }

    private void RenderBall(Snapshot older, Snapshot newer)
    {
        if (!newer.Valid)
        {
            return;
        }

        Rigidbody2D ball = FindBall();
        if (ball == null)
        {
            return;
        }

        ball.position = Sample(older, newer);
        ball.velocity = newer.Velocity;
    }

    private static Vector2 Sample(Snapshot older, Snapshot newer)
    {
        if (!older.Valid)
        {
            return newer.Position;
        }

        float span = Mathf.Max(0.001f, newer.Time - older.Time);
        float targetTime = Time.time - RenderDelay;
        float u = Mathf.Clamp01((targetTime - older.Time) / span);
        return Vector2.Lerp(older.Position, newer.Position, u);
    }

    private Rigidbody2D FindBall()
    {
        if (ballBody != null)
        {
            return ballBody;
        }

        GameObject tagged = GameObject.FindGameObjectWithTag("Ball");
        if (tagged != null)
        {
            ballBody = tagged.GetComponent<Rigidbody2D>();
        }

        if (ballBody != null)
        {
            return ballBody;
        }

        Rigidbody2D[] bodies = FindObjectsOfType<Rigidbody2D>();
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i].name.IndexOf("Ball", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ballBody = bodies[i];
                return ballBody;
            }
        }

        return null;
    }

    private static void SetInput(PlayerController controller, bool enabled)
    {
        if (controller == null)
        {
            return;
        }

        controller.SetNetworkInputEnabled(enabled);
        if (!enabled)
        {
            controller.useMobileInput = false;
        }
    }

    private static void SetSimulated(PlayerController controller, bool simulated)
    {
        if (controller == null)
        {
            return;
        }

        Rigidbody2D rb = controller.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            return;
        }

        RigidbodyType2D wanted = simulated
            ? RigidbodyType2D.Dynamic
            : RigidbodyType2D.Kinematic;

        if (rb.bodyType != wanted)
        {
            rb.velocity = Vector2.zero;
            rb.bodyType = wanted;
        }

        rb.interpolation = simulated
            ? RigidbodyInterpolation2D.Interpolate
            : RigidbodyInterpolation2D.None;
    }

    private void SetBallSimulated(bool simulated)
    {
        Rigidbody2D ball = FindBall();
        if (ball == null)
        {
            return;
        }

        RigidbodyType2D wanted = simulated
            ? RigidbodyType2D.Dynamic
            : RigidbodyType2D.Kinematic;

        if (ball.bodyType != wanted)
        {
            ball.velocity = Vector2.zero;
            ball.bodyType = wanted;
        }

        ball.interpolation = simulated
            ? RigidbodyInterpolation2D.Interpolate
            : RigidbodyInterpolation2D.None;
    }

    private bool IsLobbyAlive()
    {
        return lobbyState != null &&
               lobbyState.Object != null &&
               lobbyState.Object.IsValid;
    }

    private bool IsOnline()
    {
        return runner != null && runner.IsRunning && IsLobbyAlive();
    }

    private void ResolvePlayers()
    {
        if (bluePlayer != null && redPlayer != null)
        {
            return;
        }

        PlayerController[] controllers = FindObjectsOfType<PlayerController>();
        for (int i = 0; i < controllers.Length; i++)
        {
            PlayerController controller = controllers[i];
            string name = controller.gameObject.name;

            if (bluePlayer == null &&
                name.IndexOf("Blue", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bluePlayer = controller;
            }
            else if (redPlayer == null &&
                     name.IndexOf("Red", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                redPlayer = controller;
            }
        }
    }

    private void ResolveNetwork()
    {
        if (runner == null || !runner.IsRunning)
        {
            runner = FindObjectOfType<NetworkRunner>();
        }

        if (!IsLobbyAlive())
        {
            lobbyState = GoalBlitzLobbyState.Instance;
            if (lobbyState == null)
            {
                lobbyState = FindObjectOfType<GoalBlitzLobbyState>();
            }
        }
    }
}

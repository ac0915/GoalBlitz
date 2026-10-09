using Fusion;
using UnityEngine;

/// <summary>
/// Local player controls only their lobby team (Blue -> PlayerBlue, Red -> PlayerRed).
/// Remote avatar and the ball are copies of the host match, not a second local game.
/// Offline (no runner): both players stay locally controllable.
/// </summary>
public class PitchTeamBinder : MonoBehaviour
{
    [SerializeField] private PlayerController bluePlayer;
    [SerializeField] private PlayerController redPlayer;

    private NetworkRunner runner;
    private GoalBlitzLobbyState lobbyState;
    private bool boundOnce;
    private float nextRttLog;
    private Rigidbody2D ballBody;
    private Vector2 ballFrom;
    private Vector2 ballTo;
    private float ballLerp;

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

        if (!lobbyState.HasStateAuthority)
        {
            SmoothRemoteBall();
        }
    }

    private void FixedUpdate()
    {
        ResolvePlayers();
        ResolveNetwork();

        if (runner != null && runner.IsRunning && !IsLobbyAlive())
        {
            // Runner is up but the lobby object has not been found yet.
            // Do not fall back to offline, or both phones drive PlayerBlue.
            SetInput(bluePlayer, false);
            SetInput(redPlayer, false);
            return;
        }

        if (!IsOnline())
        {
            SetInput(bluePlayer, true);
            SetInput(redPlayer, true);
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
        }

        if (redPlayer != null)
        {
            redPlayer.teamId = GoalBlitzLobbyState.RedTeam;
            SetInput(redPlayer, localIsRed);
            redPlayer.useMobileInput = localIsRed;
        }

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

        if (!localIsBlue && bluePlayer != null)
        {
            ApplyRemote(bluePlayer, lobbyState.BluePosition, lobbyState.BlueVelocity);
        }

        if (!localIsRed && redPlayer != null)
        {
            ApplyRemote(redPlayer, lobbyState.RedPosition, lobbyState.RedVelocity);
        }

        ApplyBallAuthority();
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

    private static void ApplyRemote(
        PlayerController controller,
        Vector2 position,
        Vector2 velocity)
    {
        Rigidbody2D rb = controller.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            return;
        }

        if (position.sqrMagnitude < 0.0001f && velocity.sqrMagnitude < 0.0001f)
        {
            return;
        }

        rb.position = Vector2.Lerp(rb.position, position, 0.65f);
        rb.velocity = velocity;
    }

    private void ApplyBallAuthority()
    {
        Rigidbody2D ball = FindBall();
        if (ball == null)
        {
            return;
        }

        if (lobbyState.HasStateAuthority)
        {
            if (ball.bodyType != RigidbodyType2D.Dynamic)
            {
                ball.bodyType = RigidbodyType2D.Dynamic;
            }

            return;
        }

        if (ball.bodyType != RigidbodyType2D.Kinematic)
        {
            ball.bodyType = RigidbodyType2D.Kinematic;
            ball.velocity = Vector2.zero;
            ballFrom = ball.position;
            ballTo = lobbyState.BallPosition;
            ballLerp = 1f;
        }

        if (lobbyState.BallTick == 0 &&
            lobbyState.BallPosition.sqrMagnitude < 0.0001f &&
            lobbyState.BallVelocity.sqrMagnitude < 0.0001f)
        {
            return;
        }

        ballFrom = ball.position;
        ballTo = lobbyState.BallPosition;
        ballLerp = 0f;
        ball.velocity = lobbyState.BallVelocity;
    }

    private void SmoothRemoteBall()
    {
        Rigidbody2D ball = FindBall();
        if (ball == null)
        {
            return;
        }

        ballLerp = Mathf.MoveTowards(ballLerp, 1f, Time.deltaTime * 20f);
        ball.position = Vector2.Lerp(ballFrom, ballTo, ballLerp);
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

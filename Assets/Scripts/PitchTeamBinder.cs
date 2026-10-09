using Fusion;
using UnityEngine;

/// <summary>
/// On pitch load: local player only controls the avatar for their lobby team
/// (Blue -> PlayerBlue, Red -> PlayerRed). Also syncs positions through lobby state.
/// Works offline too (no runner = both players keep normal local control).
/// </summary>
public class PitchTeamBinder : MonoBehaviour
{
    [SerializeField] private PlayerController bluePlayer;
    [SerializeField] private PlayerController redPlayer;

    private NetworkRunner runner;
    private GoalBlitzLobbyState lobbyState;
    private bool boundOnce;

    private void Start()
    {
        ResolvePlayers();
    }

    private void FixedUpdate()
    {
        ResolvePlayers();
        ResolveNetwork();

        if (runner == null || !runner.IsRunning || lobbyState == null ||
            lobbyState.Object == null || !lobbyState.Object.IsValid)
        {
            // Offline / local Pitch.scene: leave controllers alone.
            if (bluePlayer != null)
            {
                bluePlayer.SetNetworkInputEnabled(true);
            }

            if (redPlayer != null)
            {
                redPlayer.SetNetworkInputEnabled(true);
            }

            return;
        }

        int localTeam = lobbyState.GetTeamForPlayer(runner.LocalPlayer);
        bool localIsBlue = localTeam == GoalBlitzLobbyState.BlueTeam;
        bool localIsRed = localTeam == GoalBlitzLobbyState.RedTeam;

        if (bluePlayer != null)
        {
            bluePlayer.teamId = GoalBlitzLobbyState.BlueTeam;
            bluePlayer.SetNetworkInputEnabled(localIsBlue);
            // Mobile stick drives whichever avatar this device owns.
            bluePlayer.useMobileInput = localIsBlue;
        }

        if (redPlayer != null)
        {
            redPlayer.teamId = GoalBlitzLobbyState.RedTeam;
            redPlayer.SetNetworkInputEnabled(localIsRed);
            redPlayer.useMobileInput = localIsRed;
        }

        if (!boundOnce)
        {
            boundOnce = true;
            Debug.Log(
                "PitchTeamBinder: Local team=" + localTeam +
                " (1=Blue, 2=Red). Blue input=" + localIsBlue +
                ", Red input=" + localIsRed,
                this
            );
        }

        // Send local avatar state to host for replication.
        if (localIsBlue && bluePlayer != null)
        {
            Rigidbody2D rb = bluePlayer.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                lobbyState.RPC_ReportPlayerTransform(
                    GoalBlitzLobbyState.BlueTeam,
                    rb.position,
                    rb.velocity
                );
            }
        }
        else if (localIsRed && redPlayer != null)
        {
            Rigidbody2D rb = redPlayer.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                lobbyState.RPC_ReportPlayerTransform(
                    GoalBlitzLobbyState.RedTeam,
                    rb.position,
                    rb.velocity
                );
            }
        }

        // Apply remote avatar from networked lobby state.
        if (!localIsBlue && bluePlayer != null)
        {
            ApplyRemote(bluePlayer, lobbyState.BluePosition, lobbyState.BlueVelocity);
        }

        if (!localIsRed && redPlayer != null)
        {
            ApplyRemote(redPlayer, lobbyState.RedPosition, lobbyState.RedVelocity);
        }
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

        // Skip until host has received at least one report (zeros at origin look wrong).
        if (position.sqrMagnitude < 0.0001f && velocity.sqrMagnitude < 0.0001f)
        {
            return;
        }

        rb.position = position;
        rb.velocity = velocity;
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

        if (lobbyState == null ||
            lobbyState.Object == null ||
            !lobbyState.Object.IsValid)
        {
            lobbyState = GoalBlitzLobbyState.Instance;
            if (lobbyState == null)
            {
                lobbyState = FindObjectOfType<GoalBlitzLobbyState>();
            }
        }
    }
}

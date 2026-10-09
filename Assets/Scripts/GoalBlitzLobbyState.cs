using Fusion;
using UnityEngine;

public class GoalBlitzLobbyState : NetworkBehaviour
{
    public const int NoTeam = 0;
    public const int BlueTeam = 1;
    public const int RedTeam = 2;

    public static GoalBlitzLobbyState Instance { get; private set; }

    // Survives the WaitingRoom -> pitch scene load if the networked object is destroyed.
    private static bool rosterCaptured;
    private static PlayerRef savedBlue = PlayerRef.None;
    private static PlayerRef savedRed = PlayerRef.None;
    private static NetworkString<_32> savedBlueName;
    private static NetworkString<_32> savedRedName;
    private static NetworkBool savedBlueReady;
    private static NetworkBool savedRedReady;

    [Networked]
    public PlayerRef BluePlayer { get; private set; }

    [Networked]
    public PlayerRef RedPlayer { get; private set; }

    [Networked]
    public NetworkString<_32> BluePlayerName { get; private set; }

    [Networked]
    public NetworkString<_32> RedPlayerName { get; private set; }

    [Networked]
    public NetworkBool BlueReady { get; private set; }

    [Networked]
    public NetworkBool RedReady { get; private set; }

    [Networked]
    public Vector2 BluePosition { get; private set; }

    [Networked]
    public Vector2 BlueVelocity { get; private set; }

    [Networked]
    public Vector2 RedPosition { get; private set; }

    [Networked]
    public Vector2 RedVelocity { get; private set; }

    [Networked]
    public Vector2 BallPosition { get; private set; }

    [Networked]
    public Vector2 BallVelocity { get; private set; }

    [Networked]
    public int BallTick { get; private set; }

    private Vector2 pendingBallVelocity;
    private bool hasPendingBallVelocity;
    private Rigidbody2D cachedBall;

    public static void CaptureRoster(GoalBlitzLobbyState state)
    {
        if (state == null || state.Object == null || !state.Object.IsValid)
        {
            return;
        }

        rosterCaptured = true;
        savedBlue = state.BluePlayer;
        savedRed = state.RedPlayer;
        savedBlueName = state.BluePlayerName;
        savedRedName = state.RedPlayerName;
        savedBlueReady = state.BlueReady;
        savedRedReady = state.RedReady;
    }

    public override void Spawned()
    {
        Instance = this;
        KeepAliveAcrossScenes();

        if (Object.HasStateAuthority)
        {
            RestoreRosterIfEmpty();
        }

        Debug.Log(
            "GoalBlitzLobbyState: Spawned(). StateAuthority=" +
            Object.HasStateAuthority,
            this
        );
    }

    public void KeepAliveAcrossScenes()
    {
        if (Runner != null)
        {
            transform.SetParent(Runner.transform, true);
        }

        DontDestroyOnLoad(gameObject);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        RemoveStalePlayers();
        ApplyPendingBallKick();
        PublishBall();
    }

    public bool IsBlueOccupied => BluePlayer != PlayerRef.None;

    public bool IsRedOccupied => RedPlayer != PlayerRef.None;

    public bool IsBluePlayer(PlayerRef player)
    {
        return BluePlayer == player;
    }

    public bool IsRedPlayer(PlayerRef player)
    {
        return RedPlayer == player;
    }

    public int GetTeamForPlayer(PlayerRef player)
    {
        if (IsBluePlayer(player))
        {
            return BlueTeam;
        }

        if (IsRedPlayer(player))
        {
            return RedTeam;
        }

        return NoTeam;
    }

    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority,
        HostMode = RpcHostMode.SourceIsHostPlayer
    )]
    public void RPC_RequestTeam(
        int requestedTeam,
        NetworkString<_32> playerName,
        RpcInfo info = default
    )
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        PlayerRef requestingPlayer = info.Source;

        if (requestedTeam != BlueTeam && requestedTeam != RedTeam)
        {
            return;
        }

        int currentTeam = GetTeamForPlayer(requestingPlayer);

        bool blueTakenByOtherPlayer =
            BluePlayer != PlayerRef.None &&
            BluePlayer != requestingPlayer;
        bool redTakenByOtherPlayer =
            RedPlayer != PlayerRef.None &&
            RedPlayer != requestingPlayer;

        if (requestedTeam == BlueTeam && blueTakenByOtherPlayer)
        {
            return;
        }

        if (requestedTeam == RedTeam && redTakenByOtherPlayer)
        {
            return;
        }

        if (currentTeam == BlueTeam && requestedTeam != BlueTeam)
        {
            ClearBlueSlot();
        }

        if (currentTeam == RedTeam && requestedTeam != RedTeam)
        {
            ClearRedSlot();
        }

        if (requestedTeam == BlueTeam)
        {
            BluePlayer = requestingPlayer;
            BluePlayerName = playerName;
            BlueReady = false;
            Debug.Log(
                "GoalBlitzLobbyState: BLUE assigned. Player=" + BluePlayer,
                this
            );
        }
        else
        {
            RedPlayer = requestingPlayer;
            RedPlayerName = playerName;
            RedReady = false;
            Debug.Log(
                "GoalBlitzLobbyState: RED assigned. Player=" + RedPlayer,
                this
            );
        }

        CaptureRoster(this);
    }

    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority,
        HostMode = RpcHostMode.SourceIsHostPlayer
    )]
    public void RPC_SetReady(NetworkBool ready, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        PlayerRef requestingPlayer = info.Source;

        if (BluePlayer == requestingPlayer)
        {
            BlueReady = ready;
        }
        else if (RedPlayer == requestingPlayer)
        {
            RedReady = ready;
        }

        CaptureRoster(this);
    }

    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority,
        Channel = RpcChannel.Unreliable,
        HostMode = RpcHostMode.SourceIsHostPlayer
    )]
    public void RPC_ReportPlayerTransform(
        int team,
        Vector2 position,
        Vector2 velocity,
        RpcInfo info = default
    )
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        PlayerRef source = info.Source;

        if (team == BlueTeam && BluePlayer == source)
        {
            BluePosition = position;
            BlueVelocity = velocity;
        }
        else if (team == RedTeam && RedPlayer == source)
        {
            RedPosition = position;
            RedVelocity = velocity;
        }
    }

    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority,
        Channel = RpcChannel.Unreliable,
        HostMode = RpcHostMode.SourceIsHostPlayer
    )]
    public void RPC_KickBall(Vector2 velocity, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        PlayerRef source = info.Source;
        if (source != BluePlayer && source != RedPlayer)
        {
            return;
        }

        pendingBallVelocity = velocity;
        hasPendingBallVelocity = true;

        Rigidbody2D ball = FindBall();
        if (ball != null)
        {
            ball.velocity = velocity;
        }
    }

    public void RemovePlayer(PlayerRef player)
    {
        if (!Object.HasStateAuthority)
        {
            Debug.LogWarning(
                "GoalBlitzLobbyState: RemovePlayer skipped, no state authority. Player=" +
                player,
                this
            );
            return;
        }

        bool removed = false;

        if (BluePlayer == player)
        {
            Debug.Log(
                "GoalBlitzLobbyState: Clearing BLUE slot for " + player,
                this
            );
            ClearBlueSlot();
            removed = true;
        }

        if (RedPlayer == player)
        {
            Debug.Log(
                "GoalBlitzLobbyState: Clearing RED slot for " + player,
                this
            );
            ClearRedSlot();
            removed = true;
        }

        if (!removed)
        {
            Debug.Log(
                "GoalBlitzLobbyState: RemovePlayer found no slot for " + player +
                ". Blue=" + BluePlayer + ", Red=" + RedPlayer,
                this
            );
        }

        CaptureRoster(this);
    }

    public void RemoveStalePlayers()
    {
        if (!Object.HasStateAuthority || Runner == null)
        {
            return;
        }

        if (BluePlayer != PlayerRef.None && !IsPlayerActive(BluePlayer))
        {
            Debug.Log(
                "GoalBlitzLobbyState: Stale BLUE player " + BluePlayer +
                " is not in ActivePlayers. Clearing.",
                this
            );
            ClearBlueSlot();
        }

        if (RedPlayer != PlayerRef.None && !IsPlayerActive(RedPlayer))
        {
            Debug.Log(
                "GoalBlitzLobbyState: Stale RED player " + RedPlayer +
                " is not in ActivePlayers. Clearing.",
                this
            );
            ClearRedSlot();
        }
    }

    public bool CanKickOff()
    {
        return IsBlueOccupied &&
               IsRedOccupied &&
               BlueReady &&
               RedReady;
    }

    private void RestoreRosterIfEmpty()
    {
        if (!rosterCaptured || !Object.HasStateAuthority)
        {
            return;
        }

        if (BluePlayer != PlayerRef.None || RedPlayer != PlayerRef.None)
        {
            return;
        }

        BluePlayer = savedBlue;
        RedPlayer = savedRed;
        BluePlayerName = savedBlueName;
        RedPlayerName = savedRedName;
        BlueReady = savedBlueReady;
        RedReady = savedRedReady;

        Debug.Log(
            "GoalBlitzLobbyState: Restored roster after scene load. Blue=" +
            BluePlayer + " Red=" + RedPlayer,
            this
        );
    }

    private void ApplyPendingBallKick()
    {
        if (!hasPendingBallVelocity)
        {
            return;
        }

        hasPendingBallVelocity = false;
        Rigidbody2D ball = FindBall();
        if (ball != null)
        {
            ball.velocity = pendingBallVelocity;
        }
    }

    private void PublishBall()
    {
        Rigidbody2D ball = FindBall();
        if (ball == null)
        {
            return;
        }

        BallPosition = ball.position;
        BallVelocity = ball.velocity;
        BallTick = Runner != null ? Runner.Tick.Raw : BallTick + 1;
    }

    private Rigidbody2D FindBall()
    {
        if (cachedBall != null)
        {
            return cachedBall;
        }

        GameObject tagged = GameObject.FindGameObjectWithTag("Ball");
        if (tagged != null)
        {
            cachedBall = tagged.GetComponent<Rigidbody2D>();
            if (cachedBall != null)
            {
                return cachedBall;
            }
        }

        Rigidbody2D[] bodies = FindObjectsOfType<Rigidbody2D>();
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i].gameObject.name.IndexOf(
                    "Ball",
                    System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                cachedBall = bodies[i];
                return cachedBall;
            }
        }

        return null;
    }

    private bool IsPlayerActive(PlayerRef player)
    {
        foreach (PlayerRef active in Runner.ActivePlayers)
        {
            if (active == player)
            {
                return true;
            }
        }

        return false;
    }

    private void ClearBlueSlot()
    {
        BluePlayer = PlayerRef.None;
        BluePlayerName = default;
        BlueReady = false;
    }

    private void ClearRedSlot()
    {
        RedPlayer = PlayerRef.None;
        RedPlayerName = default;
        RedReady = false;
    }
}

using Fusion;
using UnityEngine;

public class GoalBlitzLobbyState : NetworkBehaviour
{
    public const int NoTeam = 0;
    public const int BlueTeam = 1;
    public const int RedTeam = 2;

    public static GoalBlitzLobbyState Instance { get; private set; }

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

    public override void Spawned()
    {
        Instance = this;

        Debug.Log(
            "GoalBlitzLobbyState: Spawned(). StateAuthority=" +
            Object.HasStateAuthority,
            this
        );
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

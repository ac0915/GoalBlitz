using Fusion;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class WaitingRoomController : MonoBehaviour
{
    private Label roomTitleLabel;
    private Label roomCodeLabel;
    private Label blueTeamNameLabel;
    private Label redTeamNameLabel;
    private Label lobbyStatusLabel;
    private Label blueTeamCountLabel;
    private Label redTeamCountLabel;

    private VisualElement bluePlayerSlot;
    private VisualElement redPlayerSlot;

    private Button copyCodeButton;
    private Button leaveButton;
    private Button joinBlueButton;
    private Button joinRedButton;
    private Button readyButton;
    private Button startButton;

    private NetworkRunner runner;
    private GoalBlitzNetworkManager networkManager;
    private GoalBlitzLobbyState lobbyState;

    private string roomCode = "";
    private string localPlayerName = "";

    private void Start()
    {
        UIDocument document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;

        roomTitleLabel = root.Q<Label>("RoomTitleLabel");
        roomCodeLabel = root.Q<Label>("RoomCodeLabel");
        blueTeamNameLabel = root.Q<Label>("BlueTeamNameLabel");
        redTeamNameLabel = root.Q<Label>("RedTeamNameLabel");
        lobbyStatusLabel = root.Q<Label>("LobbyStatusLabel");
        blueTeamCountLabel = root.Q<Label>("BlueTeamCountLabel");
        redTeamCountLabel = root.Q<Label>("RedTeamCountLabel");

        bluePlayerSlot = root.Q<VisualElement>("BluePlayerSlot");
        redPlayerSlot = root.Q<VisualElement>("RedPlayerSlot");

        copyCodeButton = root.Q<Button>("CopyCodeButton");
        leaveButton = root.Q<Button>("LeaveButton");
        joinBlueButton = root.Q<Button>("JoinBlueButton");
        joinRedButton = root.Q<Button>("JoinRedButton");
        readyButton = root.Q<Button>("ReadyButton");
        startButton = root.Q<Button>("StartButton");

        if (roomTitleLabel == null ||
            roomCodeLabel == null ||
            blueTeamNameLabel == null ||
            redTeamNameLabel == null ||
            lobbyStatusLabel == null ||
            blueTeamCountLabel == null ||
            redTeamCountLabel == null ||
            bluePlayerSlot == null ||
            redPlayerSlot == null ||
            copyCodeButton == null ||
            leaveButton == null ||
            joinBlueButton == null ||
            joinRedButton == null ||
            readyButton == null ||
            startButton == null)
        {
            Debug.LogError(
                "WaitingRoomController: Required UI elements are missing. " +
                "Check GoalBlitz_WaitingRoom.uxml element names.",
                this
            );

            return;
        }

        copyCodeButton.clicked += CopyRoomCode;
        leaveButton.clicked += LeaveRoom;
        joinBlueButton.clicked += JoinBlueTeam;
        joinRedButton.clicked += JoinRedTeam;
        readyButton.clicked += ToggleReady;
        startButton.clicked += KickOff;

        localPlayerName =
            GoalBlitzNetworkManager.LocalPlayerName;

        if (string.IsNullOrWhiteSpace(localPlayerName))
        {
            localPlayerName = "Player";
        }

        networkManager =
            FindObjectOfType<GoalBlitzNetworkManager>();

        startButton.SetEnabled(false);

        RefreshRoomInfo();
        RefreshTeamUI();
    }

    private void OnDestroy()
    {
        if (copyCodeButton != null)
        {
            copyCodeButton.clicked -= CopyRoomCode;
        }

        if (leaveButton != null)
        {
            leaveButton.clicked -= LeaveRoom;
        }

        if (joinBlueButton != null)
        {
            joinBlueButton.clicked -= JoinBlueTeam;
        }

        if (joinRedButton != null)
        {
            joinRedButton.clicked -= JoinRedTeam;
        }

        if (readyButton != null)
        {
            readyButton.clicked -= ToggleReady;
        }

        if (startButton != null)
        {
            startButton.clicked -= KickOff;
        }
    }

    private void Update()
    {
        FindNetworkReferences();
        RefreshRoomInfo();
        RefreshTeamUI();
    }

    private void FindNetworkReferences()
    {
        if (runner == null || !runner.IsRunning)
        {
            runner = FindObjectOfType<NetworkRunner>();
        }

        if (networkManager == null)
        {
            networkManager =
                FindObjectOfType<GoalBlitzNetworkManager>();
        }

        if (lobbyState == null &&
            networkManager != null)
        {
            lobbyState = networkManager.LobbyState;
        }

        if (lobbyState == null)
        {
            lobbyState =
                FindObjectOfType<GoalBlitzLobbyState>();
        }
    }

    private void RefreshRoomInfo()
    {
        if (roomTitleLabel == null ||
            roomCodeLabel == null ||
            lobbyStatusLabel == null)
        {
            return;
        }

        string hostName =
            GoalBlitzNetworkManager.HostPlayerName;

        if (string.IsNullOrWhiteSpace(hostName))
        {
            hostName = "Player 1";
        }

        roomTitleLabel.text =
            $"{hostName.ToUpperInvariant()}'S ROOM";

        if (runner != null && runner.IsRunning)
        {
            roomCode = runner.SessionInfo.Name;
        }

        if (string.IsNullOrWhiteSpace(roomCode))
        {
            roomCode =
                GoalBlitzNetworkManager.CurrentRoomCode;
        }

        if (string.IsNullOrWhiteSpace(roomCode))
        {
            roomCode = "------";
        }

        roomCodeLabel.text = roomCode;

        if (runner == null || !runner.IsRunning)
        {
            lobbyStatusLabel.text =
                "Connecting to room...";
            return;
        }

        if (lobbyState == null ||
            lobbyState.Object == null ||
            !lobbyState.Object.IsValid)
        {
            lobbyStatusLabel.text =
                "Preparing lobby...";
            return;
        }

        int playerCount = 0;

        foreach (PlayerRef player in runner.ActivePlayers)
        {
            playerCount++;
        }

        int localTeam =
            lobbyState.GetTeamForPlayer(
                runner.LocalPlayer
            );

        bool localReady =
            localTeam == GoalBlitzLobbyState.BlueTeam
                ? lobbyState.BlueReady
                : localTeam == GoalBlitzLobbyState.RedTeam &&
                  lobbyState.RedReady;

        if (playerCount < 2)
        {
            lobbyStatusLabel.text =
                $"Waiting for another player... {playerCount} / 2";
        }
        else if (localTeam == GoalBlitzLobbyState.NoTeam)
        {
            lobbyStatusLabel.text =
                "Choose BLUE or RED team.";
        }
        else if (!localReady)
        {
            lobbyStatusLabel.text =
                "Choose READY when you are set.";
        }
        else if (!lobbyState.CanKickOff())
        {
            lobbyStatusLabel.text =
                "Ready. Waiting for the other player...";
        }
        else if (runner.IsServer)
        {
            lobbyStatusLabel.text =
                "Both players are ready. Host can kick off.";
        }
        else
        {
            lobbyStatusLabel.text =
                "Both players are ready. Waiting for host kickoff.";
        }
    }

    private void JoinBlueTeam()
    {
        Debug.Log(
            "WaitingRoomController: BLUE JOIN clicked.",
            this
        );

        RequestTeam(GoalBlitzLobbyState.BlueTeam);
    }

    private void JoinRedTeam()
    {
        Debug.Log(
            "WaitingRoomController: RED JOIN clicked.",
            this
        );

        RequestTeam(GoalBlitzLobbyState.RedTeam);
    }

    private void RequestTeam(int requestedTeam)
    {
        Debug.Log(
            "WaitingRoomController: RequestTeam called. " +
            "RequestedTeam=" + requestedTeam +
            ", RunnerNull=" + (runner == null) +
            ", RunnerRunning=" + (
                runner != null &&
                runner.IsRunning
            ) +
            ", LobbyStateNull=" + (lobbyState == null) +
            ", LobbyObjectNull=" + (
                lobbyState == null ||
                lobbyState.Object == null
            ) +
            ", LobbyObjectValid=" + (
                lobbyState != null &&
                lobbyState.Object != null &&
                lobbyState.Object.IsValid
            ) +
            ", LocalPlayerName=" + localPlayerName,
            this
        );

        if (runner == null ||
            !runner.IsRunning)
        {
            lobbyStatusLabel.text =
                "Network runner is not ready.";

            return;
        }

        if (lobbyState == null)
        {
            lobbyStatusLabel.text =
                "Lobby state reference is missing.";

            return;
        }

        if (lobbyState.Object == null ||
            !lobbyState.Object.IsValid)
        {
            lobbyStatusLabel.text =
                "Lobby state object is not valid.";

            return;
        }

        lobbyState.RPC_RequestTeam(
            requestedTeam,
            new NetworkString<_32>(localPlayerName)
        );

        Debug.Log(
            "WaitingRoomController: RPC_RequestTeam sent. " +
            "RequestedTeam=" + requestedTeam,
            this
        );
    }

    private void ToggleReady()
    {
        if (runner == null ||
            !runner.IsRunning ||
            lobbyState == null ||
            lobbyState.Object == null ||
            !lobbyState.Object.IsValid)
        {
            return;
        }

        int localTeam =
            lobbyState.GetTeamForPlayer(
                runner.LocalPlayer
            );

        if (localTeam == GoalBlitzLobbyState.NoTeam)
        {
            lobbyStatusLabel.text =
                "Choose a team before readying up.";

            return;
        }

        bool isReady =
            localTeam == GoalBlitzLobbyState.BlueTeam
                ? lobbyState.BlueReady
                : lobbyState.RedReady;

        lobbyState.RPC_SetReady(!isReady);
    }

    private void RefreshTeamUI()
    {
        if (blueTeamNameLabel == null ||
            redTeamNameLabel == null ||
            blueTeamCountLabel == null ||
            redTeamCountLabel == null ||
            bluePlayerSlot == null ||
            redPlayerSlot == null ||
            joinBlueButton == null ||
            joinRedButton == null ||
            readyButton == null ||
            startButton == null)
        {
            return;
        }

        bool lobbyReady =
            lobbyState != null &&
            lobbyState.Object != null &&
            lobbyState.Object.IsValid;

        bool blueOccupied =
            lobbyReady &&
            lobbyState.IsBlueOccupied;

        bool redOccupied =
            lobbyReady &&
            lobbyState.IsRedOccupied;

        string blueName = blueOccupied
            ? lobbyState.BluePlayerName.ToString()
            : "EMPTY";

        string redName = redOccupied
            ? lobbyState.RedPlayerName.ToString()
            : "EMPTY";

        blueTeamNameLabel.text = blueName;
        redTeamNameLabel.text = redName;

        blueTeamCountLabel.text =
            blueOccupied ? "1 / 1" : "0 / 1";

        redTeamCountLabel.text =
            redOccupied ? "1 / 1" : "0 / 1";

        bool isBlueLocalPlayer =
            runner != null &&
            lobbyReady &&
            lobbyState.IsBluePlayer(
                runner.LocalPlayer
            );

        bool isRedLocalPlayer =
            runner != null &&
            lobbyReady &&
            lobbyState.IsRedPlayer(
                runner.LocalPlayer
            );

        bluePlayerSlot.EnableInClassList(
            "team-selected",
            isBlueLocalPlayer
        );

        redPlayerSlot.EnableInClassList(
            "team-selected",
            isRedLocalPlayer
        );

        blueTeamNameLabel.EnableInClassList(
            "selected-player-name",
            isBlueLocalPlayer
        );

        redTeamNameLabel.EnableInClassList(
            "selected-player-name",
            isRedLocalPlayer
        );

        joinBlueButton.style.display =
            blueOccupied
                ? DisplayStyle.None
                : DisplayStyle.Flex;

        joinRedButton.style.display =
            redOccupied
                ? DisplayStyle.None
                : DisplayStyle.Flex;

        bool localHasTeam =
            isBlueLocalPlayer ||
            isRedLocalPlayer;

        readyButton.SetEnabled(localHasTeam);

        bool localReady =
            isBlueLocalPlayer
                ? lobbyReady && lobbyState.BlueReady
                : isRedLocalPlayer &&
                  lobbyReady &&
                  lobbyState.RedReady;

        readyButton.text = localReady
            ? "NOT READY"
            : "READY";

        bool hostCanKickOff =
            runner != null &&
            runner.IsServer &&
            lobbyReady &&
            lobbyState.CanKickOff();

        startButton.SetEnabled(hostCanKickOff);
    }

    private void KickOff()
    {
        if (runner == null ||
            !runner.IsServer ||
            lobbyState == null ||
            lobbyState.Object == null ||
            !lobbyState.Object.IsValid ||
            !lobbyState.CanKickOff())
        {
            return;
        }

        if (networkManager == null)
        {
            networkManager =
                FindObjectOfType<GoalBlitzNetworkManager>();
        }

        if (networkManager == null)
        {
            lobbyStatusLabel.text =
                "Kickoff failed. Network manager missing.";
            return;
        }

        lobbyStatusLabel.text = "Kickoff...";
        startButton.SetEnabled(false);
        networkManager.KickOffMatch();
    }

    private void CopyRoomCode()
    {
        if (string.IsNullOrWhiteSpace(roomCode) ||
            roomCode == "------")
        {
            return;
        }

        GUIUtility.systemCopyBuffer = roomCode;

        lobbyStatusLabel.text =
            $"Room code copied: {roomCode}";
    }

    private void LeaveRoom()
    {
        if (networkManager == null)
        {
            networkManager =
                FindObjectOfType<GoalBlitzNetworkManager>();
        }

        if (networkManager == null)
        {
            Debug.LogError(
                "WaitingRoomController: Could not find " +
                "GoalBlitzNetworkManager.",
                this
            );
            lobbyStatusLabel.text =
                "Could not leave room. " +
                "Network manager is missing.";
            return;
        }

        lobbyStatusLabel.text = "Leaving room...";
        networkManager.LeaveRoomAndReturnToMenu();
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GoalBlitzNetworkManager :
    MonoBehaviour,
    INetworkRunnerCallbacks
{
    [Header("Room Menu")]
    [SerializeField] private RoomMenuController roomMenuController;

    [Header("Room Settings")]
    [SerializeField] private int maxPlayers = 2;
    [SerializeField] private int roomCodeLength = 6;

    [Header("Lobby State")]
    [SerializeField] private NetworkObject lobbyStatePrefab;

    [Header("Scene Paths")]
    [SerializeField] private string waitingRoomScenePath =
        "Assets/Scenes/WaitingRoom.scene";
    [SerializeField] private string roomMenuScenePath =
        "Assets/Scenes/RoomMenu.scene";
    [SerializeField] private string onlinePitchScenePath =
        "Assets/Scenes/Pitch.scene";

    private NetworkRunner runner;
    private GoalBlitzLobbyState lobbyState;
    private bool isBusy;
    private bool isLeavingRoom;
    private bool hasLoadedWaitingRoom;

    public static string HostPlayerName { get; private set; }
    public static string LocalPlayerName { get; private set; }
    public static string CurrentRoomCode { get; private set; }

    public GoalBlitzLobbyState LobbyState => lobbyState;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnUnitySceneLoaded;
    }

    private void OnEnable()
    {
        SubscribeToMenu();
    }

    private void Start()
    {
        FindAndSubscribeToMenu();
    }

    private void OnDisable()
    {
        UnsubscribeFromMenu();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnUnitySceneLoaded;
        UnsubscribeFromMenu();
    }

    private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        FindAndSubscribeToMenu();
    }

    private void SubscribeToMenu()
    {
        if (roomMenuController == null)
        {
            return;
        }

        roomMenuController.CreateRequested -= CreateRoom;
        roomMenuController.JoinRequested -= JoinRoom;
        roomMenuController.CreateRequested += CreateRoom;
        roomMenuController.JoinRequested += JoinRoom;
    }

    private void UnsubscribeFromMenu()
    {
        if (roomMenuController == null)
        {
            return;
        }

        roomMenuController.CreateRequested -= CreateRoom;
        roomMenuController.JoinRequested -= JoinRoom;
    }

    private void FindAndSubscribeToMenu()
    {
        RoomMenuController found =
            FindObjectOfType<RoomMenuController>();

        if (found != null)
        {
            if (roomMenuController != found)
            {
                UnsubscribeFromMenu();
                roomMenuController = found;
            }

            SubscribeToMenu();
        }
    }

    private async void CreateRoom(string playerName)
    {
        if (isBusy || isLeavingRoom)
        {
            Debug.Log(
                "CreateRoom ignored. isBusy=" + isBusy +
                ", isLeavingRoom=" + isLeavingRoom,
                this
            );
            return;
        }

        string roomCode = GenerateRoomCode();
        HostPlayerName = playerName;
        LocalPlayerName = playerName;
        CurrentRoomCode = roomCode;

        await StartSession(
            GameMode.Host,
            roomCode,
            true,
            playerName
        );
    }

    private async void JoinRoom(string playerName, string roomCode)
    {
        if (isBusy || isLeavingRoom)
        {
            Debug.Log(
                "JoinRoom ignored. isBusy=" + isBusy +
                ", isLeavingRoom=" + isLeavingRoom,
                this
            );
            return;
        }

        string cleanedCode = roomCode.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(cleanedCode))
        {
            SetStatus("Enter a room code first.");
            return;
        }

        LocalPlayerName = playerName;
        CurrentRoomCode = cleanedCode;

        await StartSession(
            GameMode.Client,
            cleanedCode,
            false,
            playerName
        );
    }

    private async Task StartSession(
        GameMode gameMode,
        string roomCode,
        bool allowRoomCreation,
        string playerName
    )
    {
        isBusy = true;
        hasLoadedWaitingRoom = false;
        SetMenuButtons(false);
        SetStatus(
            gameMode == GameMode.Host
                ? "Creating room..."
                : "Joining room..."
        );

        try
        {
            await ShutdownRunner();

            GameObject runnerObject =
                new GameObject("GoalBlitz Network Runner");
            DontDestroyOnLoad(runnerObject);

            runner = runnerObject.AddComponent<NetworkRunner>();
            runner.AddCallbacks(this);
            runner.ProvideInput = true;

            NetworkSceneManagerDefault sceneManager =
                runnerObject.AddComponent<NetworkSceneManagerDefault>();

            StartGameArgs arguments = new StartGameArgs
            {
                GameMode = gameMode,
                SessionName = roomCode,
                PlayerCount = maxPlayers,
                EnableClientSessionCreation = allowRoomCreation,
                Scene = SceneRef.FromIndex(
                    SceneManager.GetActiveScene().buildIndex
                ),
                SceneManager = sceneManager
            };

            StartGameResult result = await runner.StartGame(arguments);

            if (!result.Ok)
            {
                string errorMessage =
                    $"Could not connect: {result.ErrorMessage}";
                Debug.LogError(
                    $"{errorMessage} ({result.ShutdownReason})",
                    this
                );
                SetStatus(errorMessage);
                await ShutdownRunner();
                return;
            }

            Debug.Log(
                $"Fusion connected. Mode: {gameMode}, " +
                $"Room: {roomCode}, Player: {playerName}",
                this
            );

            if (gameMode == GameMode.Host)
            {
                SetStatus(
                    $"Room created: {roomCode}. Opening waiting room..."
                );
                await LoadWaitingRoomAsHost();
            }
            else
            {
                SetStatus(
                    $"Joined room: {roomCode}. Waiting for host..."
                );
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            SetStatus("Connection failed. Check Console for details.");
            await ShutdownRunner();
        }
        finally
        {
            isBusy = false;
            SetMenuButtons(true);
        }
    }

    private async Task LoadWaitingRoomAsHost()
    {
        if (runner == null || !runner.IsRunning)
        {
            throw new InvalidOperationException(
                "Fusion runner is not running."
            );
        }

        if (!runner.IsServer)
        {
            throw new InvalidOperationException(
                "Only the host can load WaitingRoom.scene."
            );
        }

        int waitingRoomBuildIndex =
            SceneUtility.GetBuildIndexByScenePath(waitingRoomScenePath);

        if (waitingRoomBuildIndex < 0)
        {
            throw new InvalidOperationException(
                "WaitingRoom.scene is not enabled in Build Settings. Path: " +
                waitingRoomScenePath
            );
        }

        Debug.Log(
            "GoalBlitzNetworkManager: Requesting Waiting Room load.",
            this
        );

        NetworkSceneAsyncOp sceneLoadOperation = runner.LoadScene(
            SceneRef.FromIndex(waitingRoomBuildIndex),
            LoadSceneMode.Single
        );

        while (!sceneLoadOperation.IsDone)
        {
            await Task.Yield();
        }

        hasLoadedWaitingRoom = true;

        Debug.Log(
            "GoalBlitzNetworkManager: Waiting Room scene loaded. " +
            "Spawning lobby state.",
            this
        );

        SpawnLobbyState();
    }

    private void SpawnLobbyState()
    {
        if (runner == null ||
            !runner.IsRunning ||
            !runner.IsServer ||
            lobbyState != null)
        {
            return;
        }

        if (lobbyStatePrefab == null)
        {
            Debug.LogError(
                "GoalBlitzNetworkManager: Lobby State Prefab is not assigned.",
                this
            );
            return;
        }

        NetworkObject lobbyObject = runner.Spawn(lobbyStatePrefab);
        if (lobbyObject == null)
        {
            Debug.LogError(
                "GoalBlitzNetworkManager: Fusion returned null when spawning lobby state.",
                this
            );
            return;
        }

        lobbyState = lobbyObject.GetComponent<GoalBlitzLobbyState>();
        if (lobbyState == null)
        {
            Debug.LogError(
                "The assigned Lobby State Prefab does not contain GoalBlitzLobbyState.",
                this
            );
        }
    }

    public async void StartMatch()
    {
        if (runner == null || !runner.IsRunning || !runner.IsServer)
        {
            Debug.LogWarning(
                "GoalBlitzNetworkManager: StartMatch ignored (not host or runner down).",
                this
            );
            return;
        }

        GoalBlitzLobbyState state = GoalBlitzLobbyState.Instance;
        if (state == null || !state.CanKickOff())
        {
            Debug.LogWarning(
                "GoalBlitzNetworkManager: StartMatch ignored (teams not ready).",
                this
            );
            return;
        }

        // Always use Pitch.scene. Hardcoded so an old Inspector value
        // on the component cannot override it.
        const string pitchScenePath = "Assets/Scenes/Pitch.scene";
        onlinePitchScenePath = pitchScenePath;

        int pitchBuildIndex =
            SceneUtility.GetBuildIndexByScenePath(pitchScenePath);

        if (pitchBuildIndex < 0)
        {
            Debug.LogError(
                "Pitch.scene is not enabled in Build Settings. Path: " +
                pitchScenePath,
                this
            );
            return;
        }

        Debug.Log(
            "GoalBlitzNetworkManager: Loading Pitch for match. buildIndex=" +
            pitchBuildIndex,
            this
        );

        NetworkSceneAsyncOp sceneLoadOperation = runner.LoadScene(
            SceneRef.FromIndex(pitchBuildIndex),
            LoadSceneMode.Single
        );

        while (!sceneLoadOperation.IsDone)
        {
            await Task.Yield();
        }

        EnsurePitchTeamBinder();
    }

    private static void EnsurePitchTeamBinder()
    {
        if (UnityEngine.Object.FindObjectOfType<PitchTeamBinder>() != null)
        {
            return;
        }

        GameObject binderObject = new GameObject("PitchTeamBinder");
        binderObject.AddComponent<PitchTeamBinder>();
    }

    public async void LeaveRoomAndReturnToMenu()
    {
        Debug.Log(
            "=== LEAVE_CALLED === isBusy=" + isBusy +
            ", isLeavingRoom=" + isLeavingRoom +
            ", runnerNull=" + (runner == null) +
            ", runnerRunning=" + (runner != null && runner.IsRunning),
            this
        );

        if (isLeavingRoom)
        {
            Debug.Log("=== LEAVE_IGNORED (already leaving) ===", this);
            return;
        }

        isLeavingRoom = true;
        isBusy = true;

        try
        {
            Task shutdownTask = ShutdownRunner();
            Task timeoutTask = Task.Delay(5000);
            Task finished = await Task.WhenAny(shutdownTask, timeoutTask);

            if (finished != shutdownTask)
            {
                Debug.LogWarning(
                    "GoalBlitzNetworkManager: Shutdown timed out. Returning to menu anyway.",
                    this
                );
            }
            else
            {
                await shutdownTask;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
        finally
        {
            ClearRoomData();
            isBusy = false;
            isLeavingRoom = false;
            LoadRoomMenuScene();
        }
    }

    private void LoadRoomMenuScene()
    {
        int roomMenuBuildIndex =
            SceneUtility.GetBuildIndexByScenePath(roomMenuScenePath);

        if (roomMenuBuildIndex < 0)
        {
            Debug.LogError(
                "RoomMenu.scene is not enabled in Build Settings. Path: " +
                roomMenuScenePath,
                this
            );
            return;
        }

        Scene active = SceneManager.GetActiveScene();
        if (active.buildIndex == roomMenuBuildIndex)
        {
            FindAndSubscribeToMenu();
            return;
        }

        SceneManager.LoadScene(roomMenuBuildIndex, LoadSceneMode.Single);
    }

    private async Task ShutdownRunner()
    {
        if (runner == null)
        {
            lobbyState = null;
            hasLoadedWaitingRoom = false;
            return;
        }

        NetworkRunner runnerToShutdown = runner;
        runner = null;
        lobbyState = null;
        hasLoadedWaitingRoom = false;

        try
        {
            if (runnerToShutdown != null && runnerToShutdown.IsRunning)
            {
                Debug.Log(
                    "GoalBlitzNetworkManager: Calling runner.Shutdown().",
                    this
                );
                await runnerToShutdown.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError(
                "GoalBlitzNetworkManager: Exception during runner shutdown: " + ex,
                this
            );
        }

        if (runnerToShutdown != null)
        {
            Destroy(runnerToShutdown.gameObject);
        }
    }

    private void ClearRoomData()
    {
        HostPlayerName = "";
        LocalPlayerName = "";
        CurrentRoomCode = "";
        lobbyState = null;
        hasLoadedWaitingRoom = false;
    }

    public void OnObjectExitAOI(
        NetworkRunner callbackRunner,
        NetworkObject networkObject,
        PlayerRef player
    )
    {
    }

    public void OnObjectEnterAOI(
        NetworkRunner callbackRunner,
        NetworkObject networkObject,
        PlayerRef player
    )
    {
    }

    public void OnPlayerJoined(
        NetworkRunner callbackRunner,
        PlayerRef player
    )
    {
        Debug.Log(
            "GoalBlitzNetworkManager: Player joined: " + player +
            ", IsServer=" + (callbackRunner != null && callbackRunner.IsServer),
            this
        );

        if (callbackRunner != null && callbackRunner.IsServer)
        {
            GoalBlitzLobbyState state = GoalBlitzLobbyState.Instance;
            if (state != null)
            {
                state.RemoveStalePlayers();
            }
        }
    }

    public void OnPlayerLeft(
        NetworkRunner callbackRunner,
        PlayerRef player
    )
    {
        Debug.Log(
            "GoalBlitzNetworkManager: Player left: " + player +
            ", IsServer=" + (callbackRunner != null && callbackRunner.IsServer),
            this
        );

        if (callbackRunner == null || !callbackRunner.IsServer)
        {
            return;
        }

        GoalBlitzLobbyState state = GoalBlitzLobbyState.Instance;
        if (state == null)
        {
            Debug.LogWarning(
                "GoalBlitzNetworkManager: OnPlayerLeft - LobbyState.Instance is null; cannot remove " +
                player,
                this
            );
            return;
        }

        state.RemovePlayer(player);
        state.RemoveStalePlayers();
    }

    public void OnShutdown(
        NetworkRunner callbackRunner,
        ShutdownReason shutdownReason
    )
    {
        Debug.Log(
            "GoalBlitzNetworkManager: OnShutdown - " + shutdownReason +
            ", isLeavingRoom=" + isLeavingRoom,
            this
        );

        if (isLeavingRoom)
        {
            return;
        }

        ClearRoomData();
        isBusy = false;
        LoadRoomMenuScene();
    }

    public void OnDisconnectedFromServer(
        NetworkRunner callbackRunner,
        NetDisconnectReason reason
    )
    {
        Debug.Log(
            "GoalBlitzNetworkManager: OnDisconnectedFromServer - " + reason +
            ", isLeavingRoom=" + isLeavingRoom,
            this
        );

        if (isLeavingRoom)
        {
            return;
        }

        ClearRoomData();
        isBusy = false;
        LoadRoomMenuScene();
    }

    public void OnConnectRequest(
        NetworkRunner callbackRunner,
        NetworkRunnerCallbackArgs.ConnectRequest request,
        byte[] token
    )
    {
    }

    public void OnConnectFailed(
        NetworkRunner callbackRunner,
        NetAddress remoteAddress,
        NetConnectFailedReason reason
    )
    {
    }

    public void OnReliableDataReceived(
        NetworkRunner callbackRunner,
        PlayerRef player,
        ReliableKey key,
        ReadOnlySpan<byte> data
    )
    {
    }

    public void OnReliableDataProgress(
        NetworkRunner callbackRunner,
        PlayerRef player,
        ReliableKey key,
        float progress
    )
    {
    }

    public void OnInput(NetworkRunner callbackRunner, NetworkInput input)
    {
    }

    public void OnInputMissing(
        NetworkRunner callbackRunner,
        PlayerRef player,
        NetworkInput input
    )
    {
    }

    public void OnConnectedToServer(NetworkRunner callbackRunner)
    {
    }

    public void OnSessionListUpdated(
        NetworkRunner callbackRunner,
        List<SessionInfo> sessionList
    )
    {
    }

    public void OnCustomAuthenticationResponse(
        NetworkRunner callbackRunner,
        Dictionary<string, object> data
    )
    {
    }

    public void OnHostMigration(
        NetworkRunner callbackRunner,
        HostMigrationToken hostMigrationToken
    )
    {
    }

    public void OnSceneLoadDone(NetworkRunner callbackRunner)
    {
        EnsurePitchTeamBinder();
    }

    public void OnSceneLoadStart(NetworkRunner callbackRunner)
    {
    }

    private string GenerateRoomCode()
    {
        const string allowedCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        int safeLength = Mathf.Clamp(roomCodeLength, 4, 12);
        char[] roomCode = new char[safeLength];

        for (int i = 0; i < roomCode.Length; i++)
        {
            int characterIndex =
                UnityEngine.Random.Range(0, allowedCharacters.Length);
            roomCode[i] = allowedCharacters[characterIndex];
        }

        return new string(roomCode);
    }

    private void SetStatus(string message)
    {
        if (roomMenuController != null)
        {
            roomMenuController.SetStatus(message);
        }
    }

    private void SetMenuButtons(bool enabled)
    {
        if (roomMenuController != null)
        {
            roomMenuController.SetButtonsEnabled(enabled);
        }
    }
}

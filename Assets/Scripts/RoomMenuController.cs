using System;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class RoomMenuController : MonoBehaviour
{
    public event Action<string> CreateRequested;
    public event Action<string, string> JoinRequested;

    private TextField playerNameField;
    private TextField roomCodeField;
    private Button createButton;
    private Button joinButton;
    private Label statusLabel;

    private void OnEnable()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;

        playerNameField = root.Q<TextField>("PlayerNameField");
        roomCodeField = root.Q<TextField>("RoomCodeField");
        createButton = root.Q<Button>("CreateGame");
        joinButton = root.Q<Button>("JoinGame");
        statusLabel = root.Q<Label>("StatusLabel");

        if (playerNameField == null || roomCodeField == null ||
            createButton == null || joinButton == null || statusLabel == null)
        {
            Debug.LogError("RoomMenuController: UI elements missing. Check the UXML element names.", this);
            return;
        }

        createButton.clicked += HandleCreate;
        joinButton.clicked += HandleJoin;
        SetStatus("");
    }

    private void OnDisable()
    {
        if (createButton != null) createButton.clicked -= HandleCreate;
        if (joinButton != null) joinButton.clicked -= HandleJoin;
    }

    private void HandleCreate()
    {
        string playerName = GetPlayerName("Player 1");
        if (CreateRequested == null)
        {
            SetStatus("Create button works; room logic is not connected yet.");
            Debug.Log("Create requested by " + playerName);
            return;
        }

        SetStatus("Creating room...");
        CreateRequested.Invoke(playerName);
    }

    private void HandleJoin()
    {
        string code = roomCodeField.value == null
            ? ""
            : roomCodeField.value.Trim().ToUpperInvariant();

        if (code.Length == 0)
        {
            SetStatus("Enter a room code first.");
            return;
        }

        string playerName = GetPlayerName("Player 2");
        if (JoinRequested == null)
        {
            SetStatus("Join button works; room logic is not connected yet.");
            Debug.Log("Join requested by " + playerName + " for room " + code);
            return;
        }

        SetStatus("Joining room...");
        JoinRequested.Invoke(playerName, code);
    }

    private string GetPlayerName(string fallback)
    {
        string name = playerNameField.value == null ? "" : playerNameField.value.Trim();
        return name.Length == 0 ? fallback : name;
    }

    public void SetStatus(string message)
    {
        if (statusLabel != null) statusLabel.text = message;
    }

    public void SetButtonsEnabled(bool enabled)
    {
        if (createButton != null) createButton.SetEnabled(enabled);
        if (joinButton != null) joinButton.SetEnabled(enabled);
    }
}

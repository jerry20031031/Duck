using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class FusionSessionLauncher : MonoBehaviour
{
    public const int CurrentSetupVersion = 11;
    private const string PlayerNameKey = "PlayerName";
    private const string DefaultPlayerName = "小鴨";

    [SerializeField] private CharacterSelectionController characterSelection;
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private GameObject connectionPanel;
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button singlePlayerButton;
    [SerializeField] private Button joinRoomButton;
    [SerializeField] private GameObject colorPlatform;
    [SerializeField] private int maxPlayers = 8;
    [SerializeField] private Vector3 spawnOrigin = new(15.08f, 1.4f, -2.02f);
    [SerializeField, HideInInspector] private int setupVersion;

    private NetworkRunner runner;
    private bool isConnecting;

    public int SetupVersion => setupVersion;

    public void Configure(
        CharacterSelectionController selection,
        NetworkObject networkPlayerPrefab,
        GameObject panel,
        TMP_InputField nameInput,
        TMP_InputField roomInput,
        TMP_Text status,
        Button singlePlayer,
        Button joinRoom,
        GameObject multiplayerColorPlatform)
    {
        characterSelection = selection;
        playerPrefab = networkPlayerPrefab;
        connectionPanel = panel;
        playerNameInput = nameInput;
        roomNameInput = roomInput;
        statusText = status;
        singlePlayerButton = singlePlayer;
        joinRoomButton = joinRoom;
        colorPlatform = multiplayerColorPlatform;
        setupVersion = CurrentSetupVersion;
    }

    private void Awake()
    {
        ResolveSceneReferences();
        WireButtons();
        if (connectionPanel != null)
        {
            connectionPanel.SetActive(false);
        }

        if (playerNameInput != null)
        {
            playerNameInput.text = PlayerPrefs.GetString(PlayerNameKey, DefaultPlayerName);
        }

        if (colorPlatform != null)
        {
            colorPlatform.SetActive(false);
        }
    }

    private void ResolveSceneReferences()
    {
        characterSelection ??= FindFirstObjectByType<CharacterSelectionController>();
        connectionPanel ??= FindSceneObject("Fusion Connection Panel");
        colorPlatform ??= FindSceneObject("Multiplayer Color Platform");

        if (connectionPanel == null)
        {
            return;
        }

        playerNameInput ??= FindComponentInPanel<TMP_InputField>("Player Name Input");
        roomNameInput ??= FindComponentInPanel<TMP_InputField>("Room Name Input");
        statusText ??= FindComponentInPanel<TMP_Text>("Connection Status");
        singlePlayerButton ??= FindComponentInPanel<Button>("Play Single Player Button");
        joinRoomButton ??= FindComponentInPanel<Button>("Join Shared Room Button");
    }

    private T FindComponentInPanel<T>(string objectName) where T : Component
    {
        Transform child = connectionPanel.transform.Find(objectName);
        return child != null ? child.GetComponent<T>() : null;
    }

    private static GameObject FindSceneObject(string objectName)
    {
        GameObject[] sceneObjects = FindObjectsByType<GameObject>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < sceneObjects.Length; i++)
        {
            GameObject sceneObject = sceneObjects[i];
            if (sceneObject.scene.IsValid() && sceneObject.name == objectName)
            {
                return sceneObject;
            }
        }

        return null;
    }

    private void OnDestroy()
    {
        UnwireButtons();
    }

    public void ShowConnectionPanel()
    {
        SetStatus(string.Empty);
        if (connectionPanel != null)
        {
            connectionPanel.SetActive(true);
        }
    }

    private void StartSinglePlayer()
    {
        if (isConnecting)
        {
            return;
        }

        if (connectionPanel != null)
        {
            connectionPanel.SetActive(false);
        }

        string playerName = SaveAndGetPlayerName();
        characterSelection?.StartSinglePlayerWithCurrentSelection(playerName);
        if (colorPlatform != null)
        {
            colorPlatform.SetActive(true);
        }
    }

    private async void JoinSharedRoom()
    {
        if (isConnecting || playerPrefab == null)
        {
            return;
        }

        isConnecting = true;
        SaveAndGetPlayerName();
        SetButtonsInteractable(false);
        SetStatus("正在連線到 Photon…");

        GameObject runnerObject = new GameObject("Fusion Network Runner");
        runner = runnerObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = true;
        NetworkSceneManagerDefault sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();

        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = GetRoomName(),
            PlayerCount = Mathf.Max(2, maxPlayers),
            SceneManager = sceneManager
        });

        if (!result.Ok)
        {
            SetStatus("連線失敗：" + result.ErrorMessage);
            Destroy(runnerObject);
            runner = null;
            isConnecting = false;
            SetButtonsInteractable(true);
            return;
        }

        SpawnLocalPlayer();
        if (colorPlatform != null)
        {
            colorPlatform.SetActive(true);
        }

        characterSelection?.HideSelectionForMultiplayer();
        if (connectionPanel != null)
        {
            connectionPanel.SetActive(false);
        }
    }

    private void SpawnLocalPlayer()
    {
        if (runner == null || runner.TryGetPlayerObject(runner.LocalPlayer, out _))
        {
            return;
        }

        int slot = Mathf.Abs(runner.LocalPlayer.AsIndex) % 4;
        Vector3 offset = new(
            slot % 2 == 0 ? -1.5f : 1.5f,
            0f,
            slot / 2 == 0 ? -1.5f : 1.5f);
        NetworkObject playerObject = runner.Spawn(
            playerPrefab,
            spawnOrigin + offset,
            Quaternion.identity,
            runner.LocalPlayer);
        runner.SetPlayerObject(runner.LocalPlayer, playerObject);
        playerObject.GetComponent<FusionDuckPlayer>()?.SetPlayerName(SaveAndGetPlayerName());
    }

    private string GetRoomName()
    {
        string roomName = roomNameInput != null ? roomNameInput.text.Trim() : string.Empty;
        return string.IsNullOrWhiteSpace(roomName) ? "duck-room" : roomName;
    }

    private string SaveAndGetPlayerName()
    {
        string playerName = playerNameInput != null ? playerNameInput.text.Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(playerName))
        {
            playerName = DefaultPlayerName;
        }

        if (playerName.Length > 16)
        {
            playerName = playerName.Substring(0, 16);
        }

        PlayerPrefs.SetString(PlayerNameKey, playerName);
        PlayerPrefs.Save();
        return playerName;
    }

    private void WireButtons()
    {
        if (singlePlayerButton != null)
        {
            singlePlayerButton.onClick.RemoveListener(StartSinglePlayer);
            singlePlayerButton.onClick.AddListener(StartSinglePlayer);
        }

        if (joinRoomButton != null)
        {
            joinRoomButton.onClick.RemoveListener(JoinSharedRoom);
            joinRoomButton.onClick.AddListener(JoinSharedRoom);
        }
    }

    private void UnwireButtons()
    {
        singlePlayerButton?.onClick.RemoveListener(StartSinglePlayer);
        joinRoomButton?.onClick.RemoveListener(JoinSharedRoom);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (singlePlayerButton != null)
        {
            singlePlayerButton.interactable = interactable;
        }

        if (joinRoomButton != null)
        {
            joinRoomButton.interactable = interactable;
        }
    }

    private void SetStatus(string value)
    {
        if (statusText != null)
        {
            statusText.text = value;
        }
    }
}

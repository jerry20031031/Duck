using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The shared-room lobby gate. Every duck chooses ready in BigHall; the shared
/// room master loads UNIT1 only after every connected duck is ready.
/// </summary>
[DisallowMultipleComponent]
public sealed class FusionLobbyReadyController : MonoBehaviour
{
    private const string LobbySceneName = "BigHall";
    private const string GameSceneName = "UNIT1";
    private const string BoardCanvasName = "Lobby Ready Board Canvas";
    private const string FrameName = "Wood Parchment Frame";
    private const string StatusName = "Ready Status";
    private const string ButtonName = "Ready Button";
    private const string ButtonLabelName = "Label";
    private const int MinimumMatchPlayers = 1;
    private const int MinimumBattleParticipants = 4;
    private const int MaximumMatchPlayers = 6;

    private NetworkRunner runner;
    private Canvas canvas;
    private TMP_Text statusText;
    private Button readyButton;
    private TMP_Text readyButtonLabel;
    private bool gameStarting;

    public void Configure(NetworkRunner networkRunner)
    {
        runner = networkRunner;
        EnsureUi();
    }

    private void Update()
    {
        if (runner == null || !runner.IsRunning)
        {
            return;
        }

        if (SceneManager.GetActiveScene().name != LobbySceneName)
        {
            // The Canvas belongs to BigHall, so hide it rather than destroying
            // it. Designers can keep adjusting that scene object directly.
            if (canvas != null)
            {
                canvas.gameObject.SetActive(false);
            }
            Destroy(this);
            return;
        }

        EnsureUi();
        UpdateLobbyStatus();
    }

    private void EnsureUi()
    {
        if (canvas != null)
        {
            return;
        }

        canvas = FindBoardCanvas();
        if (canvas == null)
        {
            Debug.LogError("BigHall 缺少可調整的 Lobby Ready Board Canvas。請執行 Duck > Setup Fusion Multiplayer Prototype。", this);
            return;
        }

        canvas.gameObject.SetActive(true);
        canvas.worldCamera = Camera.main;
        Transform card = canvas.transform.Find("Ready Card");
        // The board content sits inside the parchment frame.  Fall back to
        // the card itself so older hand-authored board layouts still work.
        Transform content = card != null ? card.Find(FrameName) ?? card : null;
        statusText = content != null ? content.Find(StatusName)?.GetComponent<TMP_Text>() : null;
        readyButton = content != null ? content.Find(ButtonName)?.GetComponent<Button>() : null;
        readyButtonLabel = readyButton != null
            ? readyButton.transform.Find(ButtonLabelName)?.GetComponent<TMP_Text>()
            : null;

        if (readyButton != null)
        {
            readyButton.onClick.RemoveListener(ToggleLocalReady);
            readyButton.onClick.AddListener(ToggleLocalReady);
        }
    }

    private void UpdateLobbyStatus()
    {
        FusionDuckPlayer[] players = FindObjectsByType<FusionDuckPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int expectedPlayers = 0;
        foreach (PlayerRef ignored in runner.ActivePlayers)
        {
            expectedPlayers++;
        }

        int readyPlayers = 0;
        FusionDuckPlayer localPlayer = null;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null)
            {
                continue;
            }

            if (players[i].IsReady)
            {
                readyPlayers++;
            }

            if (players[i].HasStateAuthority)
            {
                localPlayer = players[i];
            }
        }

        bool localReady = localPlayer != null && localPlayer.IsReady;
        if (readyButton != null)
        {
            readyButton.interactable = localPlayer != null && !gameStarting;
        }

        if (readyButtonLabel != null)
        {
            readyButtonLabel.text = localReady ? "取消準備" : "準備完成";
        }

        if (statusText != null)
        {
            if (expectedPlayers < MinimumMatchPlayers)
            {
                statusText.text = "等待玩家連線中";
            }
            else if (expectedPlayers > MaximumMatchPlayers)
            {
                statusText.text = "房間人數超出 6 人上限";
            }
            else
            {
                int cpuCount = Mathf.Max(0, MinimumBattleParticipants - expectedPlayers);
                statusText.text = gameStarting
                    ? "所有人已準備，正在隨機抽取陣營…"
                    : "已準備 " + readyPlayers + " / " + expectedPlayers
                      + "　所有人準備後開始"
                      + (cpuCount > 0 ? "　開場補 " + cpuCount + " 隻電腦鴨" : string.Empty);
            }
        }

        bool everybodyReady = expectedPlayers >= MinimumMatchPlayers
            && expectedPlayers <= MaximumMatchPlayers
            && players.Length >= expectedPlayers
            && readyPlayers >= expectedPlayers;
        if (everybodyReady && runner.IsSharedModeMasterClient && !gameStarting && !runner.IsSceneManagerBusy)
        {
            gameStarting = true;
            runner.LoadScene(GameSceneName, LoadSceneMode.Single, LocalPhysicsMode.None, true);
        }
    }

    private void ToggleLocalReady()
    {
        FusionDuckPlayer[] players = FindObjectsByType<FusionDuckPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i].HasStateAuthority)
            {
                players[i].SetReady(!players[i].IsReady);
                return;
            }
        }
    }

    private static Canvas FindBoardCanvas()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null && canvases[i].name == BoardCanvasName)
            {
                return canvases[i];
            }
        }

        return null;
    }
}

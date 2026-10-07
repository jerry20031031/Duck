using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class MainMenuController : MonoBehaviour
{
    private const string StartButtonName = "Start Game Button";
    private const string SettingsButtonName = "Settings Button";
    private const string QuitButtonName = "Quit Game Button";

    [SerializeField] private string gameSceneName = "BigHall";
    [SerializeField] private AudioClip backgroundMusic;
    [SerializeField, Range(0f, 1f)] private float backgroundMusicVolume = 0.45f;

    private GameSettingsPanel settingsView;
    private GameObject[] mainMenuObjects;

    private void Awake()
    {
        GameSettings.ApplyAll();
        PlayBackgroundMusic();
        CacheMainMenuObjects();
        WireButton(StartButtonName, StartGame);
        WireButton(SettingsButtonName, OpenSettings);
        WireButton(QuitButtonName, QuitGame);
        BuildSettingsInterface();
    }

    private void PlayBackgroundMusic()
    {
        if (backgroundMusic == null)
        {
            Debug.LogWarning("Main menu background music is not assigned.");
            return;
        }

        AudioSource source = GetComponent<AudioSource>();
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
        }

        source.clip = backgroundMusic;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = backgroundMusicVolume;

        AudioChannelVolume channelVolume = GetComponent<AudioChannelVolume>();
        if (channelVolume == null)
        {
            channelVolume = gameObject.AddComponent<AudioChannelVolume>();
        }

        channelVolume.SetChannel(GameAudioChannel.Music);
        channelVolume.SetSourceVolume(backgroundMusicVolume);
        source.Play();
    }

    private void Update()
    {
        if (settingsView != null && settingsView.IsVisible
            && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            settingsView.HandleBack();
        }
    }

    public void StartGame()
    {
        GameSettings.Save();
        SceneManager.LoadScene(gameSceneName);
    }

    public void OpenSettings()
    {
        if (settingsView == null) return;
        SetMainMenuVisible(false);
        settingsView.ShowSettings();
    }

    public void CloseSettings()
    {
        if (settingsView == null) return;
        GameSettings.Save();
        settingsView.Hide();
        SetMainMenuVisible(true);
        EventSystem.current?.SetSelectedGameObject(GameObject.Find(SettingsButtonName));
    }

    public void OpenControls() => settingsView?.ShowControls();

    public void CloseControls() => settingsView?.ShowSettings();

    public void QuitGame()
    {
        GameSettings.Save();
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void CacheMainMenuObjects()
    {
        mainMenuObjects = new[]
        {
            GameObject.Find("Title"),
            GameObject.Find(StartButtonName),
            GameObject.Find(SettingsButtonName),
            GameObject.Find(QuitButtonName)
        };
    }

    private void BuildSettingsInterface()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("The main menu needs a Canvas.");
            return;
        }

        settingsView = new GameSettingsPanel(canvas.transform, "返回主選單", CloseSettings);
    }

    private void SetMainMenuVisible(bool visible)
    {
        foreach (GameObject menuObject in mainMenuObjects)
        {
            if (menuObject != null) menuObject.SetActive(visible);
        }
    }

    private static void WireButton(string buttonName, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = GameObject.Find(buttonName);
        if (buttonObject == null)
        {
            Debug.LogWarning("Missing main menu button: " + buttonName);
            return;
        }

        Button button = buttonObject.GetComponent<Button>();
        if (button == null)
        {
            Debug.LogWarning(buttonName + " needs a Button component.");
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }
}

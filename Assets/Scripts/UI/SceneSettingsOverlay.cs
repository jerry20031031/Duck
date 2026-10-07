using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Creates the shared wood/parchment settings page in the lobby and first level.
/// The page is local-only: opening it never pauses a shared Fusion match.
/// </summary>
[DisallowMultipleComponent]
public sealed class SceneSettingsOverlay : MonoBehaviour
{
    private const string LobbySceneName = "BigHall";
    private const string FirstLevelSceneName = "UNIT1";
    private const string OverlayObjectName = "Scene Settings Overlay";

    private static SceneSettingsOverlay activeOverlay;

    private GameSettingsPanel settingsView;
    private EventSystem ownedEventSystem;
    private CursorLockMode cursorLockBeforeOpening;
    private bool cursorVisibleBeforeOpening;

    /// <summary>True while either settings or controls owns the local player's input.</summary>
    public static bool IsOpen => activeOverlay != null
        && activeOverlay.settingsView != null && activeOverlay.settingsView.IsVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= CreateForSupportedScene;
        SceneManager.sceneLoaded += CreateForSupportedScene;
    }

    private static void CreateForSupportedScene(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != LobbySceneName && scene.name != FirstLevelSceneName) return;

        SceneSettingsOverlay[] overlays = FindObjectsByType<SceneSettingsOverlay>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (SceneSettingsOverlay overlay in overlays)
        {
            if (overlay != null && overlay.gameObject.scene == scene) return;
        }

        GameObject overlayObject = new(OverlayObjectName, typeof(RectTransform));
        SceneManager.MoveGameObjectToScene(overlayObject, scene);
        overlayObject.AddComponent<SceneSettingsOverlay>();
    }

    private void Awake()
    {
        activeOverlay = this;
        BuildInterface();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (settingsView != null && settingsView.IsVisible) settingsView.HandleBack();
            else Open();
        }
    }

    private void OnDestroy()
    {
        if (settingsView != null && settingsView.IsVisible)
        {
            GameSettings.Save();
            RestoreCursor();
        }
        if (ownedEventSystem != null) Destroy(ownedEventSystem.gameObject);
        if (activeOverlay == this) activeOverlay = null;
    }

    public void Open()
    {
        if (settingsView == null || settingsView.IsVisible) return;

        EnsureEventSystem();
        cursorLockBeforeOpening = Cursor.lockState;
        cursorVisibleBeforeOpening = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        settingsView.ShowSettings();
    }

    public void Close()
    {
        if (settingsView == null || !settingsView.IsVisible) return;

        GameSettings.Save();
        settingsView.Hide();
        RestoreCursor();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void BuildInterface()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        GameSettingsPanel.CreateShortcutButton(transform, Open);
        settingsView = new GameSettingsPanel(transform, "返回遊戲", Close);
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;

        GameObject eventSystemObject = new("Settings EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        SceneManager.MoveGameObjectToScene(eventSystemObject, gameObject.scene);
        eventSystemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        ownedEventSystem = eventSystemObject.GetComponent<EventSystem>();
    }

    private void RestoreCursor()
    {
        Cursor.lockState = cursorLockBeforeOpening;
        Cursor.visible = cursorVisibleBeforeOpening;
    }
}

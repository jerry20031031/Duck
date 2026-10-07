using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 本機死亡觀戰。只改鏡頭，不要求 StateAuthority、不送移動／攻擊輸入。
/// 真人隊友優先，其次 CPU、己方轉化鴨；不顯示敵人或中立灰影。
/// </summary>
[DisallowMultipleComponent]
public sealed class Unit1SpectatorController : MonoBehaviour
{
    private readonly List<NetworkBehaviour> candidates = new();
    private FusionDuckPlayer owner;
    private RPGCameraFollow cameraFollow;
    private Transform currentTarget;
    private Transform fallback;
    private GameObject hud;
    private GameObject ownedEventSystem;
    private TextMeshProUGUI status;
    private Button previousButton;
    private Button nextButton;
    private float refreshAt;
    private bool cameraBound;

    public bool IsSpectating { get; private set; }
    public Transform CurrentTarget => currentTarget;
    public int TargetCount => candidates.Count;
    public string StatusText => status != null ? status.text : string.Empty;

    public void Bind(FusionDuckPlayer player) => owner = player;

    private void Update()
    {
        if (owner == null || !owner.IsLocalPlayerObject)
        {
            if (hud != null) hud.SetActive(false);
            IsSpectating = false;
            cameraBound = false;
            return;
        }
        if (!owner.IsEliminated)
        {
            if (!cameraBound || IsSpectating)
            {
                cameraFollow = RPGCameraFollow.GetOrActivateMainCamera();
                cameraFollow?.SetTarget(owner.transform);
                cameraBound = true;
            }
            IsSpectating = false;
            candidates.Clear(); currentTarget = null;
            if (hud != null) hud.SetActive(false);
            return;
        }

        if (!IsSpectating)
        {
            IsSpectating = true;
            cameraFollow = RPGCameraFollow.GetOrActivateMainCamera();
            CreateHud();
            refreshAt = 0;
            currentTarget = null;
        }
        // The result screen owns the UI once the round is over.
        if (Unit1GameDirector.IsMatchFinished())
        {
            hud.SetActive(false);
            return;
        }
        hud.SetActive(true);
        if (Time.unscaledTime >= refreshAt)
        {
            RefreshTargets();
            refreshAt = Time.unscaledTime + 0.35f;
        }
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.leftArrowKey.wasPressedThisFrame) CycleTarget(-1);
            else if (keyboard.rightArrowKey.wasPressedThisFrame) CycleTarget(1);
        }
    }

    public void RefreshTargets()
    {
        if (owner == null || !owner.IsLocalPlayerObject || !owner.IsEliminated) return;
        candidates.Clear();
        foreach (FusionDuckPlayer player in FindObjectsByType<FusionDuckPlayer>(FindObjectsSortMode.None))
            if (IsPeer(player) && player != owner && !player.IsEliminated
                && FactionTeam.AreAllies(owner.FactionIndex, player.FactionIndex)) candidates.Add(player);
        foreach (Unit1BotDuck bot in FindObjectsByType<Unit1BotDuck>(FindObjectsSortMode.None))
            if (IsPeer(bot) && !bot.IsEliminated
                && FactionTeam.AreAllies(owner.FactionIndex, bot.FactionIndex)) candidates.Add(bot);
        foreach (EnemyDuckAI shadow in FindObjectsByType<EnemyDuckAI>(FindObjectsSortMode.None))
            if (IsPeer(shadow) && !shadow.IsDefeated
                && FactionTeam.AreAllies(owner.FactionIndex, shadow.FactionIndex)) candidates.Add(shadow);
        candidates.Sort((a, b) =>
        {
            int category = Category(a).CompareTo(Category(b));
            return category != 0 ? category : a.Object.Id.Raw.CompareTo(b.Object.Id.Raw);
        });
        NetworkBehaviour selected = candidates.Find(item => item.transform == currentTarget);
        if (selected == null) selected = candidates.Count > 0 ? candidates[0] : null;
        Select(selected);
    }

    private bool IsPeer(NetworkBehaviour item) => item != null && item.isActiveAndEnabled
        && item.Object != null && item.Object.IsValid && item.Runner == owner.Runner;
    private static int Category(NetworkBehaviour item) => item is FusionDuckPlayer ? 0 : item is Unit1BotDuck ? 1 : 2;

    public void CycleTarget(int direction)
    {
        if (!IsSpectating || owner == null || !owner.IsLocalPlayerObject || Unit1GameDirector.IsMatchFinished()) return;
        RefreshTargets();
        if (candidates.Count == 0) return;
        int index = candidates.FindIndex(item => item.transform == currentTarget);
        int next = (index + (direction < 0 ? -1 : 1) + candidates.Count) % candidates.Count;
        Select(candidates[next]);
    }

    private void Select(NetworkBehaviour selected)
    {
        Transform target;
        string label;
        if (selected != null)
        {
            target = selected.transform;
            label = selected is FusionDuckPlayer player ? player.PlayerName.ToString()
                : selected is Unit1BotDuck bot ? "CPU " + bot.BotNumber : "己方轉化鴨";
            int index = candidates.IndexOf(selected);
            status.text = "你已陣亡｜觀戰：" + label + "（" + (index + 1) + "/" + candidates.Count + "）";
        }
        else
        {
            FactionCrystal crystal = null;
            foreach (FactionCrystal item in FindObjectsByType<FactionCrystal>(FindObjectsSortMode.None))
                if (IsPeer(item) && !item.IsDestroyed && FactionTeam.AreAllies(owner.FactionIndex, item.FactionIndex))
                { crystal = item; break; }
            if (crystal != null)
            {
                target = crystal.transform;
                status.text = "你已陣亡｜沒有存活隊友，觀看己方水晶";
            }
            else
            {
                if (fallback == null)
                {
                    GameObject anchor = new("Spectator Death View");
                    SceneManager.MoveGameObjectToScene(anchor, gameObject.scene);
                    fallback = anchor.transform;
                }
                fallback.position = owner.GravePosition;
                target = fallback;
                status.text = "你已陣亡｜沒有可觀戰隊友，等待本局結束";
            }
        }
        previousButton.interactable = nextButton.interactable = candidates.Count > 1;
        if (target == currentTarget && cameraFollow != null && cameraFollow.FollowTarget == target) return;
        currentTarget = target;
        if (cameraFollow == null) cameraFollow = RPGCameraFollow.GetOrActivateMainCamera();
        cameraFollow?.SetTarget(target);
    }

    private void CreateHud()
    {
        if (hud != null) return;
        hud = new GameObject("Death Spectator HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(hud, gameObject.scene);
        Canvas canvas = hud.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 60;
        CanvasScaler scaler = hud.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        GameObject panel = new("Spectator Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(hud.transform, false);
        panel.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.07f, 0.93f);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
        rect.pivot = new Vector2(0.5f, 0);
        rect.anchoredPosition = new Vector2(0, 22); rect.sizeDelta = new Vector2(700, 112);
        status = MakeText(panel.transform, "Status", 25, new Vector2(670, 40), new Vector2(0, 27));
        TextMeshProUGUI hint = MakeText(panel.transform, "Controls", 19, new Vector2(360, 38), new Vector2(0, -25));
        hint.text = "←／→ 切換｜右鍵轉視角・滾輪縮放";
        hint.color = new Color(0.75f, 0.81f, 0.90f);
        previousButton = MakeButton(panel.transform, "上一位", new Vector2(-265, -25), -1);
        nextButton = MakeButton(panel.transform, "下一位", new Vector2(265, -25), 1);
        if (EventSystem.current == null && FindFirstObjectByType<EventSystem>() == null)
        {
            ownedEventSystem = new GameObject("Spectator EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            SceneManager.MoveGameObjectToScene(ownedEventSystem, gameObject.scene);
            ownedEventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }

    private Button MakeButton(Transform parent, string text, Vector2 position, int direction)
    {
        GameObject item = new(text, typeof(RectTransform), typeof(Image), typeof(Button));
        item.transform.SetParent(parent, false);
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(126, 38); rect.anchoredPosition = position;
        item.GetComponent<Image>().color = new Color(0.17f, 0.25f, 0.37f);
        Button button = item.GetComponent<Button>();
        button.targetGraphic = item.GetComponent<Image>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => CycleTarget(direction));
        MakeText(item.transform, "Label", 21, rect.sizeDelta, Vector2.zero).text = text;
        return button;
    }

    private static TextMeshProUGUI MakeText(Transform parent, string name, float size, Vector2 dimensions, Vector2 position)
    {
        GameObject item = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        item.transform.SetParent(parent, false);
        TextMeshProUGUI text = item.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset; text.fontSize = size;
        text.color = Color.white; text.alignment = TextAlignmentOptions.Center;
        text.richText = false; text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = dimensions; rect.anchoredPosition = position;
        return text;
    }

    private void OnDestroy()
    {
        if (hud != null) Destroy(hud);
        if (fallback != null) Destroy(fallback.gameObject);
        if (ownedEventSystem != null) Destroy(ownedEventSystem);
    }
}

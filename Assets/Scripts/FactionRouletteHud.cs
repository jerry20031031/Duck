using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Local-only presentation of UNIT1's authoritative faction draw. It has no
/// gameplay authority: the assigned faction is stored on FusionDuckPlayer.
/// </summary>
[DisallowMultipleComponent]
public sealed class FactionRouletteHud : MonoBehaviour
{
    private const string RootName = "Faction Roulette HUD";
    private const float SpinDuration = 1.75f;
    private const float ResultDuration = 1.25f;

    private TextMeshProUGUI spinningText;
    private TextMeshProUGUI resultText;
    private CanvasGroup canvasGroup;
    private Coroutine routine;

    public static FactionRouletteHud GetOrCreate()
    {
        FactionRouletteHud existing = FindFirstObjectByType<FactionRouletteHud>();
        if (existing != null)
        {
            return existing;
        }

        GameObject root = new(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        FactionRouletteHud hud = root.AddComponent<FactionRouletteHud>();
        hud.canvasGroup = root.GetComponent<CanvasGroup>();
        hud.BuildContent(root.transform);
        root.SetActive(false);
        return hud;
    }

    public void Play(int factionIndex)
    {
        factionIndex = Mathf.Clamp(factionIndex, 1, 6);
        gameObject.SetActive(true);
        canvasGroup.alpha = 1f;
        if (routine != null)
        {
            StopCoroutine(routine);
        }
        routine = StartCoroutine(SpinRoutine(factionIndex));
    }

    private void BuildContent(Transform parent)
    {
        GameObject panelObject = new("Roulette Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(parent, false);
        Image panel = panelObject.GetComponent<Image>();
        panel.color = new Color(0.025f, 0.035f, 0.09f, 0.92f);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = new Vector2(0f, 90f);
        panelRect.sizeDelta = new Vector2(620f, 245f);

        TextMeshProUGUI title = CreateText("Title", panelObject.transform, 31f, Color.white);
        title.text = "陣營輪盤";
        SetRect(title.rectTransform, new Vector2(520f, 46f), new Vector2(0f, 76f));

        spinningText = CreateText("Spinning Faction", panelObject.transform, 58f, Color.white);
        SetRect(spinningText.rectTransform, new Vector2(540f, 82f), new Vector2(0f, 5f));

        resultText = CreateText("Result", panelObject.transform, 23f, new Color(0.84f, 0.89f, 1f, 1f));
        SetRect(resultText.rectTransform, new Vector2(520f, 40f), new Vector2(0f, -73f));
    }

    private IEnumerator SpinRoutine(int resultFactionIndex)
    {
        resultText.text = "正在隨機抽選陣營…";
        float elapsed = 0f;
        int currentIndex = Random.Range(1, 7);
        while (elapsed < SpinDuration)
        {
            currentIndex = currentIndex % 6 + 1;
            SetFactionText(currentIndex, false);
            float interval = Mathf.Lerp(0.055f, 0.18f, elapsed / SpinDuration);
            yield return new WaitForSeconds(interval);
            elapsed += interval;
        }

        SetFactionText(resultFactionIndex, true);
        resultText.text = "你的陣營已確定";
        yield return new WaitForSeconds(ResultDuration);
        gameObject.SetActive(false);
        routine = null;
    }

    private void SetFactionText(int factionIndex, bool final)
    {
        Color color = FusionDuckPlayer.GetFactionColor(factionIndex);
        spinningText.color = color;
        spinningText.text = final
            ? "「" + FusionDuckPlayer.GetFactionName(factionIndex) + "色陣營」"
            : FusionDuckPlayer.GetFactionName(factionIndex) + "色";
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, float fontSize, Color color)
    {
        GameObject textObject = new(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.outlineColor = new Color(0.005f, 0.01f, 0.03f, 1f);
        text.outlineWidth = 0.18f;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CharacterSelectionController : MonoBehaviour
{
    private const string SelectedCharacterIndexKey = "SelectedCharacterIndex";
    private const string SelectedCharacterNameKey = "SelectedCharacterName";
    private const float DefaultColliderRadius = 0.45f;
    private const float DefaultColliderHeight = 2.1f;
    private static readonly Vector3 DefaultColliderCenter = new Vector3(0f, 1.05f, 0f);

    [System.Serializable]
    public sealed class CharacterPage
    {
        public string displayName;
        [TextArea(2, 4)] public string description;
        public GameObject model;
        public RuntimeAnimatorController animatorController;
    }

    [SerializeField] private CharacterPage[] pages = new CharacterPage[0];
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text pageText;
    [SerializeField] private TMP_Text selectedText;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button selectButton;
    [SerializeField] private GameObject selectionPanel;
    [SerializeField] private GameObject previewRoot;
    [SerializeField] private int selectablePageCount = 2;
    [SerializeField] private int currentIndex;

    private void Awake()
    {
        WireButtons();
        ShowPage(currentIndex);
    }

    private void OnEnable()
    {
        ShowPage(currentIndex);
    }

    private void OnDestroy()
    {
        if (previousButton != null)
        {
            previousButton.onClick.RemoveListener(PreviousPage);
        }

        if (nextButton != null)
        {
            nextButton.onClick.RemoveListener(NextPage);
        }

        if (selectButton != null)
        {
            selectButton.onClick.RemoveListener(SelectCurrentCharacter);
        }
    }

    public void PreviousPage()
    {
        ShowPage(currentIndex - 1);
    }

    public void NextPage()
    {
        ShowPage(currentIndex + 1);
    }

    public void SelectCurrentCharacter()
    {
        if (pages == null || pages.Length == 0)
        {
            return;
        }

        if (!IsCurrentPageSelectable())
        {
            SetText(selectedText, "這隻角色還沒開放");
            return;
        }

        CharacterPage page = pages[currentIndex];
        PlayerPrefs.SetInt(SelectedCharacterIndexKey, currentIndex);
        PlayerPrefs.SetString(SelectedCharacterNameKey, page.displayName);
        PlayerPrefs.Save();

        SetText(selectedText, "已選擇：" + page.displayName);
        EnterLobbyWithSelectedCharacter(page);
        Debug.Log("Selected character: " + page.displayName);
    }

    private void WireButtons()
    {
        if (previousButton != null)
        {
            previousButton.onClick.RemoveListener(PreviousPage);
            previousButton.onClick.AddListener(PreviousPage);
        }

        if (nextButton != null)
        {
            nextButton.onClick.RemoveListener(NextPage);
            nextButton.onClick.AddListener(NextPage);
        }

        if (selectButton != null)
        {
            selectButton.onClick.RemoveListener(SelectCurrentCharacter);
            selectButton.onClick.AddListener(SelectCurrentCharacter);
        }
    }

    private void ShowPage(int index)
    {
        if (pages == null || pages.Length == 0)
        {
            SetText(titleText, "沒有角色資料");
            SetText(descriptionText, "先把角色模型指定到 Character Select Controller。");
            SetText(pageText, "0 / 0");
            SetText(selectedText, string.Empty);
            return;
        }

        currentIndex = Mathf.Clamp(index, 0, pages.Length - 1);

        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i].model != null)
            {
                pages[i].model.SetActive(i == currentIndex);
            }
        }

        CharacterPage page = pages[currentIndex];
        SetText(titleText, page.displayName);
        SetText(descriptionText, page.description);
        SetText(pageText, (currentIndex + 1) + " / " + pages.Length);
        SetText(selectedText, string.Empty);

        if (previousButton != null)
        {
            previousButton.gameObject.SetActive(currentIndex > 0);
        }

        if (nextButton != null)
        {
            nextButton.gameObject.SetActive(currentIndex < pages.Length - 1);
        }

        if (selectButton != null)
        {
            selectButton.gameObject.SetActive(IsCurrentPageSelectable());
        }

        if (!IsCurrentPageSelectable())
        {
            SetText(selectedText, "這隻角色還沒開放");
        }
    }

    private void EnterLobbyWithSelectedCharacter(CharacterPage page)
    {
        GameObject selectedModel = page.model;
        if (selectedModel == null)
        {
            return;
        }

        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i].model != null)
            {
                pages[i].model.SetActive(pages[i].model == selectedModel);
            }
        }

        selectedModel.name = page.displayName + " Player";
        selectedModel.SetActive(true);
        if (previewRoot == null && selectedModel.transform.parent != null)
        {
            previewRoot = selectedModel.transform.parent.gameObject;
        }

        if (previewRoot != null)
        {
            previewRoot.SetActive(true);
        }

        DisableTurntable(selectedModel);
        ConfigureAnimator(selectedModel, page.animatorController);
        ConfigurePlayerPhysics(selectedModel);
        EnableDuckMover(selectedModel);
        HideSelectionCanvas();
    }

    private static void ConfigureAnimator(GameObject player, RuntimeAnimatorController controller)
    {
        Animator animator = player.GetComponent<Animator>();
        if (animator == null)
        {
            animator = player.GetComponentInChildren<Animator>();
        }

        if (animator == null)
        {
            animator = player.AddComponent<Animator>();
        }

        if (controller != null)
        {
            animator.runtimeAnimatorController = controller;
        }

        animator.applyRootMotion = false;
        animator.enabled = true;
    }

    private void DisableTurntable(GameObject selectedModel)
    {
        PreviewTurntable turntable = selectedModel.GetComponent<PreviewTurntable>();
        if (turntable != null)
        {
            turntable.enabled = false;
        }
    }

    private static void ConfigurePlayerPhysics(GameObject player)
    {
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        if (capsule == null)
        {
            capsule = player.AddComponent<CapsuleCollider>();
            ConfigureDefaultCollider(capsule);
        }
        else
        {
            capsule.enabled = true;
            capsule.isTrigger = false;
        }

        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = player.AddComponent<Rigidbody>();
        }

        body.useGravity = true;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private static void EnableDuckMover(GameObject player)
    {
        DuckMover mover = player.GetComponent<DuckMover>();
        if (mover == null)
        {
            mover = player.AddComponent<DuckMover>();
        }

        mover.enabled = true;
    }

    private void HideSelectionCanvas()
    {
        GameObject panel = selectionPanel;
        if (panel == null && selectButton != null)
        {
            panel = selectButton.transform.parent.gameObject;
        }

        Canvas canvas = panel != null ? panel.GetComponentInParent<Canvas>() : null;
        if (canvas != null)
        {
            canvas.gameObject.SetActive(false);
            return;
        }

        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    private bool IsCurrentPageSelectable()
    {
        int availablePages = Mathf.Clamp(selectablePageCount, 0, pages != null ? pages.Length : 0);
        return currentIndex >= 0 && currentIndex < availablePages;
    }

    private static void ConfigureDefaultCollider(CapsuleCollider capsule)
    {
        capsule.direction = 1;
        capsule.center = DefaultColliderCenter;
        capsule.height = DefaultColliderHeight;
        capsule.radius = DefaultColliderRadius;
        capsule.enabled = true;
        capsule.isTrigger = false;
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
        {
            text.text = value;
        }
    }
}

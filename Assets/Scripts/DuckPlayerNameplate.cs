using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DuckPlayerNameplate : MonoBehaviour
{
    private const string NameplateObjectName = "Player Nameplate";
    private const float DefaultHeight = 2.45f;
    private const float HeadClearance = 0.35f;
    private const float NameFontSize = 6f;

    [SerializeField] private TextMeshPro label;
    private Camera mainCamera;

    public void Configure(TextMeshPro nameLabel)
    {
        label = nameLabel;
        if (label != null)
        {
            StyleLabel();
            PositionAboveHead();
        }
    }

    public void SetDisplayName(string playerName)
    {
        EnsureLabel();
        StyleLabel();
        PositionAboveHead();
        label.text = string.IsNullOrWhiteSpace(playerName) ? "小鴨" : playerName.Trim();
        label.gameObject.SetActive(true);
        label.ForceMeshUpdate();
    }

    public void RefreshPosition()
    {
        EnsureLabel();
        PositionAboveHead();
    }

    private void LateUpdate()
    {
        if (label == null || !label.gameObject.activeInHierarchy)
        {
            return;
        }

        if (mainCamera == null || !mainCamera.isActiveAndEnabled)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera != null)
        {
            Vector3 direction = label.transform.position - mainCamera.transform.position;
            if (direction.sqrMagnitude > 0.001f)
            {
                label.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }
    }

    private void EnsureLabel()
    {
        if (label != null)
        {
            return;
        }

        Transform existing = transform.Find(NameplateObjectName);
        GameObject labelObject = existing != null ? existing.gameObject : new GameObject(NameplateObjectName);
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = Vector3.up * DefaultHeight;
        labelObject.transform.localRotation = Quaternion.identity;
        labelObject.transform.localScale = Vector3.one * 0.22f;

        label = labelObject.GetComponent<TextMeshPro>();
        if (label == null)
        {
            label = labelObject.AddComponent<TextMeshPro>();
        }

        StyleLabel();
        PositionAboveHead();
        mainCamera = Camera.main;
    }

    private void StyleLabel()
    {
        TMP_FontAsset uiFont = TMP_Settings.defaultFontAsset;
        if (uiFont != null && label.font != uiFont)
        {
            label.font = uiFont;
            label.fontSharedMaterial = uiFont.material;
        }

        if (label.font != null && label.fontSharedMaterial == null)
        {
            label.fontSharedMaterial = label.font.material;
        }

        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = NameFontSize;
        label.color = Color.white;
        if (Application.isPlaying)
        {
            label.outlineColor = new Color32(20, 24, 32, 255);
            label.outlineWidth = 0.22f;
        }

        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = new Vector2(14f, 3f);
        label.raycastTarget = false;
    }

    private void PositionAboveHead()
    {
        if (label == null)
        {
            return;
        }

        float height = DefaultHeight;
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            height = capsule.center.y + capsule.height * 0.5f + HeadClearance;
        }

        label.transform.localPosition = Vector3.up * height;
    }
}

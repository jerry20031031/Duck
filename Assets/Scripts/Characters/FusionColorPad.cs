using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class FusionColorPad : MonoBehaviour
{
    [SerializeField] private int colorIndex;

    public void Configure(int index)
    {
        colorIndex = Mathf.Clamp(index, 0, FusionDuckPlayer.SkinColorCount - 1);
    }

    private void Reset()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
        {
            trigger.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        FusionDuckPlayer player = other.GetComponentInParent<FusionDuckPlayer>();
        if (player != null)
        {
            if (player.HasStateAuthority)
            {
                player.SetSkinColor(colorIndex);
            }

            return;
        }

        DuckSinglePlayerAppearance singlePlayer = other.GetComponentInParent<DuckSinglePlayerAppearance>();
        singlePlayer?.SetSkinColor(colorIndex);
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckGame.Vfx
{
    public sealed class RuneCircleCaster : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private Key castKey = Key.R;

        [Header("Placement")]
        [SerializeField] private RuneCircleVfx runeCirclePrefab;
        [SerializeField] private float castDistance = 1.8f;
        [SerializeField] private float groundSearchHeight = 5f;
        [SerializeField] private float groundOffset = 0.035f;
        [SerializeField] private LayerMask groundLayers = ~0;

        [Header("VFX")]
        [SerializeField] private float radius = 1.75f;
        [SerializeField] private float duration = 2.4f;
        [SerializeField] private Color primaryColor = new Color(0.25f, 0.92f, 1f, 1f);
        [SerializeField] private Color secondaryColor = new Color(1f, 0.42f, 0.95f, 1f);

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || castKey == Key.None || !keyboard[castKey].wasPressedThisFrame)
            {
                return;
            }

            CastRuneCircle();
        }

        [ContextMenu("Cast Rune Circle")]
        public void CastRuneCircle()
        {
            Vector3 castPosition = GetCastPosition();
            RuneCircleVfx runeCircle = CreateRuneCircle(castPosition);
            runeCircle.Configure(radius, duration, primaryColor, secondaryColor);

            if (runeCircle.gameObject.activeSelf)
            {
                runeCircle.Play();
            }
            else
            {
                runeCircle.gameObject.SetActive(true);
            }
        }

        private RuneCircleVfx CreateRuneCircle(Vector3 castPosition)
        {
            if (runeCirclePrefab != null)
            {
                return Instantiate(runeCirclePrefab, castPosition, Quaternion.identity);
            }

            GameObject runeCircleObject = new GameObject("Rune Circle VFX");
            runeCircleObject.SetActive(false);
            runeCircleObject.transform.position = castPosition;
            return runeCircleObject.AddComponent<RuneCircleVfx>();
        }

        private Vector3 GetCastPosition()
        {
            Vector3 flatForward = transform.forward;
            flatForward.y = 0f;

            if (flatForward.sqrMagnitude < 0.001f)
            {
                flatForward = Vector3.forward;
            }

            Vector3 targetPosition = transform.position + flatForward.normalized * castDistance;
            Vector3 rayStart = targetPosition + Vector3.up * groundSearchHeight;

            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, groundSearchHeight * 2f, groundLayers, QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * groundOffset;
            }

            return targetPosition + Vector3.up * groundOffset;
        }
    }
}

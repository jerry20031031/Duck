using UnityEngine;

[DisallowMultipleComponent]
public sealed class PreviewTurntable : MonoBehaviour
{
    [SerializeField] private Vector3 rotationAxis = Vector3.up;
    [SerializeField] private float degreesPerSecond = 30f;

    private void Update()
    {
        transform.Rotate(rotationAxis, degreesPerSecond * Time.deltaTime, Space.World);
    }
}

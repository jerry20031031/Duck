using UnityEngine;

public enum GameAudioChannel
{
    Music,
    SoundEffect
}

/// <summary>
/// Add this component beside an AudioSource so the music and sound-effect
/// sliders can control that source independently. Master volume is handled by
/// the AudioListener and therefore applies to every source automatically.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class AudioChannelVolume : MonoBehaviour
{
    [SerializeField] private GameAudioChannel channel = GameAudioChannel.SoundEffect;

    private AudioSource audioSource;
    private float sourceVolume = 1f;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        sourceVolume = audioSource.volume;
    }

    private void OnEnable()
    {
        GameSettings.AudioVolumesChanged += ApplyVolume;
        ApplyVolume();
    }

    private void OnDisable()
    {
        GameSettings.AudioVolumesChanged -= ApplyVolume;
    }

    public void SetSourceVolume(float value)
    {
        sourceVolume = Mathf.Clamp01(value);
        ApplyVolume();
    }

    public void SetChannel(GameAudioChannel value)
    {
        channel = value;
        ApplyVolume();
    }

    private void ApplyVolume()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        float channelVolume = channel == GameAudioChannel.Music
            ? GameSettings.MusicVolume
            : GameSettings.SfxVolume;
        audioSource.volume = sourceVolume * channelVolume;
    }
}

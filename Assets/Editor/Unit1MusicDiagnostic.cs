using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class Unit1MusicDiagnostic
{
    private const string Output = "Temp/unit1-music-diagnostic.txt";
    private const string Pending = "Unit1MusicDiagnostic.PlaybackPending";
    private const string Restore = "Unit1MusicDiagnostic.RestoreScene";
    private static double startedAt;
    private static int firstSample;
    private static float peak;
    private static AudioSource music;
    private static readonly float[] samples = new float[1024];

    static Unit1MusicDiagnostic()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += () =>
        {
            Inspect();
            if (!SessionState.GetBool("Unit1MusicDiagnostic.Checked", false))
            {
                SessionState.SetBool("Unit1MusicDiagnostic.Checked", true);
                RunPlaybackCheck();
            }
        };
    }

    [MenuItem("Tools/Audio/Test UNIT1 Music")]
    private static void RunPlaybackCheck()
    {
        if (EditorApplication.isPlaying || SceneManager.GetActiveScene().isDirty) return;
        SessionState.SetString(Restore, SceneManager.GetActiveScene().path);
        EditorSceneManager.OpenScene("Assets/Scenes/UNIT1.unity", OpenSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayMode(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var listener = new GameObject("Temporary Music Check Listener");
            listener.transform.position = new Vector3(1000f, 1000f, 1000f);
            listener.AddComponent<AudioListener>();
            music = GameObject.Find("unit1")?.GetComponent<AudioSource>();
            startedAt = EditorApplication.timeSinceStartup;
            firstSample = music != null ? music.timeSamples : 0;
            peak = 0f;
            EditorApplication.update += ObservePlayback;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Pending, false);
            string original = SessionState.GetString(Restore, "");
            if (!string.IsNullOrEmpty(original)) EditorSceneManager.OpenScene(original, OpenSceneMode.Single);
        }
    }

    private static void ObservePlayback()
    {
        AudioListener.GetOutputData(samples, 0);
        foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
        if (EditorApplication.timeSinceStartup - startedAt < 6f) return;
        EditorApplication.update -= ObservePlayback;
        Inspect();
        bool passed = music != null && music.isPlaying && music.spatialBlend == 0f
            && music.timeSamples > firstSample && peak > 0.00001f;
        string result = "Playback=" + (passed ? "PASS" : "FAIL") + " listenerAtDistance=1732m peak=" + peak
            + " samplesAdvanced=" + (music != null ? music.timeSamples - firstSample : 0);
        File.AppendAllText(Output, result + Environment.NewLine);
        Debug.Log("[UNIT1 MUSIC] " + result);
        EditorApplication.isPlaying = false;
    }

    [MenuItem("Tools/Audio/Inspect UNIT1 Music")]
    private static void Inspect()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var report = new System.Text.StringBuilder();
        report.AppendLine("Playing=" + EditorApplication.isPlaying + " active=" + SceneManager.GetActiveScene().path);
        report.AppendLine("Master=" + GameSettings.MasterVolume + " Music=" + GameSettings.MusicVolume
            + " listenerVolume=" + AudioListener.volume + " listenerPause=" + AudioListener.pause);
        report.AppendLine("EditorAudioMute=" + EditorUtility.audioMasterMute);
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/UNIT1.unity");
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened && !EditorApplication.isPlaying)
            scene = EditorSceneManager.OpenScene("Assets/Scenes/UNIT1.unity", OpenSceneMode.Additive);
        try
        {
            foreach (AudioSource source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                report.AppendLine("Source=" + source.name + " scene=" + source.gameObject.scene.path + " active=" + source.gameObject.activeInHierarchy
                    + " clip=" + (source.clip != null ? source.clip.name : "NULL") + " volume=" + source.volume
                    + " spatialBlend=" + source.spatialBlend + " loop=" + source.loop + " awake=" + source.playOnAwake
                    + " playing=" + source.isPlaying + " mute=" + source.mute);
                if (source.clip != null)
                    report.AppendLine("Clip state=" + source.clip.loadState + " duration=" + source.clip.length + " channels=" + source.clip.channels);
            }
            foreach (AudioListener listener in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                report.AppendLine("Listener=" + listener.name + " enabled=" + listener.isActiveAndEnabled);
            File.WriteAllText(Output, report.ToString());
            Debug.Log("[UNIT1 MUSIC] " + report);
        }
        finally
        {
            if (opened && scene.IsValid() && scene.isLoaded && !EditorApplication.isPlaying)
                EditorSceneManager.CloseScene(scene, true);
        }
    }
}

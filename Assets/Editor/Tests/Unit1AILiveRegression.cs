using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Opt-in offline Fusion tick test in the original UNIT1 scene. No asset saves.
/// CLI: -batchmode -nographics -executeMethod Unit1AILiveRegression.Run (no -quit).
/// Combat suppression exists only in this explicitly launched test, for 60s.
/// </summary>
[InitializeOnLoad]
public static class Unit1AILiveRegression
{
    private const string SessionKey = "Unit1AILiveRegression.Pending";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static NetworkRunner runner;
    private static float movementStartedAt;
    private static float nextSample;
    private static readonly Dictionary<int, Metric> metrics = new();
    private sealed class Metric
    {
        public Vector3 Previous;
        public float Travelled;
        public bool PickedUp;
        public int Samples;
    }

    static Unit1AILiveRegression() => EditorApplication.playModeStateChanged += OnPlayMode;

    public static void Run()
    {
        if (EditorApplication.isPlaying || SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Run in a clean editor, not during another Play session.");
        EditorSceneManager.OpenScene("Assets/Scenes/UNIT1.unity", OpenSceneMode.Single);
        SessionState.SetBool(SessionKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, false);
        Execute();
    }

    private static async void Execute()
    {
        try
        {
            Unit1GameDirector director = Object.FindFirstObjectByType<Unit1GameDirector>();
            if (director == null) throw new InvalidOperationException("UNIT1 director missing.");
            // Isolate navigation from early victory. The production director
            // remains unchanged; this session is discarded on exit.
            director.enabled = false;
            var runnerObject = new GameObject("Offline AI regression runner");
            runner = runnerObject.AddComponent<NetworkRunner>();
            var manager = runnerObject.AddComponent<NetworkSceneManagerDefault>();
            int index = SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/UNIT1.unity");
            if (index < 0) throw new InvalidOperationException("UNIT1 is missing from build settings.");
            var scenes = new NetworkSceneInfo();
            scenes.AddSceneRef(SceneRef.FromIndex(index), LoadSceneMode.Single);
            StartGameResult result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single, Scene = scenes, SceneManager = manager,
                ObjectProvider = runnerObject.AddComponent<NetworkObjectProviderDefault>()
            });
            if (!result.Ok) throw new InvalidOperationException("Offline runner failed: " + result.ShutdownReason);
            float sceneDeadline = Time.time + 20f;
            do
            {
                director = Object.FindFirstObjectByType<Unit1GameDirector>();
                if (director != null) director.enabled = false;
                if (director != null && director.Object != null && director.Object.IsValid && director.Runner == runner) break;
                if (Time.time >= sceneDeadline) throw new InvalidOperationException("Offline scene objects did not attach to runner.");
                await Task.Delay(100);
            } while (true);
            director.enabled = false;
            typeof(Unit1GameDirector).GetMethod("SpawnEnemies", Private).Invoke(director, null);
            typeof(Unit1GameDirector).GetMethod("SpawnCpuBots", Private).Invoke(director,
                new object[] { 3, new List<PlayerRef> { runner.LocalPlayer } });
            Unit1BotDuck[] bots = Object.FindObjectsByType<Unit1BotDuck>(FindObjectsSortMode.None);
            EnemyDuckAI[] shadows = Object.FindObjectsByType<EnemyDuckAI>(FindObjectsSortMode.None);
            if (bots.Length != 3 || shadows.Length != 3) throw new InvalidOperationException("Expected three CPUs and three shadows.");
            for (int i = 0; i < bots.Length; i++) bots[i].AssignFaction(i % FactionTeam.Count);
            foreach (Unit1BotDuck bot in bots) Add(bot);
            foreach (EnemyDuckAI shadow in shadows) Add(shadow);
            movementStartedAt = Time.time;
            nextSample = Time.time;
            EditorApplication.update += Observe;
            while (Time.time < movementStartedAt + 60f) await Task.Delay(200);
            EditorApplication.update -= Observe;
            foreach (KeyValuePair<int, Metric> pair in metrics)
            {
                Metric metric = pair.Value;
                if (metric.Travelled < 5f || !metric.PickedUp)
                    throw new InvalidOperationException("Duck " + pair.Key + " failed: travelled=" + metric.Travelled + " picked=" + metric.PickedUp);
            }
            Debug.Log("[AI-LIVE] movement/pickup PASS six ducks; restoring normal combat for 15s.");
            foreach (Unit1BotDuck bot in bots) Write(bot, "nextAttackAt", 0f);
            foreach (EnemyDuckAI shadow in shadows) Write(shadow, "nextShadowSpellTime", 0f);
            await Task.Delay(15000);
            Debug.Log("[AI-LIVE] PASS six authoritative ducks moved and picked up through production code.");
            await runner.Shutdown();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            EditorApplication.update -= Observe;
            Debug.LogException(exception);
            Debug.LogError("[AI-LIVE] FAIL " + exception.Message);
            if (runner != null && runner.IsRunning) await runner.Shutdown();
            EditorApplication.Exit(1);
        }
    }

    private static void Add(NetworkBehaviour duck)
    {
        if (!duck.HasStateAuthority) throw new InvalidOperationException("No StateAuthority: " + duck.name);
        metrics.Add(duck.GetInstanceID(), new Metric { Previous = duck.transform.position });
    }

    private static void Observe()
    {
        foreach (Unit1BotDuck bot in Object.FindObjectsByType<Unit1BotDuck>(FindObjectsSortMode.None))
            if (bot.Object != null && bot.Object.IsValid) Write(bot, "nextAttackAt", float.PositiveInfinity);
        foreach (EnemyDuckAI shadow in Object.FindObjectsByType<EnemyDuckAI>(FindObjectsSortMode.None))
            if (shadow.Object != null && shadow.Object.IsValid) Write(shadow, "nextShadowSpellTime", float.PositiveInfinity);
        if (Time.time < nextSample) return;
        nextSample = Time.time + 1f;
        foreach (Unit1BotDuck bot in Object.FindObjectsByType<Unit1BotDuck>(FindObjectsSortMode.None)) Sample(bot, "CPU " + bot.BotNumber, bot.HasWand);
        foreach (EnemyDuckAI shadow in Object.FindObjectsByType<EnemyDuckAI>(FindObjectsSortMode.None)) Sample(shadow, shadow.name, shadow.HasWand);
    }

    private static void Sample(NetworkBehaviour duck, string label, bool hasWand)
    {
        if (!metrics.TryGetValue(duck.GetInstanceID(), out Metric metric)) return;
        Vector3 position = duck.transform.position;
        Vector3 delta = position - metric.Previous; delta.y = 0;
        metric.Travelled += delta.magnitude;
        metric.Previous = position;
        metric.Samples++;
        Unit1WandPickup held = Read(duck, "heldWand") as Unit1WandPickup;
        metric.PickedUp |= hasWand && held != null && held.IsHeldBy(duck.Object);
        Unit1WandPickup target = Read(duck, "targetWand") as Unit1WandPickup;
        var route = Read(duck, "plannedRoute") as List<Vector3>;
        Debug.Log("[AI-LIVE] " + label + " id=" + duck.GetInstanceID() + " t=" + (Time.time - movementStartedAt).ToString("F1")
            + " p=" + position.ToString("F3") + " delta=" + delta.magnitude.ToString("F3")
            + " total=" + metric.Travelled.ToString("F1") + " plan=" + Read(duck, "plannedAction")
            + " route=" + Read(duck, "plannedRouteIndex") + "/" + route?.Count
            + " target=" + (target != null ? target.name : "none") + " hasWand=" + hasWand + " confirmedPickup=" + metric.PickedUp);
    }

    private static object Read(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static void Write(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
}

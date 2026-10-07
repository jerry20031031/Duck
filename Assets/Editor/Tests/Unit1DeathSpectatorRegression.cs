using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// <summary>Opt-in real Fusion/UNIT1 regression. Never saves assets or exits the user's editor.</summary>
[InitializeOnLoad]
public static class Unit1DeathSpectatorRegression
{
    private const string Pending = "UNIT1.DeathSpectatorTest.Pending";
    private const string Restore = "UNIT1.DeathSpectatorTest.Restore";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] private sealed class Setup { public SceneSetup[] Scenes; }
    private static int assertions;

    static Unit1DeathSpectatorRegression()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                Execute();
            }
            else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetString(Restore, "") != "")
                EditorApplication.delayCall += RestoreScene;
        };
    }

    [MenuItem("Tools/UNIT1 Tests/Death Spectator Regression")]
    public static void Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Run in edit mode after compilation.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your scene changes before running this test.");
        assertions = 0;
        PresentationFixture();
        SessionState.SetString(Restore, JsonUtility.ToJson(new Setup { Scenes = EditorSceneManager.GetSceneManagerSetup() }));
        EditorSceneManager.OpenScene("Assets/Scenes/UNIT1.unity", OpenSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    private static void RestoreScene()
    {
        string json = SessionState.GetString(Restore, "");
        SessionState.EraseString(Restore);
        if (json != "") EditorSceneManager.RestoreSceneManagerSetup(JsonUtility.FromJson<Setup>(json).Scenes);
    }

    private static void PresentationFixture()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject duck = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        GameObject wandRoot = new("Fixture Wand", typeof(NetworkObject), typeof(Unit1WandPickup));
        GameObject wandVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(floor, scene); SceneManager.MoveGameObjectToScene(duck, scene);
        SceneManager.MoveGameObjectToScene(wandRoot, scene); SceneManager.MoveGameObjectToScene(wandVisual, scene);
        try
        {
            floor.transform.position = new Vector3(1000, -0.5f, 1000);
            floor.transform.localScale = new Vector3(10, 1, 10);
            duck.transform.position = new Vector3(1000, 1, 1000);
            wandVisual.transform.SetParent(duck.transform, false);
            wandRoot.GetComponent<Unit1WandPickup>().Configure(wandVisual.transform);
            Physics.SyncTransforms();
            DuckGraveMarker marker = DuckGraveMarker.GetOrAdd(duck);
            Vector3 ground = DuckGraveMarker.FindGroundPosition(duck.transform, duck.GetComponent<Collider>());
            Check(Mathf.Abs(ground.y - 0.015f) < 0.02f, "grave rests on floor");
            marker.Apply(true, ground, 30);
            Check(marker.IsShowingGrave, "grave created");
            Check(!duck.GetComponent<Renderer>().enabled && !duck.GetComponent<Collider>().enabled, "body hidden/nonblocking");
            Check(wandVisual.GetComponent<Renderer>().enabled, "held wand not hidden with body");
            Check(marker.GraveTransform.GetComponentsInChildren<Collider>(true).Length == 0, "grave has no solid collider");
            Transform original = marker.GraveTransform;
            for (int i = 0; i < 10; i++) marker.Apply(true, ground, 30);
            Check(marker.GraveTransform == original, "one grave per actor");
            duck.transform.position += Vector3.right * 20;
            marker.Apply(true, ground, 30);
            Check(Vector3.Distance(original.position, ground) < 0.001f, "grave does not drift with root");
            marker.Apply(false, ground, 30);
            Check(!marker.IsShowingGrave && duck.GetComponent<Renderer>().enabled && duck.GetComponent<Collider>().enabled, "presentation can reset");
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
        Debug.Log("[DEATH-TEST] presentation fixture PASS " + assertions);
    }

    private static async void Execute()
    {
        assertions = 0;
        NetworkRunner runner = null;
        try
        {
            Unit1GameDirector director = Object.FindFirstObjectByType<Unit1GameDirector>();
            director.enabled = false;
            GameObject root = new("Offline death spectator regression");
            runner = root.AddComponent<NetworkRunner>();
            var info = new NetworkSceneInfo();
            info.AddSceneRef(SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/UNIT1.unity")), LoadSceneMode.Single);
            StartGameResult result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single, Scene = info,
                SceneManager = root.AddComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = root.AddComponent<NetworkObjectProviderDefault>()
            });
            Check(result.Ok, "offline runner started");
            float deadline = Time.realtimeSinceStartup + 20;
            do
            {
                director = Object.FindFirstObjectByType<Unit1GameDirector>();
                if (director != null) director.enabled = false;
                if (director != null && director.Object != null && director.Object.IsValid && director.Runner == runner) break;
                if (Time.realtimeSinceStartup > deadline) throw new Exception("Scene failed to attach to Fusion.");
                await Task.Delay(100);
            } while (EditorApplication.isPlaying);
            Check(EditorApplication.isPlaying, "play session still running");
            Unit1WandPickup wand = Object.FindFirstObjectByType<Unit1WandPickup>();
            Check(wand != null && wand.Object.IsValid, "real scene wand attached");
            Vector3 start = wand.transform.position; start.y -= 0.6f;
            NetworkObject playerPrefab = Prefab("FusionDuckPlayer");
            FusionDuckPlayer local = runner.Spawn(playerPrefab, start, Quaternion.identity).GetComponent<FusionDuckPlayer>();
            runner.SetPlayerObject(runner.LocalPlayer, local.Object);
            local.FactionIndex = FactionTeam.Red; local.PlayerName = "觀戰測試鴨";
            FusionDuckPlayer friend = runner.Spawn(playerPrefab, start + Vector3.right * 4, Quaternion.identity).GetComponent<FusionDuckPlayer>();
            friend.FactionIndex = FactionTeam.Red; friend.PlayerName = "真人隊友";
            FusionDuckPlayer enemy = runner.Spawn(playerPrefab, start + Vector3.left * 5, Quaternion.identity).GetComponent<FusionDuckPlayer>();
            enemy.FactionIndex = FactionTeam.Blue;
            Unit1BotDuck bot = runner.Spawn(Prefab("Unit1CpuDuck"), start + Vector3.forward * 12, Quaternion.identity).GetComponent<Unit1BotDuck>();
            bot.ConfigureBot(1); bot.AssignFaction(FactionTeam.Red);
            EnemyDuckAI shadow = runner.Spawn(Prefab("GreyShadowDuck"), start + Vector3.back * 12, Quaternion.identity).GetComponent<EnemyDuckAI>();
            typeof(EnemyDuckAI).GetMethod("ConvertToFaction", Private).Invoke(shadow, new object[] { FactionTeam.Red });
            EnemyDuckAI neutral = runner.Spawn(Prefab("GreyShadowDuck"), start + Vector3.left * 20, Quaternion.identity).GetComponent<EnemyDuckAI>();
            FactionCrystal crystal = runner.Spawn(Prefab("FactionCrystal"), start + Vector3.forward * 20, Quaternion.identity).GetComponent<FactionCrystal>();
            crystal.AssignFaction(FactionTeam.Red);
            Write(bot, "nextAttackAt", float.PositiveInfinity);
            Write(shadow, "nextShadowSpellTime", float.PositiveInfinity);
            Write(neutral, "nextShadowSpellTime", float.PositiveInfinity);
            await Task.Delay(250);
            Check(local.IsLocalPlayerObject && !friend.IsLocalPlayerObject, "only mapped human owns spectator camera");
            Check(friend.GetComponent<Unit1SpectatorController>() == null, "unmapped human never installs spectator");
            Check(wand.TryPickupByNetworkObject(local.Object), "real wand pickup accepted");
            wand.Render();
            DuckMover mover = local.GetComponent<DuckMover>();
            Check(mover.HasWeapon && wand.IsHeldBy(local.Object), "wand held by dying player");
            DuckWandAttack attack = local.GetComponent<DuckWandAttack>();
            IEnumerator swing = (IEnumerator)typeof(DuckWandAttack).GetMethod("ResolveAttackAfterWindup", Private)
                .Invoke(attack, new object[] { wand.EffectProfile, 5f });
            Write(attack, "pendingAttack", attack.StartCoroutine(swing));
            Vector3 deathRoot = local.transform.position;
            local.TakeDamage(FusionDuckPlayer.MaximumHealth, start);
            await Task.Delay(450);
            Unit1SpectatorController spectator = local.GetComponent<Unit1SpectatorController>();
            Check(spectator != null && spectator.IsSpectating && spectator.TargetCount == 3, "human CPU converted ally only");
            Check(spectator.CurrentTarget == friend.transform, "human teammate has priority");
            RPGCameraFollow follow = Object.FindFirstObjectByType<RPGCameraFollow>();
            Check(((Transform[])Read(follow, "targets"))[0] == friend.transform, "actual camera follows selected ally");
            follow.SetTarget(local.transform);
            spectator.RefreshTargets();
            Check(follow.FollowTarget == friend.transform, "scene-transfer camera overwrite repaired");
            Check(local.GetComponent<DuckGraveMarker>().IsShowingGrave, "human grave visible");
            Check(!mover.enabled && !attack.enabled && Read(attack, "pendingAttack") == null, "dead input and pending swing disabled");
            Check(!(bool)typeof(DuckWandAttack).GetMethod("CanAttack", Private).Invoke(attack, null), "dead attack refused");
            Check(Vector3.Distance(local.transform.position, deathRoot) < 0.02f, "dead body root stays still");
            Debug.Log("[DEATH-TEST] wand after death available=" + wand.IsAvailable + " holder=" + wand.HolderObjectId
                + " renderer=" + wand.VisualRoot.GetComponentInChildren<Renderer>().enabled);
            Check(wand.IsAvailable, "death releases wand holder");
            Check(wand.VisualRoot.GetComponentInChildren<Renderer>().enabled, "released wand remains visible");
            spectator.CycleTarget(1);
            Check(spectator.CurrentTarget == bot.transform, "next selects CPU ally");
            spectator.CycleTarget(1);
            Check(spectator.CurrentTarget == shadow.transform, "next selects converted ally");
            spectator.CycleTarget(1);
            Check(spectator.CurrentTarget == friend.transform, "cycling wraps around");
            friend.TakeDamage(3, start);
            Check(friend.IsEliminated, "selected teammate died");
            await WaitFor(() => spectator.CurrentTarget == bot.transform && spectator.TargetCount == 2,
                "automatic target refresh: target=" + spectator.CurrentTarget?.name + " count=" + spectator.TargetCount);
            Check(spectator.CurrentTarget == bot.transform && spectator.TargetCount == 2, "dead target auto replaced");
            Keyboard originalKeyboard = Keyboard.current;
            Keyboard testKeyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.RightArrow));
                InputSystem.Update();
                typeof(Unit1SpectatorController).GetMethod("Update", Private).Invoke(spectator, null);
                Check(spectator.CurrentTarget == shadow.transform, "right arrow input selects next ally");
                InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.LeftArrow));
                InputSystem.Update();
                typeof(Unit1SpectatorController).GetMethod("Update", Private).Invoke(spectator, null);
                Check(spectator.CurrentTarget == bot.transform, "left arrow input selects previous ally");
            }
            finally
            {
                InputSystem.RemoveDevice(testKeyboard);
                originalKeyboard?.MakeCurrent();
            }
            // Keep the CPU beside the tomb solely for manual visual QA. This
            // explicit test fixture never changes production AI or assets.
            bot.GetComponent<Rigidbody>().position = local.GravePosition + Vector3.right * 3;
            Write(bot, "wandAttackUntil", Time.time + 200f);
            Debug.Log("[DEATH-TEST] VISUAL READY: spectator HUD and two graves. Continuing in 5s.");
            await Task.Delay(5000);
            bot.TakeDamage(3, start);
            await Task.Delay(450);
            Check(bot.GetComponent<DuckGraveMarker>().IsShowingGrave && !bot.GetComponent<Collider>().enabled, "CPU grave/nonblocking");
            Check(spectator.CurrentTarget == shadow.transform, "last ally selected automatically");
            typeof(EnemyDuckAI).GetMethod("ConvertToFaction", Private).Invoke(shadow, new object[] { FactionTeam.Blue });
            await Task.Delay(450);
            Check(spectator.TargetCount == 0 && spectator.CurrentTarget != enemy.transform && spectator.CurrentTarget != neutral.transform,
                "no allies never reveals an enemy");
            Check(spectator.CurrentTarget != null, "fallback camera always has a target");
            Check(spectator.CurrentTarget == crystal.transform, "no allies watches own crystal");
            typeof(FactionCrystal).GetProperty("IsDestroyed").SetValue(crystal, (NetworkBool)true);
            await Task.Delay(450);
            Check(spectator.CurrentTarget != crystal.transform && Vector3.Distance(spectator.CurrentTarget.position, local.GravePosition) < 0.02f,
                "destroyed crystal falls back to fixed death position");
            typeof(Unit1GameDirector).GetProperty("MatchFinished").SetValue(director, (NetworkBool)true);
            await Task.Delay(150);
            Check(!((GameObject)Read(spectator, "hud")).activeSelf, "result screen hides spectator overlay");
            Debug.Log("[DEATH-TEST] PASS " + assertions + " live assertions: death, grave, wand, camera, cycling, auto fallback, match end.");
        }
        catch (Exception exception) { Debug.LogException(exception); Debug.LogError("[DEATH-TEST] FAIL " + exception.Message); }
        finally
        {
            if (runner != null && runner.IsRunning) await runner.Shutdown();
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }

    private static NetworkObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab").GetComponent<NetworkObject>();
    private static async Task WaitFor(Func<bool> condition, string description)
    {
        float deadline = Time.realtimeSinceStartup + 4f;
        while (!condition())
        {
            if (!EditorApplication.isPlaying || Time.realtimeSinceStartup >= deadline)
                throw new Exception("Timed out: " + description);
            await Task.Delay(100);
        }
    }
    private static void Check(bool ok, string name)
    {
        assertions++;
        if (!ok) throw new Exception("Assertion failed: " + name);
    }
    private static object Read(object item, string field) => item.GetType().GetField(field, Private).GetValue(item);
    private static void Write(object item, string field, object value) => item.GetType().GetField(field, Private).SetValue(item, value);
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit local diagnostics only. No production changes, asset saves or editor exit.</summary>
[InitializeOnLoad]
public static class Unit1AIVerification
{
    private const string Pending = "UNIT1.AIVerification.Pending";
    private const string Restore = "UNIT1.AIVerification.Restore";
    private const string TimingOnly = "UNIT1.AIVerification.TimingOnly";
    private const string WandOnly = "UNIT1.AIVerification.WandOnly";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] private sealed class Setup { public SceneSetup[] Scenes; }
    private sealed class Metric
    {
        public Vector3 Previous;
        public float Travelled;
        public bool PickedUp;
        public int Health;
        public int Faction;
        public int DamageChanges;
        public int Conversions;
    }
    private static readonly Dictionary<NetworkBehaviour, Metric> metrics = new();
    private static bool suppressCombat;
    private static float sampleAt;
    private static readonly List<string> issues = new();

    static Unit1AIVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                Execute();
            }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetString(Restore, "") != "")
            {
                EditorApplication.update -= Observe;
                EditorApplication.delayCall += RestoreScene;
            }
        };
    }

    [MenuItem("Tools/UNIT1 Tests/AI Movement Pickup Combat Verification")]
    public static void Run() => StartRun(false);

    [MenuItem("Tools/UNIT1 Tests/AI Combat Timing Regression")]
    public static void RunCombatTimingRegression() => StartRun(true);

    [MenuItem("Tools/UNIT1 Tests/AI Wand Pickup Regression")]
    public static void RunWandPickupRegression() => StartRun(false, true);

    private static void StartRun(bool timingOnly, bool wandOnly = false)
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Run in edit mode after compilation.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene changes: verification cancelled.");
        Unit1AINavigationRegression.Run();
        SessionState.SetString(Restore, JsonUtility.ToJson(new Setup { Scenes = EditorSceneManager.GetSceneManagerSetup() }));
        EditorSceneManager.OpenScene("Assets/Scenes/UNIT1.unity", OpenSceneMode.Single);
        SessionState.SetBool(TimingOnly, timingOnly);
        SessionState.SetBool(WandOnly, wandOnly);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    private static void RestoreScene()
    {
        string json = SessionState.GetString(Restore, "");
        SessionState.EraseString(Restore);
        if (json != "") EditorSceneManager.RestoreSceneManagerSetup(JsonUtility.FromJson<Setup>(json).Scenes);
    }

    private static async void Execute()
    {
        NetworkRunner runner = null;
        bool timingOnly = SessionState.GetBool(TimingOnly, false);
        bool wandOnly = SessionState.GetBool(WandOnly, false);
        SessionState.EraseBool(TimingOnly);
        SessionState.EraseBool(WandOnly);
        metrics.Clear(); issues.Clear(); suppressCombat = true;
        try
        {
            Unit1GameDirector director = Object.FindFirstObjectByType<Unit1GameDirector>();
            director.enabled = false;
            GameObject root = new("Offline AI verification");
            runner = root.AddComponent<NetworkRunner>();
            var scenes = new NetworkSceneInfo();
            scenes.AddSceneRef(SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/UNIT1.unity")), LoadSceneMode.Single);
            StartGameResult result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single, Scene = scenes,
                SceneManager = root.AddComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = root.AddComponent<NetworkObjectProviderDefault>()
            });
            Require(result.Ok, "offline Fusion runner start");
            float deadline = Time.realtimeSinceStartup + 20;
            while (true)
            {
                director = Object.FindFirstObjectByType<Unit1GameDirector>();
                if (director != null) director.enabled = false;
                if (director != null && director.Object != null && director.Object.IsValid && director.Runner == runner) break;
                Require(EditorApplication.isPlaying && Time.realtimeSinceStartup < deadline, "UNIT1 scene object attachment");
                await Task.Delay(100);
            }
            if (wandOnly)
            {
                await WandPickupRegression(runner);
                Debug.Log("[AI-CHECK] WAND PICKUP REGRESSION PASS. Harness never saves assets.");
                return;
            }
            if (timingOnly)
            {
                await CombatTimingProbes(runner, director);
                Require(issues.Count == 0, "combat timing regression reported issues: " + string.Join("; ", issues));
                Debug.Log("[AI-CHECK] COMBAT TIMING REGRESSION PASS. Harness never saves assets.");
                return;
            }
            typeof(Unit1GameDirector).GetMethod("SpawnEnemies", Private).Invoke(director, null);
            typeof(Unit1GameDirector).GetMethod("SpawnCpuBots", Private).Invoke(director, new object[] { 3, new List<PlayerRef> { runner.LocalPlayer } });
            Unit1BotDuck[] bots = Object.FindObjectsByType<Unit1BotDuck>(FindObjectsSortMode.None);
            EnemyDuckAI[] shadows = Object.FindObjectsByType<EnemyDuckAI>(FindObjectsSortMode.None);
            Require(bots.Length == 3 && shadows.Length == 6, "three CPU and six shadows");
            Unit1WandPickup[] sceneWands = Object.FindObjectsByType<Unit1WandPickup>(FindObjectsSortMode.None);
            Require(sceneWands.Length == 9, "nine network wands in the real scene");
            foreach (Unit1WandAbility ability in Enum.GetValues(typeof(Unit1WandAbility)))
            {
                int count = 0;
                foreach (Unit1WandPickup wand in sceneWands)
                {
                    Require(wand.Object != null && wand.Object.IsValid && wand.Runner == runner, "wand attached to Fusion " + wand.name);
                    if (wand.Ability == ability) count++;
                }
                Require(count == 3, "three wands for " + ability);
            }
            Debug.Log("[DENSITY-CHECK] three CPU, six Grey Shadows and nine valid Fusion wands (three per ability) PASS.");
            Unit1AINavigationRegression.VerifyLiveSpawnLayout(bots, shadows);
            for (int i = 0; i < bots.Length; i++) bots[i].AssignFaction(FactionTeam.Red + i % FactionTeam.Count);
            foreach (Unit1BotDuck bot in bots) Add(bot, bot.CurrentHealth, bot.FactionIndex);
            foreach (EnemyDuckAI shadow in shadows) Add(shadow, shadow.CurrentHealth, shadow.FactionIndex);
            sampleAt = 0;
            EditorApplication.update += Observe;
            Observe();
            Debug.Log("[AI-CHECK] START 90s natural movement/pickup, then 20s normal combat. No forced routes or pickups in this phase.");
            await Delay(90000);
            foreach (KeyValuePair<NetworkBehaviour, Metric> pair in metrics)
            {
                Metric metric = pair.Value;
                Debug.Log("[AI-CHECK] MOVEMENT " + Label(pair.Key) + " travelled=" + metric.Travelled.ToString("F1") + "m confirmedPickup=" + metric.PickedUp);
                if (metric.Travelled < 5 || !metric.PickedUp)
                    issues.Add("Natural movement/pickup not confirmed for " + Label(pair.Key) + ": travel=" + metric.Travelled.ToString("F1") + " picked=" + metric.PickedUp);
            }
            foreach (Unit1WandPickup item in Object.FindObjectsByType<Unit1WandPickup>(FindObjectsSortMode.None))
            {
                Debug.Log("[AI-CHECK] WAND " + item.name + " p=" + item.transform.position.ToString("F2") + " available=" + item.IsAvailable + " holder=" + item.HolderObjectId);
                if (!item.IsAvailable) continue;
                foreach (KeyValuePair<NetworkBehaviour, Metric> pair in metrics)
                {
                    if (pair.Value.PickedUp) continue;
                    var route = new List<Vector3>(); int routeIndex = 0; Vector3 destination = Vector3.zero; float nextPlan = 0;
                    bool reachable = Unit1RouteScanner.TryGetNextWaypoint(pair.Key.transform.position, item.transform.position, item.NavigationArrivalDistance,
                        route, ref routeIndex, ref destination, ref nextPlan, out _);
                    Debug.Log("[AI-CHECK] UNPICKED " + Label(pair.Key) + " to=" + item.name
                        + " routeExists=" + reachable + " routePoints=" + route.Count
                        + " canPickupHere=" + item.CanBePickedUpBy(pair.Key.transform.position));
                }
            }
            suppressCombat = false;
            foreach (Unit1BotDuck bot in bots) Write(bot, "nextAttackAt", 0f);
            foreach (EnemyDuckAI shadow in shadows) Write(shadow, "nextShadowSpellTime", 0f);
            Debug.Log("[AI-CHECK] movement/pickup measurement complete; normal combat enabled for 20s.");
            await Delay(20000);
            Observe(); EditorApplication.update -= Observe;
            foreach (KeyValuePair<NetworkBehaviour, Metric> pair in metrics)
                Debug.Log("[AI-CHECK] COMBAT " + Label(pair.Key) + " damageChanges=" + pair.Value.DamageChanges
                    + " conversions=" + pair.Value.Conversions + " health=" + Health(pair.Key) + " faction=" + Faction(pair.Key));
            foreach (Unit1BotDuck bot in bots) { bot.StopAllCoroutines(); Write(bot, "wandAttackUntil", float.PositiveInfinity); }
            foreach (EnemyDuckAI shadow in shadows) { shadow.StopAllCoroutines(); Write(shadow, "wandAttackUntil", float.PositiveInfinity); }
            await CombatTimingProbes(runner, director);
            Debug.Log("[AI-CHECK] DONE total issues=" + issues.Count + ". Harness never saves assets.");
            foreach (string issue in issues) Debug.LogWarning("[AI-CHECK] ISSUE " + issue);
        }
        catch (Exception exception) { Debug.LogException(exception); Debug.LogError("[AI-CHECK] TEST FAILED " + exception.Message); }
        finally
        {
            EditorApplication.update -= Observe;
            if (runner != null && runner.IsRunning) await runner.Shutdown();
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }

    private static async Task CombatTimingProbes(NetworkRunner runner, Unit1GameDirector director)
    {
        Debug.Log("[AI-CHECK] controlled timing probes START (positions/wand states set only in disposable fixtures).");
        Unit1WandPickup wand = Object.FindFirstObjectByType<Unit1WandPickup>();
        if (wand.HolderObjectId != NetworkId.None) wand.ReleaseIfHeldBy(runner.FindObject(wand.HolderObjectId));
        Vector3 start = wand.transform.position; start.y -= 0.6f;
        Unit1BotDuck red = SpawnBot(runner, start, FactionTeam.Red, 90);
        Unit1BotDuck blue = SpawnBot(runner, start + Vector3.forward * 1.5f, FactionTeam.Blue, 91);
        Unit1BotDuck green = SpawnBot(runner, start - Vector3.right * 2f, FactionTeam.Green, 92);
        EnemyDuckAI grey = runner.Spawn(Prefab("GreyShadowDuck"), start + Vector3.right, Quaternion.identity).GetComponent<EnemyDuckAI>();
        Write(grey, "wandAttackUntil", float.PositiveInfinity);
        Require(wand.TryPickupByNetworkObject(grey.Object), "fixture shadow wand ownership");
        Set(grey, "HasWand", (NetworkBool)true); Write(grey, "heldWand", wand);
        Unit1WandEffectProfile profile = Unit1WandPickup.GetEffectProfile(Unit1WandAbility.Swift);
        int health = red.CurrentHealth;
        Cast(grey, "ResolveShadowCastAfterWindup", new object[] { red.transform, null, red, profile, 0.5f });
        Require(grey.TakeWandHit(red.Object, red.transform.position, 2, 0, 0), "real hit converts shadow");
        Require(grey.FactionIndex == red.FactionIndex && !grey.IsDefeated, "shadow converted to target ally");
        // Conversion intentionally clears the production windup movement lock.
        // Refreeze only this disposable fixture, without cancelling the cast.
        Write(grey, "wandAttackUntil", float.PositiveInfinity);
        await Delay(1000);
        Debug.Log("[AI-CHECK] conversion-during-cast targetHealth " + health + " -> " + red.CurrentHealth + " nowAllies=" + FactionTeam.AreAllies(grey.FactionIndex, red.FactionIndex));
        if (red.CurrentHealth < health) issues.Add("Grey cast still damages its ally after being converted during windup.");

        FactionCrystal enemyCrystal = runner.Spawn(Prefab("FactionCrystal"), start + Vector3.forward * 1.8f, Quaternion.identity).GetComponent<FactionCrystal>();
        FactionCrystal friendlyCrystal = runner.Spawn(Prefab("FactionCrystal"), start + Vector3.right * 2.4f, Quaternion.identity).GetComponent<FactionCrystal>();
        enemyCrystal.AssignFaction(FactionTeam.Blue); friendlyCrystal.AssignFaction(FactionTeam.Red);
        Set(friendlyCrystal, "CurrentHealth", friendlyCrystal.MaximumHealth - 2);
        Unit1WandEffectProfile guardian = Unit1WandPickup.GetEffectProfile(Unit1WandAbility.Guardian);

        // Old target remains hostile after Red -> Green: a faction check alone
        // would not cancel these pre-conversion spells. Exercise all three types.
        health = blue.CurrentHealth;
        int crystalHealth = enemyCrystal.CurrentHealth;
        Cast(grey, "ResolveShadowCastAfterWindup", new object[] { blue.transform, null, blue, profile, 0.5f });
        Cast(grey, "ResolveCrystalCastAfterWindup", new object[] { enemyCrystal, profile, 0.5f });
        Cast(grey, "ResolveGuardianCastAfterWindup", new object[] { guardian, 0.5f });
        Require(grey.TakeWandHit(green.Object, green.transform.position, 2, 0, 0), "second real conversion");
        Write(grey, "wandAttackUntil", float.PositiveInfinity);
        await Delay(1000);
        Require(grey.FactionIndex == FactionTeam.Green && FactionTeam.AreEnemies(grey.FactionIndex, blue.FactionIndex), "old target still hostile after conversion");
        Require(blue.CurrentHealth == health && enemyCrystal.CurrentHealth == crystalHealth && !grey.HasGuardianShield,
            "conversion invalidates damage, crystal and guardian casts even if old target stays hostile");
        Debug.Log("[AI-CHECK] conversion cancels all three old cast types PASS (old target remains hostile).");

        Cast(grey, "ResolveShadowCastAfterWindup", new object[] { blue.transform, null, blue, profile, 0.5f });
        blue.AssignFaction(FactionTeam.Green);
        await Delay(1000);
        Require(blue.CurrentHealth == health, "target becoming an ally independently suppresses impact");
        blue.AssignFaction(FactionTeam.Blue);
        Debug.Log("[AI-CHECK] target faction changed during windup PASS.");

        wand.ReleaseIfHeldBy(grey.Object); Set(grey, "HasWand", (NetworkBool)false);
        TeleportFixture(red, start);
        Require(wand.TryPickupByNetworkObject(red.Object), "fixture CPU wand ownership");
        Set(red, "HasWand", (NetworkBool)true); Write(red, "heldWand", wand);
        Unit1WandPickup secondWand = Array.Find(Object.FindObjectsByType<Unit1WandPickup>(FindObjectsSortMode.None), item => item != wand);
        if (secondWand.HolderObjectId != NetworkId.None) secondWand.ReleaseIfHeldBy(runner.FindObject(secondWand.HolderObjectId));
        TeleportFixture(grey, secondWand.transform.position - Vector3.up * 0.6f);
        Require(secondWand.TryPickupByNetworkObject(grey.Object), "second fixture shadow wand ownership");
        Set(grey, "HasWand", (NetworkBool)true); Write(grey, "heldWand", secondWand);
        TeleportFixture(grey, start + Vector3.right);
        health = blue.CurrentHealth;
        int redHealth = red.CurrentHealth, friendlyHealth = friendlyCrystal.CurrentHealth;
        Cast(red, "ResolveStrikeAfterWindup", new object[] { null, null, blue, profile, 0.5f });
        Cast(red, "ResolveCrystalStrikeAfterWindup", new object[] { enemyCrystal, profile, 0.5f });
        Cast(red, "ResolveGuardianAfterWindup", new object[] { guardian, 0.5f });
        Cast(grey, "ResolveShadowCastAfterWindup", new object[] { red.transform, null, red, profile, 0.5f });
        Cast(grey, "ResolveCrystalCastAfterWindup", new object[] { enemyCrystal, profile, 0.5f });
        Cast(grey, "ResolveGuardianCastAfterWindup", new object[] { guardian, 0.5f });
        Set(director, "MatchFinished", (NetworkBool)true);
        await Delay(1000);
        Debug.Log("[AI-CHECK] CPU cast after match-end targetHealth " + health + " -> " + blue.CurrentHealth);
        if (blue.CurrentHealth < health) issues.Add("CPU pending strike still applies damage after MatchFinished.");
        Require(blue.CurrentHealth == health && red.CurrentHealth == redHealth && enemyCrystal.CurrentHealth == crystalHealth
            && friendlyCrystal.CurrentHealth == friendlyHealth && !friendlyCrystal.HasShield && !red.HasGuardianShield && !grey.HasGuardianShield,
            "match completion suppresses all six CPU/shadow damage, crystal and guardian cast types");
        Debug.Log("[AI-CHECK] match-end all six cast types PASS (no damage, repair or shield).");
        Set(director, "MatchFinished", (NetworkBool)false);
        await Delay(1000);
        Cast(red, "ResolveStrikeAfterWindup", new object[] { null, null, blue, profile, 0.5f });
        Cast(grey, "ResolveShadowCastAfterWindup", new object[] { red.transform, null, red, profile, 0.5f });
        await Delay(1000);
        Require(blue.CurrentHealth == health - profile.Damage && red.CurrentHealth == redHealth - profile.Damage,
            "live-match CPU and converted shadow hostile attacks still work");
        Debug.Log("[AI-CHECK] positive control: live hostile CPU and shadow strikes PASS.");
        // The positive control just damaged red. Respect its real 0.8s hit
        // immunity before applying the separate lethal-hit/death fixture.
        while (Time.time < (float)Read(red, "damageImmuneUntil")) await Delay(100);
        health = blue.CurrentHealth;
        Cast(red, "ResolveStrikeAfterWindup", new object[] { null, null, blue, profile, 0.5f });
        red.TakeDamage(3, blue.transform.position);
        Vector3 death = red.transform.position;
        await Delay(1000);
        Require(red.IsEliminated && red.GetComponent<DuckGraveMarker>().IsShowingGrave && !red.GetComponent<Collider>().enabled,
            "CPU death body replaced with nonblocking grave");
        Require(blue.CurrentHealth == health && Vector3.Distance(red.transform.position, death) < 0.02f && wand.IsAvailable,
            "dead CPU pending strike suppressed, root still, wand released");
        Debug.Log("[AI-CHECK] CPU death/grave/drop/pending attack suppression PASS; shadow real-hit conversion PASS.");
    }

    private static async Task WandPickupRegression(NetworkRunner runner)
    {
        Unit1WandPickup target = Array.Find(Object.FindObjectsByType<Unit1WandPickup>(FindObjectsSortMode.None),
            item => item.name == "水晶守護杖 2");
        Require(target != null && target.IsAvailable, "original guardian wand 2 available");
        int number = 100;
        foreach (Unit1WandPickup item in Object.FindObjectsByType<Unit1WandPickup>(FindObjectsSortMode.None))
        {
            if (item == target) continue;
            Unit1BotDuck holder = SpawnBot(runner, item.transform.position - Vector3.up * 0.6f, FactionTeam.Red, number++);
            Require(item.TryPickupByNetworkObject(holder.Object), "fixture claims other wand " + item.name);
            Set(holder, "HasWand", (NetworkBool)true); Write(holder, "heldWand", item);
        }
        // All other wands are genuinely held. Neither tested duck is given a
        // forced goal, route or pickup; the production unarmed plan must choose
        // the formerly unreachable remaining wand, walk and claim it itself.
        Vector3 start = new(1.51f, -0.49f, 22.12f);
        Unit1BotDuck cpu = SpawnBot(runner, start, FactionTeam.Red, 2);
        Write(cpu, "wandAttackUntil", 0f); Write(cpu, "nextAttackAt", float.PositiveInfinity);
        await WaitForWand(cpu, target);
        Debug.Log("[AI-CHECK] guardian wand 2 CPU natural pickup PASS p=" + cpu.transform.position.ToString("F2"));
        Write(cpu, "wandAttackUntil", float.PositiveInfinity);
        target.ReleaseIfHeldBy(cpu.Object); Set(cpu, "HasWand", (NetworkBool)false); Write(cpu, "heldWand", null);

        EnemyDuckAI grey = runner.Spawn(Prefab("GreyShadowDuck"), start, Quaternion.identity).GetComponent<EnemyDuckAI>();
        Write(grey, "nextShadowSpellTime", float.PositiveInfinity);
        await WaitForWand(grey, target);
        Debug.Log("[AI-CHECK] guardian wand 2 Grey natural pickup PASS p=" + grey.transform.position.ToString("F2"));
    }

    private static async Task WaitForWand(NetworkBehaviour actor, Unit1WandPickup wand)
    {
        float deadline = Time.realtimeSinceStartup + 25f, logAt = 0f;
        Vector3 before = actor.transform.position;
        while (!wand.IsHeldBy(actor.Object))
        {
            Require(Time.realtimeSinceStartup < deadline, "remaining guardian wand pickup timed out for " + Label(actor));
            if (Time.realtimeSinceStartup >= logAt)
            {
                logAt = Time.realtimeSinceStartup + 3f;
                Debug.Log("[AI-CHECK] WAND WAIT " + Label(actor) + " p=" + actor.transform.position.ToString("F2")
                    + " action=" + Read(actor, "plannedAction") + " goal=" + (Read(actor, "targetWand") as Unit1WandPickup)?.name);
            }
            await Delay(100);
        }
        Require((actor is Unit1BotDuck bot ? (bool)bot.HasWand : (bool)((EnemyDuckAI)actor).HasWand)
            && Vector3.Distance(before, actor.transform.position) > 5f, "real movement and confirmed wand ownership " + Label(actor));
    }

    private static Unit1BotDuck SpawnBot(NetworkRunner runner, Vector3 position, int faction, int number)
    {
        Unit1BotDuck bot = runner.Spawn(Prefab("Unit1CpuDuck"), position, Quaternion.identity).GetComponent<Unit1BotDuck>();
        bot.ConfigureBot(number); bot.AssignFaction(faction); Write(bot, "wandAttackUntil", float.PositiveInfinity);
        return bot;
    }
    private static void TeleportFixture(NetworkBehaviour actor, Vector3 position)
    {
        // Interpolated Rigidbody.position alone leaves Transform at its old
        // rendered pose until a physics frame. Pickup validates Transform now.
        actor.transform.position = position;
        actor.GetComponent<Rigidbody>().position = position;
        Physics.SyncTransforms();
    }
    private static NetworkObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab").GetComponent<NetworkObject>();
    private static void Cast(MonoBehaviour actor, string method, object[] args) => actor.StartCoroutine((IEnumerator)actor.GetType().GetMethod(method, Private).Invoke(actor, args));
    private static async Task Delay(int milliseconds)
    {
        int remaining = milliseconds;
        while (remaining > 0)
        {
            Require(EditorApplication.isPlaying, "verification interrupted by stop Play");
            int chunk = Mathf.Min(remaining, 200);
            await Task.Delay(chunk); remaining -= chunk;
        }
    }
    private static void Add(NetworkBehaviour actor, int health, int faction)
    {
        Require(actor.HasStateAuthority, "AI StateAuthority");
        metrics.Add(actor, new Metric { Previous = actor.transform.position, Health = health, Faction = faction });
    }
    private static void Observe()
    {
        if (suppressCombat)
            foreach (NetworkBehaviour actor in metrics.Keys)
                if (actor != null && actor.Object != null && actor.Object.IsValid)
                    Write(actor, actor is Unit1BotDuck ? "nextAttackAt" : "nextShadowSpellTime", float.PositiveInfinity);
        if (Time.time < sampleAt) return;
        sampleAt = Time.time + 1f;
        foreach (KeyValuePair<NetworkBehaviour, Metric> pair in metrics)
        {
            NetworkBehaviour actor = pair.Key; Metric metric = pair.Value;
            if (actor == null || actor.Object == null || !actor.Object.IsValid) continue;
            Vector3 delta = actor.transform.position - metric.Previous; delta.y = 0;
            metric.Travelled += delta.magnitude; metric.Previous = actor.transform.position;
            Unit1WandPickup held = Read(actor, "heldWand") as Unit1WandPickup;
            metric.PickedUp |= held != null && held.IsHeldBy(actor.Object)
                && (actor is Unit1BotDuck bot ? (bool)bot.HasWand : (bool)((EnemyDuckAI)actor).HasWand);
            int health = Health(actor), faction = Faction(actor);
            if (health < metric.Health) metric.DamageChanges++;
            if (faction != metric.Faction) metric.Conversions++;
            metric.Health = health; metric.Faction = faction;
            if (((int)Time.time) % 10 == 0)
                Debug.Log("[AI-CHECK] SAMPLE " + Label(actor) + " p=" + actor.transform.position.ToString("F2")
                    + " travel=" + metric.Travelled.ToString("F1") + " pickup=" + metric.PickedUp + " action=" + Read(actor, "plannedAction")
                    + " goal=" + (Read(actor, "targetWand") as Unit1WandPickup)?.name
                    + " route=" + Read(actor, "plannedRouteIndex") + "/" + ((List<Vector3>)Read(actor, "plannedRoute")).Count);
        }
    }
    private static int Health(NetworkBehaviour actor) => actor is Unit1BotDuck bot ? bot.CurrentHealth : ((EnemyDuckAI)actor).CurrentHealth;
    private static int Faction(NetworkBehaviour actor) => actor is Unit1BotDuck bot ? bot.FactionIndex : ((EnemyDuckAI)actor).FactionIndex;
    private static string Label(NetworkBehaviour actor) => actor is Unit1BotDuck bot ? "CPU " + bot.BotNumber : "Grey " + actor.Object.Id;
    private static object Read(object actor, string field) => actor.GetType().GetField(field, Private).GetValue(actor);
    private static void Write(object actor, string field, object value) => actor.GetType().GetField(field, Private).SetValue(actor, value);
    private static void Set(object actor, string property, object value) => actor.GetType().GetProperty(property).SetValue(actor, value);
    private static void Require(bool ok, string description) { if (!ok) throw new Exception(description); }
}

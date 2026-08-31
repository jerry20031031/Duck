using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[InitializeOnLoad]
public static class ConfigureDuckAnimator
{
    private const string ControllerPath = "Assets/people/1/Meshy_AI_11_biped_Animation_Walking_withSkin.controller";
    private const string IdleClipPath = "Assets/motion/Idle.fbx";
    private const string WalkingClipPath = "Assets/motion/Meshy_AI_Sunny_Duckling_biped_Character_output@Walking.fbx";
    private const string RunningClipPath = "Assets/motion/Fast Run.fbx";
    private const string JumpClipPath = "Assets/motion/Jump.fbx";

    static ConfigureDuckAnimator()
    {
        EditorApplication.delayCall += Configure;
    }

    private static void Configure()
    {
        AnimationClip idleClip = LoadClip(IdleClipPath, "mixamo.com");
        AnimationClip walkingClip = LoadClip(WalkingClipPath);
        AnimationClip runningClip = LoadClip(RunningClipPath, "mixamo.com");
        AnimationClip jumpClip = LoadClip(JumpClipPath, "mixamo.com");
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        if (idleClip == null || walkingClip == null || runningClip == null || jumpClip == null || controller == null || controller.layers.Length == 0)
        {
            return;
        }

        bool changed = false;
        changed |= EnsureClipLoops(IdleClipPath);
        changed |= EnsureClipLoops(WalkingClipPath);
        changed |= EnsureClipLoops(RunningClipPath);
        changed |= SetClipLooping(JumpClipPath, false);

        ChildAnimatorState[] states = controller.layers[0].stateMachine.states;
        AnimatorState idleState = states.FirstOrDefault(state => state.state.name == "idle").state;
        AnimatorState walkingState = states.FirstOrDefault(state => state.state.name == "Walking").state;
        AnimatorState runningState = states.FirstOrDefault(state => state.state.name == "Running").state;
        AnimatorState jumpState = states.FirstOrDefault(state => state.state.name == "jump").state;

        if (idleState == null)
        {
            idleState = controller.layers[0].stateMachine.AddState("idle", new Vector3(380f, 10f, 0f));
            changed = true;
        }

        if (walkingState == null)
        {
            walkingState = controller.layers[0].stateMachine.AddState("Walking", new Vector3(380f, 110f, 0f));
            changed = true;
        }

        if (runningState == null)
        {
            runningState = controller.layers[0].stateMachine.AddState("Running", new Vector3(380f, 210f, 0f));
            changed = true;
        }

        if (jumpState == null)
        {
            jumpState = controller.layers[0].stateMachine.AddState("jump", new Vector3(380f, -80f, 0f));
            changed = true;
        }

        changed |= AssignState(idleState, idleClip, 1f);
        changed |= AssignState(walkingState, walkingClip, 1f);
        changed |= AssignState(runningState, runningClip, 1f);
        changed |= AssignState(jumpState, jumpClip, 1f);

        if (controller.layers[0].stateMachine.defaultState != idleState)
        {
            controller.layers[0].stateMachine.defaultState = idleState;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    private static AnimationClip LoadClip(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", System.StringComparison.Ordinal));
    }

    private static AnimationClip LoadClip(string path, string preferredClipName)
    {
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", System.StringComparison.Ordinal))
            .ToArray();

        return clips.FirstOrDefault(clip => clip.name == preferredClipName) ?? clips.FirstOrDefault();
    }

    private static bool AssignState(AnimatorState state, AnimationClip clip, float speed)
    {
        bool changed = false;

        if (state.motion != clip)
        {
            state.motion = clip;
            changed = true;
        }

        if (!Mathf.Approximately(state.speed, speed))
        {
            state.speed = speed;
            changed = true;
        }

        return changed;
    }

    private static bool EnsureClipLoops(string path)
    {
        return SetClipLooping(path, true);
    }

    private static bool SetClipLooping(string path, bool shouldLoop)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            return false;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }

        bool changed = false;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            if (clip.loopTime == shouldLoop && clip.loopPose == shouldLoop)
            {
                continue;
            }

            clip.loopTime = shouldLoop;
            clip.loopPose = shouldLoop;
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        return true;
    }
}

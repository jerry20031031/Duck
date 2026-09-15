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
    private const string WeaponWalkingClipPath = "Assets/motion/weapon walk.fbx";
    private const string RunningClipPath = "Assets/motion/Fast Run.fbx";
    private const string JumpClipPath = "Assets/motion/Jump.fbx";
    private const string SpeedParameter = "Speed";
    private const string GroundedParameter = "Grounded";
    private const string JumpParameter = "Jump";
    private const float WalkThreshold = 0.1f;
    private const float RunThreshold = 0.65f;

    static ConfigureDuckAnimator()
    {
        EditorApplication.delayCall += Configure;
    }

    private static void Configure()
    {
        AnimationClip idleClip = LoadClip(IdleClipPath, "mixamo.com");
        AnimationClip walkingClip = LoadClip(WalkingClipPath);
        AnimationClip weaponWalkingClip = LoadClip(WeaponWalkingClipPath);
        AnimationClip runningClip = LoadClip(RunningClipPath, "mixamo.com");
        AnimationClip jumpClip = LoadClip(JumpClipPath, "mixamo.com");
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        if (idleClip == null || walkingClip == null || weaponWalkingClip == null || runningClip == null || jumpClip == null || controller == null || controller.layers.Length == 0)
        {
            return;
        }

        bool changed = false;
        changed |= EnsureClipLoops(IdleClipPath);
        changed |= EnsureClipLoops(WalkingClipPath);
        changed |= EnsureClipLoops(WeaponWalkingClipPath);
        changed |= EnsureClipLoops(RunningClipPath);
        changed |= SetClipLooping(JumpClipPath, false);

        ChildAnimatorState[] states = controller.layers[0].stateMachine.states;
        AnimatorState idleState = states.FirstOrDefault(state => state.state.name == "idle").state;
        AnimatorState walkingState = states.FirstOrDefault(state => state.state.name == "Walking").state;
        AnimatorState weaponWalkingState = states.FirstOrDefault(state => state.state.name == "weapon walk").state;
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

        if (weaponWalkingState == null)
        {
            weaponWalkingState = controller.layers[0].stateMachine.AddState("weapon walk", new Vector3(800f, 50f, 0f));
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
        changed |= AssignState(weaponWalkingState, weaponWalkingClip, 1f);
        changed |= AssignState(runningState, runningClip, 1f);
        changed |= AssignState(jumpState, jumpClip, 1f);

        changed |= EnsureParameter(controller, SpeedParameter, AnimatorControllerParameterType.Float);
        changed |= EnsureParameter(controller, GroundedParameter, AnimatorControllerParameterType.Bool);
        changed |= EnsureParameter(controller, JumpParameter, AnimatorControllerParameterType.Trigger);
        changed |= EnsureTransitions(idleState, walkingState, runningState, jumpState);

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

    private static bool EnsureParameter(AnimatorController controller, string parameterName, AnimatorControllerParameterType parameterType)
    {
        AnimatorControllerParameter parameter = controller.parameters.FirstOrDefault(existing => existing.name == parameterName);
        if (parameter == null)
        {
            controller.AddParameter(parameterName, parameterType);
            return true;
        }

        if (parameter.type == parameterType)
        {
            return false;
        }

        controller.RemoveParameter(parameter);
        controller.AddParameter(parameterName, parameterType);
        return true;
    }

    private static bool EnsureTransitions(AnimatorState idleState, AnimatorState walkingState, AnimatorState runningState, AnimatorState jumpState)
    {
        bool changed = false;

        changed |= EnsureTransition(idleState, walkingState, false, 0f, 0.08f,
            Greater(SpeedParameter, WalkThreshold),
            Less(SpeedParameter, RunThreshold),
            If(GroundedParameter));
        changed |= EnsureTransition(idleState, runningState, false, 0f, 0.08f,
            Greater(SpeedParameter, RunThreshold),
            If(GroundedParameter));
        changed |= EnsureTransition(walkingState, idleState, false, 0f, 0.08f,
            Less(SpeedParameter, WalkThreshold),
            If(GroundedParameter));
        changed |= EnsureTransition(walkingState, runningState, false, 0f, 0.08f,
            Greater(SpeedParameter, RunThreshold),
            If(GroundedParameter));
        changed |= EnsureTransition(runningState, walkingState, false, 0f, 0.08f,
            Greater(SpeedParameter, WalkThreshold),
            Less(SpeedParameter, RunThreshold),
            If(GroundedParameter));
        changed |= EnsureTransition(runningState, idleState, false, 0f, 0.08f,
            Less(SpeedParameter, WalkThreshold),
            If(GroundedParameter));

        changed |= EnsureTransition(idleState, jumpState, false, 0f, 0.05f, Trigger(JumpParameter));
        changed |= EnsureTransition(walkingState, jumpState, false, 0f, 0.05f, Trigger(JumpParameter));
        changed |= EnsureTransition(runningState, jumpState, false, 0f, 0.05f, Trigger(JumpParameter));

        changed |= EnsureTransition(jumpState, idleState, true, 0.85f, 0.08f,
            If(GroundedParameter),
            Less(SpeedParameter, WalkThreshold));
        changed |= EnsureTransition(jumpState, walkingState, true, 0.85f, 0.08f,
            If(GroundedParameter),
            Greater(SpeedParameter, WalkThreshold),
            Less(SpeedParameter, RunThreshold));
        changed |= EnsureTransition(jumpState, runningState, true, 0.85f, 0.08f,
            If(GroundedParameter),
            Greater(SpeedParameter, RunThreshold));

        return changed;
    }

    private static bool EnsureTransition(AnimatorState source, AnimatorState destination, bool hasExitTime, float exitTime, float duration, params AnimatorCondition[] conditions)
    {
        AnimatorStateTransition transition = source.transitions.FirstOrDefault(existing => existing.destinationState == destination);
        bool changed = false;

        if (transition == null)
        {
            transition = source.AddTransition(destination);
            changed = true;
        }

        if (transition.hasExitTime != hasExitTime)
        {
            transition.hasExitTime = hasExitTime;
            changed = true;
        }

        if (!Mathf.Approximately(transition.exitTime, exitTime))
        {
            transition.exitTime = exitTime;
            changed = true;
        }

        if (!Mathf.Approximately(transition.duration, duration))
        {
            transition.duration = duration;
            changed = true;
        }

        if (!transition.hasFixedDuration)
        {
            transition.hasFixedDuration = true;
            changed = true;
        }

        if (!ConditionsMatch(transition.conditions, conditions))
        {
            foreach (AnimatorCondition condition in transition.conditions.ToArray())
            {
                transition.RemoveCondition(condition);
            }

            foreach (AnimatorCondition condition in conditions)
            {
                transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
            }

            changed = true;
        }

        return changed;
    }

    private static bool ConditionsMatch(AnimatorCondition[] current, AnimatorCondition[] expected)
    {
        if (current.Length != expected.Length)
        {
            return false;
        }

        for (int i = 0; i < current.Length; i++)
        {
            if (current[i].mode != expected[i].mode ||
                current[i].parameter != expected[i].parameter ||
                !Mathf.Approximately(current[i].threshold, expected[i].threshold))
            {
                return false;
            }
        }

        return true;
    }

    private static AnimatorCondition Greater(string parameterName, float threshold)
    {
        return Condition(AnimatorConditionMode.Greater, parameterName, threshold);
    }

    private static AnimatorCondition Less(string parameterName, float threshold)
    {
        return Condition(AnimatorConditionMode.Less, parameterName, threshold);
    }

    private static AnimatorCondition If(string parameterName)
    {
        return Condition(AnimatorConditionMode.If, parameterName, 0f);
    }

    private static AnimatorCondition Trigger(string parameterName)
    {
        return Condition(AnimatorConditionMode.If, parameterName, 0f);
    }

    private static AnimatorCondition Condition(AnimatorConditionMode mode, string parameterName, float threshold)
    {
        return new AnimatorCondition
        {
            mode = mode,
            parameter = parameterName,
            threshold = threshold
        };
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

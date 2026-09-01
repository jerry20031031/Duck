using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ConfigureDuckCharacters
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string SceneName = "SampleScene";
    private const string ControllerPath = "Assets/people/1/Meshy_AI_11_biped_Animation_Walking_withSkin.controller";
    private static readonly DuckCharacterConfig[] DuckCharacters =
    {
        new DuckCharacterConfig("Duck2", "Assets/people/2/Meshy_AI_22_biped/Meshy_AI_22_biped_Animation_Walking_withSkin.fbx"),
        new DuckCharacterConfig("Duck3", "Assets/people/3/Meshy_AI_33_biped/Meshy_AI_33_biped_Animation_Walking_withSkin.fbx"),
    };

    private static readonly string[] DuckMoverFields =
    {
        "walkSpeed",
        "runSpeed",
        "jumpSpeed",
        "cameraRelativeMovement",
        "idleStateName",
        "walkingStateName",
        "runningStateName",
        "jumpStateName",
    };

    static ConfigureDuckCharacters()
    {
        EditorApplication.delayCall += ConfigureActiveScene;
    }

    public static void ConfigureSampleSceneForBatchMode()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ConfigureScene(scene);
    }

    private static void ConfigureActiveScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.name != SceneName)
        {
            return;
        }

        ConfigureScene(scene);
    }

    private static void ConfigureScene(Scene scene)
    {
        GameObject duck1 = FindInScene(scene, "Duck1");
        if (duck1 == null)
        {
            return;
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        DuckMover duck1Mover = duck1.GetComponent<DuckMover>();
        CapsuleCollider duck1Collider = duck1.GetComponent<CapsuleCollider>();
        bool changed = false;

        foreach (DuckCharacterConfig character in DuckCharacters)
        {
            GameObject duck = FindInScene(scene, character.Name);
            if (duck == null)
            {
                continue;
            }

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(character.AvatarPath).OfType<Avatar>().FirstOrDefault();
            changed |= ConfigureDuck(duck, controller, avatar, duck1Mover, duck1Collider);
        }

        if (!changed)
        {
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static bool ConfigureDuck(
        GameObject duck,
        AnimatorController controller,
        Avatar avatar,
        DuckMover sourceMover,
        CapsuleCollider sourceCollider)
    {
        bool changed = false;

        Animator animator = GetOrAddComponent<Animator>(duck, ref changed);
        if (animator.runtimeAnimatorController != controller)
        {
            animator.runtimeAnimatorController = controller;
            changed = true;
        }

        if (avatar != null && animator.avatar != avatar)
        {
            animator.avatar = avatar;
            changed = true;
        }

        if (animator.applyRootMotion)
        {
            animator.applyRootMotion = false;
            changed = true;
        }

        DuckMover mover = GetOrAddComponent<DuckMover>(duck, ref changed);
        if (sourceMover != null)
        {
            changed |= CopyDuckMoverSettings(sourceMover, mover);
        }

        CapsuleCollider collider = GetOrAddComponent<CapsuleCollider>(duck, ref changed);
        if (sourceCollider != null)
        {
            changed |= CopyCapsuleCollider(sourceCollider, collider);
        }

        Rigidbody body = GetOrAddComponent<Rigidbody>(duck, ref changed);
        changed |= ConfigureBody(body);

        if (changed)
        {
            EditorUtility.SetDirty(duck);
        }

        return changed;
    }

    private static GameObject FindInScene(Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(transform => transform.name == name)
            ?.gameObject;
    }

    private static T GetOrAddComponent<T>(GameObject target, ref bool changed) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component != null)
        {
            return component;
        }

        changed = true;
        return target.AddComponent<T>();
    }

    private static bool CopyDuckMoverSettings(DuckMover source, DuckMover target)
    {
        SerializedObject sourceObject = new SerializedObject(source);
        SerializedObject targetObject = new SerializedObject(target);
        bool changed = false;

        foreach (string field in DuckMoverFields)
        {
            SerializedProperty property = sourceObject.FindProperty(field);
            if (property != null)
            {
                changed |= targetObject.CopyFromSerializedPropertyIfDifferent(property);
            }
        }

        if (changed)
        {
            targetObject.ApplyModifiedProperties();
        }

        return changed;
    }

    private static bool CopyCapsuleCollider(CapsuleCollider source, CapsuleCollider target)
    {
        bool changed = false;

        if (!Mathf.Approximately(target.radius, source.radius))
        {
            target.radius = source.radius;
            changed = true;
        }

        if (!Mathf.Approximately(target.height, source.height))
        {
            target.height = source.height;
            changed = true;
        }

        if (target.center != source.center)
        {
            target.center = source.center;
            changed = true;
        }

        if (target.direction != source.direction)
        {
            target.direction = source.direction;
            changed = true;
        }

        if (target.isTrigger != source.isTrigger)
        {
            target.isTrigger = source.isTrigger;
            changed = true;
        }

        return changed;
    }

    private static bool ConfigureBody(Rigidbody body)
    {
        bool changed = false;
        RigidbodyConstraints constraints = RigidbodyConstraints.FreezeRotation;

        if (!body.useGravity)
        {
            body.useGravity = true;
            changed = true;
        }

        if (body.isKinematic)
        {
            body.isKinematic = false;
            changed = true;
        }

        if (body.interpolation != RigidbodyInterpolation.Interpolate)
        {
            body.interpolation = RigidbodyInterpolation.Interpolate;
            changed = true;
        }

        if (body.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic)
        {
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            changed = true;
        }

        if (body.constraints != constraints)
        {
            body.constraints = constraints;
            changed = true;
        }

        return changed;
    }

    private readonly struct DuckCharacterConfig
    {
        public DuckCharacterConfig(string name, string avatarPath)
        {
            Name = name;
            AvatarPath = avatarPath;
        }

        public string Name { get; }
        public string AvatarPath { get; }
    }
}

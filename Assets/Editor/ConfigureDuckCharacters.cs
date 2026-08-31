using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ConfigureDuckCharacters
{
    private const string ControllerPath = "Assets/people/1/Meshy_AI_11_biped_Animation_Walking_withSkin.controller";
    private const string Duck2AvatarPath = "Assets/people/2/Meshy_AI_22_biped/Meshy_AI_22_biped_Animation_Walking_withSkin.fbx";
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
        EditorApplication.delayCall += Configure;
    }

    private static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.name != "SampleScene")
        {
            return;
        }

        GameObject duck1 = FindInScene(scene, "Duck1");
        GameObject duck2 = FindInScene(scene, "Duck2");
        if (duck1 == null || duck2 == null)
        {
            return;
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Avatar duck2Avatar = AssetDatabase.LoadAllAssetsAtPath(Duck2AvatarPath).OfType<Avatar>().FirstOrDefault();
        DuckMover duck1Mover = duck1.GetComponent<DuckMover>();
        CapsuleCollider duck1Collider = duck1.GetComponent<CapsuleCollider>();
        bool changed = false;

        Animator duck2Animator = GetOrAddComponent<Animator>(duck2, ref changed);
        if (duck2Animator.runtimeAnimatorController != controller)
        {
            duck2Animator.runtimeAnimatorController = controller;
            changed = true;
        }

        if (duck2Avatar != null && duck2Animator.avatar != duck2Avatar)
        {
            duck2Animator.avatar = duck2Avatar;
            changed = true;
        }

        if (duck2Animator.applyRootMotion)
        {
            duck2Animator.applyRootMotion = false;
            changed = true;
        }

        DuckMover duck2Mover = GetOrAddComponent<DuckMover>(duck2, ref changed);
        if (duck1Mover != null)
        {
            changed |= CopyDuckMoverSettings(duck1Mover, duck2Mover);
        }

        CapsuleCollider duck2Collider = GetOrAddComponent<CapsuleCollider>(duck2, ref changed);
        if (duck1Collider != null)
        {
            changed |= CopyCapsuleCollider(duck1Collider, duck2Collider);
        }

        Rigidbody duck2Body = GetOrAddComponent<Rigidbody>(duck2, ref changed);
        changed |= ConfigureBody(duck2Body);

        if (!changed)
        {
            return;
        }

        EditorUtility.SetDirty(duck2);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
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
}

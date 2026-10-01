using System.Linq;
using DuckGame.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ReplaceDuck1Model
{
    private const string ScenePath = "Assets/Scenes/UNIT1.unity";
    private const string NewDuckModelPath = "Assets/people/1/00991177a1.fbx";

    [MenuItem("Duck Tools/Replace Duck1 With 00991177a1")]
    public static void ReplaceDuck1()
    {
        EnsureTargetSceneIsOpen();

        GameObject oldDuck = GameObject.Find("Duck1");
        if (oldDuck == null)
        {
            Debug.LogError("Could not find a GameObject named Duck1 in the active scene.");
            return;
        }

        GameObject newDuckPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NewDuckModelPath);
        if (newDuckPrefab == null)
        {
            Debug.LogError($"Could not load replacement model at {NewDuckModelPath}.");
            return;
        }

        Transform oldTransform = oldDuck.transform;
        Transform oldParent = oldTransform.parent;
        int oldSiblingIndex = oldTransform.GetSiblingIndex();

        Vector3 localPosition = oldTransform.localPosition;
        Quaternion localRotation = oldTransform.localRotation;
        Vector3 localScale = oldTransform.localScale;

        Animator oldAnimator = oldDuck.GetComponent<Animator>();
        RuntimeAnimatorController oldController = oldAnimator != null ? oldAnimator.runtimeAnimatorController : null;

        GameObject newDuck = (GameObject)PrefabUtility.InstantiatePrefab(newDuckPrefab, oldParent);
        if (newDuck == null)
        {
            Debug.LogError("Failed to instantiate the replacement Duck1 model.");
            return;
        }

        Undo.RegisterCreatedObjectUndo(newDuck, "Create replacement Duck1");
        newDuck.name = "Duck1";
        newDuck.transform.SetSiblingIndex(oldSiblingIndex);
        newDuck.transform.localPosition = localPosition;
        newDuck.transform.localRotation = localRotation;
        newDuck.transform.localScale = localScale;

        ConfigureAnimator(newDuck, oldController);
        CopyComponentIfPresent<DuckMover>(oldDuck, newDuck);
        CopyComponentIfPresent<CapsuleCollider>(oldDuck, newDuck);
        CopyComponentIfPresent<Rigidbody>(oldDuck, newDuck);
        CopyComponentIfPresent<RuneCircleCaster>(oldDuck, newDuck);
        UpdateCameraTargets(oldTransform, newDuck.transform);

        Undo.DestroyObjectImmediate(oldDuck);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Replaced Duck1 with Assets/people/1/00991177a1.fbx. Check the material slots on child mesh char1.");
    }

    private static void EnsureTargetSceneIsOpen()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path == ScenePath)
        {
            return;
        }

        if (activeScene.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            throw new System.OperationCanceledException("Scene switch cancelled.");
        }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    private static void ConfigureAnimator(GameObject newDuck, RuntimeAnimatorController controller)
    {
        Animator animator = newDuck.GetComponent<Animator>();
        if (animator == null)
        {
            animator = newDuck.AddComponent<Animator>();
        }

        if (controller != null)
        {
            animator.runtimeAnimatorController = controller;
        }

        animator.applyRootMotion = false;

        if (animator.avatar == null)
        {
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(NewDuckModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar != null)
            {
                animator.avatar = avatar;
            }
        }
    }

    private static void CopyComponentIfPresent<T>(GameObject source, GameObject target) where T : Component
    {
        T sourceComponent = source.GetComponent<T>();
        if (sourceComponent == null)
        {
            return;
        }

        T targetComponent = target.GetComponent<T>();
        if (targetComponent == null)
        {
            targetComponent = target.AddComponent<T>();
        }

        EditorUtility.CopySerialized(sourceComponent, targetComponent);
    }

    private static void UpdateCameraTargets(Transform oldTarget, Transform newTarget)
    {
        foreach (RPGCameraFollow cameraFollow in Object.FindObjectsByType<RPGCameraFollow>(FindObjectsSortMode.None))
        {
            SerializedObject serializedCamera = new SerializedObject(cameraFollow);
            SerializedProperty targets = serializedCamera.FindProperty("targets");
            if (targets == null || !targets.isArray)
            {
                continue;
            }

            bool changed = false;
            for (int i = 0; i < targets.arraySize; i++)
            {
                SerializedProperty target = targets.GetArrayElementAtIndex(i);
                if (target.objectReferenceValue == oldTarget)
                {
                    target.objectReferenceValue = newTarget;
                    changed = true;
                }
            }

            if (changed)
            {
                serializedCamera.ApplyModifiedProperties();
                EditorUtility.SetDirty(cameraFollow);
            }
        }
    }

}

using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Keeps every player-facing TextMesh Pro label on one Traditional-Chinese
/// dynamic font. The old static SDF asset did not contain the full Chinese
/// character set used by the lobby and first-level UI, which caused □ glyphs.
/// </summary>
[InitializeOnLoad]
public static class ChineseUiFontSetup
{
    public const string SourceFontPath = "Assets/UI/TmpFont/Fonts/NotoSansTC-VF.ttf";
    public const string DynamicFontPath = "Assets/UI/TmpFont/Fonts/NotoSansTC-Dynamic SDF.asset";

    private static readonly string[] GameScenePaths =
    {
        "Assets/Scenes/MainMenu.unity",
        "Assets/Scenes/BigHall.unity",
        "Assets/Scenes/UNIT1.unity",
        "Assets/Scenes/UNIT2.unity"
    };

    static ChineseUiFontSetup()
    {
        EditorApplication.delayCall += ApplyOnEditorLoad;
    }

    [MenuItem("Duck/中文 UI/套用繁體中文字型與文案")]
    public static void ApplyChineseUi()
    {
        TMP_FontAsset font = GetChineseFont();
        if (font == null)
        {
            Debug.LogError("找不到繁體中文字型。請確認 " + SourceFontPath + " 存在。");
            return;
        }

        TMP_Settings.defaultFontAsset = font;
        EditorUtility.SetDirty(TMP_Settings.instance);

        ApplyToGameScenes(font);
        ApplyToPrefabs(font);
        AssetDatabase.SaveAssets();
        Debug.Log("已套用繁體中文字型與中文介面文案。");
    }

    public static TMP_FontAsset GetChineseFont()
    {
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (IsUsableDynamicFont(existing))
        {
            return existing;
        }

        // TMP creates the material and atlas as sub-assets. Saving only the
        // font asset loses those two objects, leaving every assigned label
        // empty. Discard that incomplete generated asset before rebuilding it.
        if (existing != null)
        {
            AssetDatabase.DeleteAsset(DynamicFontPath);
            AssetDatabase.Refresh();
        }

        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (sourceFont == null)
        {
            return null;
        }

        TMP_FontAsset generated = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            90,
            9,
            GlyphRenderMode.SDFAA,
            2048,
            2048,
            AtlasPopulationMode.Dynamic,
            true);

        if (generated == null)
        {
            return null;
        }

        generated.name = "NotoSansTC-Dynamic SDF";
        generated.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        generated.isMultiAtlasTexturesEnabled = true;
        AssetDatabase.CreateAsset(generated, DynamicFontPath);
        AssetDatabase.AddObjectToAsset(generated.material, generated);
        foreach (Texture2D atlas in generated.atlasTextures)
        {
            if (atlas != null)
            {
                AssetDatabase.AddObjectToAsset(atlas, generated);
            }
        }

        EditorUtility.SetDirty(generated.material);
        EditorUtility.SetDirty(generated);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(DynamicFontPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
    }

    private static bool IsUsableDynamicFont(TMP_FontAsset font)
    {
        return font != null
            && font.material != null
            && font.atlasTextures != null
            && font.atlasTextures.Length > 0
            && font.atlasTextures[0] != null;
    }

    private static void ApplyOnEditorLoad()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        TMP_FontAsset font = GetChineseFont();
        if (font != null && TMP_Settings.defaultFontAsset != font)
        {
            ApplyChineseUi();
        }
    }

    private static void ApplyToGameScenes(TMP_FontAsset font)
    {
        Scene previousActiveScene = SceneManager.GetActiveScene();
        foreach (string scenePath in GameScenePaths)
        {
            if (!System.IO.File.Exists(scenePath))
            {
                continue;
            }

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            }

            if (ApplyToScene(scene, font))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            if (!wasLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
        {
            SceneManager.SetActiveScene(previousActiveScene);
        }
    }

    private static bool ApplyToScene(Scene scene, TMP_FontAsset font)
    {
        bool changed = false;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            changed |= ApplyToText(root.GetComponentsInChildren<TMP_Text>(true), font);
            changed |= ApplyToInputs(root.GetComponentsInChildren<TMP_InputField>(true));
        }
        return changed;
    }

    private static void ApplyToPrefabs(TMP_FontAsset font)
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" });
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
            if (ApplyToText(prefabRoot.GetComponentsInChildren<TMP_Text>(true), font))
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
            }
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool ApplyToText(TMP_Text[] labels, TMP_FontAsset font)
    {
        bool changed = false;
        foreach (TMP_Text label in labels)
        {
            if (label.font != font)
            {
                label.font = font;
                label.fontSharedMaterial = font.material;
                EditorUtility.SetDirty(label);
                changed = true;
            }
        }
        return changed;
    }

    private static bool ApplyToInputs(TMP_InputField[] inputs)
    {
        bool changed = false;
        foreach (TMP_InputField input in inputs)
        {
            if (input.text == "duck-room")
            {
                input.text = "魔法大亂鬥房間";
                EditorUtility.SetDirty(input);
                changed = true;
            }
        }
        return changed;
    }
}

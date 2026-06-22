#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StartMenuSceneCreator
{
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string PlaygroundScenePath = "Assets/Scenes/Playground.unity";

    [MenuItem("Tools/Scenes/Create Main Menu Scene")]
    public static void CreateMainMenuScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (System.IO.File.Exists(MainMenuScenePath))
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Main Menu Scene",
                "MainMenu.unity 已存在，是否覆盖重建？",
                "覆盖",
                "取消");

            if (!overwrite)
            {
                return;
            }
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateCamera();
        CreateMenuRoot();

        EditorSceneManager.SaveScene(scene, MainMenuScenePath);
        EnsureBuildSettingsSceneOrder();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Main Menu Scene",
            "MainMenu 场景已创建。\n\n接下来你可以在场景里的 StartMenu 根对象上给 StartMenuController 赋背景图 Sprite。",
            "确定");
    }

    private static void CreateCamera()
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.07f, 0.1f, 1f);

        cameraObject.AddComponent<AudioListener>();
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.transform.rotation = Quaternion.identity;
    }

    private static void CreateMenuRoot()
    {
        GameObject root = new GameObject("StartMenu");
        StartMenuController controller = root.AddComponent<StartMenuController>();

        SerializedObject serializedObject = new SerializedObject(controller);
        serializedObject.FindProperty("gameplaySceneName").stringValue = "Playground";
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureBuildSettingsSceneOrder()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        UpsertScene(scenes, MainMenuScenePath, enabled: true, insertAtFront: true);

        if (System.IO.File.Exists(PlaygroundScenePath))
        {
            UpsertScene(scenes, PlaygroundScenePath, enabled: true, insertAtFront: false);
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void UpsertScene(List<EditorBuildSettingsScene> scenes, string path, bool enabled, bool insertAtFront)
    {
        int existingIndex = scenes.FindIndex(scene => scene.path == path);
        if (existingIndex >= 0)
        {
            EditorBuildSettingsScene existing = scenes[existingIndex];
            existing.enabled = enabled;
            scenes.RemoveAt(existingIndex);

            if (insertAtFront)
            {
                scenes.Insert(0, existing);
            }
            else
            {
                scenes.Add(existing);
            }

            return;
        }

        EditorBuildSettingsScene newScene = new EditorBuildSettingsScene(path, enabled);
        if (insertAtFront)
        {
            scenes.Insert(0, newScene);
        }
        else
        {
            scenes.Add(newScene);
        }
    }
}
#endif

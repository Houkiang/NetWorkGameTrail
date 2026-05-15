using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MultiplayerM0Setup
{
    private const string PrefabsFolder = "Assets/Prefabs";
    private const string PlayerPrefabPath = PrefabsFolder + "/PlayerPrefab.prefab";
    private const string NetworkManagerPrefabPath = PrefabsFolder + "/NetworkManager.prefab";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string GroundName = "DemoGround";

    [MenuItem("Tools/Multiplayer/Run M0 Setup")]
    public static void RunFromMenu()
    {
        RunSetup();
    }

    public static void RunFromBatchMode()
    {
        RunSetup();
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    private static void RunSetup()
    {
        EnsureFolder("Assets", "Prefabs");

        GameObject playerPrefab = CreateOrUpdatePlayerPrefab();
        GameObject networkManagerPrefab = CreateOrUpdateNetworkManagerPrefab(playerPrefab);

        PlaceNetworkManagerInScene(networkManagerPrefab);
        EnsureDemoGroundInScene();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Multiplayer setup completed: prefabs refreshed and SampleScene configured for NGO movement testing.");
    }

    private static GameObject CreateOrUpdatePlayerPrefab()
    {
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = "PlayerPrefab";

        Object.DestroyImmediate(root.GetComponent<CapsuleCollider>());

        CharacterController controller = root.AddComponent<CharacterController>();
        controller.minMoveDistance = 0f;
        controller.center = new Vector3(0f, 1f, 0f);
        controller.height = 2f;
        controller.radius = 0.45f;

        root.AddComponent<PlayerController>();
        root.AddComponent<NetworkObject>();
        root.AddComponent<NetworkTransform>();

        GameObject prefab = SaveAsPrefab(root, PlayerPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject CreateOrUpdateNetworkManagerPrefab(GameObject playerPrefab)
    {
        GameObject root = new GameObject("NetworkManager");
        NetworkManager networkManager = root.AddComponent<NetworkManager>();
        UnityTransport transport = root.AddComponent<UnityTransport>();
        NetworkBootstrap bootstrap = root.AddComponent<NetworkBootstrap>();

        if (networkManager.NetworkConfig == null)
        {
            networkManager.NetworkConfig = new NetworkConfig();
        }

        bootstrap.ApplyEditorDefaults(playerPrefab);

        networkManager.NetworkConfig.NetworkTransport = transport;
        networkManager.NetworkConfig.PlayerPrefab = playerPrefab;

        GameObject prefab = SaveAsPrefab(root, NetworkManagerPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void PlaceNetworkManagerInScene(GameObject networkManagerPrefab)
    {
        if (!File.Exists(ScenePath))
        {
            return;
        }

        EditorSceneManager.OpenScene(ScenePath);

        NetworkBootstrap existingBootstrap = Object.FindObjectOfType<NetworkBootstrap>();
        if (existingBootstrap == null)
        {
            PrefabUtility.InstantiatePrefab(networkManagerPrefab);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
    }

    private static void EnsureDemoGroundInScene()
    {
        if (!File.Exists(ScenePath))
        {
            return;
        }

        if (GameObject.Find(GroundName) != null)
        {
            return;
        }

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = GroundName;
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(3f, 1f, 3f);

        Renderer renderer = ground.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial.color = new Color(0.42f, 0.48f, 0.36f);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
    }

    private static GameObject SaveAsPrefab(GameObject source, string prefabPath)
    {
        return PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

// 1. create player at PlayerStart and connect camera
// 2. replace existing player when run again
public static class PlayerSetupBuilder
{
    const string PlayerName = "Player";
    const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    [MenuItem("TVPatrol/Create Player")]
    public static void CreatePlayer()
    {
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (actions == null)
        {
            Debug.LogError("[TVPatrol] Could not find " + InputActionsPath);
            return;
        }

        var existing = GameObject.Find(PlayerName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var start = GameObject.Find("PlayerStart");
        Vector3 position = start != null ? start.transform.position : Vector3.zero;
        Quaternion rotation = start != null ? start.transform.rotation : Quaternion.identity;

        // controller on root, visual mesh on child
        var player = new GameObject(PlayerName);
        player.tag = "Player";
        player.transform.SetPositionAndRotation(position, rotation);
        Undo.RegisterCreatedObjectUndo(player, "Create Player");

        var controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.35f;
        controller.center = new Vector3(0f, 0.9f, 0f);

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        // CharacterController handles collision
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(player.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);

        // Small "visor" so the facing direction is visible on the capsule.
        var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visor.name = "Visor";
        Object.DestroyImmediate(visor.GetComponent<Collider>());
        visor.transform.SetParent(player.transform, false);
        visor.transform.localPosition = new Vector3(0f, 1.45f, 0.3f);
        visor.transform.localScale = new Vector3(0.5f, 0.15f, 0.15f);

        var camera = Camera.main;
        if (camera == null)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camera = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            Undo.RegisterCreatedObjectUndo(camGo, "Create Camera");
        }

        var orbit = camera.GetComponent<ThirdPersonCamera>();
        if (orbit == null) orbit = Undo.AddComponent<ThirdPersonCamera>(camera.gameObject);
        var orbitSo = new SerializedObject(orbit);
        orbitSo.FindProperty("inputActions").objectReferenceValue = actions;
        orbitSo.FindProperty("target").objectReferenceValue = player.transform;
        orbitSo.ApplyModifiedProperties();

        var move = player.AddComponent<PlayerController>();
        var moveSo = new SerializedObject(move);
        moveSo.FindProperty("inputActions").objectReferenceValue = actions;
        moveSo.FindProperty("cameraTransform").objectReferenceValue = camera.transform;
        moveSo.ApplyModifiedProperties();

        var interactor = player.AddComponent<PlayerInteractor>();
        var interactorSo = new SerializedObject(interactor);
        interactorSo.FindProperty("inputActions").objectReferenceValue = actions;
        interactorSo.ApplyModifiedProperties();

        player.AddComponent<PlayerHealth>();
        player.AddComponent<PlayerNoiseEmitter>();

        CreateGameSystems(interactor);

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log("[TVPatrol] Player created at " + position);
    }

    // recreate object holding inspection, shift and HUD systems
    static void CreateGameSystems(PlayerInteractor interactor)
    {
        var existing = GameObject.Find("GameSystems");
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var systems = new GameObject("GameSystems");
        Undo.RegisterCreatedObjectUndo(systems, "Create Game Systems");
        systems.AddComponent<InspectionManager>();
        systems.AddComponent<ShiftManager>();
        var hud = systems.AddComponent<HUDController>();
        var hudSettings = new SerializedObject(hud);
        hudSettings.FindProperty("playerInteractor").objectReferenceValue = interactor;
        hudSettings.ApplyModifiedProperties();
    }
}

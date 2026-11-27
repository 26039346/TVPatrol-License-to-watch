using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 1. create dog at first patrol point and set route
// 2. replace existing dog when run again
public static class AISetupBuilder
{
    const string DogName = "Dog";

    [MenuItem("TVPatrol/Create Dog")]
    public static void CreateDog()
    {
        var route = GameObject.Find("DogPatrolPoints");
        if (route == null)
        {
            Debug.LogError("[TVPatrol] DogPatrolPoints not found. Run 'Build Greybox Neighbourhood' first.");
            return;
        }

        var existing = GameObject.Find(DogName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var points = new Transform[route.transform.childCount];
        for (int i = 0; i < points.Length; i++) points[i] = route.transform.GetChild(i);

        var dog = new GameObject(DogName);
        dog.transform.position = points[0].position;
        Undo.RegisterCreatedObjectUndo(dog, "Create Dog");

        // Visual: body + head + snout so facing direction is obvious. No colliders: SteeringAgent's own wall checks stop it.
        var body = Part("Body", dog.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.5f, 0.45f, 1f));
        Part("Head", dog.transform, new Vector3(0f, 0.75f, 0.55f), new Vector3(0.4f, 0.4f, 0.4f));
        Part("Snout", dog.transform, new Vector3(0f, 0.68f, 0.82f), new Vector3(0.2f, 0.18f, 0.25f));

        // movement follows A* routes
        dog.AddComponent<SteeringAgent>();

        var bark = dog.AddComponent<AudioSource>();
        bark.playOnAwake = false;
        bark.spatialBlend = 1f;
        bark.rolloffMode = AudioRolloffMode.Linear;
        bark.minDistance = 3f;
        bark.maxDistance = 25f;

        // RequireComponent adds Perception too
        var ai = dog.AddComponent<DogAIController>();
        var serializedObject = new SerializedObject(ai);
        var patrolPointsProperty = serializedObject.FindProperty("patrolPoints");
        patrolPointsProperty.arraySize = points.Length;
        for (int i = 0; i < points.Length; i++) patrolPointsProperty.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
        serializedObject.FindProperty("bodyRenderer").objectReferenceValue = body.GetComponent<Renderer>();
        serializedObject.FindProperty("barkSource").objectReferenceValue = bark;
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) serializedObject.FindProperty("player").objectReferenceValue = player.transform;
        serializedObject.ApplyModifiedProperties();

        EnsurePlayerComponents(player);

        EditorSceneManager.MarkSceneDirty(dog.scene);
        Selection.activeGameObject = dog;
        Debug.Log("[TVPatrol] Dog created with " + points.Length + " patrol points.");
    }

    // add health and footstep components if missing
    static void EnsurePlayerComponents(GameObject player)
    {
        if (player == null) return;
        if (player.GetComponent<PlayerHealth>() == null) Undo.AddComponent<PlayerHealth>(player);
        if (player.GetComponent<PlayerNoiseEmitter>() == null) Undo.AddComponent<PlayerNoiseEmitter>(player);
    }

    static GameObject Part(string name, Transform parent, Vector3 position, Vector3 scale)
    {
        var gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gameObject.name = name;
        Object.DestroyImmediate(gameObject.GetComponent<Collider>());
        gameObject.transform.SetParent(parent, false);
        gameObject.transform.localPosition = position;
        gameObject.transform.localScale = scale;
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Greybox/GB_Trunk.material");
        if (material != null) gameObject.GetComponent<Renderer>().sharedMaterial = material;
        return gameObject;
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// 1. create resident at house 4 with garden points, senses and warning audio
// 2. replace existing resident when run again
public static class ResidentSetupBuilder
{
    const string ResidentName = "Resident_H4";
    const string ResidentPointsName = "ResidentGardenPoints";

    // 1. house 4 at (30, -14), door faces north at z = -10, inspection point at (30, -8.5)
    // 2. Home is beside the door, so the resident never blocks the inspection point while idle.
    static readonly Vector3 ResidentHome = new Vector3(33f, 0f, -8.5f);
    static readonly Vector3[] ResidentGardenPoints =
    {
        new Vector3(33f, 0f, -8.5f),
        new Vector3(26.5f, 0f, -7.5f),
        new Vector3(35.5f, 0f, -12f),
    };

    [MenuItem("TVPatrol/Create Resident")]
    public static void CreateResident()
    {
        foreach (var name in new[] { ResidentName, ResidentPointsName })
        {
            var old = GameObject.Find(name);
            if (old != null) Undo.DestroyObjectImmediate(old);
        }

        var pointsRoot = new GameObject(ResidentPointsName);
        Undo.RegisterCreatedObjectUndo(pointsRoot, "Create Resident");
        var points = new Transform[ResidentGardenPoints.Length];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new GameObject("GardenPoint_" + i).transform;
            points[i].SetParent(pointsRoot.transform, false);
            points[i].position = ResidentGardenPoints[i];
        }

        var resident = new GameObject(ResidentName);
        resident.transform.position = ResidentHome;
        // facing the street
        resident.transform.rotation = Quaternion.LookRotation(Vector3.forward);
        Undo.RegisterCreatedObjectUndo(resident, "Create Resident");

        // Visual: taller, wider capsule than the inspector, with a "hat" so it reads differently at a distance.
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(resident.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.95f, 0f);
        body.transform.localScale = new Vector3(0.85f, 0.95f, 0.85f);
        var hat = Part("Hat", resident.transform, new Vector3(0f, 2f, 0f), new Vector3(0.7f, 0.15f, 0.7f));
        Part("Nose", resident.transform, new Vector3(0f, 1.6f, 0.4f), new Vector3(0.15f, 0.15f, 0.2f));
        hat.transform.localScale = new Vector3(0.9f, 0.12f, 0.9f);

        var agent = resident.AddComponent<NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 1.9f;
        agent.acceleration = 12f;
        agent.angularSpeed = 360f;
        agent.stoppingDistance = 0.3f;

        var voice = resident.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 1f;
        voice.rolloffMode = AudioRolloffMode.Linear;
        voice.minDistance = 3f;
        // warning heard across street
        voice.maxDistance = 30f;

        var ai = resident.AddComponent<ResidentAIController>();

        // Human senses: eyes higher up, wider view and longer sight than the dog, worse hearing.
        var senses = new SerializedObject(resident.GetComponent<Perception>());
        senses.FindProperty("viewDistance").floatValue = 12f;
        senses.FindProperty("viewAngle").floatValue = 160f;
        senses.FindProperty("closeDetectionRange").floatValue = 2f;
        senses.FindProperty("eyeHeight").floatValue = 1.6f;
        senses.FindProperty("hearingMultiplier").floatValue = 0.8f;
        senses.ApplyModifiedProperties();

        var so = new SerializedObject(ai);
        var pointsProp = so.FindProperty("gardenPoints");
        pointsProp.arraySize = points.Length;
        for (int i = 0; i < points.Length; i++) pointsProp.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
        so.FindProperty("bodyRenderer").objectReferenceValue = body.GetComponent<Renderer>();
        so.FindProperty("voiceSource").objectReferenceValue = voice;
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) so.FindProperty("player").objectReferenceValue = player.transform;
        so.ApplyModifiedProperties();

        if (player != null && player.GetComponent<PlayerHealth>() == null) Undo.AddComponent<PlayerHealth>(player);

        EditorSceneManager.MarkSceneDirty(resident.scene);
        Selection.activeGameObject = resident;
        Debug.Log("[TVPatrol] Resident created at No. 4.");
    }

    static GameObject Part(string name, Transform parent, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Greybox/GB_Trunk.mat");
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }
}

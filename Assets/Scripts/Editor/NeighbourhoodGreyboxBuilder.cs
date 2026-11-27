using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 1. build neighbourhood, replace old greybox when run again
// 2. H1-H4 south, H5-H8 north, doors face street
// 3. dog at H3, resident at H4, park south, player starts west
public static class NeighbourhoodGreyboxBuilder
{
    const string RootName = "Neighbourhood_Greybox";
    const string MaterialFolder = "Assets/Materials/Greybox";
    const string HousePrefabPath = "Assets/Prefabs/Houses/House.prefab";

    const float MapHalfX = 40f;
    const float MapHalfZ = 30f;
    const float StreetHalfWidth = 4f;
    const float PavementWidth = 2f;
    const float SidePathHalfWidth = 1.5f;
    const float HouseRowZ = 14f;
    static readonly float[] HouseColumnsX = { -30f, -14f, 14f, 30f };
    static readonly Vector3 HouseSize = new Vector3(8f, 5f, 8f);

    static readonly HashSet<string> WalkableMaterials = new HashSet<string> { "Grass", "Road", "Pavement", "Park" };
    // built-in "Not Walkable" area
    const int NotWalkableArea = 1;

    static Dictionary<string, Material> materials;

    [MenuItem("TVPatrol/Build Greybox Neighbourhood")]
    public static void Build()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Rebuild greybox?",
                    "This deletes the existing '" + RootName + "' object and rebuilds it.", "Rebuild", "Cancel"))
                return;
            Undo.DestroyObjectImmediate(existing);
        }

        CreateMaterials();

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Greybox Neighbourhood");

        BuildGround(root.transform);
        BuildHouses(root.transform);
        BuildDogYard(root.transform);
        BuildPark(root.transform);
        BuildBoundary(root.transform);
        BuildMarkers(root.transform);
        EnsureLighting();

        var surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[TVPatrol] Greybox neighbourhood built and NavMesh baked.");
    }


    static void BuildGround(Transform root)
    {
        var ground = Group("Ground", root);

        Block("Grass", ground, new Vector3(0f, -0.05f, 0f), new Vector3(MapHalfX * 2f, 0.1f, MapHalfZ * 2f), "Grass");
        Block("MainStreet", ground, new Vector3(0f, 0.01f, 0f), new Vector3(MapHalfX * 2f, 0.02f, StreetHalfWidth * 2f), "Road");

        float pavementZ = StreetHalfWidth + PavementWidth * 0.5f;
        Block("Pavement_North", ground, new Vector3(0f, 0.04f, pavementZ), new Vector3(MapHalfX * 2f, 0.08f, PavementWidth), "Pavement");
        Block("Pavement_South", ground, new Vector3(0f, 0.04f, -pavementZ), new Vector3(MapHalfX * 2f, 0.08f, PavementWidth), "Pavement");

        Block("SidePath", ground, new Vector3(0f, 0.03f, 0f), new Vector3(SidePathHalfWidth * 2f, 0.06f, MapHalfZ * 2f), "Pavement");
    }


    // 1. TV sound does not prove house unlicensed, player must inspect
    // 2. unlicensed houses: H2, H3, H4, H6, H8, quota is 4
    static readonly (bool hasLicence, bool tvIsPlaying)[] HouseStates =
    {
        // H1 licensed, TV on
        (true,  true),
        // H2 unlicensed, TV on
        (false, true),
        // H3 unlicensed, TV on   (dog yard)
        (false, true),
        // H4 unlicensed, TV on   (hostile resident)
        (false, true),
        // H5 licensed, TV off
        (true,  false),
        // H6 unlicensed, TV off  (silent offender)
        (false, false),
        // H7 licensed, TV on     (sound is a red herring)
        (true,  true),
        // H8 unlicensed, TV on
        (false, true),
    };

    static void BuildHouses(Transform root)
    {
        var prefab = CreateHousePrefab();
        var houses = Group("Houses", root);
        int id = 1;

        // South row first (H1..H4), then north row (H5..H8).
        foreach (float side in new[] { -1f, 1f })
        {
            foreach (float x in HouseColumnsX)
            {
                var house = (GameObject)PrefabUtility.InstantiatePrefab(prefab, houses);
                house.name = "House_H" + id;
                // side = +1 for the north row (door faces south), -1 for the south row (door faces north).
                house.transform.SetPositionAndRotation(new Vector3(x, 0f, side * HouseRowZ),
                    Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f));

                var state = HouseStates[id - 1];
                var serializedObject = new SerializedObject(house.GetComponent<HouseInteractable>());
                serializedObject.FindProperty("houseId").stringValue = "H" + id;
                serializedObject.FindProperty("displayName").stringValue = "No. " + id;
                serializedObject.FindProperty("hasLicence").boolValue = state.hasLicence;
                serializedObject.FindProperty("tvIsPlaying").boolValue = state.tvIsPlaying;
                serializedObject.ApplyModifiedProperties();

                if (id == 3 || id == 4)
                {
                    var body = house.transform.Find("Body").GetComponent<MeshRenderer>();
                    body.sharedMaterial = materials[id == 3 ? "HouseDog" : "HouseHostile"];
                    PrefabUtility.RecordPrefabInstancePropertyModifications(body);
                }
                id++;
            }
        }
    }

    // build house prefab and save to Assets/Prefabs/Houses
    static GameObject CreateHousePrefab()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Houses"))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Houses");

        var house = new GameObject("House").transform;

        Block("Body", house, new Vector3(0f, HouseSize.y * 0.5f, 0f), HouseSize, "House", local: true);
        Block("Roof", house, new Vector3(0f, HouseSize.y + 0.4f, 0f), new Vector3(HouseSize.x + 0.6f, 0.8f, HouseSize.z + 0.6f), "Roof", local: true);

        // Local +Z faces the street.
        float front = HouseSize.z * 0.5f;
        Block("Door", house, new Vector3(0f, 1.1f, front + 0.05f), new Vector3(1.2f, 2.2f, 0.1f), "Door", local: true);

        // Garden path from the door to the pavement.
        // distance from house centre to pavement
        float pavementEdge = HouseRowZ - StreetHalfWidth - PavementWidth;
        float pathLength = pavementEdge - front;
        Block("GardenPath", house, new Vector3(0f, 0.03f, front + pathLength * 0.5f), new Vector3(1.6f, 0.06f, pathLength), "Pavement", local: true);

        // Inspection point: where the player stands to inspect.
        var inspectionPoint = new GameObject("InspectionPoint").transform;
        inspectionPoint.SetParent(house, false);
        inspectionPoint.localPosition = new Vector3(0f, 0f, front + 1.5f);

        // 1. TV audio source, just outside the front door so walls can occlude it from the sides and back.
        // 2. Linear rolloff: inaudible beyond 14 m, so with houses 16 m apart you mostly hear one TV at a time.
        var tv = new GameObject("TVAudio");
        tv.transform.SetParent(house, false);
        tv.transform.localPosition = new Vector3(0f, 1.2f, front + 0.3f);
        var audio = tv.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.loop = true;
        audio.spatialBlend = 1f;
        audio.rolloffMode = AudioRolloffMode.Linear;
        audio.minDistance = 2f;
        audio.maxDistance = 14f;

        // Back-garden fence (behind the house).
        float back = -HouseSize.z * 0.5f - 4f;
        Fence("BackFence", house, new Vector3(-6f, 0f, back), new Vector3(6f, 0f, back), local: true);

        var interactable = house.gameObject.AddComponent<HouseInteractable>();
        house.gameObject.AddComponent<TVAudioController>();
        var serializedObject = new SerializedObject(interactable);
        serializedObject.FindProperty("inspectionPoint").objectReferenceValue = inspectionPoint;
        serializedObject.FindProperty("tvAudioSource").objectReferenceValue = audio;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(house.gameObject, HousePrefabPath);
        Object.DestroyImmediate(house.gameObject);
        return prefab;
    }


    static void BuildDogYard(Transform root)
    {
        // Fenced yard around H3 (south row, x = 14) with a 3 m gate towards the street.
        var yard = Group("DogYard_H3", root);
        float x0 = 5f, x1 = 23f;
        // just south of the pavement
        float zFront = -(StreetHalfWidth + PavementWidth) - 0.5f;
        float zBack = -25f;
        float gateHalf = 1.5f;

        Fence("Fence_West", yard, new Vector3(x0, 0f, zFront), new Vector3(x0, 0f, zBack));
        Fence("Fence_East", yard, new Vector3(x1, 0f, zFront), new Vector3(x1, 0f, zBack));
        Fence("Fence_Back", yard, new Vector3(x0, 0f, zBack), new Vector3(x1, 0f, zBack));
        Fence("Fence_Front_W", yard, new Vector3(x0, 0f, zFront), new Vector3(14f - gateHalf - 1f, 0f, zFront));
        Fence("Fence_Front_E", yard, new Vector3(14f + gateHalf + 1f, 0f, zFront), new Vector3(x1, 0f, zFront));
        // The front fence has two gaps: the garden-path gate in the middle is left open for the player/dog.
    }


    static void BuildPark(Transform root)
    {
        var park = Group("Park", root);
        Block("ParkLawn", park, new Vector3(-20f, 0.02f, -26.5f), new Vector3(36f, 0.04f, 5f), "Park");
        Block("Landmark_Fountain", park, new Vector3(-20f, 0.5f, -26.5f), new Vector3(3f, 1f, 3f), "Landmark");
        Block("Landmark_Spire", park, new Vector3(-20f, 3f, -26.5f), new Vector3(0.6f, 6f, 0.6f), "Landmark");

        foreach (var p in new[] { new Vector3(-34f, 0f, -27f), new Vector3(-8f, 0f, -27f), new Vector3(-28f, 0f, -27f) })
            Tree(park, p);
    }

    static void Tree(Transform parent, Vector3 pos)
    {
        var tree = Group("Tree", parent);
        tree.position = pos;
        Block("Trunk", tree, new Vector3(0f, 1f, 0f), new Vector3(0.5f, 2f, 0.5f), "Trunk", local: true);
        Block("Canopy", tree, new Vector3(0f, 3f, 0f), new Vector3(2.5f, 2.5f, 2.5f), "Canopy", local: true);
    }


    static void BuildBoundary(Transform root)
    {
        var b = Group("Boundary", root);
        float h = 2f, t = 0.5f;
        Block("Wall_N", b, new Vector3(0f, h * 0.5f, MapHalfZ), new Vector3(MapHalfX * 2f, h, t), "Boundary");
        Block("Wall_S", b, new Vector3(0f, h * 0.5f, -MapHalfZ), new Vector3(MapHalfX * 2f, h, t), "Boundary");
        Block("Wall_E", b, new Vector3(MapHalfX, h * 0.5f, 0f), new Vector3(t, h, MapHalfZ * 2f), "Boundary");
        Block("Wall_W", b, new Vector3(-MapHalfX, h * 0.5f, 0f), new Vector3(t, h, MapHalfZ * 2f), "Boundary");
    }


    static void BuildMarkers(Transform root)
    {
        var markers = Group("Markers", root);

        var start = new GameObject("PlayerStart").transform;
        start.SetParent(markers, false);
        // west end of the south pavement (bus stop)
        start.position = new Vector3(-37f, 0f, -5f);
        start.rotation = Quaternion.LookRotation(Vector3.right);

        // Dog patrol loop: inside the yard, out through the gate, along the pavement and back.
        var patrol = Group("DogPatrolPoints", markers);
        Vector3[] points =
        {
            new Vector3(9f, 0f, -21f),
            new Vector3(19f, 0f, -21f),
            new Vector3(19f, 0f, -9f),
            new Vector3(14f, 0f, -5f),
            new Vector3(9f, 0f, -9f),
        };
        for (int i = 0; i < points.Length; i++)
        {
            var p = new GameObject("Waypoint_" + i).transform;
            p.SetParent(patrol, false);
            p.position = points[i];
        }

        var hostile = new GameObject("HostileResidentHome_H4").transform;
        hostile.SetParent(markers, false);
        hostile.position = new Vector3(30f, 0f, -HouseRowZ + HouseSize.z * 0.5f + 1.5f);
    }

    static void EnsureLighting()
    {
        if (Object.FindFirstObjectByType<Light>() != null) return;
        var sun = new GameObject("Directional Light");
        var light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        Undo.RegisterCreatedObjectUndo(sun, "Create Light");
    }


    static Transform Group(string name, Transform parent)
    {
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(parent, false);
        return gameObject.transform;
    }

    static GameObject Block(string name, Transform parent, Vector3 position, Vector3 scale, string material, bool local = false)
    {
        var gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gameObject.name = name;
        gameObject.transform.SetParent(parent, false);
        if (local) gameObject.transform.localPosition = position;
        else gameObject.transform.position = position;
        gameObject.transform.localScale = scale;
        gameObject.GetComponent<MeshRenderer>().sharedMaterial = materials[material];
        gameObject.isStatic = true;

        // only bake NavMesh on ground, exclude roofs, fences and walls
        if (!WalkableMaterials.Contains(material))
        {
            var modifier = gameObject.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = NotWalkableArea;
        }
        return gameObject;
    }

    static void Fence(string name, Transform parent, Vector3 from, Vector3 to, bool local = false)
    {
        const float height = 1.2f;
        Vector3 mid = (from + to) * 0.5f + Vector3.up * height * 0.5f;
        Vector3 dir = to - from;
        var fence = Block(name, parent, mid, new Vector3(0.15f, height, dir.magnitude), "Fence", local);
        fence.transform.localRotation = Quaternion.LookRotation(dir.normalized);
    }

    static void CreateMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/Materials", "Greybox");

        var colours = new Dictionary<string, Color>
        {
            { "Grass", new Color(0.45f, 0.65f, 0.35f) },
            { "Road", new Color(0.25f, 0.25f, 0.27f) },
            { "Pavement", new Color(0.70f, 0.70f, 0.68f) },
            { "House", new Color(0.85f, 0.80f, 0.70f) },
            { "HouseDog", new Color(0.85f, 0.65f, 0.40f) },
            { "HouseHostile", new Color(0.75f, 0.40f, 0.40f) },
            { "Roof", new Color(0.55f, 0.25f, 0.20f) },
            { "Door", new Color(0.30f, 0.20f, 0.12f) },
            { "Fence", new Color(0.95f, 0.95f, 0.92f) },
            { "Park", new Color(0.35f, 0.58f, 0.30f) },
            { "Landmark", new Color(0.30f, 0.55f, 0.85f) },
            { "Trunk", new Color(0.40f, 0.28f, 0.18f) },
            { "Canopy", new Color(0.20f, 0.45f, 0.20f) },
            { "Boundary", new Color(0.50f, 0.50f, 0.50f) },
        };

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        materials = new Dictionary<string, Material>();
        foreach (var pair in colours)
        {
            string path = MaterialFolder + "/GB_" + pair.Key + ".material";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", pair.Value);
            material.color = pair.Value;
            EditorUtility.SetDirty(material);
            materials[pair.Key] = material;
        }
        AssetDatabase.SaveAssets();
    }
}

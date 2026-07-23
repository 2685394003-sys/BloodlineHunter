#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Creates saved, editable scene nodes. It is never called at runtime.</summary>
public static class PrototypeSceneBuilder
{
    private const string WorldName = "Prototype World";
    private const string TreePath = "Assets/Resources/sprite_0.png";
    private const string PlaceholderPath = "Assets/Art/DefaultSquare.png";

    [MenuItem("Tools/Top Down Prototype/Build Editable Sample Scene")]
    private static void BuildEditableSampleScene()
    {
        Sprite placeholder = GetOrCreatePlaceholderSprite();
        Sprite treeSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TreePath);
        if (treeSprite == null)
        {
            Debug.LogError("Could not load sprite_0.png as a Sprite. Let Unity finish importing it, then run this menu item again.");
            return;
        }

        if (GameObject.Find(WorldName) != null)
        {
            RebuildPerspectiveBaseline();
            return;
        }

        var world = new GameObject(WorldName).transform;
        CreateGround(world);
        GameObject player = CreateActor("Player", world, Vector3.zero, placeholder, new Color(0.25f, 0.65f, 1f), true);
        GameObject enemy = CreateActor("Chasing Enemy", world, new Vector3(7f, 0f, 5f), placeholder, new Color(0.95f, 0.27f, 0.25f), false);
        enemy.GetComponent<ChasingEnemy>().SetTarget(player.transform);
        CreateTrees(world, treeSprite);
        ConfigureCamera(player.transform);
        AlignSpriteCardsToMainCamera();
        ConfigureLighting(world);

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Selection.activeGameObject = world.gameObject;
        Debug.Log("Created editable Prototype World scene nodes.");
    }

    [MenuItem("Tools/Top Down Prototype/Create Separate Prototype Scene")]
    public static void CreateSeparatePrototypeScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildEditableSampleScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/PrototypeScene.unity", true);
        Debug.Log("Created Assets/Scenes/PrototypeScene.unity without changing SampleScene.");
    }

    [MenuItem("Tools/Top Down Prototype/Rebuild Perspective Baseline")]
    private static void RebuildPerspectiveBaseline()
    {
        Sprite placeholder = GetOrCreatePlaceholderSprite();
        Sprite treeSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TreePath);
        if (treeSprite == null)
        {
            Debug.LogError("Could not load sprite_0.png as a Sprite. Let Unity finish importing it, then run this menu item again.");
            return;
        }

        Transform world = GameObject.Find(WorldName)?.transform;
        if (world == null)
        {
            BuildEditableSampleScene();
            return;
        }

        CreateGroundIfMissing(world);
        GameObject player = EnsureActor("Player", world, Vector3.zero, placeholder, new Color(0.25f, 0.65f, 1f), true);
        GameObject enemy = EnsureActor("Chasing Enemy", world, new Vector3(4.5f, 0f, 3.5f), placeholder, new Color(0.95f, 0.27f, 0.25f), false);
        enemy.GetComponent<ChasingEnemy>().SetTarget(player.transform);
        RebuildBaselineTrees(world, treeSprite);
        ConfigureCamera(player.transform);
        AlignSpriteCardsToMainCamera();
        ConfigureLighting(world);

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Selection.activeGameObject = player.transform.Find("Camera Rig/Main Camera")?.gameObject;
        Debug.Log("Rebuilt editable perspective baseline: planar roots, upright sprite cards, and three composition trees.");
    }

    [MenuItem("Tools/Top Down Prototype/Lay Existing Sprite Layers Flat")]
    private static void LayExistingSpriteLayersFlat()
    {
        foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            Transform layer = renderer.transform;
            layer.localRotation = Quaternion.Euler(90f, 0f, 0f);
            layer.localPosition = new Vector3(layer.localPosition.x, 0.03f, layer.localPosition.z);
        }

        Transform treeGroup = GameObject.Find($"{WorldName}/Tree Decorations")?.transform;
        if (treeGroup != null)
        {
            foreach (Transform tree in treeGroup)
            {
                Transform layer = tree.Find("Sprite Layer");
                if (layer != null) layer.localPosition = Vector3.up * 0.02f;
            }
        }

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("Laid all editable sprite layers flat on the ground plane.");
    }

    [MenuItem("Tools/Top Down Prototype/Align Sprite Cards To Main Camera")]
    private static void AlignSpriteCardsToMainCamera()
    {
        Camera camera = Camera.main;
        Transform world = GameObject.Find(WorldName)?.transform;
        if (camera == null || world == null)
        {
            Debug.LogError("Main Camera and Prototype World must exist before aligning sprite cards.");
            return;
        }

        foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            Transform layer = renderer.transform;
            if (!layer.IsChildOf(world)) continue;
            // The artwork plane is parallel to the camera image plane.  Its
            // position comes exclusively from its root/Pivot in the scene.
            layer.rotation = camera.transform.rotation;
        }

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("Aligned editable sprite cards to Main Camera; their Pivot nodes remain on the XZ gameplay plane.");
    }

    [MenuItem("Tools/Top Down Prototype/Convert Main Camera To Editable Rig")]
    private static void ConvertMainCameraToEditableRig()
    {
        Camera camera = Camera.main;
        Transform player = GameObject.Find($"{WorldName}/Player")?.transform;
        if (camera == null || player == null)
        {
            Debug.LogError("Main Camera and Prototype World/Player must exist before creating the Camera Rig.");
            return;
        }

        Transform rig = player.Find("Camera Rig") ?? GameObject.Find("Camera Rig")?.transform;
        if (rig == null)
        {
            rig = new GameObject("Camera Rig").transform;
            rig.position = Vector3.zero;
            rig.rotation = Quaternion.identity;
        }

        // Keep the old component harmless instead of deleting it from the scene.
        TopDownCamera oldCameraFollower = camera.GetComponent<TopDownCamera>();
        if (oldCameraFollower != null) oldCameraFollower.enabled = false;

        camera.transform.SetParent(rig, false);
        ApplyCameraComposition(camera);
        camera.farClipPlane = 100f;

        // Direct parent-child binding is fully inspectable and cannot lose a
        // runtime target reference. Player's root no longer rotates for aiming.
        rig.SetParent(player, false);
        rig.localPosition = Vector3.zero;
        rig.localRotation = Quaternion.identity;
        TopDownCamera rigFollower = rig.GetComponent<TopDownCamera>();
        if (rigFollower != null) rigFollower.enabled = false;

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Selection.activeGameObject = camera.gameObject;
        Debug.Log("Created Camera Rig. Tune Main Camera local Position and Rotation in the Inspector.");
    }

    private static GameObject CreateActor(string name, Transform parent, Vector3 position, Sprite sprite, Color colour, bool isPlayer)
    {
        var actor = new GameObject(name, typeof(BoxCollider));
        actor.transform.SetParent(parent);
        actor.transform.position = position;
        actor.GetComponent<BoxCollider>().center = new Vector3(0f, 0.55f, 0f);
        actor.GetComponent<BoxCollider>().size = new Vector3(1.3f, 1.1f, 1.3f);
        if (isPlayer) actor.AddComponent<PlayerController>(); else actor.AddComponent<ChasingEnemy>();
        CreateSpriteLayer("Sprite Layer", actor.transform, sprite, colour, Vector3.up * 0.72f, Vector3.one * 1.5f, actor.transform);
        return actor;
    }

    private static GameObject EnsureActor(string name, Transform parent, Vector3 position, Sprite sprite, Color colour, bool isPlayer)
    {
        Transform existing = parent.Find(name);
        GameObject actor = existing != null ? existing.gameObject : CreateActor(name, parent, position, sprite, colour, isPlayer);
        actor.transform.localPosition = position;
        actor.transform.localRotation = Quaternion.identity;
        var collider = actor.GetComponent<BoxCollider>() ?? actor.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 0.55f, 0f);
        collider.size = new Vector3(1.3f, 1.1f, 1.3f);
        if (isPlayer && actor.GetComponent<PlayerController>() == null) actor.AddComponent<PlayerController>();
        if (!isPlayer && actor.GetComponent<ChasingEnemy>() == null) actor.AddComponent<ChasingEnemy>();

        Transform layer = actor.transform.Find("Sprite Layer");
        if (layer == null) layer = new GameObject("Sprite Layer").transform;
        ConfigureCameraAlignedSpriteLayer(layer, actor.transform, sprite, colour, Vector3.zero, Vector3.one * 1.5f);
        return actor;
    }

    private static void CreateGround(Transform parent)
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(parent);
        ground.transform.localScale = new Vector3(4f, 1f, 4f);
        ground.GetComponent<Renderer>().sharedMaterial.color = new Color(0.20f, 0.31f, 0.20f);
    }

    private static void CreateGroundIfMissing(Transform parent)
    {
        Transform ground = parent.Find("Ground");
        if (ground == null)
        {
            CreateGround(parent);
            return;
        }

        ground.localPosition = Vector3.zero;
        ground.localRotation = Quaternion.identity;
        ground.localScale = new Vector3(4f, 1f, 4f);
        Renderer renderer = ground.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial.color = new Color(0.20f, 0.31f, 0.20f);
    }

    private static void RefreshActorSprite(string actorName, Sprite sprite, Color colour)
    {
        Transform visual = GameObject.Find($"{WorldName}/{actorName}/Sprite Layer")?.transform;
        if (visual == null) return;
        var renderer = visual.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = colour;
    }

    private static void CreateTrees(Transform parent, Sprite sprite)
    {
        var group = new GameObject("Tree Decorations").transform;
        group.SetParent(parent);
        var random = new System.Random(2307);
        for (int i = 0; i < 36; i++)
        {
            float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
            float radius = Mathf.Lerp(8.5f, 18f, (float)random.NextDouble());
            var tree = new GameObject($"Tree {i + 1:00}").transform;
            tree.SetParent(group);
            tree.position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            CreateSpriteLayer("Sprite Layer", tree, sprite, Color.white, Vector3.zero,
                Vector3.one * Mathf.Lerp(0.8f, 1.45f, (float)random.NextDouble()), tree);
        }
    }

    private static void RebuildBaselineTrees(Transform parent, Sprite sprite)
    {
        Transform oldGroup = parent.Find("Tree Decorations");
        if (oldGroup != null) Object.DestroyImmediate(oldGroup.gameObject);

        Transform group = new GameObject("Tree Decorations").transform;
        group.SetParent(parent, false);
        Vector3[] positions = { new Vector3(-3.8f, 0f, 2.4f), new Vector3(3.2f, 0f, 5.8f), new Vector3(-5.5f, 0f, 7.3f) };
        for (int i = 0; i < positions.Length; i++)
        {
            Transform tree = new GameObject($"Tree {i + 1:00}").transform;
            tree.SetParent(group, false);
            tree.localPosition = positions[i];
            Transform layer = new GameObject("Sprite Layer").transform;
            ConfigureCameraAlignedSpriteLayer(layer, tree, sprite, Color.white, Vector3.zero, Vector3.one * 2.2f);
        }
    }

    private static void CreateSpriteLayer(string name, Transform parent, Sprite sprite, Color colour, Vector3 localPosition, Vector3 localScale, Transform sortingAnchor)
    {
        Transform layer = new GameObject(name).transform;
        ConfigureCameraAlignedSpriteLayer(layer, parent, sprite, colour, localPosition, localScale, sortingAnchor);
    }

    private static void ConfigureCameraAlignedSpriteLayer(Transform layer, Transform parent, Sprite sprite, Color colour, Vector3 localPosition, Vector3 localScale, Transform sortingAnchor = null)
    {
        layer.SetParent(parent, false);
        layer.localPosition = localPosition;
        layer.localRotation = Quaternion.identity;
        layer.localScale = localScale;
        SpriteRenderer renderer = layer.GetComponent<SpriteRenderer>() ?? layer.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = colour;
        PerspectiveSpriteSorting sorting = layer.GetComponent<PerspectiveSpriteSorting>() ?? layer.gameObject.AddComponent<PerspectiveSpriteSorting>();
        sorting.SetAnchor(sortingAnchor ?? parent);
    }

    private static void ConfigureCamera(Transform player)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            camera = cameraObject.GetComponent<Camera>();
        }

        Transform rig = player.Find("Camera Rig");
        if (rig == null)
        {
            rig = new GameObject("Camera Rig").transform;
            rig.SetParent(player, false);
        }
        rig.localPosition = Vector3.zero;
        rig.localRotation = Quaternion.identity;
        camera.transform.SetParent(rig, false);
        ApplyCameraComposition(camera);
        camera.farClipPlane = 100f;
        TopDownCamera follow = camera.GetComponent<TopDownCamera>();
        if (follow != null) follow.enabled = false;
    }

    private static void ApplyCameraComposition(Camera camera)
    {
        // distance = lerp(14, 25, 0.5) = 19.5; pitch = lerp(14, 40, 0.5) = 27.
        // These are written into the actual editable Camera transform, not driven
        // by a runtime follow or billboard component.
        camera.transform.localPosition = new Vector3(-12.285717f, 9.652815f, -12.285717f);
        camera.transform.localRotation = new Quaternion(0.2156754f, 0.3721099f, -0.08933568f, 0.8983526f);
        camera.fieldOfView = 35f;
    }

    private static void ConfigureLighting(Transform parent)
    {
        if (Object.FindFirstObjectByType<Light>() != null) return;
        var lightObject = new GameObject("Directional Light", typeof(Light));
        lightObject.transform.SetParent(parent);
        lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
        var light = lightObject.GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.3f;
        light.shadows = LightShadows.Soft;
    }

    private static Sprite GetOrCreatePlaceholderSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderPath);
        if (existing != null) return existing;

        Directory.CreateDirectory(Path.GetDirectoryName(PlaceholderPath));
        var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++) texture.SetPixel(x, y, Color.white);
        File.WriteAllBytes(PlaceholderPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(PlaceholderPath, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(PlaceholderPath) as TextureImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderPath);
    }
}
#endif

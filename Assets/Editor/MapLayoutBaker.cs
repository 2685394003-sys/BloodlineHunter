#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 墓地地图固定布局摆放器 / Cemetery map fixed-layout baker
/// 一次性把 prefab 摆进场景,变成永久场景物体(保存到 .unity)
/// One-shot: places prefabs into the scene as permanent scene objects (saved to .unity)
///
/// 用法 / Usage:
///   菜单 Tools → 地图布局 → 摆放布局 / Bake Layout
///   菜单 Tools → 地图布局 → 清空布局 / Clear Layout
///
/// 与 LevelGenerator/DecorationGenerator 的区别:
/// - 那两个是【运行时】随机生成,每次进游戏都变
/// - 这个是【编辑器】一次性摆放,场景里固定不变
/// </summary>
public static class MapLayoutBaker
{
    // 布局父物体名(挂在 Map 下,便于管理)
    const string LayoutRootName = "BakedLayout";
    const string MapRootName = "Map";

    struct Entry
    {
        public string prefabPath;   // 完整路径(因为名字带空格和括号)
        public Vector3 position;
        public float yRotation;

        public Entry(string path, float x, float z, float rot = 0f)
        {
            prefabPath = path;
            position = new Vector3(x, 0f, z);
            yRotation = rot;
        }
    }

    // ============ 布局设计 / Layout design ============
    // 玩家出生点约在 (23, 16),Boss 在 (50, 95),中间留空
    // 地板 X 0~50,Z 0~95
    static readonly Entry[] Layout = new Entry[]
    {
        // 西侧墓碑群(5 个)
        new Entry("Assets/Prefabs/Map/graveB.prefab",     8f, 30f,   0f),
        new Entry("Assets/Prefabs/Map/graveB.prefab",    11f, 35f,  45f),
        new Entry("Assets/Prefabs/Map/gravestone.prefab", 9f, 40f,  90f),
        new Entry("Assets/Prefabs/Map/gravestone.prefab",10f, 45f,   0f),
        new Entry("Assets/Prefabs/Map/graveB.prefab",    12f, 50f, 180f),

        // 东侧墓碑群(5 个)
        new Entry("Assets/Prefabs/Map/graveB.prefab",    40f, 30f,   0f),
        new Entry("Assets/Prefabs/Map/gravestone.prefab",38f, 35f,  90f),
        new Entry("Assets/Prefabs/Map/graveB.prefab",    41f, 40f,   0f),
        new Entry("Assets/Prefabs/Map/gravestone.prefab",39f, 45f, 270f),
        new Entry("Assets/Prefabs/Map/graveB.prefab",    40f, 50f,   0f),

        // 北中央石碑阵(3 个)
        new Entry("Assets/Prefabs/Map/gravestone.prefab",25f, 70f,   0f),
        new Entry("Assets/Prefabs/Map/gravestone.prefab",20f, 72f,  90f),
        new Entry("Assets/Prefabs/Map/gravestone.prefab",30f, 72f, 270f),

        // 中部掩体盒子(4 个)
        new Entry("Assets/Prefabs/Map/Box029 (1).prefab",20f, 50f,   0f),
        new Entry("Assets/Prefabs/Map/Box030 (1).prefab",28f, 50f,  45f),
        new Entry("Assets/Prefabs/Map/Box029 (1).prefab",18f, 55f,  90f),
        new Entry("Assets/Prefabs/Map/Box030 (1).prefab",30f, 55f,   0f),

        // 南侧入口装饰(2 个)
        new Entry("Assets/Prefabs/Map/graveB.prefab",    15f, 10f,   0f),
        new Entry("Assets/Prefabs/Map/graveB.prefab",    35f, 10f,   0f),
    };

    [MenuItem("Tools/地图布局/摆放布局 Bake Layout")]
    static void Bake()
    {
        // 1. 找/建 Map 根
        GameObject map = GameObject.Find(MapRootName);
        if (map == null)
        {
            map = new GameObject(MapRootName);
            Debug.Log("[MapLayoutBaker] 没找到 Map,已新建 / Map not found,created new");
        }

        // 2. 找/建 BakedLayout 子物体(清空旧的)
        Transform old = map.transform.Find(LayoutRootName);
        if (old != null)
        {
            Object.DestroyImmediate(old.gameObject);
            Debug.Log("[MapLayoutBaker] 已清空旧的 BakedLayout / Cleared old BakedLayout");
        }
        GameObject layoutRoot = new GameObject(LayoutRootName);
        layoutRoot.transform.SetParent(map.transform, false);
        layoutRoot.transform.localPosition = Vector3.zero;

        // 3. 逐个摆放
        int placed = 0, failed = 0;
        foreach (var entry in Layout)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[MapLayoutBaker] 找不到 prefab / prefab not found: " + entry.prefabPath);
                failed++;
                continue;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(layoutRoot.transform, false);
            instance.transform.position = entry.position;
            instance.transform.rotation = Quaternion.Euler(0f, entry.yRotation, 0f);
            // 确保 layer 3 (obstacle layer),让 FlowFieldManager 能识别
            SetLayerRecursively(instance, 3);
            placed++;
        }

        // 4. 标记场景 dirty,提醒保存
        EditorSceneManager.MarkSceneDirty(layoutRoot.scene);
        Selection.activeGameObject = layoutRoot;

        Debug.Log("[MapLayoutBaker] 摆放完成 / Bake done: " +
                  placed + "/" + Layout.Length + " 个 prefab 已放入场景" +
                  (failed > 0 ? ("(失败 " + failed + ")") : "") +
                  "。请按 Ctrl+S 保存 / Please Ctrl+S to save.");
    }

    [MenuItem("Tools/地图布局/清空布局 Clear Layout")]
    static void Clear()
    {
        GameObject map = GameObject.Find(MapRootName);
        if (map == null)
        {
            Debug.LogWarning("[MapLayoutBaker] 没找到 Map / Map not found");
            return;
        }
        Transform layoutRoot = map.transform.Find(LayoutRootName);
        if (layoutRoot == null)
        {
            Debug.LogWarning("[MapLayoutBaker] 没找到 BakedLayout / BakedLayout not found");
            return;
        }
        Object.DestroyImmediate(layoutRoot.gameObject);
        EditorSceneManager.MarkSceneDirty(map.scene);
        Debug.Log("[MapLayoutBaker] 已清空 BakedLayout / Cleared. 请按 Ctrl+S 保存 / Please Ctrl+S to save.");
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}
#endif

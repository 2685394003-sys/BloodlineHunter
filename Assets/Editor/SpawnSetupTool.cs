using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 刷怪配置一键补全工具(SpawnSetupTool)。
/// 菜单:Tools → 刷怪设置 → 一键补全刷怪组件
/// 做的事:
/// 1. 找到场景里的 shengchengfangxiang(生成方向),补挂 SpawnPointsAimBoss;
/// 2. 给所有缺 enemyPrefab 的 MonsterSpawnPoint 子点填入 Enemy 预制体;
/// 3. 标记场景为未保存,选中该物体,Console 输出处理结果。
/// </summary>
public static class SpawnSetupTool
{
    private const string TargetName = "shengchengfangxiang";
    private const string EnemyPrefabPath = "Assets/Prefabs/Enemies/Enemy.prefab";

    [MenuItem("Tools/刷怪设置/一键补全刷怪组件 Setup Spawn")]
    public static void Setup()
    {
        GameObject go = GameObject.Find(TargetName);
        if (go == null)
        {
            Debug.LogError("[刷怪设置] 场景里找不到名为 " + TargetName + " 的物体,请确认当前打开的是 New Scene。");
            return;
        }

        int fixedCount = 0;

        // 1. 补挂 SpawnPointsAimBoss(生成方向对准 Boss)
        if (go.GetComponent<SpawnPointsAimBoss>() == null)
        {
            go.AddComponent<SpawnPointsAimBoss>();
            fixedCount++;
            Debug.Log("[刷怪设置] 已在 " + TargetName + " 上补挂 SpawnPointsAimBoss。", go);
        }

        // 2. 给缺 enemyPrefab 的子点填入敌人预制体
        GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        if (enemyPrefab == null)
        {
            Debug.LogError("[刷怪设置] 找不到敌人预制体:" + EnemyPrefabPath);
        }
        else
        {
            MonsterSpawnPoint[] points = go.GetComponentsInChildren<MonsterSpawnPoint>(true);
            foreach (MonsterSpawnPoint sp in points)
            {
                if (sp.enemyPrefab == null)
                {
                    sp.enemyPrefab = enemyPrefab;
                    fixedCount++;
                    Debug.Log("[刷怪设置] 已给子点 " + sp.name + " 填入 Enemy 预制体。", sp);
                }
            }
            if (points.Length == 0)
            {
                Debug.LogWarning("[刷怪设置] " + TargetName + " 下没有任何 MonsterSpawnPoint,请先给 6 个子点挂脚本。");
            }
        }

        // 3. 收尾:标记场景未保存 + 选中物体 + 汇报
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        if (fixedCount == 0)
        {
            Debug.Log("[刷怪设置] 检查完毕:组件和预制体都已配好,无需补全。记得 Ctrl+S 保存场景。", go);
        }
        else
        {
            Debug.Log("[刷怪设置] 补全完成,共处理 " + fixedCount + " 处。记得 Ctrl+S 保存场景!", go);
        }
    }
}

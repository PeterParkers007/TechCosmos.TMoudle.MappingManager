using System;
using System.Collections.Generic;
using TechCosmos.MappingManager.Runtime;
using UnityEditor;
using UnityEngine;
namespace TechCosmos.MappingManager.Editor
{
    /// <summary>
    /// MappingManager 的运行时调试窗口。
    /// 实时展示所有已注册的类型对和映射实例，支持点击定位到场景对象。
    /// </summary>
    public class MappingManagerWindow : EditorWindow
    {
        #region 窗口入口

        [MenuItem("Tech-Cosmos/Mapping Manager/Open Debugger")]
        public static void ShowWindow()
        {
            var window = GetWindow<MappingManagerWindow>("Mapping 调试器");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        #endregion

        #region 字段

        /// <summary>当前选中的类型对索引</summary>
        private int selectedTypePairIndex = -1;

        /// <summary>当前选中的映射索引</summary>
        private int selectedMappingIndex = -1;

        /// <summary>缓存的类型对列表，避免每帧通过 IEnumerable 重新遍历</summary>
        private List<(Type typeA, Type typeB)> cachedTypePairs = new();

        /// <summary>是否开启自动刷新</summary>
        private bool autoRefresh = true;

        /// <summary>自动刷新间隔（秒）</summary>
        private float refreshInterval = 0.5f;

        /// <summary>上一次刷新的时间</summary>
        private double lastRefreshTime;

        /// <summary>滚动位置：左侧类型对列表</summary>
        private Vector2 leftScrollPos;

        /// <summary>滚动位置：右侧映射详情</summary>
        private Vector2 rightScrollPos;

        /// <summary>搜索过滤关键词</summary>
        private string searchFilter = "";

        #endregion

        #region 生命周期

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        /// <summary>
        /// 编辑器每帧回调，处理自动刷新逻辑。
        /// </summary>
        private void OnEditorUpdate()
        {
            if (!autoRefresh) return;

            if (EditorApplication.timeSinceStartup - lastRefreshTime >= refreshInterval)
            {
                lastRefreshTime = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        #endregion

        #region GUI 绘制

        private void OnGUI()
        {
            var manager = GetManager();

            // 顶部工具栏
            DrawToolbar(manager);

            if (manager == null)
            {
                EditorGUILayout.HelpBox(
                    "未找到 MappingManager 实例。\n请运行场景，Manager 会在首次访问时自动创建。",
                    MessageType.Info);
                return;
            }

            // 刷新类型对缓存
            RefreshTypePairsCache(manager);

            // 左右分栏布局
            EditorGUILayout.BeginHorizontal();

            // 左侧：类型对列表
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.4f));
            DrawTypePairList(manager);
            EditorGUILayout.EndVertical();

            // 分割线
            EditorGUILayout.BeginVertical(GUILayout.Width(2));
            var rect = EditorGUILayout.GetControlRect(GUILayout.Width(2), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(rect, Color.gray);
            EditorGUILayout.EndVertical();

            // 右侧：映射详情
            EditorGUILayout.BeginVertical();
            DrawMappingDetail(manager);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 工具栏

        /// <summary>
        /// 绘制顶部工具栏：自动刷新开关、刷新按钮、清空按钮。
        /// </summary>
        private void DrawToolbar(Runtime.MappingManager manager)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // 自动刷新
            autoRefresh = EditorGUILayout.ToggleLeft("自动刷新", autoRefresh, GUILayout.Width(80));

            if (autoRefresh)
            {
                refreshInterval = EditorGUILayout.FloatField("间隔(秒)", refreshInterval, GUILayout.Width(120));
            }

            GUILayout.FlexibleSpace();

            // 搜索框
            EditorGUILayout.LabelField("搜索:", GUILayout.Width(35));
            searchFilter = EditorGUILayout.TextField(searchFilter, EditorStyles.toolbarTextField, GUILayout.Width(150));

            // 手动刷新
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                Repaint();
            }

            // 清空所有映射
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("清空全部", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                if (EditorUtility.DisplayDialog("确认清空",
                        "确定要清空所有映射关系吗？此操作不可撤销。", "确定", "取消"))
                {
                    manager?.Clear();
                    selectedTypePairIndex = -1;
                    selectedMappingIndex = -1;
                    Repaint();
                }
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 左侧：类型对列表

        /// <summary>
        /// 绘制左侧类型对列表，展示所有泛型组合。
        /// </summary>
        private void DrawTypePairList(Runtime.MappingManager manager)
        {
            EditorGUILayout.LabelField("类型对列表", EditorStyles.boldLabel);

            if (cachedTypePairs.Count == 0)
            {
                EditorGUILayout.HelpBox("暂无映射数据。", MessageType.Info);
                return;
            }

            leftScrollPos = EditorGUILayout.BeginScrollView(leftScrollPos);

            for (int i = 0; i < cachedTypePairs.Count; i++)
            {
                var pair = cachedTypePairs[i];

                // 搜索过滤
                if (!string.IsNullOrEmpty(searchFilter))
                {
                    var typePairName = $"{pair.typeA.Name} ↔ {pair.typeB.Name}";
                    if (!typePairName.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // 获取该类型对下的映射数量
                int count = GetMappingCount(manager, pair.typeA, pair.typeB);

                // 选中高亮
                var style = (i == selectedTypePairIndex) ? EditorStyles.whiteLabel : EditorStyles.label;
                var bgColor = (i == selectedTypePairIndex) ? new Color(0.3f, 0.5f, 0.8f, 0.5f) : Color.clear;

                var buttonRect = EditorGUILayout.BeginHorizontal();

                // 背景色
                EditorGUI.DrawRect(buttonRect, bgColor);

                // 可点击的行
                if (GUILayout.Button($"{pair.typeA.Name} ↔ {pair.typeB.Name}  ({count})", style,
                        GUILayout.ExpandWidth(true)))
                {
                    selectedTypePairIndex = i;
                    selectedMappingIndex = -1;
                }

                // 删除该类型对的所有映射
                if (GUILayout.Button("×", GUILayout.Width(20)))
                {
                    if (EditorUtility.DisplayDialog("确认删除",
                            $"确定要删除 <{pair.typeA.Name}, {pair.typeB.Name}> 下的所有映射吗？",
                            "确定", "取消"))
                    {
                        RemoveAllMappingsOfType(manager, pair.typeA, pair.typeB);
                        selectedTypePairIndex = -1;
                        selectedMappingIndex = -1;
                        Repaint();
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region 右侧：映射详情

        /// <summary>
        /// 绘制右侧映射详情面板，展示选中类型对下的具体映射实例。
        /// </summary>
        private void DrawMappingDetail(Runtime.MappingManager manager)
        {
            if (selectedTypePairIndex < 0 || selectedTypePairIndex >= cachedTypePairs.Count)
            {
                EditorGUILayout.HelpBox("← 请从左侧列表选择一个类型对查看详情", MessageType.Info);
                return;
            }

            var pair = cachedTypePairs[selectedTypePairIndex];
            EditorGUILayout.LabelField($"映射详情: {pair.typeA.Name} ↔ {pair.typeB.Name}", EditorStyles.boldLabel);

            // 获取该类型对下的所有映射
            var mappings = GetMappingsOfType(manager, pair.typeA, pair.typeB);

            if (mappings.Count == 0)
            {
                EditorGUILayout.HelpBox("该类型对下暂无映射实例。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"共 {mappings.Count} 条映射", EditorStyles.miniLabel);

            rightScrollPos = EditorGUILayout.BeginScrollView(rightScrollPos);

            for (int i = 0; i < mappings.Count; i++)
            {
                var mapping = mappings[i];

                // 搜索过滤
                if (!string.IsNullOrEmpty(searchFilter))
                {
                    var keyName = mapping.Key != null ? mapping.Key.name : "null";
                    var valueName = mapping.Value != null ? mapping.Value.name : "null";
                    var combined = $"{keyName} {valueName}";
                    if (!combined.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // 选中高亮
                var bgColor = (i == selectedMappingIndex) ? new Color(0.3f, 0.5f, 0.8f, 0.3f) :
                              (i % 2 == 0) ? new Color(0.2f, 0.2f, 0.2f, 0.2f) : Color.clear;

                var rowRect = EditorGUILayout.BeginHorizontal();
                EditorGUI.DrawRect(rowRect, bgColor);

                // Key 字段（可点击定位）
                EditorGUILayout.LabelField("Key:", GUILayout.Width(35));
                if (mapping.Key != null)
                {
                    if (GUILayout.Button(mapping.Key.name, EditorStyles.linkLabel, GUILayout.MinWidth(80)))
                    {
                        Selection.activeGameObject = mapping.Key.gameObject;
                        EditorGUIUtility.PingObject(mapping.Key.gameObject);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("(已销毁)", EditorStyles.miniLabel, GUILayout.MinWidth(60));
                }

                // 箭头
                EditorGUILayout.LabelField("→", GUILayout.Width(15));

                // Value 字段（可点击定位）
                EditorGUILayout.LabelField("Value:", GUILayout.Width(40));
                if (mapping.Value != null)
                {
                    if (GUILayout.Button(mapping.Value.name, EditorStyles.linkLabel, GUILayout.MinWidth(80)))
                    {
                        Selection.activeGameObject = mapping.Value.gameObject;
                        EditorGUIUtility.PingObject(mapping.Value.gameObject);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("(已销毁)", EditorStyles.miniLabel, GUILayout.MinWidth(60));
                }

                GUILayout.FlexibleSpace();

                // 删除按钮
                if (mapping.Key != null)
                {
                    if (GUILayout.Button("删除", GUILayout.Width(40)))
                    {
                        RemoveSingleMapping(manager, pair.typeA, pair.typeB, mapping.Key);
                        Repaint();
                    }
                }

                EditorGUILayout.EndHorizontal();

                // 点击整行选中
                var clickRect = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.MouseDown && clickRect.Contains(Event.current.mousePosition))
                {
                    selectedMappingIndex = i;
                    Repaint();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取 MappingManager 实例，处理未找到的情况。
        /// </summary>
        private Runtime.MappingManager GetManager()
        {
            return FindObjectOfType<Runtime.MappingManager>();
        }

        /// <summary>
        /// 刷新类型对缓存列表。
        /// </summary>
        private void RefreshTypePairsCache(Runtime.MappingManager manager)
        {
            cachedTypePairs.Clear();
            foreach (var pair in manager.GetAllTypePairs())
            {
                cachedTypePairs.Add(pair);
            }

            // 防止选中索引越界
            if (selectedTypePairIndex >= cachedTypePairs.Count)
                selectedTypePairIndex = -1;
        }

        /// <summary>
        /// 通过反射获取指定类型对下的映射数量。
        /// </summary>
        /// <remarks>绕过泛型约束，在编辑器代码中动态调用 GetMappingCount。</remarks>
        private int GetMappingCount(Runtime.MappingManager manager, Type typeA, Type typeB)
        {
            var method = typeof(Runtime.MappingManager).GetMethod(nameof(Runtime.MappingManager.GetMappingCount));
            if (method == null) return 0;

            var genericMethod = method.MakeGenericMethod(typeA, typeB);
            return (int)genericMethod.Invoke(manager, null);
        }

        /// <summary>
        /// 通过反射获取指定类型对下的所有映射（Key-Value 对）。
        /// </summary>
        private List<KeyValuePair<MonoBehaviour, MonoBehaviour>> GetMappingsOfType(
            Runtime.MappingManager manager, Type typeA, Type typeB)
        {
            var result = new List<KeyValuePair<MonoBehaviour, MonoBehaviour>>();

            // 使用反射调用 GetAllValues 获取所有 Value
            var getAllValuesMethod = typeof(Runtime.MappingManager).GetMethod(nameof(Runtime.MappingManager.GetAllValues));
            if (getAllValuesMethod == null) return result;

            var genericGetAllValues = getAllValuesMethod.MakeGenericMethod(typeA, typeB);
            var values = genericGetAllValues.Invoke(manager, null) as System.Collections.IList;

            if (values == null) return result;

            // 通过反射调用 GetKey 反查每个 Value 对应的 Key
            var getKeyMethod = typeof(Runtime.MappingManager).GetMethod(nameof(Runtime.MappingManager.GetKey));
            if (getKeyMethod == null) return result;

            var genericGetKey = getKeyMethod.MakeGenericMethod(typeA, typeB);

            foreach (var value in values)
            {
                var mbValue = value as MonoBehaviour;
                if (mbValue == null) continue;

                var key = genericGetKey.Invoke(manager, new object[] { mbValue }) as MonoBehaviour;
                result.Add(new KeyValuePair<MonoBehaviour, MonoBehaviour>(key, mbValue));
            }

            return result;
        }

        /// <summary>
        /// 通过反射删除指定类型对下的单条映射。
        /// </summary>
        private void RemoveSingleMapping(Runtime.MappingManager manager, Type typeA, Type typeB, MonoBehaviour key)
        {
            var method = typeof(Runtime.MappingManager).GetMethod(nameof(Runtime.MappingManager.Unregister));
            if (method == null) return;

            var genericMethod = method.MakeGenericMethod(typeA, typeB);
            genericMethod.Invoke(manager, new object[] { key });
        }

        /// <summary>
        /// 通过反射删除指定类型对下的所有映射。
        /// </summary>
        /// <remarks>通过遍历并逐一调用 Unregister 实现。</remarks>
        private void RemoveAllMappingsOfType(Runtime.MappingManager manager, Type typeA, Type typeB)
        {
            var mappings = GetMappingsOfType(manager, typeA, typeB);
            foreach (var mapping in mappings)
            {
                if (mapping.Key != null)
                    RemoveSingleMapping(manager, typeA, typeB, mapping.Key);
            }
        }

        #endregion
    }
}

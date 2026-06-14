using System;
using System.Collections.Generic;
using UnityEngine;
namespace TechCosmos.MappingManager.Runtime
{
    /// <summary>
    /// 类型安全的双向映射管理器。
    /// 用于在两种 MonoBehaviour 之间建立显式的、可查询的关联关系。
    /// </summary>
    /// <remarks>
    /// 这不是事件总线，不是消息系统。它是一个运行时的关系数据库。
    /// <para>典型用例：Enemy ↔ HealthBar、Player ↔ Inventory、Unit ↔ AIBrain</para>
    /// </remarks>
    public class MappingManager : MonoBehaviour
    {
        #region 单例

        private static MappingManager instance;

        /// <summary>
        /// 全局唯一实例。首次访问时自动在场景中查找或创建。
        /// </summary>
        public static MappingManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<MappingManager>();

                    if (instance == null)
                    {
                        var go = new GameObject("[MappingManager]");
                        instance = go.AddComponent<MappingManager>();
                        DontDestroyOnLoad(go);
                    }
                }

                return instance;
            }
        }

        /// <summary>
        /// 在 Awake 中保证单例唯一性。
        /// </summary>
        protected virtual void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        #endregion

        #region 类型键定义

        /// <summary>
        /// 由两种 Type 组成的不可变键，用于索引特定泛型组合的映射表。
        /// </summary>
        /// <remarks>
        /// 重写了 Equals 和 GetHashCode，保证字典查找的正确性和性能。
        /// </remarks>
        private struct TypePair : IEquatable<TypePair>
        {
            /// <summary>映射中的第一种类型（如 Enemy）</summary>
            public readonly Type TypeA;

            /// <summary>映射中的第二种类型（如 HealthBar）</summary>
            public readonly Type TypeB;

            public TypePair(Type a, Type b)
            {
                TypeA = a;
                TypeB = b;
            }

            public bool Equals(TypePair other)
            {
                return TypeA == other.TypeA && TypeB == other.TypeB;
            }

            public override bool Equals(object obj)
            {
                return obj is TypePair other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((TypeA != null ? TypeA.GetHashCode() : 0) * 397)
                         ^ (TypeB != null ? TypeB.GetHashCode() : 0);
                }
            }
        }

        #endregion

        #region 字段

        /// <summary>
        /// 正向映射表。结构：[类型对] → { Key实例 → Value实例 }
        /// </summary>
        /// <remarks>例如：[Enemy, HealthBar] → { 哥布林A → 哥布林A的血条 }</remarks>
        private readonly Dictionary<TypePair, Dictionary<MonoBehaviour, MonoBehaviour>> forwardMap = new();

        /// <summary>
        /// 反向映射表。结构：[类型对] → { Value实例 → Key实例 }
        /// </summary>
        /// <remarks>与 forwardMap 互为逆映射，保证 O(1) 双向查询。</remarks>
        private readonly Dictionary<TypePair, Dictionary<MonoBehaviour, MonoBehaviour>> reverseMap = new();

        /// <summary>
        /// 记录每个已注册对象涉及的所有类型对，用于对象销毁时批量清理。
        /// </summary>
        /// <remarks>Key 是注册过的 MonoBehaviour，Value 是它参与的所有 TypePair 集合。</remarks>
        private readonly Dictionary<MonoBehaviour, HashSet<TypePair>> destroyCallbacks = new();

        #endregion

        #region 注册 / 注销

        /// <summary>
        /// 注册一个类型安全的双向映射。
        /// </summary>
        /// <typeparam name="T">映射的 Key 类型（如 Enemy）</typeparam>
        /// <typeparam name="U">映射的 Value 类型（如 HealthBar）</typeparam>
        /// <param name="key">Key 实例</param>
        /// <param name="value">Value 实例</param>
        /// <returns>注册成功返回 true；key/value 为 null、已存在映射或 value 已被占用时返回 false。</returns>
        public bool Register<T, U>(T key, U value)
            where T : MonoBehaviour
            where U : MonoBehaviour
        {
            if (key == null)
            {
                Debug.LogError($"[MappingManager] Register 失败：key 为 null。类型对 <{typeof(T).Name}, {typeof(U).Name}>");
                return false;
            }

            if (value == null)
            {
                Debug.LogError($"[MappingManager] Register 失败：value 为 null。类型对 <{typeof(T).Name}, {typeof(U).Name}>");
                return false;
            }

            var pair = new TypePair(typeof(T), typeof(U));

            // 获取或创建正向字典
            if (!forwardMap.TryGetValue(pair, out var forwardDict))
            {
                forwardDict = new Dictionary<MonoBehaviour, MonoBehaviour>();
                forwardMap[pair] = forwardDict;
            }

            // 获取或创建反向字典
            if (!reverseMap.TryGetValue(pair, out var reverseDict))
            {
                reverseDict = new Dictionary<MonoBehaviour, MonoBehaviour>();
                reverseMap[pair] = reverseDict;
            }

            // 禁止同一个 key 重复注册
            if (forwardDict.ContainsKey(key))
            {
                Debug.LogWarning($"[MappingManager] Register 跳过：key 已存在映射。key={key.name}, 类型对 <{typeof(T).Name}, {typeof(U).Name}>");
                return false;
            }

            // 禁止同一个 value 被多个 key 映射（保证一对一关系）
            if (reverseDict.ContainsKey(value))
            {
                Debug.LogWarning($"[MappingManager] Register 跳过：value 已被其他 key 映射。value={value.name}, 类型对 <{typeof(T).Name}, {typeof(U).Name}>");
                return false;
            }

            forwardDict[key] = value;
            reverseDict[value] = key;

            // 订阅双方的生命周期，以便对象销毁时自动清理映射
            SubscribeToDestroy(key, typeof(T), typeof(U));
            SubscribeToDestroy(value, typeof(T), typeof(U));

            return true;
        }

        /// <summary>
        /// 注销一个映射关系。只需提供 Key 实例即可找到并移除整个映射。
        /// </summary>
        /// <typeparam name="T">映射的 Key 类型</typeparam>
        /// <typeparam name="U">映射的 Value 类型</typeparam>
        /// <param name="key">要注销的 Key 实例</param>
        /// <returns>成功找到并移除返回 true；key 为 null 或不存在映射时返回 false。</returns>
        public bool Unregister<T, U>(T key)
            where T : MonoBehaviour
            where U : MonoBehaviour
        {
            if (key == null) return false;

            var pair = new TypePair(typeof(T), typeof(U));

            if (!forwardMap.TryGetValue(pair, out var forwardDict))
                return false;

            if (!forwardDict.TryGetValue(key, out var value))
                return false;

            forwardDict.Remove(key);

            if (reverseMap.TryGetValue(pair, out var reverseDict))
            {
                reverseDict.Remove(value);
            }

            return true;
        }

        /// <summary>
        /// 清理指定对象参与的所有映射关系，无论它作为 Key 还是 Value。
        /// </summary>
        /// <param name="obj">要清理的 MonoBehaviour 实例</param>
        /// <remarks>通常在对象销毁时自动调用，也可手动调用以强制解绑。</remarks>
        public void UnregisterAll(MonoBehaviour obj)
        {
            if (obj == null) return;

            foreach (var kvp in forwardMap)
            {
                var pair = kvp.Key;
                var forwardDict = kvp.Value;

                // 作为 Key：找到对应的 Value 并双向移除
                if (forwardDict.TryGetValue(obj, out var value))
                {
                    forwardDict.Remove(obj);
                    if (reverseMap.TryGetValue(pair, out var reverseDict))
                    {
                        reverseDict.Remove(value);
                    }
                }

                // 作为 Value：找到对应的 Key 并双向移除
                if (reverseMap.TryGetValue(pair, out var revDict))
                {
                    if (revDict.TryGetValue(obj, out var key))
                    {
                        revDict.Remove(obj);
                        forwardDict.Remove(key);
                    }
                }
            }
        }

        #endregion

        #region 查询

        /// <summary>
        /// 根据 Key 实例获取其映射的 Value 实例。
        /// </summary>
        /// <typeparam name="U">返回值类型（如 HealthBar）</typeparam>
        /// <typeparam name="T">Key 的类型（如 Enemy）</typeparam>
        /// <param name="key">Key 实例</param>
        /// <returns>映射的 Value 实例；如果 key 为 null 或不存在映射则返回 null。</returns>
        public U Get<U, T>(T key)
            where T : MonoBehaviour
            where U : MonoBehaviour
        {
            if (key == null) return null;

            var pair = new TypePair(typeof(T), typeof(U));

            if (forwardMap.TryGetValue(pair, out var dict)
                && dict.TryGetValue(key, out var value))
            {
                return value as U;
            }

            return null;
        }

        /// <summary>
        /// 尝试根据 Key 获取 Value。安全的查询方式，不会抛出异常。
        /// </summary>
        /// <typeparam name="U">返回值类型</typeparam>
        /// <typeparam name="T">Key 的类型</typeparam>
        /// <param name="key">Key 实例</param>
        /// <param name="value">输出参数，映射存在时赋值为对应的 Value 实例，否则为 null</param>
        /// <returns>映射存在且 value 非 null 时返回 true。</returns>
        public bool TryGet<U, T>(T key, out U value)
            where T : MonoBehaviour
            where U : MonoBehaviour
        {
            value = Get<U, T>(key);
            return value != null;
        }

        /// <summary>
        /// 根据 Value 实例反查其映射的 Key 实例。
        /// </summary>
        /// <typeparam name="T">要返回的 Key 类型（如 Enemy）</typeparam>
        /// <typeparam name="U">Value 的类型（如 HealthBar）</typeparam>
        /// <param name="value">Value 实例</param>
        /// <returns>映射的 Key 实例；如果 value 为 null 或不存在映射则返回 null。</returns>
        public T GetKey<T, U>(U value)
            where T : MonoBehaviour
            where U : MonoBehaviour
        {
            if (value == null) return null;

            var pair = new TypePair(typeof(T), typeof(U));

            if (reverseMap.TryGetValue(pair, out var dict)
                && dict.TryGetValue(value, out var key))
            {
                return key as T;
            }

            return null;
        }

        /// <summary>
        /// 获取指定类型对下的所有 Value 实例。
        /// </summary>
        /// <typeparam name="T">Key 的类型</typeparam>
        /// <typeparam name="U">Value 的类型</typeparam>
        /// <returns>所有 Value 实例的列表。无映射时返回空列表（不是 null）。</returns>
        public List<U> GetAllValues<T, U>()
            where T : MonoBehaviour
            where U : MonoBehaviour
        {
            var result = new List<U>();
            var pair = new TypePair(typeof(T), typeof(U));

            if (forwardMap.TryGetValue(pair, out var dict))
            {
                foreach (var value in dict.Values)
                {
                    if (value is U u)
                        result.Add(u);
                }
            }

            return result;
        }

        /// <summary>
        /// 检查指定 Key 实例是否已存在映射。
        /// </summary>
        /// <typeparam name="T">Key 的类型</typeparam>
        /// <typeparam name="U">Value 的类型</typeparam>
        /// <param name="key">要检查的 Key 实例</param>
        /// <returns>存在映射返回 true，否则返回 false。</returns>
        public bool Contains<T, U>(T key)
            where T : MonoBehaviour
            where U : MonoBehaviour
        {
            if (key == null) return false;

            var pair = new TypePair(typeof(T), typeof(U));
            return forwardMap.TryGetValue(pair, out var dict) && dict.ContainsKey(key);
        }

        /// <summary>
        /// 获取指定类型对下当前已注册的映射数量。
        /// </summary>
        /// <typeparam name="T">Key 的类型</typeparam>
        /// <typeparam name="U">Value 的类型</typeparam>
        /// <returns>映射总数。</returns>
        public int GetMappingCount<T, U>()
            where T : MonoBehaviour
            where U : MonoBehaviour
        {
            var pair = new TypePair(typeof(T), typeof(U));
            return forwardMap.TryGetValue(pair, out var dict) ? dict.Count : 0;
        }

        #endregion

        #region 自动清理

        /// <summary>
        /// 为指定对象注册销毁回调。当对象的 GameObject 被销毁时，自动清理其所有映射。
        /// </summary>
        /// <param name="obj">要监听的目标对象</param>
        /// <param name="typeA">关联的类型 A</param>
        /// <param name="typeB">关联的类型 B</param>
        private void SubscribeToDestroy(MonoBehaviour obj, Type typeA, Type typeB)
        {
            if (!destroyCallbacks.ContainsKey(obj))
            {
                destroyCallbacks[obj] = new HashSet<TypePair>();

                // 借助 DestroyTracker 组件监听 OnDestroy
                var tracker = obj.gameObject.GetComponent<DestroyTracker>();
                if (tracker == null)
                    tracker = obj.gameObject.AddComponent<DestroyTracker>();

                tracker.OnDestroyed += HandleObjectDestroyed;
            }

            destroyCallbacks[obj].Add(new TypePair(typeA, typeB));
        }

        /// <summary>
        /// 当被监听的对象销毁时，自动清理其所有映射关系。
        /// </summary>
        /// <param name="obj">被销毁的 MonoBehaviour</param>
        private void HandleObjectDestroyed(MonoBehaviour obj)
        {
            UnregisterAll(obj);
            destroyCallbacks.Remove(obj);
        }

        #endregion

        #region 调试 / 编辑器支持

        /// <summary>
        /// 获取当前所有已注册的类型对，供编辑器窗口或调试面板遍历使用。
        /// </summary>
        /// <returns>所有类型对的枚举。</returns>
        public IEnumerable<(Type, Type)> GetAllTypePairs()
        {
            foreach (var pair in forwardMap.Keys)
            {
                yield return (pair.TypeA, pair.TypeB);
            }
        }

        /// <summary>
        /// 清空所有映射数据和销毁回调记录。谨慎使用，会彻底重置管理器状态。
        /// </summary>
        public void Clear()
        {
            forwardMap.Clear();
            reverseMap.Clear();
            destroyCallbacks.Clear();
        }

        #endregion
    }

    /// <summary>
    /// 挂载在 GameObject 上的轻量级组件，用于在对象销毁时通知 MappingManager。
    /// </summary>
    /// <remarks>不包含任何业务逻辑，只负责转发 OnDestroy 事件。</remarks>
    internal class DestroyTracker : MonoBehaviour
    {
        /// <summary>
        /// 在所属 MonoBehaviour 的 GameObject 被销毁时触发。
        /// </summary>
        public event Action<MonoBehaviour> OnDestroyed;

        private void OnDestroy()
        {
            OnDestroyed?.Invoke(this);
        }
    }
}

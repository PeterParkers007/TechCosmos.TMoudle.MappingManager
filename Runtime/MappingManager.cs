using System;
using System.Collections.Generic;
using UnityEngine;

namespace TechCosmos.MappingManager.Runtime
{
    /// <summary>
    /// 类型安全的双向映射管理器。
    /// 用于在两种 MonoBehaviour 之间建立显式的、可查询的关联关系。
    /// </summary>
    public class MappingManager : MonoBehaviour
    {
        #region 单例

        private static MappingManager instance;

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

        private struct TypePair : IEquatable<TypePair>
        {
            public readonly Type TypeA;
            public readonly Type TypeB;

            public TypePair(Type a, Type b)
            {
                TypeA = a;
                TypeB = b;
            }

            public bool Equals(TypePair other) => TypeA == other.TypeA && TypeB == other.TypeB;
            public override bool Equals(object obj) => obj is TypePair other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(TypeA, TypeB);
        }

        #endregion

        #region 字段

        private readonly Dictionary<TypePair, Dictionary<MonoBehaviour, MonoBehaviour>> forwardMap = new();
        private readonly Dictionary<TypePair, Dictionary<MonoBehaviour, MonoBehaviour>> reverseMap = new();
        private readonly Dictionary<MonoBehaviour, HashSet<TypePair>> destroyCallbacks = new();

        #endregion

        #region 注册 / 注销

        public bool Register<T, U>(T key, U value) where T : MonoBehaviour where U : MonoBehaviour
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

            if (!forwardMap.TryGetValue(pair, out var forwardDict))
            {
                forwardDict = new Dictionary<MonoBehaviour, MonoBehaviour>();
                forwardMap[pair] = forwardDict;
            }
            if (!reverseMap.TryGetValue(pair, out var reverseDict))
            {
                reverseDict = new Dictionary<MonoBehaviour, MonoBehaviour>();
                reverseMap[pair] = reverseDict;
            }

            if (forwardDict.ContainsKey(key))
            {
                Debug.LogWarning($"[MappingManager] Register 跳过：key 已存在映射。key={key.name}");
                return false;
            }
            if (reverseDict.ContainsKey(value))
            {
                Debug.LogWarning($"[MappingManager] Register 跳过：value 已被其他 key 映射。value={value.name}");
                return false;
            }

            forwardDict[key] = value;
            reverseDict[value] = key;
            SubscribeToDestroy(key, typeof(T), typeof(U));
            SubscribeToDestroy(value, typeof(T), typeof(U));
            return true;
        }

        public bool Unregister<T, U>(T key) where T : MonoBehaviour where U : MonoBehaviour
        {
            if (key == null) return false;
            var pair = new TypePair(typeof(T), typeof(U));
            if (!forwardMap.TryGetValue(pair, out var forwardDict)) return false;
            if (!forwardDict.TryGetValue(key, out var value)) return false;

            forwardDict.Remove(key);
            if (reverseMap.TryGetValue(pair, out var reverseDict))
                reverseDict.Remove(value);
            return true;
        }

        public void UnregisterAll(MonoBehaviour obj)
        {
            if (obj == null) return;
            foreach (var kvp in forwardMap)
            {
                var pair = kvp.Key;
                var forwardDict = kvp.Value;
                if (forwardDict.TryGetValue(obj, out var value))
                {
                    forwardDict.Remove(obj);
                    if (reverseMap.TryGetValue(pair, out var reverseDict))
                        reverseDict.Remove(value);
                }
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

        #region 查询（修改后：符合直觉的顺序）

        /// <summary>
        /// 根据 Key 获取 Value。Get&lt;T, U&gt;：传入 T，返回 U。
        /// </summary>
        public U Get<T, U>(T key) where T : MonoBehaviour where U : MonoBehaviour
        {
            if (key == null) return null;
            var pair = new TypePair(typeof(T), typeof(U));
            if (forwardMap.TryGetValue(pair, out var dict) && dict.TryGetValue(key, out var value))
                return value as U;
            return null;
        }

        /// <summary>
        /// 安全获取。TryGet&lt;T, U&gt;：传入 T，返回 U。
        /// </summary>
        public bool TryGet<T, U>(T key, out U value) where T : MonoBehaviour where U : MonoBehaviour
        {
            value = Get<T, U>(key);
            return value != null;
        }

        /// <summary>
        /// 根据 Value 反查 Key。GetKey&lt;T, U&gt;：传入 T（Value），返回 U（Key）。
        /// </summary>
        public U GetKey<T, U>(T value) where T : MonoBehaviour where U : MonoBehaviour
        {
            if (value == null) return null;
            var pair = new TypePair(typeof(U), typeof(T));
            if (reverseMap.TryGetValue(pair, out var dict) && dict.TryGetValue(value, out var key))
                return key as U;
            return null;
        }

        public List<U> GetAllValues<T, U>() where T : MonoBehaviour where U : MonoBehaviour
        {
            var result = new List<U>();
            var pair = new TypePair(typeof(T), typeof(U));
            if (forwardMap.TryGetValue(pair, out var dict))
                foreach (var value in dict.Values)
                    if (value is U u) result.Add(u);
            return result;
        }

        public bool Contains<T, U>(T key) where T : MonoBehaviour where U : MonoBehaviour
        {
            if (key == null) return false;
            var pair = new TypePair(typeof(T), typeof(U));
            return forwardMap.TryGetValue(pair, out var dict) && dict.ContainsKey(key);
        }

        public int GetMappingCount<T, U>() where T : MonoBehaviour where U : MonoBehaviour
        {
            var pair = new TypePair(typeof(T), typeof(U));
            return forwardMap.TryGetValue(pair, out var dict) ? dict.Count : 0;
        }

        #endregion

        #region 自动清理

        private void SubscribeToDestroy(MonoBehaviour obj, Type typeA, Type typeB)
        {
            if (!destroyCallbacks.ContainsKey(obj))
            {
                destroyCallbacks[obj] = new HashSet<TypePair>();
                var tracker = obj.gameObject.GetComponent<DestroyTracker>();
                if (tracker == null) tracker = obj.gameObject.AddComponent<DestroyTracker>();
                tracker.OnDestroyed += HandleObjectDestroyed;
            }
            destroyCallbacks[obj].Add(new TypePair(typeA, typeB));
        }

        private void HandleObjectDestroyed(MonoBehaviour obj)
        {
            UnregisterAll(obj);
            destroyCallbacks.Remove(obj);
        }

        #endregion

        #region 调试

        public IEnumerable<(Type, Type)> GetAllTypePairs()
        {
            foreach (var pair in forwardMap.Keys)
                yield return (pair.TypeA, pair.TypeB);
        }

        public void Clear()
        {
            forwardMap.Clear();
            reverseMap.Clear();
            destroyCallbacks.Clear();
        }

        #endregion
    }

    internal class DestroyTracker : MonoBehaviour
    {
        public event Action<MonoBehaviour> OnDestroyed;
        private void OnDestroy() => OnDestroyed?.Invoke(this);
    }
}
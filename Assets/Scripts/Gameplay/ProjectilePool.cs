using System.Collections.Generic;
using StarVortex.Core;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>勇者弹丸池注册表：builder 创建预制体后按名字注册，战斗代码按键取用。</summary>
    public static class ProjectilePool
    {
        public const string Fireball = "proj_fireball";
        public const string SwordWave = "proj_swordwave";
        public const string Arrow = "proj_arrow";

        private static readonly Dictionary<string, GameObject> _prefabs = new();

        public static void Register(string key, GameObject prefab) => _prefabs[key] = prefab;

        public static GameObject Get(string key) =>
            _prefabs.TryGetValue(key, out var p) ? p : null;

        public static GameObject Spawn(string key, Vector3 pos, Quaternion rot)
        {
            var prefab = Get(key);
            if (prefab == null || PoolService.Instance == null) return null;
            return PoolService.Instance.Spawn(prefab, pos, rot);
        }

        /// <summary>场景卸载时清注册表（防跨场景残留旧预制体引用）。</summary>
        public static void Clear() => _prefabs.Clear();
    }
}

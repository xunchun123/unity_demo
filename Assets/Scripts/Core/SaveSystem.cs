using System;
using System.IO;
using UnityEngine;

namespace StarVortex.Core
{
    /// <summary>最高纪录（Json 存档，纯数据结构）。</summary>
    [Serializable]
    public class BestRecord
    {
        public float survivalSeconds;
        public int kills;
        public int level;
        public string date;
    }

    /// <summary>
    /// 本地存档：JsonUtility 写入 persistentDataPath。
    /// 面试点：为什么用 JsonUtility 而不是 Newtonsoft——零依赖、足够覆盖扁平数据结构；
    /// 数据变复杂时再换 Newtonsoft（可序列化接口/字典）或 SQLite。
    /// </summary>
    public static class SaveSystem
    {
        private static string FilePath =>
            Path.Combine(Application.persistentDataPath, "best_record.json");

        public static BestRecord LoadBest()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                return JsonUtility.FromJson<BestRecord>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] 读取存档失败：{e.Message}");
                return null;
            }
        }

        /// <summary>只有刷新纪录才写盘（存活时间取旧的更长者）。</summary>
        public static bool TryUpdateBestRecord(BestRecord record)
        {
            var best = LoadBest();
            if (best != null && best.survivalSeconds >= record.survivalSeconds) return false;

            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(record, prettyPrint: true));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] 写入存档失败：{e.Message}");
                return false;
            }
        }
    }
}

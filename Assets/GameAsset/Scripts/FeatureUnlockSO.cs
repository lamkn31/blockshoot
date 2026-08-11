using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class FeatureUnlockEntry
{
    [Tooltip("Bật/tắt sử dụng feature này. Nếu false thì sẽ bị bỏ qua hoàn toàn.")]
    public bool isEnabled = true;

    [Tooltip("ID duy nhất của tính năng (vd: feature_1, booster_swap, ...)")]
    public string id;

    [Tooltip("Tên hiển thị trên UI")]
    public string title;

    [TextArea(2, 4)]
    [Tooltip("Mô tả ngắn về tính năng (optional)")]
    public string description;

    [Tooltip("Ảnh icon của tính năng")]
    public Sprite icon;
    [Tooltip("Ảnh Title Feature")]
    public Sprite titleImage;

    [Tooltip("Level mà tính năng được mở khóa hoàn toàn (100% -> hiện Win_ShowMeta)")]
    public int unlockLevel;
}

[CreateAssetMenu(fileName = "FeatureUnlockConfig", menuName = "Bus Game/Feature Unlock Config")]
public class FeatureUnlockSO : ScriptableObject
{
    public List<FeatureUnlockEntry> features = new List<FeatureUnlockEntry>();
    public FeatureUnlockEntry GetFeatureForLevel(int level)
    {
        if (features == null || level <= 0) return null;
        FeatureUnlockEntry candidate = null;
        int candidateUnlock = int.MaxValue;
        for (int i = 0; i < features.Count; i++)
        {
            var f = features[i];
            if (f == null) continue;
            if (f.unlockLevel >= level && f.unlockLevel < candidateUnlock)
            {
                candidate = f;
                candidateUnlock = f.unlockLevel;
            }
        }
        if (candidate != null && !candidate.isEnabled) return null;
        return candidate;
    }
    public int GetStartLevelOf(FeatureUnlockEntry entry)
    {
        if (entry == null || features == null) return 1;
        int prevUnlock = 0;
        for (int i = 0; i < features.Count; i++)
        {
            var f = features[i];
            if (f == null) continue;
            if (f.unlockLevel < entry.unlockLevel && f.unlockLevel > prevUnlock)
            {
                prevUnlock = f.unlockLevel;
            }
        }
        return prevUnlock + 1;
    }

    public float GetProgressFor(FeatureUnlockEntry entry, int level)
    {
        if (entry == null) return 0f;
        int prevUnlock = GetStartLevelOf(entry) - 1;
        int totalRange = entry.unlockLevel - prevUnlock;
        if (totalRange <= 0) return level >= entry.unlockLevel ? 1f : 0f;
        int completed = level - prevUnlock;
        return Mathf.Clamp01((float)completed / totalRange);
    }

    public FeatureUnlockEntry GetPreviousFeatureOf(FeatureUnlockEntry entry)
    {
        if (entry == null || features == null) return null;
        FeatureUnlockEntry prev = null;
        int prevUnlock = 0;
        for (int i = 0; i < features.Count; i++)
        {
            var f = features[i];
            if (f == null) continue;
            if (f.unlockLevel < entry.unlockLevel && f.unlockLevel > prevUnlock)
            {
                prev = f;
                prevUnlock = f.unlockLevel;
            }
        }
        return prev;
    }

    public FeatureUnlockEntry GetFeatureById(string id)
    {
        if (string.IsNullOrEmpty(id) || features == null) return null;
        for (int i = 0; i < features.Count; i++)
        {
            var f = features[i];
            if (f != null && f.isEnabled && f.id == id) return f;
        }
        return null;
    }
}

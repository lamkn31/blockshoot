using UnityEngine;

public class FeatureUnlockManager : MonoBehaviour
{
    public enum DisplayState
    {
        None,        // Không có feature nào trong level này
        Progress,    // Đang tiến trình -> Win_ProgressMeta
        Unlocked     // Đúng unlockLevel - vừa mở khóa xong -> Win_ShowMeta
    }

    [Header("Config")]
    public FeatureUnlockSO config;

    [Tooltip("UserProgress chứa currentLevelIndex (0-based). Level dùng cho feature = currentLevelIndex + 1.")]
    public UserProgressSO userProgress;
    public FeatureUnlockEntry GetFeatureForLevel(int level)
    {
        return config != null ? config.GetFeatureForLevel(level) : null;
    }

    public FeatureUnlockEntry GetCurrentFeature()
    {
        return GetFeatureForLevel(GetCurrentLevel());
    }

    public DisplayState GetStateForLevel(int level)
    {
        var entry = GetFeatureForLevel(level);
        if (entry == null) return DisplayState.None;
        return level == entry.unlockLevel ? DisplayState.Unlocked : DisplayState.Progress;
    }

    public DisplayState GetCurrentState()
    {
        return GetStateForLevel(GetCurrentLevel());
    }

    public float GetProgressForLevel(int level)
    {
        if (config == null) return 0f;
        var entry = config.GetFeatureForLevel(level);
        return config.GetProgressFor(entry, level);
    }

    public float GetCurrentProgress()
    {
        return GetProgressForLevel(GetCurrentLevel());
    }

    public bool IsUnlocked(string featureId)
    {
        if (config == null) return false;
        var entry = config.GetFeatureById(featureId);
        if (entry == null) return false;
        return GetCurrentLevel() >= entry.unlockLevel;
    }

    private int GetCurrentLevel() => CurrentLevel;

    public int CurrentLevel => userProgress != null ? userProgress.currentLevelIndex + 1 : 0;
}

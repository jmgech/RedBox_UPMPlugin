using System;
using System.Collections.Generic;
using UnityEngine;

public static class CardScanStatsService
{
    private const string KeyPrefix = "RK.Redbox.ScanStats";
    private const string IndexSuffix = "__ids";

    private static readonly Dictionary<string, int> SessionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static HardwareSettings.ScanStatsMode _mode = HardwareSettings.ScanStatsMode.SessionOnly;
    private static string _contextId = "default";

    public static bool IsPersistent => _mode == HardwareSettings.ScanStatsMode.PersistentByContext;
    public static string ActiveContextId => _contextId;

    public static void Configure(HardwareSettings settings)
    {
        SessionCounts.Clear();

        if (settings == null)
        {
            _mode = HardwareSettings.ScanStatsMode.SessionOnly;
            _contextId = "default";
            return;
        }

        _mode = settings.scanStatsMode;
        _contextId = NormalizeContextId(settings.scanStatsContextId);

        if (IsPersistent && settings.resetPersistentScanStatsOnStart)
            ResetPersistentContext();
    }

    public static int RegisterScan(string cardId)
    {
        string key = NormalizeCardId(cardId);
        if (string.IsNullOrEmpty(key)) return 0;

        SessionCounts.TryGetValue(key, out int session);
        session++;
        SessionCounts[key] = session;

        if (!IsPersistent)
            return session;

        int persistent = GetPersistentCount(key) + 1;
        SetPersistentCount(key, persistent);
        return persistent;
    }

    public static int GetSessionCount(string cardId)
    {
        string key = NormalizeCardId(cardId);
        if (string.IsNullOrEmpty(key)) return 0;
        return SessionCounts.TryGetValue(key, out int value) ? value : 0;
    }

    public static int GetContextCount(string cardId)
    {
        string key = NormalizeCardId(cardId);
        if (string.IsNullOrEmpty(key)) return 0;
        return IsPersistent ? GetPersistentCount(key) : GetSessionCount(key);
    }

    public static void ResetSession()
    {
        SessionCounts.Clear();
    }

    public static void ResetPersistentContext()
    {
        string indexKey = BuildIndexKey();
        string[] ids = PlayerPrefs.GetString(indexKey, string.Empty)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (string id in ids)
            PlayerPrefs.DeleteKey(BuildPersistentKey(id));

        PlayerPrefs.DeleteKey(indexKey);
        PlayerPrefs.Save();
    }

    private static int GetPersistentCount(string cardId)
    {
        return PlayerPrefs.GetInt(BuildPersistentKey(cardId), 0);
    }

    private static void SetPersistentCount(string cardId, int value)
    {
        string normalized = NormalizeCardId(cardId);
        if (string.IsNullOrEmpty(normalized)) return;

        PlayerPrefs.SetInt(BuildPersistentKey(normalized), Mathf.Max(0, value));
        AddCardIdToIndex(normalized);
        PlayerPrefs.Save();
    }

    private static void AddCardIdToIndex(string cardId)
    {
        string indexKey = BuildIndexKey();
        string raw = PlayerPrefs.GetString(indexKey, string.Empty);

        List<string> ids = new List<string>(raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
        if (!ids.Contains(cardId))
        {
            ids.Add(cardId);
            PlayerPrefs.SetString(indexKey, string.Join(";", ids));
        }
    }

    private static string BuildPersistentKey(string cardId)
    {
        return $"{KeyPrefix}.{_contextId}.{cardId}";
    }

    private static string BuildIndexKey()
    {
        return $"{KeyPrefix}.{_contextId}.{IndexSuffix}";
    }

    private static string NormalizeCardId(string cardId)
    {
        return string.IsNullOrWhiteSpace(cardId) ? string.Empty : cardId.Trim().ToLowerInvariant();
    }

    private static string NormalizeContextId(string contextId)
    {
        if (string.IsNullOrWhiteSpace(contextId))
            return "default";

        return contextId.Trim().ToLowerInvariant().Replace(" ", "_");
    }
}

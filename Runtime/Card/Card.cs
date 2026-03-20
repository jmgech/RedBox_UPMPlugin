using System;
using UnityEngine;


public abstract class Card : ScriptableObject
{
    [Tooltip("Unique identifier used to match this card against NFC scans and API lookups.\n" +
             "Use the short ID token only \u2014 e.g. \"001\", \"T001\", \"DB\", \"P02\".\n" +
             "Do NOT include name or type here (those belong in cardName / cardType).")]
    public string cardId;

    [Tooltip("Display name shown in UI. For action cards, this describes the action (e.g. \"Back\", \"Explode\").")]
    public string cardName;

    [Tooltip("Card category. Should match the TYPE token written on the physical tag (e.g. STUDENT, DIRECTION, POWER, TOOL).")]
    public string cardType;
    public string description;
    public int hp;
    public int mp;
    public int at;

    [Header("── REDbox Taxonomy ───────────────────────────────────────────────")]
    [Tooltip("Taxonomy type mapping to the RBX1 't' field on the physical NFC tag. " +
             "Set to Unknown for legacy cards not yet migrated to RBX1 format.")]
    public RedboxCardType cardTaxonomyType;

    [Tooltip("Taxonomy subtype string (RBX1 's' field). " +
             "E.g. \"ally\", \"attack\", \"buff\", \"memory\". Empty for legacy cards.")]
    public string subtype;

    [Header("── Art ─────────────────────────────────────────────────────────────")]
    [Tooltip("Optional card artwork. Shown in the Card Database editor and the LastScanBadge widget.")]
    public Texture2D cardArt;

    [Header("── Runtime Scan Stats ─────────────────────────────────────────────")]
    [NonSerialized] private int _sessionScanCount;
    [NonSerialized] private int _contextScanCount;
    [NonSerialized] private string _lastScanUtcIso;
    [NonSerialized] private string _statsContextId = "session";
    [NonSerialized] private bool _statsPersistent;

    public int SessionScanCount => _sessionScanCount;
    public int ContextScanCount => _contextScanCount;
    public string LastScanUtcIso => _lastScanUtcIso;
    public string StatsContextId => _statsContextId;
    public bool StatsPersistent => _statsPersistent;

    public void RegisterScan()
    {
        RegisterScan(_sessionScanCount + 1, _sessionScanCount + 1, "session", false);
    }

    public void RegisterScan(int sessionScanCount, int contextScanCount, string contextId, bool statsPersistent)
    {
        _sessionScanCount = Mathf.Max(0, sessionScanCount);
        _contextScanCount = Mathf.Max(0, contextScanCount);
        _statsContextId = string.IsNullOrWhiteSpace(contextId) ? "default" : contextId;
        _statsPersistent = statsPersistent;
        _lastScanUtcIso = _contextScanCount > 0 ? DateTime.UtcNow.ToString("o") : null;
    }

    public void ResetSessionScanStats()
    {
        _sessionScanCount = 0;
        _contextScanCount = 0;
        _statsContextId = "session";
        _statsPersistent = false;
        _lastScanUtcIso = null;
    }

    public abstract void Activate();
}

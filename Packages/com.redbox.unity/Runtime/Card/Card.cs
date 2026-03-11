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

    [Header("── Art ─────────────────────────────────────────────")]
    [Tooltip("Optional card artwork. Shown in the Card Database editor and the LastScanBadge widget.")]
    public Texture2D cardArt;

    public abstract void Activate();
}

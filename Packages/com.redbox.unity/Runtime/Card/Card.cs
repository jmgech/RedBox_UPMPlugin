using UnityEngine;


public abstract class Card : ScriptableObject
{
    public string cardId;
    public string cardName;
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

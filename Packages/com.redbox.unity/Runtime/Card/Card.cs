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

    public abstract void Activate();
}

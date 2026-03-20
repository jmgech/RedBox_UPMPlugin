using UnityEngine;
using UnityEngine.Events;

public class EventManager : MonoBehaviour
{
    public static EventManager Instance;

    /// <summary>Fired on every card scan (TAP). bool = true when presented, false when removed (legacy).</summary>
    public UnityEvent<Card, bool> OnCardScanned  = new UnityEvent<Card, bool>();

    /// <summary>Fired when a card is placed on the reader (NFC ENTER / TAP).</summary>
    public UnityEvent<Card>       OnCardPresented = new UnityEvent<Card>();

    /// <summary>Fired when a card is removed from the reader (NFC EXIT).</summary>
    public UnityEvent<Card>       OnCardRemoved   = new UnityEvent<Card>();

    public UnityEvent<bool>       OnScannerMissing = new UnityEvent<bool>();

    /// <summary>Fired when the scanner reads a UID that has no matching Card asset.</summary>
    public UnityEvent<string>     OnUnknownCardScanned = new UnityEvent<string>();

    private ArduinoBridge arduinoBridge;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Ensure the EventManager persists across scenes
            
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void CardScanned(Card cardData, bool status)
    {
        OnCardScanned.Invoke(cardData, status);
    }

    public void CardPresented(Card cardData)
    {
        OnCardPresented.Invoke(cardData);
        OnCardScanned.Invoke(cardData, true);   // keep legacy listeners working
    }

    public void CardRemoved(Card cardData)
    {
        OnCardRemoved.Invoke(cardData);
        OnCardScanned.Invoke(cardData, false);  // keep legacy listeners working
    }

    public void ScannerMissing(bool status)
    {
        OnScannerMissing.Invoke(status);
    }

    public void UnknownCardScanned(string rawUid)
    {
        OnUnknownCardScanned.Invoke(rawUid);
    }
}

using UnityEngine;
using UnityEngine.Events;

public class EventManager : MonoBehaviour
{
    public static EventManager Instance;

    public UnityEvent<Card, bool> OnCardScanned = new UnityEvent<Card, bool>();
    public UnityEvent<bool> OnScannerMissing = new UnityEvent<bool>();

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

    public void ScannerMissing(bool status)
    {
        OnScannerMissing.Invoke(status);
    }
}

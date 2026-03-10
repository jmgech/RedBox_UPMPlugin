using UnityEngine;

/// <summary>
/// Minimal feedback sink for runtime logs/status.
/// Keep this lightweight to maximize package portability.
/// </summary>
public class UIDisplayManager : MonoBehaviour
{
    public static UIDisplayManager instance;

    private void Awake()
    {
        if (instance == null) instance = this;
        else Destroy(gameObject);
    }

    public void ShowCard(Card card)
    {
        if (card == null) return;
        Debug.Log($"[UIDisplayManager] Card: {card.cardName} ({card.cardId})");
    }

    public void ShowStatus(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            Debug.Log($"[UIDisplayManager] {message}");
    }

    public void ShowTemporaryStatus(string message, float duration)
    {
        ShowStatus(message);
    }

    public void ClearAll()
    {
    }
}

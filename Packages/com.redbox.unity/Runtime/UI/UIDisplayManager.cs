using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Gestionnaire d'interface utilisateur REDbox.
///
/// RESPONSABILITÉS :
///   - Afficher les informations de la dernière carte scannée
///   - Afficher les messages de statut de la connexion Arduino
///   - Écouter les événements EventManager (OnCardScanned, OnScannerMissing)
///   - Masquer automatiquement les infos après une durée configurable (GameConfig)
///
/// SETUP SCÈNE :
///   1. Ajouter ce composant sur un GameObject UI
///   2. Assigner les champs TMP_Text depuis l'Inspector
///   3. (optionnel) Assigner GameConfig pour le délai d'affichage automatique
/// </summary>
public class UIDisplayManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    public static UIDisplayManager instance;

    // ─── Champs UI ────────────────────────────────────────────────────────────
    [Header("Textes d'information de carte")]
    [Tooltip("Nom de la carte scannée")]
    public TMP_Text nameText;

    [Tooltip("Description / lore de la carte")]
    public TMP_Text descriptionText;

    [Tooltip("Identifiant brut NFC de la carte")]
    public TMP_Text cardIdText;

    [Header("Texte de statut système")]
    [Tooltip("Messages de statut : connexion, erreurs, inconnus…")]
    public TMP_Text statusText;

    [Header("Configuration")]
    [Tooltip("(Optionnel) Référence au GameConfig pour le délai d'auto-masquage.")]
    public GameConfig gameConfig;

    [Tooltip("Durée d'affichage des infos carte (secondes). 0 = permanent. " +
             "Écrasé par GameConfig si assigné.")]
    [Range(0f, 30f)]
    public float cardDisplayDuration = 5f;

    // ─── Privé ────────────────────────────────────────────────────────────────
    private Coroutine _clearCoroutine;
    private Coroutine _statusClearCoroutine;

    // ═════════════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (instance == null)
            instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        // Récupère la durée depuis GameConfig si disponible
        if (gameConfig != null)
            cardDisplayDuration = gameConfig.cardDisplayDuration;

        ClearAll();

        // Abonnement aux événements système
        if (EventManager.Instance != null)
        {
            EventManager.Instance.OnCardScanned.AddListener(OnCardScanned);
            EventManager.Instance.OnScannerMissing.AddListener(OnScannerMissing);
        }
        else
        {
            Debug.LogWarning("[UIDisplayManager] EventManager introuvable en scène.");
        }
    }

    private void OnDestroy()
    {
        if (EventManager.Instance != null)
        {
            EventManager.Instance.OnCardScanned.RemoveListener(OnCardScanned);
            EventManager.Instance.OnScannerMissing.RemoveListener(OnScannerMissing);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ÉCOUTE DES ÉVÉNEMENTS
    // ═════════════════════════════════════════════════════════════════════════

    private void OnCardScanned(Card card, bool isScanned)
    {
        if (isScanned) ShowCard(card);
    }

    private void OnScannerMissing(bool isMissing)
    {
        if (isMissing)
            ShowStatus("Scanner REDbox indisponible - verification en cours...");
        else
            ShowTemporaryStatus("Scanner REDbox connecte.", 1.5f);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // API PUBLIQUE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Affiche les informations d'une carte dans l'UI.
    /// Appelé automatiquement lors d'un scan, ou manuellement.
    /// </summary>
    public void ShowCard(Card card)
    {
        if (card == null) return;

        SetText(nameText,        card.cardName);
        SetText(descriptionText, card.description);
        SetText(cardIdText,      $"ID : {card.cardId}");
        SetText(statusText,      $"OK {card.cardType}");

        // Lance le timer d'auto-masquage si configuré
        if (cardDisplayDuration > 0f)
        {
            if (_clearCoroutine != null) StopCoroutine(_clearCoroutine);
            _clearCoroutine = StartCoroutine(ClearAfterDelay(cardDisplayDuration));
        }
    }

    /// <summary>Affiche un message de statut système (connexion, erreur, etc.).</summary>
    public void ShowStatus(string message)
    {
        if (_statusClearCoroutine != null)
        {
            StopCoroutine(_statusClearCoroutine);
            _statusClearCoroutine = null;
        }
        SetText(statusText, message);
    }

    /// <summary>Affiche un statut temporaire puis efface uniquement la ligne de statut.</summary>
    public void ShowTemporaryStatus(string message, float duration)
    {
        ShowStatus(message);
        if (duration <= 0f) return;

        if (_statusClearCoroutine != null) StopCoroutine(_statusClearCoroutine);
        _statusClearCoroutine = StartCoroutine(ClearStatusAfterDelay(duration));
    }

    /// <summary>Efface tous les champs de l'UI.</summary>
    public void ClearAll()
    {
        SetText(nameText,        string.Empty);
        SetText(descriptionText, string.Empty);
        SetText(cardIdText,      string.Empty);
        SetText(statusText,      string.Empty);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UTILITAIRES
    // ═════════════════════════════════════════════════════════════════════════

    private void SetText(TMP_Text field, string value)
    {
        if (field != null) field.text = value;
    }

    private IEnumerator ClearAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        SetText(nameText,        string.Empty);
        SetText(descriptionText, string.Empty);
        SetText(cardIdText,      string.Empty);
        // On garde le statusText visible plus longtemps
    }

    private IEnumerator ClearStatusAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SetText(statusText, string.Empty);
        _statusClearCoroutine = null;
    }
}

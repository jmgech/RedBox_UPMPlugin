using UnityEngine;

/// <summary>
/// Configuration globale du jeu REDbox.
///
/// COMMENT CRÉER : clic droit dans le Project → Create → RK/Settings → Game Config
/// Centralise tous les magic numbers pour un réglage facile sans toucher le code.
/// </summary>
[CreateAssetMenu(fileName = "GameConfig", menuName = "RK/Settings/Game Config")]
public class GameConfig : ScriptableObject
{
    [Header("── Joueur FPS ────────────────────────────────────")]
    [Tooltip("Vitesse de marche (m/s)")]
    [Range(1f, 20f)]
    public float walkSpeed = 5f;

    [Tooltip("Vitesse de sprint (m/s)")]
    [Range(1f, 30f)]
    public float sprintSpeed = 10f;

    [Tooltip("Hauteur de saut")]
    [Range(0.5f, 10f)]
    public float jumpHeight = 2f;

    [Tooltip("Sensibilité souris")]
    [Range(10f, 500f)]
    public float mouseSensitivity = 100f;

    [Tooltip("Distance d'approche des limites avant ralentissement (m)")]
    [Range(0.5f, 10f)]
    public float boundaryNearDistance = 2f;

    [Header("── Rover ───────────────────────────────────────────")]
    [Tooltip("Vitesse de déplacement du rover (m/s)")]
    [Range(0.5f, 20f)]
    public float roverMoveSpeed = 5f;

    [Tooltip("Vitesse de rotation du rover (°/sec)")]
    [Range(10f, 360f)]
    public float roverTurnSpeed = 90f;

    [Tooltip("Distance d'un pas du rover (m)")]
    [Range(0.1f, 10f)]
    public float roverStepLength = 1f;

    [Header("── UI / Feedback ────────────────────────────────────")]
    [Tooltip("Durée d'affichage des infos de carte après un scan (secondes). 0 = permanent.")]
    [Range(0f, 30f)]
    public float cardDisplayDuration = 5f;

    [Tooltip("Couleur de la connexion active dans le DebugOverlay")]
    public Color connectedColor = new Color(0.2f, 0.9f, 0.2f);

    [Tooltip("Couleur de déconnexion dans le DebugOverlay")]
    public Color disconnectedColor = new Color(0.9f, 0.2f, 0.2f);

    [Tooltip("Couleur de reconnexion en cours dans le DebugOverlay")]
    public Color reconnectingColor = new Color(0.9f, 0.7f, 0.1f);

    [Header("── Debug ────────────────────────────────────────────")]
    [Tooltip("Touche pour afficher/masquer le DebugOverlay en jeu")]
    public KeyCode debugOverlayKey = KeyCode.F1;

    [Tooltip("Afficher le DebugOverlay au démarrage")]
    public bool showDebugOverlayOnStart = false;
}

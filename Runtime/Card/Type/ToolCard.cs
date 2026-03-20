using UnityEngine;

/// <summary>
/// Carte de type Outil.
///
/// COMPORTEMENT au scan :
///   - Déclenche une action sur un GameObject ciblé par tag
///   - Lance un VFX et un son optionnels
///   - Peut spawner un objet "outil" dans la scène
///
/// SETUP :
///   Créer via clic droit → Create → RK/Tool
/// </summary>
[CreateAssetMenu(fileName = "NewToolCard", menuName = "RK/Tool", order = 3)]
public class ToolCard : Card
{
    [Header("── Effet en Scène ─────────────────────────────────")]
    [Tooltip("Prefab à instancier lors de l'activation (outil physique, projectile…).")]
    public GameObject toolPrefab;

    [Tooltip("Décalage de spawn par rapport au joueur.")]
    public Vector3 spawnOffset = new Vector3(0f, 0.5f, 2f);

    [Tooltip("Tag de la cible sur laquelle l'outil agit (ex: 'Enemy', 'Interactable').")]
    public string targetTag = string.Empty;

    [Header("── Feedback ────────────────────────────────────────")]
    [Tooltip("VFX joué à l'activation.")]
    public GameObject activationVFX;

    [Tooltip("Son joué à l'activation.")]
    public AudioClip activationSound;

    // ═════════════════════════════════════════════════════════════════════════
    // ACTIVATION
    // ═════════════════════════════════════════════════════════════════════════

    public override void Activate()
    {
        Debug.Log($"[ToolCard] Activation : {cardName}");

        Transform playerTransform = GameObject.FindWithTag("Player")?.transform;
        Vector3 spawnPos = playerTransform != null
            ? playerTransform.position + playerTransform.TransformDirection(spawnOffset)
            : spawnOffset;

        // ── Spawn de l'outil ──────────────────────────────────────────────
        if (toolPrefab != null)
        {
            Instantiate(toolPrefab, spawnPos, playerTransform != null
                ? playerTransform.rotation
                : Quaternion.identity);

            Debug.Log($"[ToolCard] Outil '{cardName}' spawné en {spawnPos}");
        }

        // ── VFX ──────────────────────────────────────────────────────────
        if (activationVFX != null)
        {
            GameObject vfx = Instantiate(activationVFX, spawnPos, Quaternion.identity);
            Destroy(vfx, 3f);
        }

        // ── Son ──────────────────────────────────────────────────────────
        if (activationSound != null)
        {
            AudioSource.PlayClipAtPoint(activationSound, spawnPos);
        }

        // ── Action sur cible taguée ───────────────────────────────────────
        if (!string.IsNullOrEmpty(targetTag))
        {
            GameObject target = GameObject.FindWithTag(targetTag);
            if (target != null)
            {
                Debug.Log($"[ToolCard] Interaction avec cible '{target.name}' (tag: {targetTag})");
                // Exemple : target.GetComponent<IInteractable>()?.Interact(this);
            }
        }
    }
}

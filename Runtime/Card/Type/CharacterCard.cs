using UnityEngine;

/// <summary>
/// Carte de type Personnage.
///
/// COMPORTEMENT au scan :
///   - Spawne un prefab de personnage devant le joueur (si assigné)
///   - Déclenche une animation ou un VFX d'invocation (si assigné)
///   - Loggue les stats de la carte (HP, MP, AT)
///
/// SETUP :
///   Créer via clic droit → Create → RK/Character
///   Assigner un prefab dans "characterPrefab" pour le spawn physique.
/// </summary>
[CreateAssetMenu(fileName = "NewCharacterCard", menuName = "RK/Character", order = 1)]
public class CharacterCard : Card
{
    [Header("── Spawn ────────────────────────────────────────")]
    [Tooltip("Prefab à instancier lors de l'activation. Laisser vide pour désactiver le spawn.")]
    public GameObject characterPrefab;

    [Tooltip("Décalage de spawn par rapport au joueur (espace local).")]
    public Vector3 spawnOffset = new Vector3(0f, 0f, 2f);

    [Tooltip("Le personnage spawné est-il détruit automatiquement après X secondes ? 0 = permanent.")]
    [Range(0f, 60f)]
    public float autoDestroyAfter = 0f;

    [Header("── Effets ───────────────────────────────────────")]
    [Tooltip("Particules d'invocation jouées au spawn.")]
    public GameObject summonVFX;

    [Tooltip("Son joué à l'invocation.")]
    public AudioClip summonSound;

    // ═════════════════════════════════════════════════════════════════════════
    // ACTIVATION
    // ═════════════════════════════════════════════════════════════════════════

    public override void Activate()
    {
        Debug.Log($"[CharacterCard] Activation : {cardName} | HP:{hp} MP:{mp} AT:{at}");

        Transform spawnOrigin = ResolveSpawnOrigin();
        Vector3   spawnPos    = spawnOrigin != null
            ? spawnOrigin.position + spawnOrigin.TransformDirection(spawnOffset)
            : spawnOffset;

        // ── Spawn du personnage ───────────────────────────────────────────
        if (characterPrefab != null)
        {
            GameObject spawned = Instantiate(characterPrefab, spawnPos, Quaternion.identity);

            if (autoDestroyAfter > 0f)
                Destroy(spawned, autoDestroyAfter);

            Debug.Log($"[CharacterCard] '{cardName}' spawné en {spawnPos}");
        }
        else
        {
            Debug.LogWarning($"[CharacterCard] Aucun prefab assigné pour '{cardName}'. " +
                              "Assigne un prefab dans l'Inspector de la carte.");
        }

        // ── VFX d'invocation ─────────────────────────────────────────────
        if (summonVFX != null)
        {
            GameObject vfx = Instantiate(summonVFX, spawnPos, Quaternion.identity);
            Destroy(vfx, 3f); // Nettoie le VFX après 3s
        }

        // ── Son ──────────────────────────────────────────────────────────
        if (summonSound != null)
        {
            AudioSource.PlayClipAtPoint(summonSound, spawnPos);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UTILITAIRES
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cherche le joueur en scène comme point d'origine du spawn.
    /// Retourne null si aucun GameObject tagué "Player" n'est trouvé.
    /// </summary>
    private static Transform ResolveSpawnOrigin()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("[CharacterCard] Aucun GameObject avec le tag 'Player' trouvé. " +
                             "Spawn en Vector3.zero + offset.");
        }
        return player?.transform;
    }
}

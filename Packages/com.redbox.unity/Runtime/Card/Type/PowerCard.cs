using System.Collections;
using UnityEngine;

/// <summary>
/// Carte de type Pouvoir.
///
/// COMPORTEMENT au scan :
///   - Applique un effet (Soin, Bouclier, Attaque, Buff) à une cible en scène
///   - Chaque effet est configurable via l'Inspector
///   - Effet temporaire avec durée configurable
///
/// SETUP :
///   Créer via clic droit → Create → RK/Power
///   Configurer le type d'effet et sa valeur dans l'Inspector.
/// </summary>
[CreateAssetMenu(fileName = "NewPowerCard", menuName = "RK/Power", order = 2)]
public class PowerCard : Card
{
    // ─── Types d'effets ───────────────────────────────────────────────────────
    public enum PowerType
    {
        Heal,       // Restaure des HP
        Shield,     // Protège temporairement
        Attack,     // Inflige des dégâts à une cible ennemie
        SpeedBoost, // Accélère le joueur
        Freeze,     // Fige les ennemis
        Reveal      // Révèle des objets cachés
    }

    [Header("── Type d'effet ────────────────────────────────────")]
    [Tooltip("Effet appliqué lorsque la carte est scannée.")]
    public PowerType powerType = PowerType.Heal;

    [Header("── Paramètres ──────────────────────────────────────")]
    [Tooltip("Valeur de l'effet (points de soin, dégâts, % de boost…)")]
    [Range(1, 999)]
    public int powerValue = 30;

    [Tooltip("Durée de l'effet en secondes. 0 = instantané.")]
    [Range(0f, 60f)]
    public float effectDuration = 5f;

    [Tooltip("Ciblage : 'Player' pour le joueur, 'Enemy' pour les ennemis, etc.")]
    public string targetTag = "Player";

    [Header("── Effets Visuels ───────────────────────────────────")]
    [Tooltip("VFX à jouer sur la cible lors de l'activation.")]
    public GameObject effectVFX;

    [Tooltip("Son joué lors de l'activation.")]
    public AudioClip activationSound;

    [Header("── Feedback Visuel ─────────────────────────────────")]
    [Tooltip("Couleur de flash d'écran à l'activation (alpha = intensité).")]
    public Color screenFlashColor = new Color(1f, 1f, 0f, 0.3f);

    // ═════════════════════════════════════════════════════════════════════════
    // ACTIVATION
    // ═════════════════════════════════════════════════════════════════════════

    public override void Activate()
    {
        Debug.Log($"[PowerCard] Activation : {cardName} | Type:{powerType} Valeur:{powerValue} Durée:{effectDuration}s");

        GameObject target = FindTarget();
        if (target == null)
        {
            Debug.LogWarning($"[PowerCard] Aucune cible avec le tag '{targetTag}' trouvée en scène.");
            return;
        }

        ApplyEffect(target);
        PlayFeedback(target.transform.position);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // APPLICATION DE L'EFFET
    // ═════════════════════════════════════════════════════════════════════════

    private void ApplyEffect(GameObject target)
    {
        switch (powerType)
        {
            case PowerType.Heal:
                ApplyHeal(target);
                break;

            case PowerType.Shield:
                ApplyShield(target);
                break;

            case PowerType.Attack:
                ApplyAttack(target);
                break;

            case PowerType.SpeedBoost:
                ApplySpeedBoost(target);
                break;

            case PowerType.Freeze:
                ApplyFreeze(target);
                break;

            case PowerType.Reveal:
                ApplyReveal();
                break;
        }
    }

    private void ApplyHeal(GameObject target)
    {
        // Interface générique : cherche un composant avec une méthode Heal
        // Adapter à ton système de santé si tu en as un
        Debug.Log($"[PowerCard] ❤ Soin de {powerValue} HP sur '{target.name}'");

        // Exemple d'intégration si tu as un HealthComponent :
        // var health = target.GetComponent<HealthComponent>();
        // if (health != null) health.Heal(powerValue);
    }

    private void ApplyShield(GameObject target)
    {
        Debug.Log($"[PowerCard] 🛡 Bouclier de {powerValue} pts pendant {effectDuration}s sur '{target.name}'");

        // Exemple avec un MonoBehaviour temporaire :
        // var shield = target.AddComponent<TemporaryShield>();
        // shield.Initialize(powerValue, effectDuration);
    }

    private void ApplyAttack(GameObject target)
    {
        Debug.Log($"[PowerCard] ⚔ Attaque de {powerValue} dégâts sur '{target.name}'");

        // Exemple :
        // var health = target.GetComponent<HealthComponent>();
        // if (health != null) health.TakeDamage(powerValue);
    }

    private void ApplySpeedBoost(GameObject target)
    {
        Debug.Log($"[PowerCard] ⚡ Speed Boost +{powerValue}% pendant {effectDuration}s sur '{target.name}'");

        // Exemple avec le FPSController :
        // var fps = target.GetComponent<FPSController>();
        // if (fps != null && effectDuration > 0f)
        //     fps.StartCoroutine(fps.ApplySpeedBoost(powerValue / 100f, effectDuration));
    }

    private void ApplyFreeze(GameObject target)
    {
        Debug.Log($"[PowerCard] ❄ Gel pendant {effectDuration}s sur '{target.name}'");

        // Exemple : désactive le contrôleur pendant la durée
        // if (effectDuration > 0f)
        // {
        //     var ctrl = target.GetComponent<CharacterController>();
        //     if (ctrl != null) ctrl.enabled = false;
        //     target.GetComponent<MonoBehaviour>().StartCoroutine(ReenableAfter(ctrl, effectDuration));
        // }
    }

    private void ApplyReveal()
    {
        Debug.Log($"[PowerCard] 👁 Révélation — activation des objets cachés en scène.");

        // Révèle tous les GameObjects avec le tag "Hidden"
        GameObject[] hidden = GameObject.FindGameObjectsWithTag("Hidden");
        foreach (var obj in hidden)
        {
            obj.SetActive(true);
            Debug.Log($"[PowerCard] Révélé : '{obj.name}'");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // FEEDBACK
    // ═════════════════════════════════════════════════════════════════════════

    private void PlayFeedback(Vector3 position)
    {
        if (effectVFX != null)
        {
            GameObject vfx = Object.Instantiate(effectVFX, position, Quaternion.identity);
            Object.Destroy(vfx, Mathf.Max(effectDuration, 2f));
        }

        if (activationSound != null)
        {
            AudioSource.PlayClipAtPoint(activationSound, position);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UTILITAIRES
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject FindTarget()
    {
        return GameObject.FindWithTag(targetTag);
    }
}

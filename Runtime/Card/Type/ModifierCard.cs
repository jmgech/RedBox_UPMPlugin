using UnityEngine;

/// <summary>
/// Modifier card — a stat or elemental augmentation applied to another card or the active context.
///
/// SETUP: Create via Create → REDbox/Modifier
/// Set cardTaxonomyType = Modifier and subtype to "buff", "debuff", or "elemental".
/// </summary>
[CreateAssetMenu(fileName = "NewModifierCard", menuName = "REDbox/Modifier", order = 12)]
public class ModifierCard : Card
{
    [Header("── Modifier ─────────────────────────────────────────")]
    [Tooltip("The stat being modified. E.g. \"strength\", \"speed\", \"focus\".")]
    public string targetStat;

    [Tooltip("Multiplier applied to the target stat. 1.0 = no change.")]
    [Range(0.1f, 5f)]
    public float statMultiplier = 1f;

    [Tooltip("Duration of the modifier effect in seconds. 0 = permanent until removed.")]
    [Range(0f, 120f)]
    public float durationSeconds = 10f;

    public override void Activate()
    {
        Debug.Log($"[ModifierCard] {cardName} | subtype={subtype} | stat={targetStat} x{statMultiplier} for {durationSeconds}s");
    }
}

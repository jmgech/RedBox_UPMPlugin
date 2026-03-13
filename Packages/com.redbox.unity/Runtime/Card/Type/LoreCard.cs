using UnityEngine;

/// <summary>
/// Lore card — narrative content: memory fragments, codex entries, or active quest hooks.
///
/// SETUP: Create via Create → REDbox/Lore
/// Set cardTaxonomyType = Lore and subtype to "memory", "codex", or "quest".
/// </summary>
[CreateAssetMenu(fileName = "NewLoreCard", menuName = "REDbox/Lore", order = 13)]
public class LoreCard : Card
{
    [Header("── Lore ─────────────────────────────────────────────")]
    [Tooltip("Content localisation key used to look up narrative text in the content system. " +
             "E.g. \"lore.greta_origin.full_text\".")]
    public string contentKey;

    [Tooltip("Short teaser line shown immediately when the card is scanned.")]
    [TextArea(2, 5)]
    public string teaserText;

    [Tooltip("If true, scanning this card will attempt to trigger the associated quest step.")]
    public bool triggerQuestStep;

    public override void Activate()
    {
        Debug.Log($"[LoreCard] {cardName} | subtype={subtype} | key={contentKey}");
    }
}

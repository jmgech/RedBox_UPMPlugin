using UnityEngine;

/// <summary>
/// Instruction card — a player-issued command (movement, attack, defense, effect, or summon).
///
/// SETUP: Create via Create → REDbox/Instruction
/// Set cardTaxonomyType = Instruction and subtype to one of the CardSubtypeConstants.Instruction values.
/// </summary>
[CreateAssetMenu(fileName = "NewInstructionCard", menuName = "REDbox/Instruction", order = 11)]
public class InstructionCard : Card
{
    [Header("── Instruction ──────────────────────────────────────")]
    [Tooltip("Unique instruction identifier, used by the game system to look up the concrete action. " +
             "Should match the last segment of the full dot-notation card id (e.g. \"fireball\").")]
    public string instructionId;

    [Tooltip("If true, this instruction can be held for a channelled / continuous effect.")]
    public bool isChannelled;

    [Tooltip("Base cooldown in seconds before this instruction can be used again.")]
    [Range(0f, 60f)]
    public float cooldownSeconds = 1f;

    public override void Activate()
    {
        Debug.Log($"[InstructionCard] {cardName} | id={instructionId} | subtype={subtype}");
    }
}

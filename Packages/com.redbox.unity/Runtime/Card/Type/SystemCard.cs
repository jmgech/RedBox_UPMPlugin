using UnityEngine;

/// <summary>
/// System card — operational card used for profile management, save-states, debug commands, or admin operations.
///
/// SETUP: Create via Create → REDbox/System
/// Set cardTaxonomyType = System and subtype to "profile", "save", "debug", or "admin".
/// </summary>
[CreateAssetMenu(fileName = "NewSystemCard", menuName = "REDbox/System", order = 16)]
public class SystemCard : Card
{
    [Header("── System ────────────────────────────────────────────")]
    [Tooltip("Command string dispatched to the game system when this card is scanned. " +
             "E.g. \"SAVE_CHECKPOINT\", \"LOAD_PROFILE\", \"TOGGLE_DEBUG_OVERLAY\".")]
    public string systemCommand;

    [Tooltip("If true, the command requires an explicit player confirmation before executing.")]
    public bool requiresConfirmation;

    [Tooltip("If true, this card is only functional in editor/debug builds.")]
    public bool debugOnly;

    public override void Activate()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugOnly)
        {
            Debug.Log($"[SystemCard] {cardName} | subtype={subtype} | cmd={systemCommand}");
            return;
        }
#else
        if (debugOnly) return;
#endif
        Debug.Log($"[SystemCard] {cardName} | subtype={subtype} | cmd={systemCommand}");
    }
}

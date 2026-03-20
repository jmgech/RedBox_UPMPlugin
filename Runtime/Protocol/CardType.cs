/// <summary>
/// Taxonomy card type — maps to the RBX1 "t" field on the physical NFC tag.
/// </summary>
public enum RedboxCardType
{
    /// <summary>Card was scanned but type could not be determined (legacy or unknown format).</summary>
    Unknown = 0,

    /// <summary>Actor: a named character — ally or enemy. Drives spawning and combat presence.</summary>
    Actor = 1,

    /// <summary>Instruction: a player-issued command (movement, attack, defense, effect, summon).</summary>
    Instruction = 2,

    /// <summary>Modifier: a stat or elemental augmentation applied to any other card.</summary>
    Modifier = 3,

    /// <summary>Lore: narrative content — memory fragments, codex entries, active quests.</summary>
    Lore = 4,

    /// <summary>Cosmetic: appearance override — skin, outfit, aura, or emote.</summary>
    Cosmetic = 5,

    /// <summary>World: environmental state card — location, weather, or time-of-day.</summary>
    World = 6,

    /// <summary>System: operational card — profile, save-state, debug, or admin command.</summary>
    System = 7,
}

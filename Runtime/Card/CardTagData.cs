using System;

/// <summary>
/// Structured data decoded from an NFC tag's NDEF payload.
///
/// Payload v1 (legacy, written on physical tag):  {id}:{name}:{type}
///   Examples:
///     "001:Greta:Student"     → Id="001"  Name="GRETA"    Type="STUDENT"
///     "T001:Lamp:Tool"        → Id="T001" Name="LAMP"     Type="TOOL"
///
/// Payload v1 (RBX1 JSON, on-tag NDEF MIME):
///   {"p":"RBX1","t":"instruction","s":"attack","id":"instruction.attack.fireball","v":1}
///   → Id="instruction.attack.fireball"  TaxonomyType=Instruction  Subtype="attack"
///
/// Produced by ArduinoBridge on every NFC ENTER event.
/// Carried by OnCardTagRead, OnUnknownCardScanned, OnRedboxEvent, and the type-handler registry.
///
/// ROUTING PATTERN:
///   - Use Id            for ScriptableObject lookup and API calls.
///   - Use Name          for instant action dispatch (legacy Direction, Power cards).
///   - Use Type          to route to the legacy handler via RegisterTypeHandler().
///   - Use TaxonomyType  to route to the RBX1 taxonomy handler.
///   - Use CardId        for the full dot-notation taxonomy id (RBX1 only).
///   - Use SlotId        to determine which physical slot the tag was read from.
/// </summary>
[Serializable]
public struct CardTagData
{
    /// <summary>
    /// Unique card identifier. Used as the ScriptableObject registry key and API lookup key.
    /// Normalised (uppercase, alphanumeric only). E.g. "001", "T001", "DB", "P02".
    /// For RBX1 cards this is the full dot-notation id (e.g. "instruction.attack.fireball").
    /// </summary>
    public string Id;

    /// <summary>
    /// Action or display name encoded on the tag (legacy format only).
    /// Empty for RBX1 cards — use the ScriptableObject or the CardId for display.
    /// </summary>
    public string Name;

    /// <summary>
    /// Legacy card category — the routing key for RegisterTypeHandler().
    /// E.g. "STUDENT", "TOOL", "DIRECTION", "POWER".
    /// Empty for RBX1 cards — use TaxonomyType instead.
    /// </summary>
    public string Type;

    /// <summary>
    /// Hardware NFC chip UID (from TAGUID= field). Not used for card matching.
    /// Useful for diagnostics and for uniquely identifying a physical chip.
    /// </summary>
    public string TagUid;

    // ── RBX1 taxonomy fields ────────────────────────────────────────────────

    /// <summary>
    /// Taxonomy card type parsed from the RBX1 CARDTYPE= field.
    /// <see cref="RedboxCardType.Unknown"/> for legacy cards.
    /// </summary>
    public RedboxCardType TaxonomyType;

    /// <summary>
    /// Taxonomy subtype string from the RBX1 SUBTYPE= field.
    /// E.g. "ally", "attack", "buff", "memory", "location".
    /// Empty for legacy cards.
    /// </summary>
    public string Subtype;

    /// <summary>
    /// Full dot-notation card id from the RBX1 CARDID= field.
    /// E.g. "instruction.attack.fireball".
    /// Empty for legacy cards (use Id instead).
    /// </summary>
    public string CardId;

    /// <summary>
    /// Physical slot the reader occupies (from the V1 SLOT= field).
    /// E.g. "center", "left", "right".
    /// </summary>
    public string SlotId;

    /// <summary>True when this struct contains at least a valid Id.</summary>
    public bool IsValid => !string.IsNullOrEmpty(Id);

    /// <summary>An empty, invalid CardTagData.</summary>
    public static readonly CardTagData Empty = new CardTagData();

    /// <summary>Returns "Id:Name:Type" — mirrors the legacy on-tag payload format.</summary>
    public override string ToString() =>
        $"{Id ?? "?"}:{Name ?? ""}:{Type ?? ""}";
}

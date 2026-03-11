using System;

/// <summary>
/// Structured data decoded from an NFC tag's NDEF payload.
///
/// Payload written on the physical tag:  {id}:{name}:{type}
///   Examples:
///     "001:Greta:Student"     → Id="001"  Name="GRETA"    Type="STUDENT"
///     "T001:Lamp:Tool"        → Id="T001" Name="LAMP"     Type="TOOL"
///     "DB:Back:Direction"     → Id="DB"   Name="BACK"     Type="DIRECTION"
///     "P02:Explode:Power"     → Id="P02"  Name="EXPLODE"  Type="POWER"
///
/// Produced by ArduinoBridge on every NFC ENTER event.
/// Carried by OnCardTagRead, OnUnknownCardScanned, and the type-handler registry.
///
/// ROUTING PATTERN:
///   - Use Id   for ScriptableObject lookup and API calls.
///   - Use Name for instant action dispatch (Direction, Power cards — no DB needed).
///   - Use Type to route to the handler for this card category via RegisterTypeHandler().
/// </summary>
[Serializable]
public struct CardTagData
{
    /// <summary>
    /// Unique card identifier. Used as the ScriptableObject registry key and API lookup key.
    /// Normalised (uppercase, alphanumeric only). E.g. "001", "T001", "DB", "P02".
    /// </summary>
    public string Id;

    /// <summary>
    /// Action or display name encoded on the tag.
    /// For character cards: display name fallback (e.g. "GRETA").
    /// For action cards:    the action to execute (e.g. "BACK", "EXPLODE").
    /// Empty on cache-hit ENTER frames — use ScriptableObject or API for rich display in that case.
    /// </summary>
    public string Name;

    /// <summary>
    /// Card category — the routing key for RegisterTypeHandler().
    /// E.g. "STUDENT", "TOOL", "DIRECTION", "POWER".
    /// Empty on cache-hit ENTER frames.
    /// </summary>
    public string Type;

    /// <summary>
    /// Hardware NFC chip UID (from TAGUID= field). Not used for card matching.
    /// Useful for diagnostics and for uniquely identifying a physical chip independent of its payload.
    /// </summary>
    public string TagUid;

    /// <summary>True when this struct contains at least a valid Id.</summary>
    public bool IsValid => !string.IsNullOrEmpty(Id);

    /// <summary>An empty, invalid CardTagData.</summary>
    public static readonly CardTagData Empty = new CardTagData();

    /// <summary>Returns "Id:Name:Type" — mirrors the on-tag payload format.</summary>
    public override string ToString() =>
        $"{Id ?? "?"}:{Name ?? ""}:{Type ?? ""}";
}

/// <summary>
/// Structured representation of a decoded REDbox card, produced by ArduinoBridge
/// after parsing an RBX1 NDEF payload.
///
/// This is a pure-data class (not a ScriptableObject). It carries protocol fields
/// extracted from a single NFC scan and is the payload of <see cref="RedboxEvent"/>.
///
/// Protocol source: RBX1 JSON on-tag format
///   {"p":"RBX1","t":"instruction","s":"attack","id":"instruction.attack.fireball","v":1}
///
/// V1 serial frame fields mapped here:
///   UID=        → <see cref="Uid"/> (also <see cref="CardId"/> for RBX1)
///   CARDTYPE=   → <see cref="CardType"/>
///   SUBTYPE=    → <see cref="Subtype"/>
///   CARDID=     → <see cref="CardId"/>
///   SLOT=       → carried by the parent <see cref="RedboxEvent"/>
///   TAGUID=     → <see cref="PhysicalTagUid"/>
/// </summary>
public sealed class RedboxCard
{
    /// <summary>
    /// Logical card identifier — the V1 UID field.
    /// For RBX1 cards this is the full dot-notation taxonomy id (e.g. "instruction.attack.fireball").
    /// For legacy cards this is the short token (e.g. "001").
    /// </summary>
    public string Uid { get; }

    /// <summary>Taxonomy card type (from the RBX1 "t" field).</summary>
    public RedboxCardType CardType { get; }

    /// <summary>
    /// Taxonomy subtype string (from the RBX1 "s" field).
    /// E.g. "ally", "attack", "buff", "memory", "location".
    /// Empty for legacy cards.
    /// </summary>
    public string Subtype { get; }

    /// <summary>
    /// Full dot-notation card id (from the RBX1 "id" field or the V1 CARDID= field).
    /// E.g. "instruction.attack.fireball". Same as <see cref="Uid"/> for RBX1 cards.
    /// Empty for legacy cards.
    /// </summary>
    public string CardId { get; }

    /// <summary>Hardware NFC chip UID (TAGUID= field). Useful for diagnostics.</summary>
    public string PhysicalTagUid { get; }

    /// <summary>RBX1 protocol version read from the tag ("v" field). 0 for legacy cards.</summary>
    public int PayloadVersion { get; }

    public RedboxCard(string uid, RedboxCardType cardType, string subtype,
                      string cardId, string physicalTagUid, int payloadVersion = 1)
    {
        Uid             = uid            ?? string.Empty;
        CardType        = cardType;
        Subtype         = subtype        ?? string.Empty;
        CardId          = cardId         ?? string.Empty;
        PhysicalTagUid  = physicalTagUid ?? string.Empty;
        PayloadVersion  = payloadVersion;
    }

    public override string ToString() =>
        $"RedboxCard(uid={Uid}, type={CardType}, subtype={Subtype}, id={CardId})";
}

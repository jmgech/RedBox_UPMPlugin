using System;

/// <summary>
/// A discrete REDbox interaction event, produced by <see cref="ArduinoBridge"/> and
/// fired via <see cref="ArduinoBridge.OnRedboxEvent"/>.
///
/// Each event represents a single physical action: a card being placed, held, or removed
/// from a specific reader slot.
///
/// For high-level gameplay reactions subscribe to the typed events on ArduinoBridge
/// (OnCardPresented, OnCardRemoved). Subscribe to OnRedboxEvent when you need access
/// to full protocol metadata (slot, sequence number, subtype, card id, etc.).
/// </summary>
public sealed class RedboxEvent
{
    /// <summary>
    /// Describes what happened in this event.
    /// </summary>
    public enum EventType
    {
        /// <summary>A card was placed on the reader (NFC ENTER).</summary>
        CardEnter,

        /// <summary>A card is still present on the reader (heartbeat / NFC PRESENT).</summary>
        CardPresent,

        /// <summary>A card was removed from the reader (NFC EXIT).</summary>
        CardExit,
    }

    /// <summary>What happened.</summary>
    public EventType Type { get; }

    /// <summary>
    /// Decoded card data. Populated on Enter and Present events.
    /// <c>null</c> on Exit events when the card data is no longer readable.
    /// </summary>
    public RedboxCard Card { get; }

    /// <summary>
    /// Logical reader identifier (e.g. "reader_1").
    /// Populated from the firmware source / reader registration.
    /// </summary>
    public string ReaderId { get; }

    /// <summary>
    /// Physical slot the reader occupies (e.g. "center", "left", "right").
    /// Populated from the V1 SLOT= field.
    /// </summary>
    public string SlotId { get; }

    /// <summary>
    /// Wall-clock time when the event was received on the Unity side (UTC milliseconds since epoch).
    /// </summary>
    public long TimestampMs { get; }

    /// <summary>
    /// Monotonically increasing per-session event counter. Useful for ordering
    /// events when multiple readers are active.
    /// </summary>
    public int Sequence { get; }

    public RedboxEvent(EventType type, RedboxCard card, string readerId, string slotId, int sequence)
    {
        Type        = type;
        Card        = card;
        ReaderId    = readerId ?? string.Empty;
        SlotId      = slotId   ?? string.Empty;
        TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Sequence    = sequence;
    }

    public override string ToString() =>
        $"RedboxEvent({Type}, slot={SlotId}, seq={Sequence}, card={Card})";
}

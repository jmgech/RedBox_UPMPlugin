/// <summary>
/// String constants for all taxonomy card subtypes used in the REDbox Protocol (RBX1).
///
/// These match the RBX1 JSON "s" field written on physical NFC tags and emitted in
/// V1 serial frames as SUBTYPE=.
///
/// Format: {taxonomy}.{subtype} maps to CardSubtypeConstants.{TaxonomyName}.{SubtypeName}.
/// </summary>
public static class CardSubtypeConstants
{
    // ── Actor ──────────────────────────────────────────────────────────────────
    public static class Actor
    {
        public const string Ally   = "ally";
        public const string Enemy  = "enemy";
    }

    // ── Instruction ───────────────────────────────────────────────────────────
    public static class Instruction
    {
        public const string Movement = "movement";
        public const string Attack   = "attack";
        public const string Defense  = "defense";
        public const string Effect   = "effect";
        public const string Summon   = "summon";
    }

    // ── Modifier ──────────────────────────────────────────────────────────────
    public static class Modifier
    {
        public const string Buff      = "buff";
        public const string Debuff    = "debuff";
        public const string Elemental = "elemental";
    }

    // ── Lore ──────────────────────────────────────────────────────────────────
    public static class Lore
    {
        public const string Memory = "memory";
        public const string Codex  = "codex";
        public const string Quest  = "quest";
    }

    // ── Cosmetic ──────────────────────────────────────────────────────────────
    public static class Cosmetic
    {
        public const string Skin   = "skin";
        public const string Outfit = "outfit";
        public const string Aura   = "aura";
        public const string Emote  = "emote";
    }

    // ── World ─────────────────────────────────────────────────────────────────
    public static class World
    {
        public const string Location  = "location";
        public const string Weather   = "weather";
        public const string TimeState = "time_state";
    }

    // ── System ────────────────────────────────────────────────────────────────
    public static class System
    {
        public const string Profile = "profile";
        public const string Save    = "save";
        public const string Debug   = "debug";
        public const string Admin   = "admin";
    }
}

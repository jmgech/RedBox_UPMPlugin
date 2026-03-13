#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility that batch-creates the REDbox Founders Set v0.1 (54 cards) as ScriptableObject assets.
///
/// USAGE: REDbox → Generate Founders Set
///
/// OUTPUT: Assets/Resources/REDbox/FoundersSet/{Type}/  (54 .asset files)
///
/// The generator is additive — it skips cards that already exist at the target path.
/// Run it again after clearing the output folder to regenerate from scratch.
/// </summary>
public static class FoundersSetGenerator
{
    private const string OutputRoot = "Assets/Resources/REDbox/FoundersSet";

    // ── Card definition ────────────────────────────────────────────────────────
    private readonly struct CardDef
    {
        public readonly string CardId;        // dot-notation id (e.g. "actor.ally.greta")
        public readonly string DisplayName;   // friendly name (may be empty)
        public readonly RedboxCardType Type;
        public readonly string Subtype;
        public readonly int Hp; public readonly int Mp; public readonly int At;

        public CardDef(string id, string name, RedboxCardType t, string sub,
                       int hp = 0, int mp = 0, int at = 0)
        {
            CardId = id; DisplayName = name; Type = t; Subtype = sub;
            Hp = hp; Mp = mp; At = at;
        }
    }

    private static readonly CardDef[] s_catalog = {
        // ── Actor / ally (6) ─────────────────────────────────────────────────
        new("actor.ally.greta",  "Greta",  RedboxCardType.Actor, "ally", hp:80,  mp:40,  at:30),
        new("actor.ally.max",    "Max",    RedboxCardType.Actor, "ally", hp:70,  mp:60,  at:25),
        new("actor.ally.jiffy",  "Jiffy",  RedboxCardType.Actor, "ally", hp:100, mp:20,  at:40),
        new("actor.ally.aeria",  "Aeria",  RedboxCardType.Actor, "ally", hp:60,  mp:80,  at:20),
        new("actor.ally.solvek", "Solvek", RedboxCardType.Actor, "ally", hp:75,  mp:50,  at:35),
        new("actor.ally.nerin",  "Nerin",  RedboxCardType.Actor, "ally", hp:65,  mp:70,  at:28),
        // ── Actor / enemy (6) ────────────────────────────────────────────────
        new("actor.enemy.zorch",         "Zorch",         RedboxCardType.Actor, "enemy", hp:90,  mp:10,  at:55),
        new("actor.enemy.shade",         "Shade",         RedboxCardType.Actor, "enemy", hp:50,  mp:30,  at:45),
        new("actor.enemy.grak",          "Grak",          RedboxCardType.Actor, "enemy", hp:120, mp:0,   at:60),
        new("actor.enemy.vexmor",        "Vexmor",        RedboxCardType.Actor, "enemy", hp:70,  mp:80,  at:35),
        new("actor.enemy.nullis",        "Nullis",        RedboxCardType.Actor, "enemy", hp:40,  mp:100, at:50),
        new("actor.enemy.the_void_lich", "The Void Lich", RedboxCardType.Actor, "enemy", hp:200, mp:150, at:80),
        // ── Instruction / movement (5) ───────────────────────────────────────
        new("instruction.movement.sprint",   "Sprint",   RedboxCardType.Instruction, "movement"),
        new("instruction.movement.sidestep", "Sidestep", RedboxCardType.Instruction, "movement"),
        new("instruction.movement.backstep", "Backstep", RedboxCardType.Instruction, "movement"),
        new("instruction.movement.dash",     "Dash",     RedboxCardType.Instruction, "movement"),
        new("instruction.movement.leap",     "Leap",     RedboxCardType.Instruction, "movement"),
        // ── Instruction / attack (4) ─────────────────────────────────────────
        new("instruction.attack.fireball",      "Fireball",      RedboxCardType.Instruction, "attack", at:40),
        new("instruction.attack.frost_bolt",    "Frost Bolt",    RedboxCardType.Instruction, "attack", at:30),
        new("instruction.attack.shadow_strike", "Shadow Strike", RedboxCardType.Instruction, "attack", at:50),
        new("instruction.attack.lightning_zap", "Lightning Zap", RedboxCardType.Instruction, "attack", at:35),
        // ── Instruction / defense (4) ────────────────────────────────────────
        new("instruction.defense.shield_bash", "Shield Bash", RedboxCardType.Instruction, "defense"),
        new("instruction.defense.parry",       "Parry",       RedboxCardType.Instruction, "defense"),
        new("instruction.defense.deflect",     "Deflect",     RedboxCardType.Instruction, "defense"),
        new("instruction.defense.barrier",     "Barrier",     RedboxCardType.Instruction, "defense"),
        // ── Instruction / effect (4) ─────────────────────────────────────────
        new("instruction.effect.heal",    "Heal",    RedboxCardType.Instruction, "effect", hp:30),
        new("instruction.effect.silence", "Silence", RedboxCardType.Instruction, "effect"),
        new("instruction.effect.blind",   "Blind",   RedboxCardType.Instruction, "effect"),
        new("instruction.effect.slow",    "Slow",    RedboxCardType.Instruction, "effect"),
        // ── Instruction / summon (3) ─────────────────────────────────────────
        new("instruction.summon.specter", "Specter", RedboxCardType.Instruction, "summon"),
        new("instruction.summon.golem",   "Golem",   RedboxCardType.Instruction, "summon"),
        new("instruction.summon.wisp",    "Wisp",    RedboxCardType.Instruction, "summon"),
        // ── Modifier / buff (3) ──────────────────────────────────────────────
        new("modifier.buff.strength", "Strength", RedboxCardType.Modifier, "buff"),
        new("modifier.buff.speed",    "Speed",    RedboxCardType.Modifier, "buff"),
        new("modifier.buff.focus",    "Focus",    RedboxCardType.Modifier, "buff"),
        // ── Modifier / debuff (3) ────────────────────────────────────────────
        new("modifier.debuff.weakness",  "Weakness",  RedboxCardType.Modifier, "debuff"),
        new("modifier.debuff.confusion", "Confusion", RedboxCardType.Modifier, "debuff"),
        new("modifier.debuff.despair",   "Despair",   RedboxCardType.Modifier, "debuff"),
        // ── Modifier / elemental (4) ─────────────────────────────────────────
        new("modifier.elemental.fire",      "Fire",      RedboxCardType.Modifier, "elemental"),
        new("modifier.elemental.ice",       "Ice",       RedboxCardType.Modifier, "elemental"),
        new("modifier.elemental.lightning", "Lightning", RedboxCardType.Modifier, "elemental"),
        new("modifier.elemental.shadow",    "Shadow",    RedboxCardType.Modifier, "elemental"),
        // ── Lore / memory (2) ────────────────────────────────────────────────
        new("lore.memory.greta_origin",      "Greta's Origin",      RedboxCardType.Lore, "memory"),
        new("lore.memory.redbox_chronicles", "REDbox Chronicles",   RedboxCardType.Lore, "memory"),
        // ── Lore / codex (2) ─────────────────────────────────────────────────
        new("lore.codex.world_rules",   "World Rules",   RedboxCardType.Lore, "codex"),
        new("lore.codex.faction_atlas", "Faction Atlas", RedboxCardType.Lore, "codex"),
        // ── Lore / quest (2) ─────────────────────────────────────────────────
        new("lore.quest.first_encounter", "First Encounter", RedboxCardType.Lore, "quest"),
        new("lore.quest.vault_breach",    "Vault Breach",    RedboxCardType.Lore, "quest"),
        // ── World / location (2) ─────────────────────────────────────────────
        new("world.location.cloudspire_academy", "Cloudspire Academy", RedboxCardType.World, "location"),
        new("world.location.nexus_vault",        "Nexus Vault",        RedboxCardType.World, "location"),
        // ── World / weather (2) ──────────────────────────────────────────────
        new("world.weather.storm", "Storm", RedboxCardType.World, "weather"),
        new("world.weather.fog",   "Fog",   RedboxCardType.World, "weather"),
        // ── World / time_state (2) ───────────────────────────────────────────
        new("world.time_state.dawn", "Dawn", RedboxCardType.World, "time_state"),
        new("world.time_state.dusk", "Dusk", RedboxCardType.World, "time_state"),
    };

    // ── Menu entry ─────────────────────────────────────────────────────────────
    [MenuItem("REDbox/Generate Founders Set", priority = 200)]
    public static void GenerateFoundersSet()
    {
        int created = 0;
        int skipped = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (CardDef def in s_catalog)
            {
                string assetPath = BuildAssetPath(def);

                if (AssetDatabase.LoadAssetAtPath<Card>(assetPath) != null)
                {
                    skipped++;
                    continue;
                }

                Card asset = CreateCardAsset(def);
                if (asset == null) continue;

                EnsureDirectory(Path.GetDirectoryName(assetPath));
                AssetDatabase.CreateAsset(asset, assetPath);
                created++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[FoundersSetGenerator] Done. Created: {created}  Skipped (already exist): {skipped}");
        EditorUtility.DisplayDialog(
            "Founders Set Generated",
            $"Created {created} card assets.\nSkipped {skipped} (already exist).\n\nOutput: {OutputRoot}/",
            "OK");
    }

    [MenuItem("REDbox/Generate Founders Set", validate = true)]
    private static bool ValidateGenerateFoundersSet() => !EditorApplication.isPlaying;

    // ── Helpers ────────────────────────────────────────────────────────────────
    private static string BuildAssetPath(CardDef def)
    {
        // Derive subfolder from the card type + last-segment of cardId
        // e.g. "actor.ally.greta" → "Actor/actor.ally.greta.asset"
        return $"{OutputRoot}/{def.Type}/{def.CardId}.asset";
    }

    private static Card CreateCardAsset(CardDef def)
    {
        Card asset = def.Type switch {
            RedboxCardType.Actor       => ScriptableObject.CreateInstance<ActorCard>(),
            RedboxCardType.Instruction => ScriptableObject.CreateInstance<InstructionCard>(),
            RedboxCardType.Modifier    => ScriptableObject.CreateInstance<ModifierCard>(),
            RedboxCardType.Lore        => ScriptableObject.CreateInstance<LoreCard>(),
            RedboxCardType.World       => ScriptableObject.CreateInstance<WorldCard>(),
            RedboxCardType.Cosmetic    => ScriptableObject.CreateInstance<CosmeticCard>(),
            RedboxCardType.System      => ScriptableObject.CreateInstance<SystemCard>(),
            _ => null,
        };

        if (asset == null)
        {
            Debug.LogWarning($"[FoundersSetGenerator] Unknown type {def.Type} for card {def.CardId} — skipped.");
            return null;
        }

        asset.cardId           = def.CardId;
        asset.cardName         = string.IsNullOrEmpty(def.DisplayName) ? LastSegment(def.CardId) : def.DisplayName;
        asset.cardType         = def.Type.ToString();
        asset.cardTaxonomyType = def.Type;
        asset.subtype          = def.Subtype;
        asset.hp               = def.Hp;
        asset.mp               = def.Mp;
        asset.at               = def.At;

        // Type-specific defaults
        if (asset is InstructionCard ic)
            ic.instructionId = LastSegment(def.CardId);

        if (asset is LoreCard lc)
            lc.contentKey = def.CardId + ".text";

        return asset;
    }

    private static string LastSegment(string dotPath)
    {
        int i = dotPath.LastIndexOf('.');
        return i >= 0 ? dotPath.Substring(i + 1) : dotPath;
    }

    private static void EnsureDirectory(string path)
    {
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path);
        EnsureDirectory(parent);
        string folder = Path.GetFileName(path);
        AssetDatabase.CreateFolder(parent, folder);
    }
}
#endif

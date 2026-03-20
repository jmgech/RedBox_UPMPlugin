using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "RK/Sample/Visual Novel/Story Data", fileName = "REDbox_VN_Story")]
public class REDboxVNStoryData : ScriptableObject
{
    [Header("Story")]
    public string storyTitle = "REDbox Story Demo";

    [Tooltip("Node id used when the story starts.")]
    public string startNodeId = "intro";

    [Tooltip("All nodes that make up the sample narrative graph.")]
    public VNNode[] nodes = Array.Empty<VNNode>();

    private Dictionary<string, VNNode> _lookup;

    public bool TryGetNode(string nodeId, out VNNode node)
    {
        if (_lookup == null)
            BuildLookup();

        return _lookup.TryGetValue(Normalize(nodeId), out node);
    }

    public void BuildLookup()
    {
        _lookup = new Dictionary<string, VNNode>(StringComparer.OrdinalIgnoreCase);
        if (nodes == null) return;

        foreach (var n in nodes)
        {
            if (n == null || string.IsNullOrWhiteSpace(n.id))
                continue;

            string key = Normalize(n.id);
            if (_lookup.ContainsKey(key))
                continue;

            _lookup.Add(key, n);
        }
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().ToLowerInvariant();
    }
}

[Serializable]
public class VNNode
{
    [Tooltip("Unique node id used for transitions.")]
    public string id;

    [Header("Presentation")]
    public string chapter = "Chapter";
    public string speaker = "Narrator";

    [TextArea(3, 8)]
    public string text;

    [TextArea(2, 5)]
    [Tooltip("Pedagogical hint shown to explain why this step exists.")]
    public string learningHint;

    [Tooltip("Optional logical background key for custom UI themes.")]
    public string backgroundKey;

    [Header("Flow")]
    public bool isEnding;
    public string nextNodeId;

    [Tooltip("If true, progression waits for an NFC card/tag match.")]
    public bool requiresCard;

    public VNCardRequirement requiredCard;
    public VNChoice[] choices = Array.Empty<VNChoice>();

    public bool HasChoices => choices != null && choices.Length > 0;
}

[Serializable]
public class VNChoice
{
    public string id;
    public string label;
    public string nextNodeId;

    [TextArea(1, 3)]
    [Tooltip("Optional short explanation shown next to this choice.")]
    public string learningHint;

    [Tooltip("When enabled, this choice appears only when a matching card is scanned.")]
    public bool requiresCard;

    public VNCardRequirement requiredCard;
}

[Serializable]
public class VNCardRequirement
{
    [Tooltip("Matches tag.CardId first, then tag.Id, then known Card.cardId.")]
    public string expectedId;

    [Tooltip("Legacy tag type (CardTagData.Type), e.g. LORE or POWER.")]
    public string expectedLegacyType;

    public RedboxCardType expectedTaxonomyType = RedboxCardType.Unknown;

    [Tooltip("RBX1 subtype, e.g. memory, location, ally, effect.")]
    public string expectedSubtype;

    [Tooltip("Allow cards that do not resolve to a ScriptableObject asset.")]
    public bool allowUnknownCard = true;

    public bool Matches(CardTagData tagData, Card knownCard)
    {
        if (!allowUnknownCard && knownCard == null)
            return false;

        if (expectedTaxonomyType != RedboxCardType.Unknown && tagData.TaxonomyType != expectedTaxonomyType)
            return false;

        if (!string.IsNullOrWhiteSpace(expectedLegacyType))
        {
            if (!string.Equals(expectedLegacyType.Trim(), tagData.Type?.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedSubtype))
        {
            if (!string.Equals(expectedSubtype.Trim(), tagData.Subtype?.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedId))
        {
            string expected = REDboxVNStoryData.Normalize(expectedId);
            string tagCardId = REDboxVNStoryData.Normalize(tagData.CardId);
            string tagId = REDboxVNStoryData.Normalize(tagData.Id);
            string knownId = REDboxVNStoryData.Normalize(knownCard != null ? knownCard.cardId : string.Empty);

            if (expected != tagCardId && expected != tagId && expected != knownId)
                return false;
        }

        return true;
    }
}

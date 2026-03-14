using System;
using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("REDbox/Sample/Visual Novel Controller")]
public class REDboxVNSampleController : MonoBehaviour
{
    [Header("Data")]
    public REDboxVNStoryData storyData;

    [Header("Behavior")]
    [Tooltip("Starts the story automatically on Start().")]
    public bool autoStart = true;

    [Tooltip("When true, a non-card node can auto-advance after entering.")]
    public bool autoAdvanceLinearNodes;

    [Tooltip("Delay used by auto-advance linear nodes.")]
    public float autoAdvanceDelay = 0.25f;

    [Header("Debug")]
    public bool verboseLogs = true;

    public event Action OnStateChanged;

    public VNNode CurrentNode { get; private set; }
    public bool StoryStarted { get; private set; }
    public bool StoryEnded { get; private set; }
    public string StatusLabel { get; private set; } = "Idle";
    public string LastCardDebug { get; private set; } = "No card yet";

    private float _autoAdvanceAt;
    private bool _pendingAutoAdvance;
    private readonly List<Card> _availableCards = new List<Card>();

    private void OnEnable()
    {
        ArduinoBridge.OnCardTagRead += OnCardTagRead;
    }

    private void OnDisable()
    {
        ArduinoBridge.OnCardTagRead -= OnCardTagRead;
    }

    private void Start()
    {
        RefreshAvailableCards();

        if (autoStart)
            StartStory();
    }

    private void Update()
    {
        if (_pendingAutoAdvance && Time.unscaledTime >= _autoAdvanceAt)
        {
            _pendingAutoAdvance = false;
            Advance();
        }
    }

    public void StartStory()
    {
        if (storyData == null)
        {
            SetStatus("Missing REDboxVNStoryData asset.");
            return;
        }

        storyData.BuildLookup();

        if (!storyData.TryGetNode(storyData.startNodeId, out var startNode))
        {
            SetStatus($"Invalid start node: {storyData.startNodeId}");
            return;
        }

        StoryStarted = true;
        StoryEnded = false;
        EnterNode(startNode, "Story start");
    }

    public void RestartStory()
    {
        RefreshAvailableCards();

        StoryStarted = false;
        StoryEnded = false;
        CurrentNode = null;
        _pendingAutoAdvance = false;
        StartStory();
    }

    public void Advance()
    {
        if (!StoryStarted || StoryEnded || CurrentNode == null)
            return;

        if (CurrentNode.requiresCard)
        {
            SetStatus("Scan the requested card to continue.");
            return;
        }

        if (CurrentNode.HasChoices)
        {
            SetStatus("Choose an option.");
            return;
        }

        GoToNode(CurrentNode.nextNodeId, "Next");
    }

    public void SelectChoice(int choiceIndex)
    {
        if (!StoryStarted || StoryEnded || CurrentNode == null || !CurrentNode.HasChoices)
            return;

        if (choiceIndex < 0 || choiceIndex >= CurrentNode.choices.Length)
        {
            SetStatus("Invalid choice index.");
            return;
        }

        var choice = CurrentNode.choices[choiceIndex];
        GoToNode(choice.nextNodeId, string.IsNullOrWhiteSpace(choice.label) ? "Choice" : choice.label);
    }

    public void SimulateTag(CardTagData tagData)
    {
        if (!tagData.IsValid)
            return;

        ProcessTag(tagData);
    }

    public bool CanSimulateRecommendedCard()
    {
        if (!StoryStarted || StoryEnded || CurrentNode == null)
            return false;

        if (CurrentNode.requiresCard)
            return true;

        if (!CurrentNode.HasChoices)
            return false;

        for (int i = 0; i < CurrentNode.choices.Length; i++)
        {
            if (CurrentNode.choices[i].requiresCard)
                return true;
        }

        return false;
    }

    public string GetRecommendedCardLabel()
    {
        if (!CanSimulateRecommendedCard())
            return "No card required";

        VNCardRequirement req = GetRecommendedRequirement();
        Card matched = FindFirstMatchingCard(req);
        if (matched != null)
            return $"{BuildRequirementLabel(req)} -> {GetCardDisplayName(matched)}";

        return BuildRequirementLabel(req);
    }

    public string GetValidCardOptionsLabel()
    {
        if (!CanSimulateRecommendedCard())
            return "";

        VNCardRequirement req = GetRecommendedRequirement();
        if (req == null)
            return "";

        List<Card> matches = FindMatchingCards(req);
        if (matches.Count == 0)
            return "No matching local card found. Fallback simulation is available.";

        int showCount = Mathf.Min(4, matches.Count);
        string label = "Valid local cards: ";
        for (int i = 0; i < showCount; i++)
        {
            if (i > 0) label += ", ";
            label += GetCardDisplayName(matches[i]);
        }

        if (matches.Count > showCount)
            label += $", +{matches.Count - showCount} more";

        return label;
    }

    public void SimulateRecommendedCard()
    {
        if (!CanSimulateRecommendedCard())
            return;

        VNCardRequirement req = GetRecommendedRequirement();
        CardTagData tagData = BuildTagDataFromAvailable(req);
        ProcessTag(tagData);
    }

    private void OnCardTagRead(CardTagData tagData)
    {
        ProcessTag(tagData);
    }

    private void ProcessTag(CardTagData tagData)
    {
        LastCardDebug = $"{tagData.Id} | {tagData.Type} | {tagData.TaxonomyType}/{tagData.Subtype}";

        if (!StoryStarted || StoryEnded || CurrentNode == null)
        {
            RaiseChanged();
            return;
        }

        Card knownCard = null;
        if (EventManager.Instance != null)
        {
            // Card resolution happens in ArduinoBridge before this callback. We only use
            // knownCard for requirement checks where an asset-bound card id is required.
            knownCard = null;
        }

        if (CurrentNode.HasChoices)
        {
            for (int i = 0; i < CurrentNode.choices.Length; i++)
            {
                var choice = CurrentNode.choices[i];
                if (!choice.requiresCard)
                    continue;

                if (choice.requiredCard != null && choice.requiredCard.Matches(tagData, knownCard))
                {
                    GoToNode(choice.nextNodeId, $"Card choice: {choice.label}");
                    return;
                }
            }
        }

        if (CurrentNode.requiresCard)
        {
            if (CurrentNode.requiredCard == null || CurrentNode.requiredCard.Matches(tagData, knownCard))
            {
                SetStatus($"Accepted card: {tagData.Id}");
                GoToNode(CurrentNode.nextNodeId, "Card progression");
            }
            else
            {
                SetStatus($"Wrong card for this step: {tagData.Id}. Need {BuildRequirementLabel(CurrentNode.requiredCard)}.");
                RaiseChanged();
            }
            return;
        }

        RaiseChanged();
    }

    private void GoToNode(string nodeId, string reason)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            StoryEnded = true;
            SetStatus("End of route.");
            RaiseChanged();
            return;
        }

        if (!storyData.TryGetNode(nodeId, out var next))
        {
            StoryEnded = true;
            SetStatus($"Missing node: {nodeId}");
            RaiseChanged();
            return;
        }

        EnterNode(next, reason);
    }

    private void EnterNode(VNNode node, string reason)
    {
        CurrentNode = node;
        StoryEnded = node != null && node.isEnding;

        SetStatus(string.IsNullOrWhiteSpace(reason) ? "Node entered" : reason);

        if (verboseLogs && node != null)
            Debug.Log($"[REDboxVN] Node={node.id}, ending={node.isEnding}, requiresCard={node.requiresCard}, choices={node.choices?.Length ?? 0}");

        RaiseChanged();

        _pendingAutoAdvance = false;
        if (!StoryEnded
            && autoAdvanceLinearNodes
            && node != null
            && !node.requiresCard
            && !node.HasChoices
            && !string.IsNullOrWhiteSpace(node.nextNodeId))
        {
            _pendingAutoAdvance = true;
            _autoAdvanceAt = Time.unscaledTime + Mathf.Max(0f, autoAdvanceDelay);
        }
    }

    private void SetStatus(string text)
    {
        StatusLabel = string.IsNullOrWhiteSpace(text) ? "-" : text;
    }

    private void RaiseChanged()
    {
        OnStateChanged?.Invoke();
    }

    private VNCardRequirement GetRecommendedRequirement()
    {
        if (CurrentNode == null)
            return null;

        if (CurrentNode.requiresCard)
            return CurrentNode.requiredCard;

        if (CurrentNode.choices != null)
        {
            for (int i = 0; i < CurrentNode.choices.Length; i++)
            {
                var choice = CurrentNode.choices[i];
                if (choice.requiresCard)
                    return choice.requiredCard;
            }
        }

        return null;
    }

    private static CardTagData BuildTagData(VNCardRequirement req)
    {
        if (req == null)
        {
            return new CardTagData
            {
                Id = "instruction.attack.default",
                CardId = "instruction.attack.default",
                Type = "INSTRUCTION",
                TaxonomyType = RedboxCardType.Instruction,
                Subtype = "attack",
                Name = "instruction.attack.default",
            };
        }

        string subtype = string.IsNullOrWhiteSpace(req.expectedSubtype) ? "default" : req.expectedSubtype;
        string type = !string.IsNullOrWhiteSpace(req.expectedLegacyType)
            ? req.expectedLegacyType.ToUpperInvariant()
            : req.expectedTaxonomyType.ToString().ToUpperInvariant();

        string id = !string.IsNullOrWhiteSpace(req.expectedId)
            ? req.expectedId
            : $"{req.expectedTaxonomyType.ToString().ToLowerInvariant()}.{subtype}.sample";

        if (req.expectedTaxonomyType == RedboxCardType.Unknown && string.IsNullOrWhiteSpace(req.expectedLegacyType))
        {
            type = "SYSTEM";
            id = "system.default.sample";
        }

        return new CardTagData
        {
            Id = id,
            CardId = id,
            Type = type,
            TaxonomyType = req.expectedTaxonomyType,
            Subtype = subtype,
            Name = id,
        };
    }

    private CardTagData BuildTagDataFromAvailable(VNCardRequirement req)
    {
        Card matched = FindFirstMatchingCard(req);
        if (matched == null)
            return BuildTagData(req);

        string id = string.IsNullOrWhiteSpace(matched.cardId)
            ? GetCardDisplayName(matched)
            : matched.cardId;

        RedboxCardType inferredType = InferTaxonomyType(matched);
        string inferredSubtype = InferSubtype(matched);

        RedboxCardType payloadType = req != null && req.expectedTaxonomyType != RedboxCardType.Unknown
            ? req.expectedTaxonomyType
            : inferredType;

        string payloadSubtype = !string.IsNullOrWhiteSpace(req?.expectedSubtype)
            ? req.expectedSubtype
            : inferredSubtype;

        return new CardTagData
        {
            Id = id,
            CardId = id,
            Name = matched.cardName,
            Type = string.IsNullOrWhiteSpace(matched.cardType) ? matched.cardTaxonomyType.ToString().ToUpperInvariant() : matched.cardType.ToUpperInvariant(),
            TaxonomyType = payloadType,
            Subtype = payloadSubtype,
        };
    }

    private static string BuildRequirementLabel(VNCardRequirement req)
    {
        if (req == null)
            return "any card";

        string typeLabel = req.expectedTaxonomyType != RedboxCardType.Unknown
            ? req.expectedTaxonomyType.ToString()
            : (string.IsNullOrWhiteSpace(req.expectedLegacyType) ? "AnyType" : req.expectedLegacyType.ToUpperInvariant());

        string subtypeLabel = string.IsNullOrWhiteSpace(req.expectedSubtype) ? "any-subtype" : req.expectedSubtype;
        return $"{typeLabel}/{subtypeLabel}";
    }

    private void RefreshAvailableCards()
    {
        _availableCards.Clear();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Card[] resourceCards = Resources.LoadAll<Card>(string.Empty);
        for (int i = 0; i < resourceCards.Length; i++)
            TryAddCard(resourceCards[i], seen);

        if (ArduinoBridge.Instance != null && ArduinoBridge.Instance.cardDataArray != null)
        {
            for (int i = 0; i < ArduinoBridge.Instance.cardDataArray.Length; i++)
                TryAddCard(ArduinoBridge.Instance.cardDataArray[i], seen);
        }
    }

    private void TryAddCard(Card card, HashSet<string> seen)
    {
        if (card == null)
            return;

        string key = string.IsNullOrWhiteSpace(card.cardId)
            ? card.name
            : card.cardId.Trim();

        if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
            return;

        _availableCards.Add(card);
    }

    private Card FindFirstMatchingCard(VNCardRequirement req)
    {
        List<Card> matches = FindMatchingCards(req);
        return matches.Count > 0 ? matches[0] : null;
    }

    private List<Card> FindMatchingCards(VNCardRequirement req)
    {
        var result = new List<Card>();
        if (req == null)
            return result;

        for (int i = 0; i < _availableCards.Count; i++)
        {
            Card card = _availableCards[i];
            if (card == null)
                continue;

            RedboxCardType cardType = InferTaxonomyType(card);
            string cardSubtype = InferSubtype(card);
            string cardLegacyType = InferLegacyType(card);

            if (req.expectedTaxonomyType != RedboxCardType.Unknown && cardType != req.expectedTaxonomyType)
                continue;

            if (!string.IsNullOrWhiteSpace(req.expectedSubtype)
                && !string.Equals(req.expectedSubtype.Trim(), cardSubtype?.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(req.expectedLegacyType)
                && !string.Equals(req.expectedLegacyType.Trim(), cardLegacyType?.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(req.expectedId)
                && !string.Equals(req.expectedId.Trim(), card.cardId?.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(card);
        }

        return result;
    }

    private static string GetCardDisplayName(Card card)
    {
        if (card == null)
            return "UnknownCard";

        if (!string.IsNullOrWhiteSpace(card.cardName))
            return card.cardName;

        if (!string.IsNullOrWhiteSpace(card.cardId))
            return card.cardId;

        return card.name;
    }

    private static RedboxCardType InferTaxonomyType(Card card)
    {
        if (card == null)
            return RedboxCardType.Unknown;

        if (card.cardTaxonomyType != RedboxCardType.Unknown)
            return card.cardTaxonomyType;

        string id = card.cardId?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(id))
        {
            if (id.StartsWith("actor.")) return RedboxCardType.Actor;
            if (id.StartsWith("instruction.")) return RedboxCardType.Instruction;
            if (id.StartsWith("modifier.")) return RedboxCardType.Modifier;
            if (id.StartsWith("lore.")) return RedboxCardType.Lore;
            if (id.StartsWith("world.")) return RedboxCardType.World;
            if (id.StartsWith("cosmetic.")) return RedboxCardType.Cosmetic;
            if (id.StartsWith("system.")) return RedboxCardType.System;
        }

        string legacy = card.cardType?.Trim().ToLowerInvariant();
        return legacy switch
        {
            "actor" => RedboxCardType.Actor,
            "instruction" => RedboxCardType.Instruction,
            "modifier" => RedboxCardType.Modifier,
            "lore" => RedboxCardType.Lore,
            "world" => RedboxCardType.World,
            "cosmetic" => RedboxCardType.Cosmetic,
            "system" => RedboxCardType.System,
            _ => RedboxCardType.Unknown,
        };
    }

    private static string InferSubtype(Card card)
    {
        if (card == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(card.subtype))
            return card.subtype.Trim().ToLowerInvariant();

        string id = card.cardId?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(id))
            return string.Empty;

        string[] parts = id.Split('.');
        if (parts.Length >= 2)
            return parts[1];

        return string.Empty;
    }

    private static string InferLegacyType(Card card)
    {
        if (card == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(card.cardType))
            return card.cardType.Trim();

        RedboxCardType type = InferTaxonomyType(card);
        return type == RedboxCardType.Unknown ? string.Empty : type.ToString();
    }
}

using System;
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
        return BuildRequirementLabel(req);
    }

    public void SimulateRecommendedCard()
    {
        if (!CanSimulateRecommendedCard())
            return;

        VNCardRequirement req = GetRecommendedRequirement();
        CardTagData tagData = BuildTagData(req);
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
}

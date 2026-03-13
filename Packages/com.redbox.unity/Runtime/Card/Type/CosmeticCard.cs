using UnityEngine;

/// <summary>
/// Cosmetic card — applies an appearance override: skin, outfit, aura, or emote.
///
/// SETUP: Create via Create → REDbox/Cosmetic
/// Set cardTaxonomyType = Cosmetic and subtype to "skin", "outfit", "aura", or "emote".
/// </summary>
[CreateAssetMenu(fileName = "NewCosmeticCard", menuName = "REDbox/Cosmetic", order = 15)]
public class CosmeticCard : Card
{
    [Header("── Cosmetic ─────────────────────────────────────────")]
    [Tooltip("Target avatar or prefab that receives the cosmetic override. Leave empty for global apply.")]
    public string targetAvatarId;

    [Tooltip("Replacement material applied as the cosmetic skin/outfit. Leave empty to skip.")]
    public Material cosmeticMaterial;

    [Tooltip("Particle system prefab used as the aura or emote VFX.")]
    public GameObject cosmeticVFX;

    [Tooltip("Animation trigger string sent to any Animator on the target avatar.")]
    public string animationTrigger;

    public override void Activate()
    {
        Debug.Log($"[CosmeticCard] {cardName} | subtype={subtype} | target={targetAvatarId}");
    }
}

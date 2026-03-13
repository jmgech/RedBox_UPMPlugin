using UnityEngine;

/// <summary>
/// World card — sets or changes the active environmental state (location, weather, or time-of-day).
///
/// SETUP: Create via Create → REDbox/World
/// Set cardTaxonomyType = World and subtype to "location", "weather", or "time_state".
/// </summary>
[CreateAssetMenu(fileName = "NewWorldCard", menuName = "REDbox/World", order = 14)]
public class WorldCard : Card
{
    [Header("── World ─────────────────────────────────────────────")]
    [Tooltip("If true, this card applies its effect while the physical tag remains present on the reader " +
             "(presence-based semantics). If false, the effect fires once on ENTER.")]
    public bool isPresenceBased = true;

    [Tooltip("Scene or environment identifier to load/activate on scan. Leave empty to skip scene change.")]
    public string environmentId;

    [Tooltip("Skybox material applied while this world state is active. Leave empty to skip.")]
    public Material skyboxOverride;

    [Tooltip("Ambient audio clip played while this world state is active.")]
    public AudioClip ambientAudio;

    public override void Activate()
    {
        Debug.Log($"[WorldCard] {cardName} | subtype={subtype} | env={environmentId} | presenceBased={isPresenceBased}");
    }
}

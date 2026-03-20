using UnityEngine;

/// <summary>
/// Actor card — represents a named character (ally or enemy).
///
/// SETUP: Create via Create → REDbox/Actor
/// Set cardTaxonomyType = Actor and subtype to "ally" or "enemy".
/// </summary>
[CreateAssetMenu(fileName = "NewActorCard", menuName = "REDbox/Actor", order = 10)]
public class ActorCard : Card
{
    [Header("── Actor ────────────────────────────────────────────")]
    [Tooltip("Prefab spawned when this actor is activated. Leave empty to skip spawning.")]
    public GameObject actorPrefab;

    [Tooltip("Spawn offset from the player origin (local space).")]
    public Vector3 spawnOffset = new Vector3(0f, 0f, 2f);

    [Tooltip("If > 0, the spawned actor is auto-destroyed after this many seconds.")]
    [Range(0f, 300f)]
    public float autoDestroyAfter = 0f;

    [Header("── Effects ──────────────────────────────────────────")]
    public GameObject summonVFX;
    public AudioClip  summonSound;

    public override void Activate()
    {
        Debug.Log($"[ActorCard] {cardName} | subtype={subtype} | HP:{hp} MP:{mp} AT:{at}");

        if (actorPrefab == null) return;

        Transform origin = Camera.main != null ? Camera.main.transform : null;
        Vector3 pos = origin != null
            ? origin.position + origin.TransformDirection(spawnOffset)
            : spawnOffset;

        GameObject instance = Instantiate(actorPrefab, pos, Quaternion.identity);

        if (summonVFX != null)
            Instantiate(summonVFX, pos, Quaternion.identity);

        if (summonSound != null)
        {
            var src = new GameObject("SummonAudio").AddComponent<AudioSource>();
            src.PlayOneShot(summonSound);
            Destroy(src.gameObject, summonSound.length + 0.1f);
        }

        if (autoDestroyAfter > 0f)
            Destroy(instance, autoDestroyAfter);
    }
}

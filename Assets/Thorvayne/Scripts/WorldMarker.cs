using UnityEngine;

// Tags a placeholder settlement/landmark/cave placed by the world builders.
// Later this can feed Waycraft destinations, spawn points, region names, etc.
public class WorldMarker : MonoBehaviour
{
    public string markerType;      // village, town, city, castle, fortress, tower, spire, warcamp, cave
    [Range(1, 5)] public int dangerTier = 1;   // 1 = safe starter area, 5 = most dangerous
    public bool isStarter;

#if UNITY_EDITOR
    static readonly Color[] TierColors =
    {
        new Color(1f, 0.95f, 0.5f), new Color(0.85f, 1f, 0.6f), new Color(1f, 0.8f, 0.45f),
        new Color(1f, 0.5f, 0.3f), new Color(0.9f, 0.15f, 0.15f)
    };

    void OnDrawGizmos()
    {
        var c = TierColors[Mathf.Clamp(dangerTier, 1, 5) - 1];
        Gizmos.color = c;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, isStarter ? 12f : 6f);
        var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel) { normal = { textColor = c } };
        UnityEditor.Handles.Label(transform.position + Vector3.up * 30f,
            $"{name}  (T{dangerTier}{(isStarter ? ", START" : "")})", style);
    }
#endif
}

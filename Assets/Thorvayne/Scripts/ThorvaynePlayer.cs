using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

// Simple third-person test character for walking around the generated worlds.
// WASD move, mouse look, Shift sprint, Space jump, mouse wheel zoom,
// F fly mode (Space up / Ctrl down), T teleport to the next town or cave, R back to spawn,
// Esc frees the mouse (click to grab it again).
// Works with both the new Input System and the old Input Manager.
[RequireComponent(typeof(CharacterController))]
public class ThorvaynePlayer : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 6f;
    public float sprintSpeed = 12f;
    public float flySpeed = 70f;
    public float jumpHeight = 1.4f;
    public float gravity = -22f;
    public float turnSpeed = 12f;

    [Header("Camera")]
    public Camera cam;
    public float mouseSensitivity = 1f;
    public float cameraDistance = 6f;
    public float minDistance = 2f, maxDistance = 60f;
    public float shoulderOffset = 0.7f;     // over-the-shoulder: camera sits a little to the right
    public float pivotHeight = 1.7f;

    [Header("World")]
    public float seaLevel = -10000f;         // set by the world builder; below this the player swims

    CharacterController cc;
    float yaw, pitch = 15f, vy;
    bool flying, swimming, cursorLocked = true;

    // Read by ThorvayneCharacterAnimator to pick animations.
    public bool IsFlying => flying;
    public bool IsSwimming => swimming;
    public bool IsGrounded => cc != null && cc.isGrounded;
    public float GroundSpeed { get; private set; }      // horizontal speed in m/s
    public float VerticalSpeed => vy;
    Vector3 spawnPos;
    readonly List<WorldMarker> markers = new List<WorldMarker>();
    int teleportIndex = -1;
    string nearestText = "", eventText = "";
    float nearestTimer, eventTimer;

    enum Act { Sprint, Jump, Down, Fly, Teleport, Respawn, Escape }

    void Start()
    {
        cc = GetComponent<CharacterController>();
        if (cam == null) cam = Camera.main;
        if (cam != null && cam.farClipPlane < 3000f) cam.farClipPlane = 4000f;
        spawnPos = transform.position;
        yaw = transform.eulerAngles.y;
        CollectMarkers();
        SetCursor(true);
    }

    void CollectMarkers()
    {
        markers.Clear();
#if UNITY_2023_1_OR_NEWER
        markers.AddRange(FindObjectsByType<WorldMarker>(FindObjectsSortMode.None));
#else
        markers.AddRange(FindObjectsOfType<WorldMarker>());
#endif
        // starter first, then cities, towns, villages, other landmarks, caves
        markers.Sort((a, b) =>
        {
            int c = Rank(a).CompareTo(Rank(b));
            return c != 0 ? c : string.Compare(a.name, b.name, System.StringComparison.Ordinal);
        });
    }

    static int Rank(WorldMarker m)
    {
        if (m.isStarter) return 0;
        switch (m.markerType) { case "city": return 1; case "town": return 2; case "village": return 3; case "cave": return 5; default: return 4; }
    }

    void Update()
    {
        if (Down(Act.Escape)) SetCursor(false);
        if (!cursorLocked && MouseClicked()) SetCursor(true);

        if (cursorLocked)
        {
            Vector2 look = LookDelta() * mouseSensitivity;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, -35f, 75f);
        }
        cameraDistance = Mathf.Clamp(cameraDistance - Scroll() * cameraDistance * 0.15f, minDistance, maxDistance);

        if (Down(Act.Fly)) { flying = !flying; vy = 0; Flash(flying ? "Fly mode ON (Space up, Ctrl down)" : "Fly mode OFF"); }
        if (Down(Act.Teleport)) TeleportNext();
        if (Down(Act.Respawn)) { Teleport(spawnPos); Flash("Back to spawn"); }

        Vector2 mv = MoveInput();
        var camFwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
        var camRight = Quaternion.Euler(0, yaw, 0) * Vector3.right;
        Vector3 dir = camFwd * mv.y + camRight * mv.x;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        float feet = transform.position.y;
        swimming = !flying && feet < seaLevel - 1.3f;
        Vector3 motion;
        if (flying)
        {
            float up = (Held(Act.Jump) ? 1 : 0) - (Held(Act.Down) ? 1 : 0);
            float speed = flySpeed * (Held(Act.Sprint) ? 3f : 1f);
            motion = (Quaternion.Euler(pitch * 0.5f, yaw, 0) * new Vector3(mv.x, 0, mv.y) + Vector3.up * up) * speed;
        }
        else
        {
            float speed = Held(Act.Sprint) ? sprintSpeed : walkSpeed;
            if (swimming)
            {
                speed *= 0.55f;
                vy = Mathf.Lerp(vy, (seaLevel - 1.3f - feet) * 3f, Time.deltaTime * 5f);   // float at the surface
            }
            else if (cc.isGrounded)
            {
                vy = -2f;
                if (Down(Act.Jump)) vy = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            else vy += gravity * Time.deltaTime;
            motion = dir * speed + Vector3.up * vy;
        }
        cc.Move(motion * Time.deltaTime);
        GroundSpeed = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;

        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * turnSpeed);

        if (transform.position.y < -100f) { Teleport(spawnPos); Flash("Fell out of the world, back to spawn"); }

        nearestTimer -= Time.deltaTime;
        if (nearestTimer <= 0) { nearestTimer = 0.5f; UpdateNearest(); }
        if (eventTimer > 0) eventTimer -= Time.deltaTime;
    }

    void LateUpdate()
    {
        if (cam == null) return;
        var rot = Quaternion.Euler(pitch, yaw, 0);
        Vector3 pivot = transform.position + Vector3.up * pivotHeight;
        Vector3 offset = rot * new Vector3(shoulderOffset, 0, -cameraDistance);
        float dist = offset.magnitude;
        // keep the camera from going through hills and walls
        if (Physics.SphereCast(pivot, 0.3f, offset.normalized, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.transform != transform)
            dist = Mathf.Max(0.5f, hit.distance);
        cam.transform.position = pivot + offset.normalized * dist;
        cam.transform.rotation = rot;
    }

    void OnTriggerEnter(Collider other)
    {
        var m = other.GetComponentInParent<WorldMarker>();
        if (m != null && m.markerType == "cave") Flash($"Cave entrance: {Clean(m.name)} (danger tier {m.dangerTier})");
    }

    // ---------------- teleport & info ----------------
    void TeleportNext()
    {
        if (markers.Count == 0) CollectMarkers();
        if (markers.Count == 0) { Flash("No towns or caves found in the scene"); return; }
        teleportIndex = (teleportIndex + 1) % markers.Count;
        var m = markers[teleportIndex];
        // stand in front of cave mouths, and a little off the centre of towns
        Vector3 p = m.markerType == "cave" ? m.transform.position + m.transform.forward * 8f : m.transform.position + new Vector3(0, 0, -12f);
        if (Physics.Raycast(p + Vector3.up * 600f, Vector3.down, out RaycastHit hit, 1200f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
        Teleport(p + Vector3.up * 0.2f);
        if (m.markerType == "cave") yaw = m.transform.eulerAngles.y + 180f;   // face the cave
        Flash($"Teleported to {Clean(m.name)}  ({teleportIndex + 1}/{markers.Count})");
    }

    void Teleport(Vector3 p)
    {
        cc.enabled = false;
        transform.position = p;
        cc.enabled = true;
        vy = 0;
    }

    void UpdateNearest()
    {
        WorldMarker best = null; float bd = float.MaxValue;
        foreach (var m in markers)
        {
            if (m == null) continue;
            float d = Vector3.Distance(m.transform.position, transform.position);
            if (d < bd) { bd = d; best = m; }
        }
        nearestText = best == null ? "" : $"Nearest: {Clean(best.name)}  ({best.markerType}, tier {best.dangerTier}, {bd:0} m)";
    }

    static string Clean(string n) { int i = n.LastIndexOf(" ("); return i > 0 ? n.Substring(0, i) : n; }

    void Flash(string text) { eventText = text; eventTimer = 3f; }

    void OnGUI()
    {
        var p = transform.position;
        string mode = flying ? "FLYING" : swimming ? "swimming" : "walking";
        string text = $"{nearestText}\nPosition {p.x:0}, {p.z:0}   height {p.y:0} m   {mode}\n" +
                      "WASD move · Shift sprint · Space jump · wheel zoom\nF fly · T next town/cave · R spawn · Esc free mouse";
        GUI.Box(new Rect(10, 10, 430, 76), "");
        GUI.Label(new Rect(18, 14, 420, 72), text);
        if (eventTimer > 0)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(0, Screen.height * 0.18f, Screen.width, 40), eventText, style);
        }
    }

    void SetCursor(bool locked)
    {
        cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // ---------------- input (new Input System or old Input Manager) ----------------
#if ENABLE_INPUT_SYSTEM
    static KeyControl K(Act k)
    {
        var kb = Keyboard.current;
        if (kb == null) return null;
        switch (k)
        {
            case Act.Sprint: return kb.leftShiftKey;
            case Act.Jump: return kb.spaceKey;
            case Act.Down: return kb.leftCtrlKey;
            case Act.Fly: return kb.fKey;
            case Act.Teleport: return kb.tKey;
            case Act.Respawn: return kb.rKey;
            default: return kb.escapeKey;
        }
    }
    static bool Held(Act k) { var c = K(k); return c != null && c.isPressed; }
    static bool Down(Act k) { var c = K(k); return c != null && c.wasPressedThisFrame; }
    static Vector2 MoveInput()
    {
        var kb = Keyboard.current;
        if (kb == null) return Vector2.zero;
        return new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
    }
    static Vector2 LookDelta() { return Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.1f : Vector2.zero; }
    static float Scroll() { return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f; }
    static bool MouseClicked() { return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame; }
#else
    static KeyCode K(Act k)
    {
        switch (k)
        {
            case Act.Sprint: return KeyCode.LeftShift;
            case Act.Jump: return KeyCode.Space;
            case Act.Down: return KeyCode.LeftControl;
            case Act.Fly: return KeyCode.F;
            case Act.Teleport: return KeyCode.T;
            case Act.Respawn: return KeyCode.R;
            default: return KeyCode.Escape;
        }
    }
    static bool Held(Act k) { return Input.GetKey(K(k)); }
    static bool Down(Act k) { return Input.GetKeyDown(K(k)); }
    static Vector2 MoveInput() { return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")); }
    static Vector2 LookDelta() { return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 2f; }
    static float Scroll() { return Input.mouseScrollDelta.y; }
    static bool MouseClicked() { return Input.GetMouseButtonDown(0); }
#endif
}

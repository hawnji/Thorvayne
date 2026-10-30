using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Woodcutting on the terrain's trees. Walk up to a tree and press E: the character swings an axe,
// each swing has a chance to give a log and Woodcutting XP, and the tree falls when it runs out of logs.
// A stump is left behind and the tree grows back after a while.
// Trees are terrain trees, so a felled tree is hidden by shrinking it; all of them are put back when Play stops.
public class ThorvayneWoodcutting : MonoBehaviour
{
    [Serializable]
    public class TreeType
    {
        public string prefabName;         // matches the tree prefab (Tree_<prefabName>)
        public string displayName;
        public string logId;              // item in ThorvayneItems
        public int level = 1;             // Woodcutting level needed
        public float xp = 25;
        [Range(0, 1)] public float fallChance = 1f;   // chance the tree falls each time it gives a log
        public float respawnSeconds = 20;
    }

    public TreeType[] treeTypes =
    {
        new TreeType { prefabName = "Broadleaf", displayName = "Tree", logId = "logs", level = 1, xp = 25, fallChance = 1f, respawnSeconds = 15 },
        new TreeType { prefabName = "Conifer", displayName = "Pine tree", logId = "pine_logs", level = 15, xp = 37.5f, fallChance = 0.25f, respawnSeconds = 25 },
        new TreeType { prefabName = "Palm", displayName = "Palm tree", logId = "palm_logs", level = 30, xp = 67.5f, fallChance = 0.15f, respawnSeconds = 35 },
        new TreeType { prefabName = "SwampTree", displayName = "Swamp tree", logId = "swamp_logs", level = 45, xp = 100f, fallChance = 0.125f, respawnSeconds = 50 },
        new TreeType { prefabName = "SnowPine", displayName = "Frost pine", logId = "frost_logs", level = 60, xp = 175f, fallChance = 0.125f, respawnSeconds = 70 },
    };

    public KeyCode chopKey = KeyCode.E;
    public float reach = 3.2f;            // how close (m) you must stand to a tree
    public float swingTime = 1.8f;        // one swing; matches the Chop animation
    [Range(0, 1)] public float strikeTime = 0.68f;   // when in the swing the axe hits
    [Tooltip("Multiplies Woodcutting XP. Raise it (e.g. 20) to reach the higher trees quickly while testing.")]
    public float xpMultiplier = 1f;

    public bool IsChopping => chopping != null;
    public string Prompt { get; private set; } = "";

    class TerrainTrees { public Terrain terrain; public TreeInstance[] original; public TreeType[] typeByProto; public bool[] hidden; }
    class Target { public TerrainTrees tt; public int index; public TreeType type; public Vector3 pos; }

    readonly List<TerrainTrees> terrains = new List<TerrainTrees>();
    Target near, chopping;
    float scanTimer, swingClock;
    bool struck;
    ThorvaynePlayer player;
    ThorvayneSkills skills;
    ThorvayneInventory inventory;
    ThorvayneGameUI ui;
    GameObject axe;

    void Start()
    {
        player = GetComponent<ThorvaynePlayer>();
        skills = GetComponent<ThorvayneSkills>();
        inventory = GetComponent<ThorvayneInventory>();
        ui = GetComponent<ThorvayneGameUI>();
        foreach (var t in Terrain.activeTerrains)
        {
            var td = t.terrainData;
            var protos = td.treePrototypes;
            var tt = new TerrainTrees { terrain = t, original = td.treeInstances, typeByProto = new TreeType[protos.Length] };
            tt.hidden = new bool[tt.original.Length];
            bool any = false;
            for (int p = 0; p < protos.Length; p++)
            {
                if (protos[p].prefab == null) continue;
                string n = protos[p].prefab.name;
                foreach (var type in treeTypes)
                    if (n == type.prefabName || n.EndsWith("_" + type.prefabName)) { tt.typeByProto[p] = type; any = true; }
            }
            if (any && tt.original.Length > 0) terrains.Add(tt);
        }
        BuildAxe();
    }

    void OnDisable()
    {
        // put every felled tree back, so the terrain assets are left exactly as they were
        foreach (var tt in terrains)
        {
            if (tt.terrain == null) continue;
            for (int i = 0; i < tt.hidden.Length; i++)
                if (tt.hidden[i]) { tt.terrain.terrainData.SetTreeInstance(i, tt.original[i]); tt.hidden[i] = false; }
        }
        chopping = null;
    }

    void Update()
    {
        if (player == null || skills == null || inventory == null) return;
        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0) { scanTimer = 0.15f; near = FindNearest(); }

        if (ThorvayneKeys.Down(chopKey))
        {
            if (chopping != null) Stop();
            else if (near != null) TryStart(near);
        }

        if (chopping != null)
        {
            if (player.HasMoveInput || player.IsFlying || player.IsSwimming || chopping.tt.hidden[chopping.index]) { Stop(); }
            else
            {
                var d = chopping.pos - transform.position; d.y = 0;
                if (d.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), Time.deltaTime * 10f);
                swingClock += Time.deltaTime;
                if (!struck && swingClock >= strikeTime * swingTime) { struck = true; Strike(); }
                if (swingClock >= swingTime) { swingClock -= swingTime; struck = false; }
            }
        }

        if (chopping != null) Prompt = $"Chopping {chopping.type.displayName.ToLower()}...  (E or move to stop)";
        else if (near != null)
        {
            int need = near.type.level;
            Prompt = skills.Level("Woodcutting") >= need ? $"E  Chop {near.type.displayName.ToLower()}"
                                                          : $"{near.type.displayName}  (needs Woodcutting {need})";
        }
        else Prompt = "";
    }

    void TryStart(Target t)
    {
        if (skills.Level("Woodcutting") < t.type.level)
        {
            Say($"You need a Woodcutting level of {t.type.level} to chop down this {t.type.displayName.ToLower()}.");
            return;
        }
        if (!inventory.CanAdd(t.type.logId)) { Say("Your inventory is too full to hold any more logs."); return; }
        chopping = t;
        swingClock = 0; struck = false;
        if (axe != null) axe.SetActive(true);
        Say("You swing your axe at the tree.");
    }

    void Stop()
    {
        chopping = null;
        if (axe != null) axe.SetActive(false);
    }

    void Strike()
    {
        var t = chopping;
        int level = skills.Level("Woodcutting");
        // 35% per swing at the tree's level, rising with your level
        float chance = Mathf.Clamp(0.35f + (level - t.type.level) * 0.012f, 0.35f, 0.95f);
        if (UnityEngine.Random.value > chance) return;

        inventory.Add(t.type.logId);
        skills.AddXp("Woodcutting", t.type.xp * xpMultiplier);
        var item = ThorvayneItems.Get(t.type.logId);
        Say($"You get some {(item != null ? item.name.ToLower() : "logs")}.");

        if (UnityEngine.Random.value < t.type.fallChance) { Fell(t); Stop(); }
        else if (!inventory.CanAdd(t.type.logId)) { Say("Your inventory is too full to hold any more logs."); Stop(); }
    }

    void Say(string text) { if (ui != null) ui.Message(text); }

    // ---------------- finding trees ----------------
    Target FindNearest()
    {
        Vector3 p = transform.position;
        Target best = null; float bd = reach * reach;
        foreach (var tt in terrains)
        {
            if (tt.terrain == null) continue;
            Vector3 tp = tt.terrain.transform.position, size = tt.terrain.terrainData.size;
            if (p.x < tp.x - reach || p.z < tp.z - reach || p.x > tp.x + size.x + reach || p.z > tp.z + size.z + reach) continue;
            for (int i = 0; i < tt.original.Length; i++)
            {
                if (tt.hidden[i]) continue;
                var inst = tt.original[i];
                if (inst.prototypeIndex < 0 || inst.prototypeIndex >= tt.typeByProto.Length || tt.typeByProto[inst.prototypeIndex] == null) continue;
                float x = tp.x + inst.position.x * size.x, z = tp.z + inst.position.z * size.z;
                float d = (x - p.x) * (x - p.x) + (z - p.z) * (z - p.z);
                if (d >= bd) continue;
                float y = tp.y + inst.position.y * size.y;
                if (Mathf.Abs(y - p.y) > 4f) continue;
                bd = d;
                best = new Target { tt = tt, index = i, type = tt.typeByProto[inst.prototypeIndex], pos = new Vector3(x, y, z) };
            }
        }
        return best;
    }

    // ---------------- felling and regrowing ----------------
    void Fell(Target t)
    {
        var inst = t.tt.original[t.index];
        var hiddenInst = inst; hiddenInst.widthScale = 0; hiddenInst.heightScale = 0;
        t.tt.terrain.terrainData.SetTreeInstance(t.index, hiddenInst);
        t.tt.hidden[t.index] = true;
        var away = t.pos - transform.position; away.y = 0;
        if (away.sqrMagnitude < 0.01f) away = transform.forward;
        StartCoroutine(FallAndRegrow(t, inst, away.normalized));
    }

    IEnumerator FallAndRegrow(Target t, TreeInstance inst, Vector3 away)
    {
        var prefab = t.tt.terrain.terrainData.treePrototypes[inst.prototypeIndex].prefab;
        var stump = MakeStump(prefab, t.pos, inst.widthScale);
        if (prefab != null)
        {
            var tree = Instantiate(prefab, t.pos, Quaternion.Euler(0, inst.rotation * Mathf.Rad2Deg, 0));
            tree.transform.localScale = new Vector3(inst.widthScale, inst.heightScale, inst.widthScale);
            foreach (var c in tree.GetComponentsInChildren<Collider>()) Destroy(c);
            var axis = Vector3.Cross(Vector3.up, away);
            var start = tree.transform.rotation;
            for (float time = 0; time < 1.4f; time += Time.deltaTime)
            {
                float k = time / 1.4f;
                tree.transform.rotation = Quaternion.AngleAxis(88f * k * k, axis) * start;   // slow, then faster
                yield return null;
            }
            tree.transform.rotation = Quaternion.AngleAxis(88f, axis) * start;
            yield return new WaitForSeconds(1.5f);
            for (float time = 0; time < 1.5f; time += Time.deltaTime)
            {
                tree.transform.position += Vector3.down * 2f * Time.deltaTime;   // sink into the ground
                yield return null;
            }
            Destroy(tree);
        }
        yield return new WaitForSeconds(Mathf.Max(1f, t.type.respawnSeconds - 4.4f));
        t.tt.terrain.terrainData.SetTreeInstance(t.index, inst);
        t.tt.hidden[t.index] = false;
        if (stump != null) Destroy(stump);
    }

    static GameObject MakeStump(GameObject prefab, Vector3 pos, float width)
    {
        var stump = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stump.name = "Stump";
        Destroy(stump.GetComponent<Collider>());
        stump.transform.position = pos + Vector3.up * 0.15f;
        stump.transform.localScale = new Vector3(0.75f * width, 0.2f, 0.75f * width);
        var r = prefab != null ? prefab.GetComponentInChildren<Renderer>() : null;
        if (r != null && r.sharedMaterials.Length > 0) stump.GetComponent<Renderer>().sharedMaterial = r.sharedMaterials[0];   // bark
        return stump;
    }

    // ---------------- the axe ----------------
    // A simple axe made of two boxes, held in the right hand while chopping.
    // Its direction is worked out from the hand in the model's rest pose (palm down, fingers along the arm),
    // so it fits any humanoid character without hand-tuning.
    void BuildAxe()
    {
        var anim = GetComponentInChildren<Animator>();
        if (anim == null || !anim.isHuman) return;
        Transform hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        Transform upper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform finger = anim.GetBoneTransform(HumanBodyBones.RightIndexProximal);
        if (finger == null) finger = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        if (hand == null || upper == null) return;

        Vector3 up = transform.up, fwd = transform.forward;
        Vector3 arm = (hand.position - upper.position).normalized;
        Vector3 palm = -(up - arm * Vector3.Dot(up, arm)).normalized;
        Vector3 fingers = finger != null ? finger.position - hand.position : arm * 0.1f;
        float len = fingers.magnitude;
        fingers = (fingers - palm * Vector3.Dot(fingers, palm)).normalized;
        Vector3 handle = Vector3.Cross(fingers, palm).normalized;
        if (Vector3.Dot(handle, fwd) < 0) handle = -handle;           // the head sits on the thumb side
        Vector3 edge = (fingers - handle * Vector3.Dot(fingers, handle)).normalized;

        axe = new GameObject("Axe");
        axe.transform.SetPositionAndRotation(hand.position + fingers * len * 0.6f + palm * 0.025f, Quaternion.LookRotation(edge, handle));
        axe.transform.SetParent(hand, true);
        var skin = GetComponentInChildren<Renderer>();
        var shaft = Part(axe.transform, new Vector3(0, 0.23f, 0), new Vector3(0.035f, 0.7f, 0.035f), skin, new Color(0.45f, 0.3f, 0.15f));
        var head = Part(axe.transform, new Vector3(0, 0.5f, 0.07f), new Vector3(0.035f, 0.16f, 0.22f), skin, new Color(0.62f, 0.63f, 0.66f));
        shaft.name = "Handle"; head.name = "Head";
        axe.SetActive(false);
    }

    static GameObject Part(Transform parent, Vector3 localPos, Vector3 size, Renderer like, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        // undo any scale on the hand bones so the axe keeps its real size
        Vector3 s = parent.lossyScale;
        go.transform.localScale = new Vector3(size.x / s.x, size.y / s.y, size.z / s.z);
        go.transform.localPosition = new Vector3(localPos.x / s.x, localPos.y / s.y, localPos.z / s.z);
        if (like != null && like.sharedMaterial != null)
        {
            var mat = new Material(like.sharedMaterial);
            foreach (var prop in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })   // plain colour, not the character's skin
                if (mat.HasProperty(prop)) mat.SetTexture(prop, null);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
        return go;
    }
}

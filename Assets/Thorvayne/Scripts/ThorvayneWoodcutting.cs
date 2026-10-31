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
    public float chopDistance = 1.2f;     // where you stand from the tree's middle, so the swing meets the trunk
    [Tooltip("Hit sounds; left empty, the WoodThud sounds in Resources/Audio are used.")]
    public AudioClip[] hitSounds;
    [Range(0, 1)] public float hitVolume = 0.8f;
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
    ThorvayneEquipment equipment;
    GameObject axe;
    Renderer axeRenderer;
    Animator axeAnimator;
    int gripLayer = -1;
    ThorvayneItems.Item choppingAxe;
    CharacterController controller;
    // while chopping, the terrain tree is swapped for a copy of it that can shake when hit
    GameObject standIn;
    Target standInFor;
    Quaternion standInRotation;
    Vector3 shakeAxis;
    float shakeClock = -1;
    ParticleSystem chips;
    AudioSource hitAudio;

    void Start()
    {
        player = GetComponent<ThorvaynePlayer>();
        skills = GetComponent<ThorvayneSkills>();
        inventory = GetComponent<ThorvayneInventory>();
        ui = GetComponent<ThorvayneGameUI>();
        equipment = GetComponent<ThorvayneEquipment>();
        if (equipment != null) equipment.Changed += UpdateAxe;
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
        FindAxe();
        UpdateAxe();
        controller = GetComponent<CharacterController>();
        MakeHitEffects();
    }

    void OnDestroy() { if (equipment != null) equipment.Changed -= UpdateAxe; }

    void OnDisable()
    {
        // put every felled tree back, so the terrain assets are left exactly as they were
        foreach (var tt in terrains)
        {
            if (tt.terrain == null) continue;
            for (int i = 0; i < tt.hidden.Length; i++)
                if (tt.hidden[i]) { tt.terrain.terrainData.SetTreeInstance(i, tt.original[i]); tt.hidden[i] = false; }
        }
        RemoveStandIn();
        chopping = null;
    }

    void Update()
    {
        if (player == null || skills == null || inventory == null) return;
        if (equipment == null && (equipment = GetComponent<ThorvayneEquipment>()) != null)   // added later by ThorvaynePlayer
        {
            equipment.Changed += UpdateAxe;
            UpdateAxe();
        }
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
                // step in or back to swinging distance, so the axe meets the trunk
                float off = d.magnitude - chopDistance;
                if (controller != null && Mathf.Abs(off) > 0.05f && d.sqrMagnitude > 0.01f)
                    controller.Move(d.normalized * Mathf.Clamp(off, -1f, 1f) * Mathf.Min(1f, Time.deltaTime * 5f));
                swingClock += Time.deltaTime;
                if (!struck && swingClock >= strikeTime * swingTime) { struck = true; Strike(); }
                if (swingClock >= swingTime) { swingClock -= swingTime; struck = false; }
            }
        }

        Shake();

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
        var best = BestAxe(out bool tooLow);
        if (best == null)
        {
            Say(tooLow ? "You don't have the Woodcutting level to use your axe." : "You need an axe to chop down this tree.");
            return;
        }
        if (!inventory.CanAdd(t.type.logId)) { Say("Your inventory is too full to hold any more logs."); return; }
        chopping = t; choppingAxe = best;
        swingClock = 0; struck = false;
        MakeStandIn(t);
        UpdateAxe();
        Say("You swing your axe at the tree.");
    }

    void Stop()
    {
        RemoveStandIn();
        chopping = null; choppingAxe = null;
        UpdateAxe();
    }

    void Strike()
    {
        var t = chopping;
        HitEffects(t);
        int level = skills.Level("Woodcutting");
        // 35% per swing at the tree's level with a bronze axe, rising with your level and a better axe
        int power = choppingAxe != null ? choppingAxe.power : 1;
        float chance = Mathf.Clamp(0.35f + (level - t.type.level) * 0.012f + (power - 1) * 0.04f, 0.35f, 0.95f);
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
        if (standIn != null) { Destroy(standIn); standIn = null; standInFor = null; }   // the falling copy takes over
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
            float depth = 6f * Mathf.Max(inst.widthScale, inst.heightScale);   // deeper than the fallen crown is wide
            for (float time = 0; time < 1.5f; time += Time.deltaTime)
            {
                tree.transform.position += Vector3.down * (depth / 1.5f) * Time.deltaTime;   // sink into the ground
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
        if (r != null && r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != null)
        {
            // plain bark colour (tree models have a texture made for their own shape)
            var mat = new Material(r.sharedMaterials[0]);
            foreach (var prop in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })
                if (mat.HasProperty(prop)) mat.SetTexture(prop, null);
            var bark = new Color(0.38f, 0.26f, 0.16f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", bark);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", bark);
            stump.GetComponent<Renderer>().sharedMaterial = mat;
        }
        return stump;
    }

    // ---------------- hits: wood chips, a thud and the tree shaking ----------------
    void MakeHitEffects()
    {
        var go = new GameObject("WoodChips");
        chips = go.AddComponent<ParticleSystem>();
        chips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = chips.main;
        main.playOnAwake = false; main.loop = false; main.maxParticles = 300;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
        main.gravityModifier = 1.6f;
        main.startSize3D = true;                                              // thin flat chips
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(0.01f, 0.02f);
        main.startSizeZ = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
        main.startRotation3D = true;
        main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.7f, 0.48f), new Color(0.55f, 0.4f, 0.24f));
        var emission = chips.emission; emission.enabled = false;                // sent out in bursts by HitEffects
        var shape = chips.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 35f; shape.radius = 0.05f;
        var spin = chips.rotationOverLifetime; spin.enabled = true; spin.separateAxes = true;
        spin.x = spin.y = spin.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
        var hit = chips.collision; hit.enabled = true; hit.type = ParticleSystemCollisionType.World;
        hit.bounce = 0.2f; hit.dampen = 0.6f; hit.lifetimeLoss = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = cube.GetComponent<MeshFilter>().sharedMesh;
        Destroy(cube);
        var shader = Shader.Find(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                                 ? "Universal Render Pipeline/Particles/Lit" : "Particles/Standard Surface");
        r.material = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));

        if (hitSounds == null || hitSounds.Length == 0)
            hitSounds = Array.FindAll(Resources.LoadAll<AudioClip>("Audio"), c => c.name.StartsWith("WoodThud"));
        hitAudio = new GameObject("ChopSound").AddComponent<AudioSource>();
        hitAudio.playOnAwake = false; hitAudio.spatialBlend = 1f;
        hitAudio.rolloffMode = AudioRolloffMode.Linear; hitAudio.minDistance = 3f; hitAudio.maxDistance = 45f;
    }

    void HitEffects(Target t)
    {
        var toMe = transform.position - t.pos; toMe.y = 0;
        toMe = toMe.sqrMagnitude > 0.01f ? toMe.normalized : -transform.forward;
        var point = t.pos + Vector3.up * 1.1f + toMe * 0.3f;          // where the blade bites the trunk
        if (chips != null)
        {
            chips.transform.SetPositionAndRotation(point, Quaternion.LookRotation(toMe + Vector3.up * 0.6f));
            chips.Emit(UnityEngine.Random.Range(10, 17));
        }
        if (hitAudio != null && hitSounds != null && hitSounds.Length > 0)
        {
            hitAudio.transform.position = point;
            hitAudio.pitch = UnityEngine.Random.Range(0.92f, 1.08f);
            hitAudio.PlayOneShot(hitSounds[UnityEngine.Random.Range(0, hitSounds.Length)], hitVolume);
        }
        if (standIn != null) { shakeAxis = Vector3.Cross(Vector3.up, -toMe); shakeClock = 0; }   // tips away from you
    }

    // A quick wobble that dies away, pivoting at the base.
    void Shake()
    {
        if (shakeClock < 0 || standIn == null) return;
        shakeClock += Time.deltaTime;
        float angle = 1.8f * Mathf.Sin(shakeClock * 32f) * Mathf.Exp(-shakeClock * 6f);
        standIn.transform.rotation = Quaternion.AngleAxis(angle, shakeAxis) * standInRotation;
        if (shakeClock > 0.8f) { standIn.transform.rotation = standInRotation; shakeClock = -1; }
    }

    // Terrain trees can't move on their own, so the one being chopped is hidden and a copy stands in for it.
    void MakeStandIn(Target t)
    {
        RemoveStandIn();
        var inst = t.tt.original[t.index];
        var prefab = t.tt.terrain.terrainData.treePrototypes[inst.prototypeIndex].prefab;
        if (prefab == null) return;
        standInRotation = Quaternion.Euler(0, inst.rotation * Mathf.Rad2Deg, 0);
        standIn = Instantiate(prefab, t.pos, standInRotation); standInFor = t;
        standIn.transform.localScale = new Vector3(inst.widthScale, inst.heightScale, inst.widthScale);
        var hidden = inst; hidden.widthScale = 0; hidden.heightScale = 0;
        t.tt.terrain.terrainData.SetTreeInstance(t.index, hidden);
        shakeClock = -1;
    }

    void RemoveStandIn()
    {
        if (standIn == null) return;
        Destroy(standIn); standIn = null;
        var t = standInFor; standInFor = null;
        if (t != null && !t.tt.hidden[t.index] && t.tt.terrain != null)   // not felled: put the terrain tree back
            t.tt.terrain.terrainData.SetTreeInstance(t.index, t.tt.original[t.index]);
    }

    // ---------------- the axe ----------------
    // The hero model has an "Axe" object in its right hand (Extras/add_axe.py). It shows while an axe is
    // wielded or while chopping with one from the inventory, with the head coloured by the axe's metal
    // and the hand closed around the handle.
    void FindAxe()
    {
        var anim = GetComponentInChildren<Animator>();
        var t = anim != null ? FindDeep(anim.transform, "Axe") : null;
        if (t == null) return;
        axe = t.gameObject;
        axeRenderer = axe.GetComponent<Renderer>();
        axeAnimator = anim;
        gripLayer = anim.GetLayerIndex("Grip");   // closes the right hand (ThorvayneTestPlayerSetup)
    }

    void UpdateAxe()
    {
        if (axe == null) return;
        var it = chopping != null ? choppingAxe : ThorvayneItems.Get(equipment != null ? equipment.Get("Weapon") : null);
        bool show = ThorvayneItems.IsAxe(it);
        axe.SetActive(show);
        if (gripLayer >= 0) axeAnimator.SetLayerWeight(gripLayer, show ? 1f : 0f);
        if (show && axeRenderer != null && axeRenderer.sharedMaterials.Length > 1)
        {
            var mats = axeRenderer.materials;   // per-character copies, so the shared material stays as is
            if (mats[1].HasProperty("_BaseColor")) mats[1].SetColor("_BaseColor", it.color);
            if (mats[1].HasProperty("_Color")) mats[1].SetColor("_Color", it.color);
        }
    }

    // the best axe you can use, wielded or in the inventory (null if none)
    ThorvayneItems.Item BestAxe(out bool tooLow)
    {
        ThorvayneItems.Item best = null; tooLow = false;
        var ids = new List<string>();
        if (equipment != null && equipment.Get("Weapon") != null) ids.Add(equipment.Get("Weapon"));
        foreach (var s in inventory.slots) if (s != null) ids.Add(s.id);
        int level = skills.Level("Woodcutting");
        foreach (var id in ids)
        {
            var it = ThorvayneItems.Get(id);
            if (!ThorvayneItems.IsAxe(it)) continue;
            if (it.level > level) { tooLow = true; continue; }
            if (best == null || it.power > best.power) best = it;
        }
        return best;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
        return null;
    }
}

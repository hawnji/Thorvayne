using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// One-click prototype world builder for Thorvayne.
// Menu: Thorvayne > Build World From Map
// Reads the data generated from the world map picture (Assets/Thorvayne/MapData) and builds:
//   terrain (heights + painted layers), low-poly placeholder trees, a sea plane,
//   placeholder settlements/landmarks with WorldMarker tags, and a PlayerSpawn at the starter village.
// Running it again replaces the previous build.
public static class ThorvayneWorldBuilder
{
    const string DataDir = "Assets/Thorvayne/MapData";
    const string GenDir = "Assets/Thorvayne/Generated";
    const string RootName = "Thorvayne World";

    // Tweakables
    const float TreeSpacing = 6f;        // metres between tree candidates; lower = denser forests
    const float TreeDensity = 0.9f;      // 0..1 multiplier on the forest map
    const float LayerTileSize = 10f;     // metres per texture repeat
    const int Seed = 12345;
    const bool TransparentWater = false; // solid water always renders; true = see-through (may need the material toggle fix in URP)

    [Serializable] class Marker { public string name; public string type; public float u; public float v; public int dangerTier; public bool isStarter; }
    [Serializable] class World
    {
        public float worldWidth, worldLength, heightScale, seaLevel;
        public int heightmapResolution, alphamapResolution, treeMapResolution;
        public string[] layers;
        public Marker[] markers;
    }

    [MenuItem("Thorvayne/Build World From Map")]
    public static void Build()
    {
        try
        {
            var world = JsonUtility.FromJson<World>(File.ReadAllText(Path.Combine(DataDir, "thorvayne_world.json")));
            EnsureFolder(GenDir);

            var old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            var root = new GameObject(RootName);

            Progress("Heightmap", 0.05f);
            var td = BuildTerrainData(world);

            Progress("Terrain layers", 0.25f);
            PaintLayers(td, world);

            Progress("Trees", 0.5f);
            var terrainGO = Terrain.CreateTerrainGameObject(td);
            terrainGO.name = "Terrain";
            terrainGO.transform.SetParent(root.transform, false);
            var terrain = terrainGO.GetComponent<Terrain>();
            terrain.heightmapPixelError = 4;
            terrain.basemapDistance = 1200;
            terrain.treeDistance = 900;
            terrain.treeBillboardDistance = 900;   // placeholder trees have no billboards, so draw them as meshes
            terrain.drawInstanced = true;
            PlantTrees(td, world);

            Progress("Sea", 0.75f);
            BuildSea(root.transform, world);

            Progress("Settlements", 0.85f);
            var spawn = PlaceMarkers(root.transform, terrain, world);

            terrain.Flush();
            AssetDatabase.SaveAssets();
            Undo.RegisterCreatedObjectUndo(root, "Build Thorvayne World");
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null && spawn != null)
                SceneView.lastActiveSceneView.LookAt(spawn.position, Quaternion.Euler(35f, 20f, 0f), 220f);
            Debug.Log($"Thorvayne world built: {world.worldWidth} x {world.worldLength} m, {td.treeInstanceCount} trees, {world.markers.Length} markers.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static void Progress(string step, float t) => EditorUtility.DisplayProgressBar("Building Thorvayne world", step, t);

    // ---------------- terrain ----------------
    static TerrainData BuildTerrainData(World w)
    {
        int res = w.heightmapResolution;
        byte[] raw = File.ReadAllBytes(Path.Combine(DataDir, "thorvayne_height.r16"));
        if (raw.Length != res * res * 2) throw new Exception("Heightmap size does not match thorvayne_world.json");
        var heights = new float[res, res];
        for (int y = 0, i = 0; y < res; y++)
            for (int x = 0; x < res; x++, i += 2)
                heights[y, x] = (raw[i] | (raw[i + 1] << 8)) / 65535f;   // row 0 = south edge

        var td = new TerrainData();
        td.heightmapResolution = res;   // set before size
        td.size = new Vector3(w.worldWidth, w.heightScale, w.worldLength);
        td.SetHeights(0, 0, heights);
        ReplaceAsset(td, GenDir + "/ThorvayneTerrain.asset");
        return td;
    }

    static void PaintLayers(TerrainData td, World w)
    {
        int n = w.layers.Length;
        var layers = new TerrainLayer[n];
        for (int i = 0; i < n; i++)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{DataDir}/Textures/tv_{w.layers[i]}.png");
            var tl = new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(LayerTileSize, LayerTileSize), smoothness = 0f, metallic = 0f };
            ReplaceAsset(tl, $"{GenDir}/TL_{w.layers[i]}.terrainlayer");
            layers[i] = tl;
        }
        td.terrainLayers = layers;

        int res = w.alphamapResolution;
        td.alphamapResolution = res;
        td.baseMapResolution = 1024;
        byte[] raw = File.ReadAllBytes(Path.Combine(DataDir, "thorvayne_splat.bytes"));
        if (raw.Length != res * res * n) throw new Exception("Splat map size does not match thorvayne_world.json");
        var maps = new float[res, res, n];
        for (int y = 0, i = 0; y < res; y++)
            for (int x = 0; x < res; x++, i += n)
            {
                float sum = 0;
                for (int l = 0; l < n; l++) sum += raw[i + l];
                if (sum <= 0) { maps[y, x, 0] = 1; continue; }
                for (int l = 0; l < n; l++) maps[y, x, l] = raw[i + l] / sum;
            }
        td.SetAlphamaps(0, 0, maps);
    }

    // ---------------- trees ----------------
    static void PlantTrees(TerrainData td, World w)
    {
        var bark = Mat("Bark", new Color(0.38f, 0.26f, 0.16f));
        var protos = new[]
        {
            TreePrefab("Conifer", BuildConifer(), bark, Mat("ConiferLeaves", new Color(0.18f, 0.42f, 0.22f))),
            TreePrefab("Broadleaf", BuildBroadleaf(), bark, Mat("AutumnLeaves", new Color(0.62f, 0.42f, 0.16f))),
            TreePrefab("SnowPine", BuildConifer(), bark, Mat("SnowPineLeaves", new Color(0.72f, 0.82f, 0.82f))),
        };
        var tps = new TreePrototype[protos.Length];
        for (int i = 0; i < protos.Length; i++) tps[i] = new TreePrototype { prefab = protos[i] };
        td.treePrototypes = tps;

        int tm = w.treeMapResolution;
        byte[] dens = File.ReadAllBytes(Path.Combine(DataDir, "thorvayne_trees.bytes"));
        var rnd = new System.Random(Seed);
        var list = new List<TreeInstance>();
        float minH = (w.seaLevel + 1f) / w.heightScale;
        for (float z = 0; z < w.worldLength; z += TreeSpacing)
            for (float x = 0; x < w.worldWidth; x += TreeSpacing)
            {
                float u = (x + (float)rnd.NextDouble() * TreeSpacing) / w.worldWidth;
                float v = (z + (float)rnd.NextDouble() * TreeSpacing) / w.worldLength;
                if (u >= 1 || v >= 1) continue;
                int i = (Mathf.FloorToInt(v * tm) * tm + Mathf.FloorToInt(u * tm)) * 3;
                int best = 0; for (int k = 1; k < 3; k++) if (dens[i + k] > dens[i + best]) best = k;
                float p = dens[i + best] / 255f * TreeDensity;
                if (rnd.NextDouble() >= p) continue;
                if (td.GetInterpolatedHeight(u, v) / w.heightScale < minH) continue;
                if (td.GetSteepness(u, v) > 35f) continue;
                float s = 0.75f + (float)rnd.NextDouble() * 0.6f;
                list.Add(new TreeInstance
                {
                    position = new Vector3(u, 0, v),
                    prototypeIndex = best,
                    widthScale = s,
                    heightScale = s * (0.85f + (float)rnd.NextDouble() * 0.3f),
                    rotation = (float)rnd.NextDouble() * Mathf.PI * 2f,
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }
        td.SetTreeInstances(list.ToArray(), true);
    }

    static GameObject TreePrefab(string name, Mesh mesh, Material trunk, Material leaves)
    {
        ReplaceAsset(mesh, $"{GenDir}/Mesh_{name}.asset");
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = new[] { trunk, leaves };
        var col = go.AddComponent<CapsuleCollider>();
        col.radius = 0.4f; col.height = 4f; col.center = new Vector3(0, 2f, 0);
        string path = $"{GenDir}/Tree_{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        UnityEngine.Object.DestroyImmediate(go);
        return prefab;
    }

    // Flat-shaded low-poly meshes: submesh 0 = trunk, submesh 1 = foliage.
    class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<int>[] subs = { new List<int>(), new List<int>() };
        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c)
        {
            int i = verts.Count; verts.Add(a); verts.Add(b); verts.Add(c);
            subs[sub].Add(i); subs[sub].Add(i + 1); subs[sub].Add(i + 2);
        }
        public void Cone(int sub, float y0, float h, float r, int sides, float twist = 0f)
        {
            var apex = new Vector3(0, y0 + h, 0); var center = new Vector3(0, y0, 0);
            for (int s = 0; s < sides; s++)
            {
                Vector3 p0 = Ring(s, sides, r, y0, twist), p1 = Ring(s + 1, sides, r, y0, twist);
                Tri(sub, p0, apex, p1); Tri(sub, p1, center, p0);
            }
        }
        public void Cylinder(int sub, float y0, float h, float r, int sides)
        {
            for (int s = 0; s < sides; s++)
            {
                Vector3 a = Ring(s, sides, r, y0, 0), b = Ring(s + 1, sides, r, y0, 0);
                Vector3 c = Ring(s, sides, r * 0.8f, y0 + h, 0), d = Ring(s + 1, sides, r * 0.8f, y0 + h, 0);
                Tri(sub, a, c, b); Tri(sub, b, c, d);
            }
        }
        static Vector3 Ring(int s, int sides, float r, float y, float twist)
        {
            float a = (s % sides) / (float)sides * Mathf.PI * 2f + twist;
            return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }
        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(verts);
            m.subMeshCount = 2;
            m.SetTriangles(subs[0], 0); m.SetTriangles(subs[1], 1);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }

    static Mesh BuildConifer()
    {
        var b = new MeshBuilder();
        b.Cylinder(0, 0f, 1.6f, 0.35f, 5);
        b.Cone(1, 1.2f, 4.2f, 2.3f, 7);
        b.Cone(1, 3.0f, 3.6f, 1.7f, 7, 0.4f);
        b.Cone(1, 4.8f, 3.2f, 1.1f, 6, 0.2f);
        return b.Build("Conifer");
    }

    static Mesh BuildBroadleaf()
    {
        var b = new MeshBuilder();
        b.Cylinder(0, 0f, 3.2f, 0.4f, 5);
        // low-poly icosahedron crown
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var v = new[]
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        int[] f =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };
        var scale = new Vector3(1.45f, 1.25f, 1.45f); var off = new Vector3(0, 4.6f, 0);
        for (int i = 0; i < f.Length; i += 3)
            b.Tri(1, Vector3.Scale(v[f[i]], scale) + off, Vector3.Scale(v[f[i + 1]], scale) + off, Vector3.Scale(v[f[i + 2]], scale) + off);
        return b.Build("Broadleaf");
    }

    // ---------------- sea ----------------
    static void BuildSea(Transform root, World w)
    {
        var sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
        sea.name = "Sea";
        UnityEngine.Object.DestroyImmediate(sea.GetComponent<Collider>());
        sea.transform.SetParent(root, false);
        sea.transform.position = new Vector3(w.worldWidth / 2f, w.seaLevel, w.worldLength / 2f);
        sea.transform.localScale = new Vector3(w.worldWidth * 2f / 10f, 1, w.worldLength * 2f / 10f);
        // Start from a fresh material so settings from an earlier build don't linger.
        AssetDatabase.DeleteAsset(GenDir + "/Mat_Water.mat");
        matCache.Remove(GenDir + "/Mat_Water.mat");
        var mat = Mat("Water", new Color(0.16f, 0.45f, 0.6f, TransparentWater ? 0.78f : 1f), 0.85f);
        if (TransparentWater) MakeTransparent(mat);
        var mr = sea.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---------------- settlements & landmarks ----------------
    static Transform PlaceMarkers(Transform root, Terrain terrain, World w)
    {
        var parent = new GameObject("Settlements & Landmarks").transform;
        parent.SetParent(root, false);
        var rnd = new System.Random(Seed);
        Transform spawn = null;
        foreach (var m in w.markers)
        {
            var pos = new Vector3(m.u * w.worldWidth, 0, m.v * w.worldLength);
            pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;
            var go = new GameObject(m.name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var wm = go.AddComponent<WorldMarker>();
            wm.markerType = m.type; wm.dangerTier = m.dangerTier; wm.isStarter = m.isStarter;
            BuildPlaceholder(go.transform, m, rnd);
            if (m.isStarter)
            {
                spawn = new GameObject("PlayerSpawn").transform;
                spawn.SetParent(root, false);
                spawn.position = pos + new Vector3(0, 1f, -14f);
            }
        }
        return spawn;
    }

    static void BuildPlaceholder(Transform t, Marker m, System.Random rnd)
    {
        // Brighter, friendlier colours near the start; darker toward dangerous zones.
        float dark = (m.dangerTier - 1) / 4f;
        var wall = Mat($"Wall_T{m.dangerTier}", Color.Lerp(new Color(0.93f, 0.86f, 0.72f), new Color(0.22f, 0.2f, 0.22f), dark));
        var roof = Mat($"Roof_T{m.dangerTier}", Color.Lerp(new Color(0.85f, 0.3f, 0.2f), new Color(0.35f, 0.08f, 0.1f), dark));
        var stone = Mat("Stone", new Color(0.55f, 0.55f, 0.58f));
        switch (m.type)
        {
            case "village": Houses(t, 5, 14f, wall, roof, rnd); break;
            case "town": Houses(t, 9, 18f, wall, roof, rnd); break;
            case "city":
                Houses(t, 16, 28f, wall, roof, rnd);
                Box(t, "Keep", new Vector3(0, 0, 0), new Vector3(9, 16, 9), stone);
                break;
            case "castle":
            case "fortress":
                float s = m.type == "castle" ? 16f : 13f;
                for (int i = 0; i < 4; i++)
                {
                    var c = new Vector3((i % 2 == 0 ? -1 : 1) * s / 2, 0, (i < 2 ? -1 : 1) * s / 2);
                    Cyl(t, "Tower", c, 2.6f, 14f, stone);
                    Cyl(t, "TowerRoof", c + Vector3.up * 14f, 3.0f, 2f, roof);
                }
                Box(t, "WallN", new Vector3(0, 0, s / 2), new Vector3(s, 8, 1.5f), stone);
                Box(t, "WallS", new Vector3(0, 0, -s / 2), new Vector3(s, 8, 1.5f), stone);
                Box(t, "WallE", new Vector3(s / 2, 0, 0), new Vector3(1.5f, 8, s), stone);
                Box(t, "WallW", new Vector3(-s / 2, 0, 0), new Vector3(1.5f, 8, s), stone);
                Box(t, "Keep", Vector3.zero, new Vector3(6, 12, 6), stone);
                break;
            case "tower":
                Cyl(t, "Watchtower", Vector3.zero, 2.5f, 16f, stone);
                Cyl(t, "Roof", Vector3.up * 16f, 3f, 2.5f, roof);
                break;
            case "spire":
                var jade = Mat("ElvenJade", new Color(0.35f, 0.75f, 0.55f));
                Cyl(t, "Spire", Vector3.zero, 2f, 26f, Mat("ElvenWhite", new Color(0.92f, 0.95f, 0.95f)));
                Cyl(t, "SpireTop", Vector3.up * 26f, 2.4f, 4f, jade);
                Houses(t, 4, 12f, Mat("ElvenWhite", Color.white), jade, rnd);
                break;
            case "warcamp":
                var tent = Mat("WarTent", new Color(0.6f, 0.12f, 0.08f));
                for (int i = 0; i < 7; i++)
                {
                    float a = i / 7f * Mathf.PI * 2f, r = 6f + (float)rnd.NextDouble() * 6f;
                    var tr = Box(t, "Tent", new Vector3(Mathf.Cos(a) * r, -0.5f, Mathf.Sin(a) * r), new Vector3(4, 4, 4), tent);
                    tr.localRotation = Quaternion.Euler(45, (float)rnd.NextDouble() * 360f, 0);
                }
                Cyl(t, "Totem", Vector3.zero, 0.6f, 9f, Mat("WarPole", new Color(0.2f, 0.12f, 0.08f)));
                break;
        }
    }

    static void Houses(Transform t, int count, float radius, Material wall, Material roof, System.Random rnd)
    {
        for (int i = 0; i < count; i++)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = 3f + (float)rnd.NextDouble() * radius;
            var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            float yaw = (float)rnd.NextDouble() * 360f;
            var h = Box(t, "House", p, new Vector3(4, 3, 5), wall);
            h.localRotation = Quaternion.Euler(0, yaw, 0);
            var rf = Box(t, "Roof", p + Vector3.up * 2.4f, new Vector3(3.2f, 3.2f, 5.4f), roof);
            rf.localRotation = Quaternion.Euler(0, yaw, 45f);
        }
    }

    static Transform Box(Transform parent, string name, Vector3 basePos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = basePos + Vector3.up * size.y / 2f;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go.transform;
    }

    static Transform Cyl(Transform parent, string name, Vector3 basePos, float radius, float height, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = basePos + Vector3.up * height / 2f;
        go.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go.transform;
    }

    // ---------------- materials (Built-in, URP or HDRP) ----------------
    static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

    static Material Mat(string name, Color color, float smoothness = 0.05f)
    {
        string path = $"{GenDir}/Mat_{name}.mat";
        if (matCache.TryGetValue(path, out var cached) && cached != null) return cached;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(LitShader());
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = LitShader();
        if (mat.HasProperty("_Color")) mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        matCache[path] = mat;
        return mat;
    }

    static Shader LitShader()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        string name = rp == null ? "Standard"
            : rp.GetType().Name.Contains("HD") ? "HDRP/Lit" : "Universal Render Pipeline/Lit";
        return Shader.Find(name) ?? Shader.Find("Standard");
    }

    static void MakeTransparent(Material mat)
    {
        mat.SetOverrideTag("RenderType", "Transparent");
        if (mat.HasProperty("_Surface"))        // URP
        {
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        else if (mat.HasProperty("_Mode"))      // Built-in Standard
        {
            mat.SetFloat("_Mode", 3);
            mat.EnableKeyword("_ALPHABLEND_ON");
        }
        else return;                             // HDRP: leave opaque
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
    }

    // ---------------- asset helpers ----------------
    static void ReplaceAsset(UnityEngine.Object obj, string path)
    {
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(obj, path);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Thorvayne.WorldGen;

namespace Thorvayne.WorldGen.EditorTools
{
    // Window: Thorvayne > Random World Generator
    // Pick a seed (or roll a new one), press Generate. The same seed always rebuilds the same world.
    public class ThorvayneRandomWorldWindow : EditorWindow
    {
        const string PrefsKey = "Thorvayne.RandomWorld.Settings";
        WorldGenSettings settings;
        Vector2 scroll;

        [MenuItem("Thorvayne/Random World Generator")]
        static void Open() { GetWindow<ThorvayneRandomWorldWindow>("Thorvayne World").minSize = new Vector2(340, 420); }

        [MenuItem("Thorvayne/Generate Random World (new seed)")]
        static void QuickNewSeed()
        {
            var s = Load();
            s.seed = NewSeed();
            Save(s);
            ThorvayneRandomWorldBuilder.Build(s);
        }

        static int NewSeed() { return UnityEngine.Random.Range(1, 999999); }

        static WorldGenSettings Load()
        {
            var s = new WorldGenSettings();
            var json = EditorPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, s);
            return s;
        }

        static void Save(WorldGenSettings s) { EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(s)); }

        void OnEnable() { settings = Load(); }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Random 5 x 5 km world", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Same seed = same world. Roll a new seed for a new world, then press Generate.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            settings.seed = EditorGUILayout.IntField("Seed", settings.seed);
            if (GUILayout.Button("New seed", GUILayout.Width(80))) settings.seed = NewSeed();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Land", EditorStyles.boldLabel);
            settings.landAmount = EditorGUILayout.Slider("Land vs sea", settings.landAmount, 0.35f, 0.85f);
            settings.mountainAmount = EditorGUILayout.Slider("Mountains", settings.mountainAmount, 0f, 1f);
            settings.riverAmount = EditorGUILayout.Slider("Rivers", settings.riverAmount, 0f, 1f);
            settings.maxLakes = EditorGUILayout.IntSlider("Max lakes", settings.maxLakes, 0, 12);
            settings.forestAmount = EditorGUILayout.Slider("Forest density", settings.forestAmount, 0f, 2f);
            settings.erosion = EditorGUILayout.Toggle("Erosion (nicer valleys)", settings.erosion);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Places", EditorStyles.boldLabel);
            settings.cities = EditorGUILayout.IntSlider("Cities", settings.cities, 0, 6);
            settings.towns = EditorGUILayout.IntSlider("Towns", settings.towns, 0, 14);
            settings.villages = EditorGUILayout.IntSlider("Villages", settings.villages, 0, 24);
            settings.caves = EditorGUILayout.IntSlider("Cave entrances", settings.caves, 0, 40);

            EditorGUILayout.Space();
            if (GUILayout.Button("Reset to defaults")) { int seed = settings.seed; settings = new WorldGenSettings { seed = seed }; }
            GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
            if (GUILayout.Button("Generate world", GUILayout.Height(36)))
            {
                Save(settings);
                ThorvayneRandomWorldBuilder.Build(settings);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.HelpBox("Generating replaces the previous random world in the open scene (Ctrl+Z undoes it). " +
                "A map picture and a list of all places are saved in Assets/Thorvayne/Generated/RandomWorld. " +
                "A Test Player is placed at the starter town: press Play to walk around.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            if (GUI.changed) Save(settings);
        }
    }

    public static class ThorvayneRandomWorldBuilder
    {
        const string TexDir = "Assets/Thorvayne/MapData/Textures";
        const string GenDir = "Assets/Thorvayne/Generated/RandomWorld";
        const string RootName = "Thorvayne Random World";

        // Tweakables
        const float LayerTileSize = 10f;     // metres per ground texture repeat
        const float WaterMeshStep = 4f;      // metres between river/lake water mesh vertices
        static readonly bool TransparentWater = false; // opaque water always renders in URP; true = see-through

        public static void Build(WorldGenSettings s)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = WorldGenerator.Generate(s, (step, t) => Progress(step, t * 0.6f));
                var towns = TownPlanner.PlanAll(r);
                EnsureFolder(GenDir);
                matCache.Clear();

                var old = GameObject.Find(RootName);
                if (old != null) Undo.DestroyObjectImmediate(old);
                var root = new GameObject(RootName);

                Progress("Ground textures", 0.6f);
                var layers = BuildLayers();
                Progress("Tree models", 0.62f);
                var protos = BuildTreePrototypes();
                var house = HousePrefab();

                int tiles = s.tilesPerSide;
                float tileSize = s.worldSize / tiles;
                var terrains = new Terrain[tiles, tiles];
                var terrainParent = new GameObject("Terrain").transform;
                terrainParent.SetParent(root.transform, false);
                var treesByTile = SplitTrees(r, tiles, tileSize);
                for (int tz = 0; tz < tiles; tz++)
                    for (int tx = 0; tx < tiles; tx++)
                    {
                        Progress($"Terrain tile {tz * tiles + tx + 1} / {tiles * tiles}", 0.64f + 0.26f * (tz * tiles + tx) / (tiles * tiles));
                        terrains[tx, tz] = BuildTile(r, tx, tz, tileSize, layers, protos, treesByTile[tz * tiles + tx], towns, terrainParent);
                    }
                for (int tz = 0; tz < tiles; tz++)
                    for (int tx = 0; tx < tiles; tx++)
                        terrains[tx, tz].SetNeighbors(tx > 0 ? terrains[tx - 1, tz] : null, tz < tiles - 1 ? terrains[tx, tz + 1] : null,
                                                      tx < tiles - 1 ? terrains[tx + 1, tz] : null, tz > 0 ? terrains[tx, tz - 1] : null);

                Progress("Sea, rivers and lakes", 0.9f);
                BuildSea(root.transform, s);
                BuildRiversAndLakes(root.transform, r, tiles, tileSize);

                Progress("Towns, caves and bridges", 0.94f);
                var spawn = PlaceSettlements(root.transform, r, towns, house);
                PlaceCaves(root.transform, r);
                PlaceBridges(root.transform, r);

                Progress("Saving map and place list", 0.97f);
                SaveMapAndList(r);

                foreach (var t in terrains) t.Flush();
                AssetDatabase.SaveAssets();
                Undo.RegisterCreatedObjectUndo(root, "Generate Thorvayne World");
                EditorSceneManager.MarkSceneDirty(root.scene);
                GameObject player = spawn != null ? ThorvayneTestPlayerSetup.Setup(spawn.position, s.seaLevel) : null;
                Selection.activeGameObject = player != null ? player : root;
                if (SceneView.lastActiveSceneView != null && spawn != null)
                    SceneView.lastActiveSceneView.LookAt(spawn.position, Quaternion.Euler(35f, 20f, 0f), 260f);
                Debug.Log($"Thorvayne random world (seed {s.seed}) built in {sw.ElapsedMilliseconds / 1000f:0.0} s: " +
                          $"{r.settlements.Count} towns/cities, {r.caves.Count} caves, {r.bridges.Count} bridges, {r.trees.Count} trees.\n" +
                          string.Join("\n", r.log));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void Progress(string step, float t) { EditorUtility.DisplayProgressBar("Generating Thorvayne world", step, t); }

        // ---------------- terrain ----------------
        static Terrain BuildTile(WorldGenResult r, int tx, int tz, float tileSize, TerrainLayer[] layers, GameObject[] protos,
                                 List<TreePlacement> trees, List<TownPlan> towns, Transform parent)
        {
            var s = r.settings;
            int res = s.tileResolution, step = res - 1;
            var heights = new float[res, res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    heights[y, x] = Mathf.Clamp01(r.height[(tz * step + y) * r.N + tx * step + x] / s.heightScale);

            var td = new TerrainData();
            td.heightmapResolution = res;   // set before size
            td.size = new Vector3(tileSize, s.heightScale, tileSize);
            td.SetHeights(0, 0, heights);

            td.terrainLayers = layers;
            int ares = res - 1;
            td.alphamapResolution = ares;
            td.baseMapResolution = 512;
            int n = WorldGenResult.LayerCount;
            var maps = new float[ares, ares, n];
            float ox = tx * tileSize, oz = tz * tileSize, px = tileSize / ares;
            // the towns on this tile, for their streets, squares and door paths
            int pathLayer = Array.IndexOf(WorldGenResult.Layers, "path");
            var here = towns.Where(t => t.settlement.x + t.settlement.radius * 1.2f > ox && t.settlement.x - t.settlement.radius * 1.2f < ox + tileSize &&
                                        t.settlement.z + t.settlement.radius * 1.2f > oz && t.settlement.z - t.settlement.radius * 1.2f < oz + tileSize).ToList();
            Parallel.For(0, ares, y =>
            {
                var w = new float[n];
                for (int x = 0; x < ares; x++)
                {
                    float wx = ox + (x + 0.5f) * px, wz = oz + (y + 0.5f) * px;
                    r.SplatAt(wx, wz, w);
                    foreach (var town in here)
                    {
                        float p = town.PathWeight(wx, wz);
                        if (p <= 0) continue;
                        for (int l = 0; l < n; l++) w[l] *= 1 - p;
                        w[pathLayer] += p;
                    }
                    for (int l = 0; l < n; l++) maps[y, x, l] = w[l];
                }
            });
            td.SetAlphamaps(0, 0, maps);

            var tps = new TreePrototype[protos.Length];
            for (int i = 0; i < protos.Length; i++) tps[i] = new TreePrototype { prefab = protos[i] };
            td.treePrototypes = tps;
            var inst = new TreeInstance[trees.Count];
            for (int i = 0; i < trees.Count; i++)
            {
                var t = trees[i];
                float nx = (t.x - ox) / tileSize, nz = (t.z - oz) / tileSize;
                inst[i] = new TreeInstance
                {
                    position = new Vector3(nx, GroundedTreeHeight(td, nx, nz, t), nz),
                    prototypeIndex = (int)t.kind,
                    widthScale = t.scale,
                    heightScale = t.scale * (0.9f + 0.2f * Mathf.Repeat(t.rotation * 7.3f, 1f)),
                    rotation = t.rotation * Mathf.Deg2Rad,
                    color = Color.white,
                    lightmapColor = Color.white,
                };
            }
            td.SetTreeInstances(inst, false);   // heights come from GroundedTreeHeight
            ReplaceAsset(td, $"{GenDir}/Terrain_{tx}_{tz}.asset");

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = $"Tile {tx},{tz}";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(ox, 0, oz);
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 5;
            terrain.basemapDistance = 1000;
            terrain.treeDistance = 800;
            terrain.treeBillboardDistance = 800;   // placeholder trees have no billboards, so draw them as meshes
            terrain.drawInstanced = true;
            terrain.groupingID = 7001;
            terrain.allowAutoConnect = true;
            return terrain;
        }

        static List<TreePlacement>[] SplitTrees(WorldGenResult r, int tiles, float tileSize)
        {
            var lists = new List<TreePlacement>[tiles * tiles];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<TreePlacement>();
            foreach (var t in r.trees)
            {
                int tx = Mathf.Clamp((int)(t.x / tileSize), 0, tiles - 1), tz = Mathf.Clamp((int)(t.z / tileSize), 0, tiles - 1);
                lists[tz * tiles + tx].Add(t);
            }
            return lists;
        }

        static TerrainLayer[] BuildLayers()
        {
            var names = WorldGenResult.Layers;
            var layers = new TerrainLayer[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/tv_{names[i]}.png");
                if (tex == null) Debug.LogWarning($"Missing ground texture {TexDir}/tv_{names[i]}.png");
                var tl = new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(LayerTileSize, LayerTileSize), smoothness = 0f, metallic = 0f };
                ReplaceAsset(tl, $"{GenDir}/TL_{names[i]}.terrainlayer");
                layers[i] = tl;
            }
            return layers;
        }

        // ---------------- water ----------------
        static void BuildSea(Transform root, WorldGenSettings s)
        {
            var sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
            sea.name = "Sea";
            UnityEngine.Object.DestroyImmediate(sea.GetComponent<Collider>());
            sea.transform.SetParent(root, false);
            sea.transform.position = new Vector3(s.worldSize / 2f, s.seaLevel, s.worldSize / 2f);
            sea.transform.localScale = new Vector3(s.worldSize * 2f / 10f, 1, s.worldSize * 2f / 10f);
            var mr = sea.GetComponent<MeshRenderer>();
            mr.sharedMaterial = WaterMat("Sea", new Color(0.16f, 0.45f, 0.6f));
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // One mesh per terrain tile that follows the river/lake surface (it slopes downhill with the river).
        static void BuildRiversAndLakes(Transform root, WorldGenResult r, int tiles, float tileSize)
        {
            var parent = new GameObject("Rivers & Lakes").transform;
            parent.SetParent(root, false);
            var mat = WaterMat("River", new Color(0.2f, 0.5f, 0.62f));
            float sea = r.settings.seaLevel;
            int cells = Mathf.RoundToInt(tileSize / WaterMeshStep);
            for (int tz = 0; tz < tiles; tz++)
                for (int tx = 0; tx < tiles; tx++)
                {
                    float ox = tx * tileSize, oz = tz * tileSize;
                    int vn = cells + 1;
                    var level = new float[vn * vn];
                    var wet = new bool[vn * vn];
                    for (int j = 0; j < vn; j++)
                        for (int i = 0; i < vn; i++)
                        {
                            float x = ox + i * WaterMeshStep, z = oz + j * WaterMeshStep;
                            float wl = r.WaterLevelAt(x, z);
                            level[j * vn + i] = wl;
                            wet[j * vn + i] = !float.IsNaN(wl) && wl > sea + 0.05f && r.HeightAt(x, z) < wl;
                        }
                    var verts = new List<Vector3>();
                    var tris = new List<int>();
                    var map = new int[vn * vn];
                    for (int i = 0; i < map.Length; i++) map[i] = -1;
                    for (int j = 0; j < cells; j++)
                        for (int i = 0; i < cells; i++)
                        {
                            int a = j * vn + i, b = a + 1, c = a + vn, d = c + 1;
                            if (!(wet[a] || wet[b] || wet[c] || wet[d])) continue;
                            if (float.IsNaN(level[a]) || float.IsNaN(level[b]) || float.IsNaN(level[c]) || float.IsNaN(level[d])) continue;
                            int va = V(a, vn, ox, oz, level, map, verts), vb = V(b, vn, ox, oz, level, map, verts);
                            int vc = V(c, vn, ox, oz, level, map, verts), vd = V(d, vn, ox, oz, level, map, verts);
                            tris.Add(va); tris.Add(vc); tris.Add(vb);
                            tris.Add(vb); tris.Add(vc); tris.Add(vd);
                        }
                    if (tris.Count == 0) continue;
                    var mesh = new Mesh { name = $"Water_{tx}_{tz}", indexFormat = IndexFormat.UInt32 };
                    mesh.SetVertices(verts);
                    mesh.SetTriangles(tris, 0);
                    mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    ReplaceAsset(mesh, $"{GenDir}/Water_{tx}_{tz}.asset");
                    var go = new GameObject($"Water {tx},{tz}");
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                }
        }

        static int V(int k, int vn, float ox, float oz, float[] level, int[] map, List<Vector3> verts)
        {
            if (map[k] >= 0) return map[k];
            map[k] = verts.Count;
            verts.Add(new Vector3(ox + (k % vn) * WaterMeshStep, level[k], oz + (k / vn) * WaterMeshStep));
            return map[k];
        }

        static Material WaterMat(string name, Color c)
        {
            // Start from a fresh material so settings from an earlier build don't linger.
            string path = $"{GenDir}/Mat_{name}.mat";
            AssetDatabase.DeleteAsset(path);
            matCache.Remove(path);
            var mat = Mat(name, new Color(c.r, c.g, c.b, TransparentWater ? 0.78f : 1f), 0.85f);
            if (TransparentWater) MakeTransparent(mat);
            return mat;
        }

        // ---------------- settlements, caves, bridges ----------------
        static Transform PlaceSettlements(Transform root, WorldGenResult r, List<TownPlan> towns, GameObject house)
        {
            var parent = new GameObject("Towns & Cities").transform;
            parent.SetParent(root, false);
            Transform spawn = null;
            foreach (var plan in towns)
            {
                var st = plan.settlement;
                var go = new GameObject($"{st.name} ({st.type})");
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(st.x, r.HeightAt(st.x, st.z), st.z);
                var wm = go.AddComponent<WorldMarker>();
                wm.markerType = st.type; wm.dangerTier = st.dangerTier; wm.isStarter = st.isStarter;
                BuildSettlement(go.transform, plan, house);
                if (st.isStarter)
                {
                    spawn = new GameObject("PlayerSpawn").transform;
                    spawn.SetParent(root, false);
                    var p = new Vector3(st.x, 0, st.z - plan.plazaRadius * 0.6f);   // on the town square
                    p.y = r.HeightAt(p.x, p.z) + 1f;
                    spawn.position = p;
                }
            }
            return spawn;
        }

        static void BuildSettlement(Transform t, TownPlan plan, GameObject house)
        {
            var st = plan.settlement;
            // Brighter, friendlier colours near the start; darker toward dangerous zones.
            float dark = (st.dangerTier - 1) / 4f;
            var wall = Mat($"Wall_T{st.dangerTier}", Color.Lerp(new Color(0.93f, 0.86f, 0.72f), new Color(0.22f, 0.2f, 0.22f), dark));
            var roof = Mat($"Roof_T{st.dangerTier}", Color.Lerp(new Color(0.85f, 0.3f, 0.2f), new Color(0.35f, 0.08f, 0.1f), dark));
            var stone = Mat("Stone", new Color(0.55f, 0.55f, 0.58f));
            Houses(t, plan, house, wall, roof);
            switch (st.type)
            {
                case "town":
                    Cyl(t, "Well", Vector3.zero, 1.2f, 1.2f, stone);
                    break;
                case "city":
                    Box(t, "Keep", Vector3.zero, new Vector3(12, 20, 12), stone);
                    // ring wall with towers, open where the main streets leave town
                    float rw = st.radius * 0.88f;
                    int segs = 24;
                    for (int i = 0; i < segs; i++)
                    {
                        float a = (i + 0.5f) / segs * Mathf.PI * 2f;
                        if (NearGate(plan, a, 0.2f)) continue;
                        var w = Box(t, "Wall", new Vector3(Mathf.Cos(a) * rw, 0, Mathf.Sin(a) * rw), new Vector3(2f, 7f, 2f * Mathf.PI * rw / segs + 0.5f), stone);
                        w.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0);
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        float a = (i * 6 + 3 + 0.5f) / segs * Mathf.PI * 2f;
                        if (NearGate(plan, a, 0.3f)) continue;
                        var c = new Vector3(Mathf.Cos(a) * rw, 0, Mathf.Sin(a) * rw);
                        Cyl(t, "Tower", c, 3f, 13f, stone);
                        Cyl(t, "TowerRoof", c + Vector3.up * 13f, 3.4f, 2f, roof);
                    }
                    break;
            }
        }

        static bool NearGate(TownPlan plan, float a, float within)
        {
            foreach (var g in plan.gateAngles)
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, g * Mathf.Rad2Deg)) < within * Mathf.Rad2Deg) return true;
            return false;
        }

        // The houses TownPlanner laid out, each facing its street: the house model, or a plain box house without one.
        static void Houses(Transform t, TownPlan plan, GameObject house, Material wall, Material roof)
        {
            foreach (var h in plan.houses)
            {
                var pos = new Vector3(h.x, h.y, h.z);
                var rot = Quaternion.Euler(0, h.yaw, 0);
                if (house != null)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(house);
                    go.transform.SetParent(t, false);
                    go.transform.SetPositionAndRotation(pos, rot);
                    continue;
                }
                var b = Box(t, "House", Vector3.zero, new Vector3(TownPlanner.HouseLength - 1f, 3, TownPlanner.HouseDepth - 1f), wall);
                b.SetPositionAndRotation(pos + Vector3.up * 1.5f, rot);
                var rf = Box(t, "Roof", Vector3.zero, new Vector3(TownPlanner.HouseLength - 0.6f, 3.6f, 3.6f), roof);
                rf.SetPositionAndRotation(pos + Vector3.up * 3.4f, rot * Quaternion.Euler(45f, 0, 0));
            }
        }

        static void PlaceCaves(Transform root, WorldGenResult r)
        {
            var parent = new GameObject("Cave Entrances").transform;
            parent.SetParent(root, false);
            var rock = Mat("CaveRock", new Color(0.36f, 0.34f, 0.33f));
            var dark = Mat("CaveDark", new Color(0.02f, 0.02f, 0.03f), 0f);
            foreach (var c in r.caves)
            {
                var go = new GameObject($"{c.name} (cave)");
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(c.x, r.HeightAt(c.x, c.z), c.z);
                go.transform.rotation = Quaternion.Euler(0, c.yaw, 0);   // local +Z points out of the cave
                var wm = go.AddComponent<WorldMarker>();
                wm.markerType = "cave"; wm.dangerTier = c.dangerTier;
                // rough stone arch with a black opening pushed back into the cliff
                Box(go.transform, "PillarL", new Vector3(-3.2f, -1f, 0), new Vector3(1.8f, 7.5f, 2.2f), rock).localRotation = Quaternion.Euler(0, 0, 4);
                Box(go.transform, "PillarR", new Vector3(3.2f, -1f, 0), new Vector3(1.8f, 7.5f, 2.2f), rock).localRotation = Quaternion.Euler(0, 0, -5);
                Box(go.transform, "Lintel", new Vector3(0, 5.6f, 0), new Vector3(8.6f, 1.8f, 2.6f), rock).localRotation = Quaternion.Euler(0, 0, 3);
                Box(go.transform, "Opening", new Vector3(0, -1f, -4f), new Vector3(5f, 7f, 8f), dark);
                // trigger volume where a cave/dungeon scene could be loaded later
                var trig = new GameObject("EnterTrigger");
                trig.transform.SetParent(go.transform, false);
                trig.transform.localPosition = new Vector3(0, 2.5f, -1.5f);
                var bc = trig.AddComponent<BoxCollider>();
                bc.isTrigger = true; bc.size = new Vector3(4.5f, 5f, 3f);
            }
        }

        static void PlaceBridges(Transform root, WorldGenResult r)
        {
            var parent = new GameObject("Bridges").transform;
            parent.SetParent(root, false);
            var wood = Mat("BridgeWood", new Color(0.45f, 0.3f, 0.18f));
            foreach (var b in r.bridges)
            {
                var go = new GameObject("Bridge");
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(b.x, b.y, b.z);
                go.transform.rotation = Quaternion.Euler(0, b.yaw, 0);
                Box(go.transform, "Deck", new Vector3(0, -0.5f, 0), new Vector3(5f, 0.5f, b.length), wood);
                Box(go.transform, "RailL", new Vector3(-2.4f, 0, 0), new Vector3(0.25f, 1.1f, b.length), wood);
                Box(go.transform, "RailR", new Vector3(2.4f, 0, 0), new Vector3(0.25f, 1.1f, b.length), wood);
            }
        }

        static void SaveMapAndList(WorldGenResult r)
        {
            int size = 1024;
            var img = r.RenderMap(size);
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.LoadRawTextureData(img);   // row 0 = south = bottom of the picture
            tex.Apply();
            File.WriteAllBytes($"{GenDir}/WorldMap_seed{r.settings.seed}.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            var list = new PlaceList { seed = r.settings.seed, worldSize = r.settings.worldSize, seaLevel = r.settings.seaLevel };
            foreach (var st in r.settlements)
                list.places.Add(new PlaceEntry { name = st.name, type = st.type, x = st.x, y = st.y, z = st.z, dangerTier = st.dangerTier, isStarter = st.isStarter, biome = st.biome.ToString() });
            foreach (var c in r.caves)
                list.places.Add(new PlaceEntry { name = c.name, type = "cave", x = c.x, y = c.y, z = c.z, yaw = c.yaw, dangerTier = c.dangerTier, biome = c.biome.ToString() });
            File.WriteAllText($"{GenDir}/Places_seed{r.settings.seed}.json", JsonUtility.ToJson(list, true));
            AssetDatabase.Refresh();
        }

        // A tree's base is flat but the ground under it may slope: standing at the height of its centre, the downhill side
        // of the trunk and roots floats. Sink each tree by how much the ground drops across its base (wider for the
        // broadleaf model, whose roots spread about a metre each way), so every side meets the ground.
        static readonly float[] TreeBaseRadius = { 1.1f, 0.45f, 0.45f, 0.35f, 0.5f, 0.6f };   // TreeKind order

        static float GroundedTreeHeight(TerrainData td, float nx, float nz, TreePlacement t)
        {
            float ground = td.GetInterpolatedHeight(nx, nz);
            float slope = Mathf.Min(td.GetSteepness(nx, nz), 50f) * Mathf.Deg2Rad;
            int k = (int)t.kind;
            float radius = (k < TreeBaseRadius.Length ? TreeBaseRadius[k] : 0.5f) * t.scale;
            float sink = 0.05f + Mathf.Min(radius * Mathf.Tan(slope), 1.5f);
            return Mathf.Max(0f, ground - sink) / td.size.y;
        }

        [Serializable] class PlaceEntry { public string name, type, biome; public float x, y, z, yaw; public int dangerTier; public bool isStarter; }
        [Serializable] class PlaceList { public int seed; public float worldSize, seaLevel; public List<PlaceEntry> places = new List<PlaceEntry>(); }

        // ---------------- trees ----------------
        // Rebuilds the tree prefabs and points every terrain in the scene at them (Thorvayne > Check Trees uses this
        // to repair a world without generating it again). Returns false if a prefab still has no mesh.
        internal static bool RebuildTreePrefabs(Terrain[] terrains)
        {
            EnsureFolder(GenDir);
            matCache.Clear();
            var protos = BuildTreePrototypes();
            foreach (var t in terrains)
            {
                var td = t.terrainData;
                if (td == null || td.treePrototypes.Length != protos.Length) continue;
                var tps = new TreePrototype[protos.Length];
                for (int i = 0; i < protos.Length; i++) tps[i] = new TreePrototype { prefab = protos[i] };
                td.treePrototypes = tps;
                td.RefreshPrototypes();
                EditorUtility.SetDirty(td);
            }
            AssetDatabase.SaveAssets();
            return protos.All(p => p != null && p.GetComponentsInChildren<MeshFilter>(true).All(f => f.sharedMesh != null));
        }

        static GameObject[] BuildTreePrototypes()
        {
            var bark = Mat("Bark", new Color(0.38f, 0.26f, 0.16f));
            var palmBark = Mat("PalmBark", new Color(0.55f, 0.43f, 0.28f));
            // order must match the TreeKind enum
            var protos = new[]
            {
                ModelTree("Broadleaf", 7f) ?? TreePrefab("Broadleaf", BuildBroadleaf(1f, 1f), bark, Mat("Leaves", new Color(0.26f, 0.5f, 0.2f))),
                TreePrefab("Conifer", BuildConifer(), bark, Mat("ConiferLeaves", new Color(0.16f, 0.38f, 0.22f))),
                TreePrefab("SnowPine", BuildConifer(), bark, Mat("SnowPineLeaves", new Color(0.72f, 0.82f, 0.82f))),
                TreePrefab("Palm", BuildPalm(), palmBark, Mat("PalmLeaves", new Color(0.35f, 0.58f, 0.2f))),
                TreePrefab("Shrub", BuildShrub(), bark, Mat("ShrubLeaves", new Color(0.45f, 0.5f, 0.22f))),
                TreePrefab("SwampTree", BuildBroadleaf(1.3f, 0.7f), Mat("SwampBark", new Color(0.28f, 0.24f, 0.18f)), Mat("SwampLeaves", new Color(0.3f, 0.36f, 0.18f))),
            };
            AssetDatabase.SaveAssets();
            foreach (var p in protos)
                foreach (var f in p.GetComponentsInChildren<MeshFilter>(true))
                    if (f.sharedMesh == null) Debug.LogError($"Tree prefab {p.name}: {f.name} lost its mesh, so these trees won't show.");
            return protos;
        }

        // ---- tree models ----
        // A real tree model replaces a placeholder when Assets/Thorvayne/Trees/<name>.fbx exists, with child objects
        // LOD0 (the tree, textured with <name>_Albedo.png) and LOD1 (crossed cards using <name>_Impostor.png).
        // Extras/make_tree.py makes these from a high-poly .glb. Returns null when there is no model.
        const string TreeModelDir = "Assets/Thorvayne/Trees";

        static GameObject ModelTree(string name, float height)
        {
            string fbx = $"{TreeModelDir}/{name}.fbx";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(fbx) == null) return null;
            // read the model and its pictures fresh from disk: a model from one run of make_tree.py with the texture
            // from another (each run lays the texture out differently) paints the tree in scrambled patches
            foreach (var f in new[] { fbx, $"{TreeModelDir}/{name}_Albedo.png", $"{TreeModelDir}/{name}_Impostor.png" })
                if (File.Exists(f)) AssetDatabase.ImportAsset(f, ImportAssetOptions.ForceUpdate);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (importer != null && (importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importAnimation))
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = false;
                importer.SaveAndReimport();
                model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            }

            var go = UnityEngine.Object.Instantiate(model);
            go.name = name;
            go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
            var lod0 = go.transform.Find("LOD0"); var lod1 = go.transform.Find("LOD1");
            if (lod0 == null) { UnityEngine.Object.DestroyImmediate(go); Debug.LogWarning($"{fbx} has no LOD0 object"); return null; }
            // make it the intended height whatever unit scale the FBX came in with
            var r0 = lod0.GetComponent<MeshRenderer>();
            float h = r0.bounds.size.y;
            float k = h > 0.01f ? height / h : 1f;
            // Terrain trees don't reliably keep child transforms: trees redrawn after being changed at runtime
            // (felled and regrown) came back lying on their side.
            BakeParts(go, k, $"Tree{name}");

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TreeModelDir}/{name}_Albedo.png");
            var far = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TreeModelDir}/{name}_Impostor.png");
            r0.sharedMaterials = new[] { TreeMat($"Tree{name}", albedo, false) };
            var lods = new List<LOD> { new LOD(lod1 != null ? 0.12f : 0.01f, new Renderer[] { r0 }) };
            var r1 = lod1 != null ? lod1.GetComponent<MeshRenderer>() : null;
            if (r1 != null)
            {
                r1.sharedMaterials = new[] { TreeMat($"Tree{name}Far", far, true) };
                r1.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lods.Add(new LOD(0.01f, new Renderer[] { r1 }));
            }
            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(lods.ToArray());
            lod.RecalculateBounds();
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 0.5f; col.height = 4f; col.center = new Vector3(0, 2f, 0);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{GenDir}/Tree_{name}.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        // Bakes each part's position, rotation (FBX files often carry a -90 degree turn) and a scale into its mesh,
        // so every part sits at identity under the root. The meshes are saved as Mesh_<prefix>_<part>.asset.
        static void BakeParts(GameObject go, float k, string prefix)
        {
            foreach (Transform c in go.transform)
            {
                var mf = c.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var m = Matrix4x4.Scale(Vector3.one * k) * go.transform.worldToLocalMatrix * c.localToWorldMatrix;
                var mesh = UnityEngine.Object.Instantiate(mf.sharedMesh);
                var v = mesh.vertices; var n = mesh.normals;
                for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
                for (int i = 0; i < n.Length; i++) n[i] = m.MultiplyVector(n[i]).normalized;
                mesh.vertices = v; if (n.Length == v.Length) mesh.normals = n;
                mesh.RecalculateBounds();
                mf.sharedMesh = SaveMesh(mesh, $"{GenDir}/Mesh_{prefix}_{c.name}.asset");
                c.localPosition = Vector3.zero; c.localRotation = Quaternion.identity; c.localScale = Vector3.one;
            }
        }

        // ---- buildings ----
        // The town house: Assets/Thorvayne/Buildings/House.fbx (objects LOD0 and LOD1, textured with House_Albedo.png),
        // made by Extras/make_house.py, front facing +Z with the pivot at ground level. Null when there is no model.
        const string BuildingDir = "Assets/Thorvayne/Buildings";

        static GameObject HousePrefab()
        {
            string fbx = $"{BuildingDir}/House.fbx";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(fbx) == null) return null;
            foreach (var f in new[] { fbx, $"{BuildingDir}/House_Albedo.png" })
                if (File.Exists(f)) AssetDatabase.ImportAsset(f, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (importer != null && (importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importAnimation))
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = false;
                importer.SaveAndReimport();
            }
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            go.name = "House";
            go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
            var lod0 = go.transform.Find("LOD0"); var lod1 = go.transform.Find("LOD1");
            if (lod0 == null) { UnityEngine.Object.DestroyImmediate(go); Debug.LogWarning($"{fbx} has no LOD0 object"); return null; }
            // the intended size whatever unit scale the FBX came in with (the long side runs along X)
            var r0 = lod0.GetComponent<MeshRenderer>();
            float len = r0.bounds.size.x;
            BakeParts(go, len > 0.01f ? 10.5f / len : 1f, "House");

            var mat = TreeMat("House", AssetDatabase.LoadAssetAtPath<Texture2D>($"{BuildingDir}/House_Albedo.png"), false);
            var lods = new List<LOD>();
            foreach (var part in new[] { lod0, lod1 })
            {
                var rend = part != null ? part.GetComponent<MeshRenderer>() : null;
                if (rend == null) continue;
                rend.sharedMaterials = new[] { mat };
                lods.Add(new LOD(lods.Count == 0 ? (lod1 != null ? 0.2f : 0.015f) : 0.015f, new Renderer[] { rend }));
            }
            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(lods.ToArray());
            lod.RecalculateBounds();
            // solid walls: a box around the house below the roof
            var b = r0.bounds;
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(b.center.x, b.size.y * 0.2f, b.center.z);
            col.size = new Vector3(b.size.x * 0.9f, b.size.y * 0.4f, b.size.z * 0.85f);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{GenDir}/Building_House.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        // Textured, double-sided material for tree models; cutout = see-through where the picture is transparent.
        static Material TreeMat(string name, Texture2D tex, bool cutout)
        {
            var mat = Mat(name, Color.white, 0.02f);
            foreach (var prop in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })
                if (mat.HasProperty(prop)) mat.SetTexture(prop, tex);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0);                       // URP: render both sides
            if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1);   // HDRP
            if (cutout)
            {
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1);          // URP
                if (mat.HasProperty("_AlphaCutoffEnable")) mat.SetFloat("_AlphaCutoffEnable", 1);   // HDRP
                if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 1);                    // Built-in Standard
                if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
                if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = 2450;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static GameObject TreePrefab(string name, Mesh mesh, Material trunk, Material leaves)
        {
            mesh = SaveMesh(mesh, $"{GenDir}/Mesh_{name}.asset");
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { trunk, leaves };
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 0.4f; col.height = 4f; col.center = new Vector3(0, 2f, 0);
            // A single-level LODGroup tells the terrain to draw this as a plain mesh tree, so it doesn't
            // demand the Nature/Soft Occlusion shader (and warn about billboards) for these placeholder trees.
            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.01f, new Renderer[] { go.GetComponent<MeshRenderer>() }) });
            lod.RecalculateBounds();
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
            public void Cylinder(int sub, Vector3 from, Vector3 to, float r0, float r1, int sides)
            {
                for (int s = 0; s < sides; s++)
                {
                    Vector3 a = from + Ring(s, sides, r0, 0, 0), b = from + Ring(s + 1, sides, r0, 0, 0);
                    Vector3 c = to + Ring(s, sides, r1, 0, 0), d = to + Ring(s + 1, sides, r1, 0, 0);
                    Tri(sub, a, c, b); Tri(sub, b, c, d);
                }
            }
            public void Ico(int sub, Vector3 scale, Vector3 off)
            {
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
                for (int i = 0; i < f.Length; i += 3)
                    Tri(sub, Vector3.Scale(v[f[i]], scale) + off, Vector3.Scale(v[f[i + 1]], scale) + off, Vector3.Scale(v[f[i + 2]], scale) + off);
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
            b.Cylinder(0, Vector3.zero, new Vector3(0, 1.6f, 0), 0.35f, 0.28f, 5);
            b.Cone(1, 1.2f, 4.2f, 2.3f, 7);
            b.Cone(1, 3.0f, 3.6f, 1.7f, 7, 0.4f);
            b.Cone(1, 4.8f, 3.2f, 1.1f, 6, 0.2f);
            return b.Build("Conifer");
        }

        static Mesh BuildBroadleaf(float width, float height)
        {
            var b = new MeshBuilder();
            b.Cylinder(0, Vector3.zero, new Vector3(0, 3.2f * height, 0), 0.4f, 0.32f, 5);
            b.Ico(1, new Vector3(1.45f * width, 1.25f * height, 1.45f * width), new Vector3(0, 3.2f * height + 1.4f, 0));
            return b.Build("Broadleaf");
        }

        static Mesh BuildShrub()
        {
            var b = new MeshBuilder();
            b.Cylinder(0, Vector3.zero, new Vector3(0, 0.5f, 0), 0.2f, 0.15f, 4);
            b.Ico(1, new Vector3(0.9f, 0.6f, 0.9f), new Vector3(0, 1.0f, 0));
            return b.Build("Shrub");
        }

        static Mesh BuildPalm()
        {
            var b = new MeshBuilder();
            // gently leaning trunk in three segments
            var p0 = Vector3.zero; var p1 = new Vector3(0.3f, 2.5f, 0); var p2 = new Vector3(0.8f, 5f, 0); var top = new Vector3(1.4f, 7f, 0);
            b.Cylinder(0, p0, p1, 0.3f, 0.26f, 5);
            b.Cylinder(0, p1, p2, 0.26f, 0.22f, 5);
            b.Cylinder(0, p2, top, 0.22f, 0.2f, 5);
            // drooping fronds
            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var side = new Vector3(-dir.z, 0, dir.x) * 0.55f;
                var mid = top + dir * 1.8f + Vector3.up * 0.4f;
                var tip = top + dir * 3.4f - Vector3.up * 1.0f;
                b.Tri(1, top, mid + side, mid - side);
                b.Tri(1, top, mid - side, mid + side);
                b.Tri(1, mid + side, tip, mid - side);
                b.Tri(1, mid - side, tip, mid + side);
            }
            return b.Build("Palm");
        }

        // ---------------- primitives ----------------
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
        // Saves a tree mesh and returns the saved asset. A mesh already saved there is overwritten in place (same file,
        // same id), so prefabs keep pointing at it. Deleting and re-creating it left every tree prefab with a missing
        // mesh, so the terrains had all their trees but drew none of them.
        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                ReplaceAsset(mesh, path);
                return mesh;
            }
            // copy the mesh data over explicitly (vertices, normals, UVs, triangles), so nothing from the old mesh stays behind
            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.vertices = mesh.vertices;
            existing.normals = mesh.normals;
            existing.tangents = mesh.tangents;
            existing.uv = mesh.uv;
            existing.colors = mesh.colors;
            existing.subMeshCount = mesh.subMeshCount;
            for (int i = 0; i < mesh.subMeshCount; i++) existing.SetTriangles(mesh.GetTriangles(i), i);
            existing.RecalculateBounds();
            existing.name = Path.GetFileNameWithoutExtension(path);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }

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
}

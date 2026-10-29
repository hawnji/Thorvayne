// Thorvayne random world generator (core).
// Plain C# with no UnityEngine code, so the same generator runs inside the editor and in offline tools.
// ThorvayneRandomWorldBuilder turns the result into terrain tiles, water, trees and placeholder objects.
//
// Pipeline: continent shape -> mountains, hills, cliffs and mesas -> droplet erosion -> rivers and lakes
// -> climate and biomes -> towns and cities -> cave entrances -> roads and bridges -> trees.
// Everything is driven by WorldGenSettings.seed, so the same seed always gives the same world.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thorvayne.WorldGen
{
    public enum Biome : byte { Ocean, Beach, Grassland, Forest, PineForest, Snow, Desert, Savanna, Swamp, Mountain, SnowPeak }
    public enum TreeKind : byte { Broadleaf, Conifer, SnowPine, Palm, Shrub, SwampTree }

    [Serializable]
    public class WorldGenSettings
    {
        public int seed = 1234;
        public float worldSize = 5000f;      // metres per side
        public float heightScale = 600f;     // tallest possible point (Unity terrain height)
        public float seaLevel = 40f;
        public float landAmount = 0.6f;      // share of the map that is land (0.3..0.85)
        public float mountainAmount = 0.5f;  // 0 = few mountains, 1 = lots
        public float riverAmount = 0.5f;     // 0 = few big rivers, 1 = many streams
        public float forestAmount = 1f;      // multiplier on tree density
        public int cities = 3, towns = 6, villages = 10, caves = 18;
        public int maxLakes = 5;
        public float treeSpacing = 7f;       // metres between tree candidates
        public bool erosion = true;
        public int tilesPerSide = 5;         // terrain tiles per side (5 x 5 tiles of 1 km)
        public int tileResolution = 513;     // heightmap samples per tile side (2^n + 1)
    }

    public class Settlement
    {
        public string name, type;            // type: city, town, village
        public float x, z, y, radius;
        public int dangerTier = 1;
        public bool isStarter, coastal, riverside;
        public Biome biome;
    }

    public class Cave
    {
        public string name;
        public float x, z, y, yaw;           // yaw in degrees, 0 = mouth faces +z (north), 90 = faces +x (east)
        public int dangerTier = 1;
        public Biome biome;
    }

    public class Bridge { public float x, z, y, length, yaw; }

    public struct TreePlacement { public float x, z, scale, rotation; public TreeKind kind; }

    public class WorldGenResult
    {
        public WorldGenSettings settings;
        public int N;                        // full-res samples per side
        public float cell;                   // metres per full-res sample
        public float[] height;               // N*N metres, index = z*N + x, row 0 = south
        public byte[] road;                  // N*N road strength 0..255
        public int M;                        // analysis grid samples per side (every 4th full-res sample)
        public float aCell;
        public float[] waterLevel;           // M*M, NaN where there is no river/lake water
        public bool[] ocean, river, lake;    // M*M
        public byte[] biome;                 // M*M (Biome)
        public float[] layerWeights;         // M*M*LayerCount, smooth ground-texture mix per biome
        public List<Settlement> settlements = new List<Settlement>();
        public List<Cave> caves = new List<Cave>();
        public List<Bridge> bridges = new List<Bridge>();
        public List<float[]> roads = new List<float[]>();   // polylines, x0,z0,x1,z1,...
        public List<TreePlacement> trees = new List<TreePlacement>();
        public List<string> log = new List<string>();

        public static readonly string[] Layers = { "grass", "meadow", "dryland", "sand", "snow", "cliff", "darkrock", "dirt", "path" };
        public const int LayerCount = 9;
        const int Grass = 0, Meadow = 1, Dry = 2, Sand = 3, Snow = 4, CliffL = 5, DarkRock = 6, Dirt = 7, PathL = 8;

        public float HeightAt(float x, float z)
        {
            float fx = Clamp(x / cell, 0, N - 1.001f), fz = Clamp(z / cell, 0, N - 1.001f);
            int x0 = (int)fx, z0 = (int)fz; float tx = fx - x0, tz = fz - z0;
            int i = z0 * N + x0;
            return Lerp(Lerp(height[i], height[i + 1], tx), Lerp(height[i + N], height[i + N + 1], tx), tz);
        }

        // Slope in degrees at a world position.
        public float SlopeAt(float x, float z)
        {
            float d = cell;
            float gx = (HeightAt(x + d, z) - HeightAt(x - d, z)) / (2 * d);
            float gz = (HeightAt(x, z + d) - HeightAt(x, z - d)) / (2 * d);
            return (float)(Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI);
        }

        // River/lake water surface at a world position, or NaN. Sea is handled by a flat sea plane at seaLevel.
        public float WaterLevelAt(float x, float z)
        {
            float fx = Clamp(x / aCell, 0, M - 1.001f), fz = Clamp(z / aCell, 0, M - 1.001f);
            int x0 = (int)fx, z0 = (int)fz; float tx = fx - x0, tz = fz - z0;
            int i = z0 * M + x0;
            float sum = 0, wsum = 0;
            Acc(waterLevel[i], (1 - tx) * (1 - tz), ref sum, ref wsum);
            Acc(waterLevel[i + 1], tx * (1 - tz), ref sum, ref wsum);
            Acc(waterLevel[i + M], (1 - tx) * tz, ref sum, ref wsum);
            Acc(waterLevel[i + M + 1], tx * tz, ref sum, ref wsum);
            return wsum > 0.0001f ? sum / wsum : float.NaN;
        }
        static void Acc(float v, float w, ref float sum, ref float wsum) { if (!float.IsNaN(v)) { sum += v * w; wsum += w; } }

        public bool IsWet(float x, float z)
        {
            float h = HeightAt(x, z);
            if (h < settings.seaLevel) return true;
            float w = WaterLevelAt(x, z);
            return !float.IsNaN(w) && h < w;
        }

        public Biome BiomeAt(float x, float z)
        {
            int i = (int)Clamp(x / aCell + 0.5f, 0, M - 1), j = (int)Clamp(z / aCell + 0.5f, 0, M - 1);
            return (Biome)biome[j * M + i];
        }

        public float RoadAt(float x, float z)
        {
            int i = (int)Clamp(x / cell + 0.5f, 0, N - 1), j = (int)Clamp(z / cell + 0.5f, 0, N - 1);
            return road[j * N + i] / 255f;
        }

        // Ground texture weights (LayerCount values, summing to 1) at a world position.
        public void SplatAt(float x, float z, float[] w)
        {
            float fx = Clamp(x / aCell, 0, M - 1.001f), fz = Clamp(z / aCell, 0, M - 1.001f);
            int x0 = (int)fx, z0 = (int)fz; float tx = fx - x0, tz = fz - z0;
            int i = (z0 * M + x0) * LayerCount, iM = M * LayerCount;
            for (int l = 0; l < LayerCount; l++)
                w[l] = Lerp(Lerp(layerWeights[i + l], layerWeights[i + LayerCount + l], tx),
                            Lerp(layerWeights[i + iM + l], layerWeights[i + iM + LayerCount + l], tx), tz);

            float h = HeightAt(x, z), sea = settings.seaLevel;
            float slope = SlopeAt(x, z);
            float wl = WaterLevelAt(x, z);
            // river and lake beds and banks
            if (!float.IsNaN(wl) && h < wl + 1.2f)
            {
                float t = Smooth(wl + 1.2f, wl - 0.5f, h);
                Blend(w, Dirt, t * 0.7f); Blend(w, Sand, t * 0.4f);
            }
            if (h < sea + 0.6f) Blend(w, Sand, Smooth(sea + 0.6f, sea - 1f, h));
            // steep ground shows rock
            float cl = Smooth(30f, 42f, slope);
            if (cl > 0) Blend(w, h > sea + 360 ? DarkRock : CliffL, cl);
            float r = RoadAt(x, z);
            if (r > 0) Blend(w, PathL, r);
            float s = 0; for (int l = 0; l < LayerCount; l++) s += w[l];
            if (s <= 0) { w[Grass] = 1; return; }
            for (int l = 0; l < LayerCount; l++) w[l] /= s;
        }

        static void Blend(float[] w, int layer, float t)
        {
            if (t <= 0) return;
            if (t > 1) t = 1;
            for (int l = 0; l < LayerCount; l++) w[l] *= 1 - t;
            w[layer] += t;
        }

        // Top-down colour map (RGB, row 0 = south) for previews and an in-editor minimap.
        public byte[] RenderMap(int size)
        {
            var img = new byte[size * size * 3];
            float sea = settings.seaLevel, ws = settings.worldSize;
            Parallel.For(0, size, py =>
            {
                for (int px = 0; px < size; px++)
                {
                    float x = (px + 0.5f) / size * ws, z = (py + 0.5f) / size * ws;
                    float h = HeightAt(x, z);
                    float d = ws / size;
                    float gx = (HeightAt(x + d, z) - HeightAt(x - d, z)) / (2 * d);
                    float gz = (HeightAt(x, z + d) - HeightAt(x, z - d)) / (2 * d);
                    // light from the north-west
                    float shade = Clamp(1f + (-gx * 0.7f + gz * 0.7f) * 1.6f, 0.45f, 1.35f);
                    float r, g, b;
                    float wl = WaterLevelAt(x, z);
                    if (h < sea)
                    {
                        float depth = Clamp((sea - h) / 40f, 0, 1);
                        r = Lerp(0.35f, 0.10f, depth); g = Lerp(0.62f, 0.27f, depth); b = Lerp(0.72f, 0.48f, depth);
                    }
                    else if (!float.IsNaN(wl) && h < wl)
                    {
                        r = 0.22f; g = 0.50f; b = 0.68f;
                    }
                    else
                    {
                        BiomeColorAt(x, z, out r, out g, out b);
                        float slope = (float)(Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI);
                        float c = Smooth(30f, 42f, slope);
                        r = Lerp(r, 0.50f, c); g = Lerp(g, 0.47f, c); b = Lerp(b, 0.44f, c);
                        float rd = RoadAt(x, z);
                        if (rd > 0.2f) { r = Lerp(r, 0.78f, rd); g = Lerp(g, 0.66f, rd); b = Lerp(b, 0.45f, rd); }
                        r *= shade; g *= shade; b *= shade;
                    }
                    int o = (py * size + px) * 3;
                    img[o] = (byte)Clamp(r * 255, 0, 255); img[o + 1] = (byte)Clamp(g * 255, 0, 255); img[o + 2] = (byte)Clamp(b * 255, 0, 255);
                }
            });
            return img;
        }

        // Biome colour blended between neighbouring analysis cells, so borders aren't blocky.
        void BiomeColorAt(float x, float z, out float r, out float g, out float b)
        {
            float fx = Clamp(x / aCell, 0, M - 1.001f), fz = Clamp(z / aCell, 0, M - 1.001f);
            int x0 = (int)fx, z0 = (int)fz; float tx = fx - x0, tz = fz - z0;
            r = g = b = 0; float wsum = 0;
            for (int c = 0; c < 4; c++)
            {
                int i = (z0 + (c >> 1)) * M + x0 + (c & 1);
                float w = ((c & 1) == 1 ? tx : 1 - tx) * ((c >> 1) == 1 ? tz : 1 - tz);
                if ((Biome)biome[i] == Biome.Ocean) w *= 0.05f;
                float cr, cg, cb; BiomeColor((Biome)biome[i], out cr, out cg, out cb);
                r += cr * w; g += cg * w; b += cb * w; wsum += w;
            }
            if (wsum > 0) { r /= wsum; g /= wsum; b /= wsum; }
        }

        static void BiomeColor(Biome bm, out float r, out float g, out float b)
        {
            switch (bm)
            {
                case Biome.Beach: r = 0.86f; g = 0.80f; b = 0.58f; break;
                case Biome.Grassland: r = 0.55f; g = 0.72f; b = 0.36f; break;
                case Biome.Forest: r = 0.30f; g = 0.55f; b = 0.25f; break;
                case Biome.PineForest: r = 0.22f; g = 0.42f; b = 0.30f; break;
                case Biome.Snow: r = 0.88f; g = 0.91f; b = 0.93f; break;
                case Biome.Desert: r = 0.89f; g = 0.77f; b = 0.50f; break;
                case Biome.Savanna: r = 0.74f; g = 0.71f; b = 0.40f; break;
                case Biome.Swamp: r = 0.35f; g = 0.44f; b = 0.28f; break;
                case Biome.Mountain: r = 0.52f; g = 0.50f; b = 0.47f; break;
                case Biome.SnowPeak: r = 0.95f; g = 0.96f; b = 0.98f; break;
                default: r = 0.3f; g = 0.5f; b = 0.7f; break;
            }
        }

        internal static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
        internal static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        internal static float Smooth(float e0, float e1, float x)
        {
            float t = Clamp((x - e0) / (e1 - e0), 0, 1);
            return t * t * (3 - 2 * t);
        }
    }

    // Small deterministic RNG (same results in the Unity editor and offline tools).
    public class Rng
    {
        ulong s;
        public Rng(int seed, int stream = 0) { s = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL ^ ((ulong)(uint)stream << 32) ^ 0xD1B54A32D192ED03UL; Next(); Next(); }
        public ulong Next()
        {
            ulong z = (s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        public float Float() { return (Next() >> 40) / (float)(1UL << 24); }
        public float Range(float a, float b) { return a + (b - a) * Float(); }
        public int Int(int max) { return (int)(Next() % (ulong)max); }
    }

    public class Noise
    {
        readonly int[] p = new int[512];
        readonly float ox, oy;
        public Noise(int seed, int stream)
        {
            var r = new Rng(seed, stream);
            var q = new int[256];
            for (int i = 0; i < 256; i++) q[i] = i;
            for (int i = 255; i > 0; i--) { int j = r.Int(i + 1); int t = q[i]; q[i] = q[j]; q[j] = t; }
            for (int i = 0; i < 512; i++) p[i] = q[i & 255];
            ox = r.Range(0, 200); oy = r.Range(0, 200);
        }
        static float Fade(float t) { return t * t * t * (t * (t * 6 - 15) + 10); }
        static float Grad(int h, float x, float y)
        {
            switch (h & 7)
            {
                case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
                case 4: return x; case 5: return -x; case 6: return y; default: return -y;
            }
        }
        // Perlin noise, roughly -1..1
        public float Get(float x, float y)
        {
            x += ox; y += oy;
            int xf = (int)Math.Floor(x), yf = (int)Math.Floor(y);
            float tx = x - xf, ty = y - yf;
            int X = xf & 255, Y = yf & 255;
            float u = Fade(tx), v = Fade(ty);
            int a = p[X] + Y, b = p[X + 1] + Y;
            float n00 = Grad(p[a], tx, ty), n10 = Grad(p[b], tx - 1, ty);
            float n01 = Grad(p[a + 1], tx, ty - 1), n11 = Grad(p[b + 1], tx - 1, ty - 1);
            float nx0 = n00 + (n10 - n00) * u, nx1 = n01 + (n11 - n01) * u;
            return (nx0 + (nx1 - nx0) * v) * 0.9f;
        }
        public float Fbm(float x, float y, int octaves, float gain = 0.5f)
        {
            float sum = 0, amp = 1, norm = 0, f = 1;
            for (int o = 0; o < octaves; o++) { sum += Get(x * f, y * f) * amp; norm += amp; amp *= gain; f *= 2.03f; }
            return sum / norm;
        }
        // Sharp ridges 0..1, for mountain chains.
        public float Ridged(float x, float y, int octaves)
        {
            float sum = 0, amp = 1, norm = 0, f = 1, w = 1;
            for (int o = 0; o < octaves; o++)
            {
                float n = 1 - Math.Abs(Get(x * f, y * f));
                n *= n; n *= w;
                w = WorldGenResult.Clamp(n * 1.6f, 0, 1);
                sum += n * amp; norm += amp; amp *= 0.5f; f *= 2.1f;
            }
            return sum / norm;
        }
    }

    public static class WorldGenerator
    {
        public static WorldGenResult Generate(WorldGenSettings s, Action<string, float> progress = null)
        {
            return new Gen(s, progress).Run();
        }
    }

    class Gen
    {
        readonly WorldGenSettings S;
        readonly Action<string, float> progress;
        readonly WorldGenResult R = new WorldGenResult();
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        int N, M; float cell, aCell, sea;
        float[] H;                    // full res heights (m)
        float[] HA, F;                // analysis grid: heights, depression-filled heights
        float[] signedCoast;          // analysis grid: + inland distance to sea, - distance offshore
        float[] mountainMask, mesaMask, cliffMask;
        bool[] ocean, river, lake, mainland, water;
        int[] receiver; float[] acc;
        float[] distWater, distOcean, temperature, moisture;
        const int Step = 4;           // full-res samples per analysis cell
        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };
        static readonly float[] DL = { 1, 1, 1, 1, 1.41421f, 1.41421f, 1.41421f, 1.41421f };

        public Gen(WorldGenSettings s, Action<string, float> p) { S = s; progress = p; }

        void Report(string step, float t)
        {
            if (sw.IsRunning) R.log.Add(string.Format("{0,-28} {1,6} ms", lastStep, sw.ElapsedMilliseconds));
            lastStep = step; sw.Reset(); sw.Start();
            if (progress != null) progress(step, t);
        }
        string lastStep = "";

        public WorldGenResult Run()
        {
            N = S.tilesPerSide * (S.tileResolution - 1) + 1;
            cell = S.worldSize / (N - 1);
            M = (N - 1) / Step + 1;
            aCell = cell * Step;
            sea = S.seaLevel;
            R.settings = S; R.N = N; R.cell = cell; R.M = M; R.aCell = aCell;

            Report("Continents", 0.02f); Continents();
            Report("Mountains and hills", 0.08f); Heights(); R.height = H;
            if (S.erosion) { Report("Erosion", 0.2f); Erode(); }
            Report("Rivers and lakes", 0.4f); Hydrology();
            Report("Climate and biomes", 0.5f); Biomes();
            Report("Towns and cities", 0.58f); Settlements();
            Report("Carving rivers", 0.64f); CarveRivers();
            Report("Caves", 0.7f); Caves();
            Report("Roads and bridges", 0.76f); Roads();
            Report("Trees", 0.88f); Trees();
            Report("Done", 1f);
            return R;
        }

        // ---------------------------------------------------------------- shape
        void Continents()
        {
            var warpA = new Noise(S.seed, 1); var warpB = new Noise(S.seed, 2); var cont = new Noise(S.seed, 3);
            var raw = new float[M * M];
            for (int j = 0; j < M; j++)
                for (int i = 0; i < M; i++)
                {
                    float u = i / (M - 1f), v = j / (M - 1f);
                    float wx = u + 0.22f * warpA.Fbm(u * 2.2f, v * 2.2f, 4);
                    float wy = v + 0.22f * warpB.Fbm(u * 2.2f + 7, v * 2.2f + 3, 4);
                    float c = cont.Fbm(wx * 2.4f, wy * 2.4f, 6);
                    float edge = Math.Min(Math.Min(u, 1 - u), Math.Min(v, 1 - v));
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float radial = (float)Math.Sqrt(dx * dx + dy * dy) / 0.7071f;
                    raw[j * M + i] = c + 0.45f * WorldGenResult.Smooth(0f, 0.3f, edge) - 0.5f * radial * radial;
                }
            float t = Percentile(raw, null, 1 - WorldGenResult.Clamp(S.landAmount, 0.2f, 0.9f));
            var land = new bool[M * M];
            for (int j = 0; j < M; j++)
                for (int i = 0; i < M; i++)
                {
                    float edge = Math.Min(Math.Min(i, M - 1 - i), Math.Min(j, M - 1 - j)) * aCell;
                    land[j * M + i] = raw[j * M + i] > t && edge > 180f;
                }
            RemoveSmall(land, true, 60);    // specks of land
            RemoveSmall(land, false, 30);   // pin-prick ponds (bigger enclosed water becomes lakes later)

            var toSea = Chamfer(Not(land));
            var toLand = Chamfer(land);
            signedCoast = new float[M * M];
            for (int k = 0; k < M * M; k++) signedCoast[k] = land[k] ? toSea[k] - aCell * 0.5f : -(toLand[k] - aCell * 0.5f);
            Blur(signedCoast, 2);

            // mountain ranges cover a share of the land set by mountainAmount
            var mn = new Noise(S.seed, 4); var mesaN = new Noise(S.seed, 5); var clN = new Noise(S.seed, 6);
            var mraw = new float[M * M]; var mesaRaw = new float[M * M]; var clRaw = new float[M * M];
            for (int j = 0; j < M; j++)
                for (int i = 0; i < M; i++)
                {
                    float u = i / (M - 1f), v = j / (M - 1f);
                    int k = j * M + i;
                    mraw[k] = mn.Fbm(u * 3.0f, v * 3.0f, 4);
                    mesaRaw[k] = mesaN.Fbm(u * 4f, v * 4f, 3);
                    clRaw[k] = clN.Fbm(u * 6f, v * 6f, 3);
                }
            float mt = Percentile(mraw, land, 1 - (0.08f + 0.3f * WorldGenResult.Clamp(S.mountainAmount, 0, 1)));
            float mesaT = Percentile(mesaRaw, land, 0.88f);
            float clT = Percentile(clRaw, land, 0.7f);
            mountainMask = new float[M * M]; mesaMask = new float[M * M]; cliffMask = new float[M * M];
            for (int k = 0; k < M * M; k++)
            {
                float sc = signedCoast[k];
                mountainMask[k] = WorldGenResult.Smooth(mt - 0.03f, mt + 0.09f, mraw[k]) * WorldGenResult.Smooth(150f, 700f, sc);
                mesaMask[k] = WorldGenResult.Smooth(mesaT - 0.02f, mesaT + 0.04f, mesaRaw[k]) * WorldGenResult.Smooth(100f, 400f, sc) * (1 - mountainMask[k]);
                cliffMask[k] = WorldGenResult.Smooth(clT, clT + 0.06f, clRaw[k]);
            }
            Blur(mountainMask, 2); Blur(mesaMask, 1); Blur(cliffMask, 2);
        }

        void Heights()
        {
            H = new float[N * N];
            var hills = new Noise(S.seed, 10); var low = new Noise(S.seed, 11); var ridge = new Noise(S.seed, 12);
            var detail = new Noise(S.seed, 13); var sea2 = new Noise(S.seed, 14);
            float hs = S.heightScale;
            Parallel.For(0, N, z =>
            {
                for (int x = 0; x < N; x++)
                {
                    float u = x / (N - 1f), v = z / (N - 1f);
                    float ax = x / (float)Step, az = z / (float)Step;
                    float det = detail.Fbm(u * 60f, v * 60f, 3);
                    float sc = SampleA(signedCoast, ax, az) + 30f * det;
                    float h;
                    if (sc > 0)
                    {
                        float inland = WorldGenResult.Clamp(sc / 1600f, 0, 1);
                        float baseH = 1.2f * WorldGenResult.Smooth(0, 50, sc) + sc * 0.004f
                                    + 55f * WorldGenResult.Smooth(0, 1, inland) * (0.6f + 0.5f * low.Fbm(u * 3f, v * 3f, 2));
                        float hill = 38f * hills.Fbm(u * 14f, v * 14f, 5) * WorldGenResult.Smooth(30f, 400f, sc);
                        float mm = SampleA(mountainMask, ax, az);
                        float mount = 0;
                        if (mm > 0.001f)
                        {
                            float rg = ridge.Ridged(u * 7f, v * 7f, 6);
                            mount = mm * (60f + 430f * (float)Math.Pow(rg, 1.5f));
                        }
                        h = baseH + Math.Max(hill, -baseH * 0.6f) + mount;
                        float cm = SampleA(cliffMask, ax, az);
                        if (cm > 0.001f) h += cm * 24f * WorldGenResult.Smooth(4f, 26f, sc);
                        float me = SampleA(mesaMask, ax, az);
                        if (me > 0.001f) h = WorldGenResult.Lerp(h, Terrace(h + 30f * me, 22f), me);
                        h += 1.5f * det;
                    }
                    else
                    {
                        float off = -sc;
                        h = -(1.0f * WorldGenResult.Smooth(0, 25, off) + 32f * WorldGenResult.Smooth(0, 600, off) + 4f * sea2.Fbm(u * 10f, v * 10f, 3) * WorldGenResult.Smooth(0, 200, off));
                    }
                    H[z * N + x] = WorldGenResult.Clamp(sea + h, 2f, hs - 2f);
                }
            });
        }

        static float Terrace(float h, float stepH)
        {
            float k = h / stepH, f = (float)Math.Floor(k), t = k - f;
            t = WorldGenResult.Smooth(0.75f, 1f, t);          // flat steps with a steep riser
            return (f + t) * stepH;
        }

        // ---------------------------------------------------------------- erosion (droplets)
        void Erode()
        {
            const int radius = 3;
            // brush offsets/weights
            var bw = new List<float>(); var bx = new List<int>(); var bz = new List<int>();
            float wsum = 0;
            for (int dz = -radius; dz <= radius; dz++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    float d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d >= radius) continue;
                    float w = 1 - d / radius; bx.Add(dx); bz.Add(dz); bw.Add(w); wsum += w;
                }
            int bc = bw.Count;
            var BX = bx.ToArray(); var BZ = bz.ToArray(); var BW = new float[bc];
            for (int i = 0; i < bc; i++) BW[i] = bw[i] / wsum;

            // work in "cell units" so a slope of 1 is 45 degrees
            float unit = cell;
            var h = new float[N * N];
            for (int i = 0; i < h.Length; i++) h[i] = H[i] / unit;
            float seaU = (sea - 2f) / unit;

            var rng = new Rng(S.seed, 20);
            int drops = (int)(N * (long)N / 18);
            const float inertia = 0.05f, capacityK = 3f, minCap = 0.01f, erodeK = 0.3f, depositK = 0.3f, evaporate = 0.02f, gravity = 4f;
            const int lifetime = 45;
            for (int d = 0; d < drops; d++)
            {
                float px = rng.Range(radius, N - radius - 2), pz = rng.Range(radius, N - radius - 2);
                int ci = (int)pz * N + (int)px;
                if (h[ci] < seaU) continue;
                float dirX = 0, dirZ = 0, speed = 1, water = 1, sed = 0;
                for (int life = 0; life < lifetime; life++)
                {
                    int nx = (int)px, nz = (int)pz;
                    int idx = nz * N + nx;
                    float cx = px - nx, cz = pz - nz;
                    float gx, gz, hOld = HeightGrad(h, px, pz, out gx, out gz);
                    dirX = dirX * inertia - gx * (1 - inertia);
                    dirZ = dirZ * inertia - gz * (1 - inertia);
                    float len = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
                    if (len < 1e-6f) break;
                    dirX /= len; dirZ /= len;
                    px += dirX; pz += dirZ;
                    if (px < radius || pz < radius || px >= N - radius - 1 || pz >= N - radius - 1) break;
                    float hNew = HeightGrad(h, px, pz, out gx, out gz);
                    float dh = hNew - hOld;
                    float cap = Math.Max(-dh * speed * water * capacityK, minCap);
                    if (sed > cap || dh > 0)
                    {
                        float amt = dh > 0 ? Math.Min(dh, sed) : (sed - cap) * depositK;
                        sed -= amt;
                        h[idx] += amt * (1 - cx) * (1 - cz);
                        h[idx + 1] += amt * cx * (1 - cz);
                        h[idx + N] += amt * (1 - cx) * cz;
                        h[idx + N + 1] += amt * cx * cz;
                    }
                    else
                    {
                        float amt = Math.Min((cap - sed) * erodeK, -dh);
                        for (int b = 0; b < bc; b++)
                        {
                            int k = (nz + BZ[b]) * N + nx + BX[b];
                            float e = amt * BW[b];
                            float take = h[k] < e ? h[k] : e;
                            h[k] -= take; sed += take;
                        }
                    }
                    if (hNew < seaU) break;
                    speed = (float)Math.Sqrt(Math.Max(0, speed * speed + dh * gravity));
                    water *= 1 - evaporate;
                }
            }
            for (int i = 0; i < h.Length; i++) H[i] = WorldGenResult.Clamp(h[i] * unit, 2f, S.heightScale - 2f);
        }

        float HeightGrad(float[] h, float px, float pz, out float gx, out float gz)
        {
            int x = (int)px, z = (int)pz; float u = px - x, v = pz - z;
            int i = z * N + x;
            float a = h[i], b = h[i + 1], c = h[i + N], d = h[i + N + 1];
            gx = (b - a) * (1 - v) + (d - c) * v;
            gz = (c - a) * (1 - u) + (d - b) * u;
            return a * (1 - u) * (1 - v) + b * u * (1 - v) + c * (1 - u) * v + d * u * v;
        }

        // ---------------------------------------------------------------- water
        void Hydrology()
        {
            HA = new float[M * M];
            for (int j = 0; j < M; j++)
                for (int i = 0; i < M; i++)
                {
                    float s = 0; int n = 0;
                    for (int dz = -2; dz <= 2; dz++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int x = i * Step + dx, z = j * Step + dz;
                            if (x < 0 || z < 0 || x >= N || z >= N) continue;
                            s += H[z * N + x]; n++;
                        }
                    HA[j * M + i] = s / n;
                }

            // ocean = below sea and connected to the map edge
            ocean = new bool[M * M];
            var q = new Queue<int>();
            for (int k = 0; k < M; k++)
            {
                TrySeed(k, q); TrySeed((M - 1) * M + k, q); TrySeed(k * M, q); TrySeed(k * M + M - 1, q);
            }
            while (q.Count > 0)
            {
                int c = q.Dequeue(); int ci = c % M, cj = c / M;
                for (int d = 0; d < 4; d++)
                {
                    int ni = ci + DX[d], nj = cj + DZ[d];
                    if (ni < 0 || nj < 0 || ni >= M || nj >= M) continue;
                    int n = nj * M + ni;
                    if (!ocean[n] && HA[n] < sea) { ocean[n] = true; q.Enqueue(n); }
                }
            }

            // priority-flood depression filling: every land cell then drains to the sea
            F = new float[M * M];
            var done = new bool[M * M];
            var heap = new MinHeap(M * 8);
            for (int k = 0; k < M * M; k++)
            {
                int i = k % M, j = k / M;
                bool border = i == 0 || j == 0 || i == M - 1 || j == M - 1;
                if (ocean[k] || border) { F[k] = Math.Max(HA[k], ocean[k] ? sea : HA[k]); if (ocean[k]) F[k] = sea; done[k] = true; heap.Push(F[k], k); }
            }
            const float eps = 0.004f;
            while (heap.Count > 0)
            {
                float lv; int c = heap.Pop(out lv);
                int ci = c % M, cj = c / M;
                for (int d = 0; d < 8; d++)
                {
                    int ni = ci + DX[d], nj = cj + DZ[d];
                    if (ni < 0 || nj < 0 || ni >= M || nj >= M) continue;
                    int n = nj * M + ni;
                    if (done[n]) continue;
                    done[n] = true;
                    F[n] = Math.Max(HA[n], F[c] + eps);
                    heap.Push(F[n], n);
                }
            }

            // flow directions and accumulation
            receiver = new int[M * M]; acc = new float[M * M];
            for (int k = 0; k < M * M; k++)
            {
                receiver[k] = -1;
                if (ocean[k]) continue;
                int i = k % M, j = k / M; float best = 0;
                for (int d = 0; d < 8; d++)
                {
                    int ni = i + DX[d], nj = j + DZ[d];
                    if (ni < 0 || nj < 0 || ni >= M || nj >= M) continue;
                    int n = nj * M + ni;
                    float drop = (F[k] - F[n]) / DL[d];
                    if (drop > best) { best = drop; receiver[k] = n; }
                }
            }
            var order = new int[M * M]; var keys = new float[M * M];
            for (int k = 0; k < M * M; k++) { order[k] = k; keys[k] = -F[k]; acc[k] = 1; }
            Array.Sort(keys, order);
            foreach (int k in order) if (receiver[k] >= 0 && !ocean[k]) acc[receiver[k]] += acc[k];

            // lakes: filled depressions that are deep and big enough
            lake = new bool[M * M];
            var cand = new bool[M * M];
            for (int k = 0; k < M * M; k++) cand[k] = !ocean[k] && F[k] - HA[k] > 0.8f;
            var lakes = new List<List<int>>();
            foreach (var comp in Components(cand))
            {
                float deepest = 0;
                foreach (int k in comp) deepest = Math.Max(deepest, F[k] - HA[k]);
                if (comp.Count >= 200 && deepest >= 3f) lakes.Add(comp);
            }
            lakes.Sort((a, b) => b.Count.CompareTo(a.Count));
            for (int l = 0; l < Math.Min(lakes.Count, S.maxLakes); l++) foreach (int k in lakes[l]) lake[k] = true;

            // every other hollow is filled in, so rivers run through instead of leaving puddles
            var raise = new float[M * M];
            for (int k = 0; k < M * M; k++)
                if (!ocean[k] && !lake[k] && F[k] > HA[k]) { float d = F[k] - HA[k]; raise[k] = d + Math.Min(0.3f, d); }
            Blur(raise, 1);
            Parallel.For(0, N, z =>
            {
                for (int x = 0; x < N; x++) H[z * N + x] += SampleA(raise, x / (float)Step, z / (float)Step);
            });
            for (int k = 0; k < M * M; k++) HA[k] += raise[k];

            float thresh = WorldGenResult.Lerp(5000f, 900f, WorldGenResult.Clamp(S.riverAmount, 0, 1));
            river = new bool[M * M];
            for (int k = 0; k < M * M; k++) river[k] = !ocean[k] && !lake[k] && acc[k] >= thresh;
            riverThresh = thresh;

            // water surface for rivers and lakes (sea is a separate flat plane)
            var L = new float[M * M];
            for (int k = 0; k < M * M; k++)
                L[k] = lake[k] ? F[k] : river[k] ? Math.Max(sea, F[k] - 0.5f) : ocean[k] ? sea : float.NaN;
            for (int pass = 0; pass < 2; pass++)
            {
                var L2 = (float[])L.Clone();
                for (int k = 0; k < M * M; k++)
                {
                    if (!float.IsNaN(L[k])) continue;
                    int i = k % M, j = k / M; float m = float.NaN;
                    for (int d = 0; d < 8; d++)
                    {
                        int ni = i + DX[d], nj = j + DZ[d];
                        if (ni < 0 || nj < 0 || ni >= M || nj >= M) continue;
                        float v = L[nj * M + ni];
                        if (!float.IsNaN(v) && (float.IsNaN(m) || v < m)) m = v;
                    }
                    L2[k] = m;
                }
                L = L2;
            }
            R.waterLevel = L; R.ocean = ocean; R.river = river; R.lake = lake;

            water = new bool[M * M];
            for (int k = 0; k < M * M; k++) water[k] = ocean[k] || lake[k] || river[k];
            distWater = Chamfer(water);
            distOcean = Chamfer(ocean);

            // the biggest landmass gets the towns and roads
            var landMask = new bool[M * M];
            for (int k = 0; k < M * M; k++) landMask[k] = !ocean[k];
            mainland = new bool[M * M];
            List<int> biggest = null;
            foreach (var comp in Components(landMask)) if (biggest == null || comp.Count > biggest.Count) biggest = comp;
            if (biggest != null) foreach (int k in biggest) mainland[k] = true;

            int rc = 0, lc = 0; for (int k = 0; k < M * M; k++) { if (river[k]) rc++; if (lake[k]) lc++; }
            R.log.Add(string.Format("river cells {0}, lake cells {1}", rc, lc));
        }
        float riverThresh;

        void TrySeed(int k, Queue<int> q) { if (!ocean[k] && HA[k] < sea) { ocean[k] = true; q.Enqueue(k); } }

        // Cut river channels into the full-res heightmap, segment by segment.
        void CarveRivers()
        {
            for (int k = 0; k < M * M; k++)
            {
                if (!river[k]) continue;
                int r = receiver[k];
                if (r < 0) continue;
                float lvA = R.waterLevel[k], lvB = lake[r] || ocean[r] ? (ocean[r] ? sea : F[r]) : R.waterLevel[r];
                if (float.IsNaN(lvB)) lvB = lvA;
                float ratA = (float)Math.Sqrt(acc[k] / riverThresh), ratB = (float)Math.Sqrt(acc[r] / riverThresh);
                float hwA = Math.Min(2.2f + 1.6f * ratA, 16f), hwB = Math.Min(2.2f + 1.6f * ratB, 16f);
                float dA = Math.Min(1.4f + 0.45f * ratA, 4.5f), dB = Math.Min(1.4f + 0.45f * ratB, 4.5f);
                float ax = (k % M) * aCell, az = (k / M) * aCell, bxw = (r % M) * aCell, bzw = (r / M) * aCell;
                float reach = Math.Max(hwA, hwB) + 14f;
                int x0 = Math.Max(0, (int)((Math.Min(ax, bxw) - reach) / cell)), x1 = Math.Min(N - 1, (int)((Math.Max(ax, bxw) + reach) / cell) + 1);
                int z0 = Math.Max(0, (int)((Math.Min(az, bzw) - reach) / cell)), z1 = Math.Min(N - 1, (int)((Math.Max(az, bzw) + reach) / cell) + 1);
                float sx = bxw - ax, sz = bzw - az, sl2 = sx * sx + sz * sz;
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float wx = x * cell, wz = z * cell;
                        float t = WorldGenResult.Clamp(((wx - ax) * sx + (wz - az) * sz) / sl2, 0, 1);
                        float qx = ax + sx * t - wx, qz = az + sz * t - wz;
                        float d = (float)Math.Sqrt(qx * qx + qz * qz);
                        float hw = WorldGenResult.Lerp(hwA, hwB, t), depth = WorldGenResult.Lerp(dA, dB, t);
                        float lv = WorldGenResult.Lerp(lvA, lvB, t);
                        float target;
                        if (d < hw) { float e = d / hw; target = lv - 0.3f - depth * (1 - e * e); }
                        else target = lv + 0.25f + (d - hw) * 0.55f;
                        int i = z * N + x;
                        if (target < H[i]) H[i] = Math.Max(target, 2f);
                    }
            }
        }

        // ---------------------------------------------------------------- climate
        void Biomes()
        {
            var tn = new Noise(S.seed, 30); var mnz = new Noise(S.seed, 31); var gm = new Noise(S.seed, 32);
            temperature = new float[M * M]; moisture = new float[M * M];
            var b = new byte[M * M];
            var lw = new float[M * M * WorldGenResult.LayerCount];
            for (int j = 0; j < M; j++)
                for (int i = 0; i < M; i++)
                {
                    int k = j * M + i;
                    float u = i / (M - 1f), v = j / (M - 1f);
                    float alt = Math.Max(0, HA[k] - sea);
                    // north is colder, south warmer; height cools
                    float temp = 1.15f - 1.05f * v + 0.25f * tn.Fbm(u * 2.5f, v * 2.5f, 3) - alt / 520f;
                    float moist = 0.5f + 0.7f * mnz.Fbm(u * 3f, v * 3f, 4) + 0.3f * (float)Math.Exp(-distWater[k] / 300f) - 0.15f * WorldGenResult.Smooth(0.55f, 1f, temp);
                    temperature[k] = temp; moisture[k] = moist;
                    Biome bm;
                    float slope = SlopeA(i, j);
                    if (ocean[k]) bm = Biome.Ocean;
                    else if (alt > 400 - 60 * (1 - WorldGenResult.Clamp(temp + 0.3f, 0, 1)) || (alt > 260 && temp < 0.05f)) bm = Biome.SnowPeak;
                    else if (alt > 290 || (mountainMask[k] > 0.5f && alt > 200 && slope > 22)) bm = Biome.Mountain;
                    else if (distOcean[k] < 26 + 20 * gm.Get(u * 40f, v * 40f) && alt < 3.5f && cliffMask[k] < 0.5f) bm = Biome.Beach;
                    else if (temp < 0.18f) bm = Biome.Snow;
                    else if (temp < 0.4f) bm = Biome.PineForest;
                    else if (temp > 0.78f && moist < 0.42f) bm = Biome.Desert;
                    else if (temp > 0.62f && moist < 0.52f) bm = Biome.Savanna;
                    else if (moist > 0.78f && alt < 25 && distWater[k] < 400) bm = Biome.Swamp;
                    else if (moist > 0.5f) bm = Biome.Forest;
                    else bm = Biome.Grassland;
                    b[k] = (byte)bm;

                    float g = 0.5f + 0.5f * gm.Fbm(u * 20f, v * 20f, 2);
                    int o = k * WorldGenResult.LayerCount;
                    switch (bm)
                    {
                        case Biome.Ocean: case Biome.Beach: lw[o + 3] = 1; break;
                        case Biome.Grassland: lw[o + 0] = 1 - g; lw[o + 1] = g; break;
                        case Biome.Forest: lw[o + 0] = 0.6f; lw[o + 7] = 0.4f; break;
                        case Biome.PineForest: lw[o + 7] = 0.5f; lw[o + 0] = 0.35f; lw[o + 6] = 0.15f; break;
                        case Biome.Snow: lw[o + 4] = 0.85f; lw[o + 7] = 0.15f; break;
                        case Biome.Desert: lw[o + 3] = 0.8f; lw[o + 2] = 0.2f; break;
                        case Biome.Savanna: lw[o + 2] = 0.7f; lw[o + 0] = 0.3f * g; lw[o + 1] = 0.3f * (1 - g); break;
                        case Biome.Swamp: lw[o + 7] = 0.6f; lw[o + 0] = 0.4f; break;
                        case Biome.Mountain: lw[o + 6] = 0.55f; lw[o + 5] = 0.45f; break;
                        case Biome.SnowPeak: lw[o + 4] = 1f; break;
                    }
                }
            R.biome = b; R.layerWeights = lw;
        }

        float SlopeA(int i, int j)
        {
            int i0 = Math.Max(0, i - 1), i1 = Math.Min(M - 1, i + 1), j0 = Math.Max(0, j - 1), j1 = Math.Min(M - 1, j + 1);
            float gx = (HA[j * M + i1] - HA[j * M + i0]) / ((i1 - i0) * aCell);
            float gz = (HA[j1 * M + i] - HA[j0 * M + i]) / ((j1 - j0) * aCell);
            return (float)(Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI);
        }

        // ---------------------------------------------------------------- settlements
        void Settlements()
        {
            var slope = new float[M * M];
            for (int j = 0; j < M; j++) for (int i = 0; i < M; i++) slope[j * M + i] = SlopeA(i, j);
            var sat = Integral(slope);
            var jit = new Noise(S.seed, 40);
            var names = new NameMaker(S.seed);
            string[] types = { "city", "town", "village" };
            int[] counts = { S.cities, S.towns, S.villages };
            float[] radius = { 110f, 65f, 38f };
            float[] spacing = { 1300f, 700f, 420f };
            for (int t = 0; t < 3; t++)
            {
                int rCells = (int)Math.Ceiling(radius[t] / aCell);
                var cands = new List<KeyValuePair<float, int>>();
                for (int j = rCells + 2; j < M - rCells - 2; j += 2)
                    for (int i = rCells + 2; i < M - rCells - 2; i += 2)
                    {
                        int k = j * M + i;
                        if (!mainland[k] || water[k]) continue;
                        float alt = HA[k] - sea;
                        if (alt < 3f || alt > 230f) continue;
                        var bm = (Biome)R.biome[k];
                        if (bm == Biome.Mountain || bm == Biome.SnowPeak || bm == Biome.Beach) continue;
                        if (t < 2 && bm == Biome.Snow) continue;
                        float dw = distWater[k];
                        if (dw < radius[t] + 18f) continue;
                        if ((bm == Biome.Desert) && dw > 300f) continue;   // desert towns only at oases
                        float meanSlope = BoxMean(sat, i, j, rCells);
                        if (meanSlope > 9f) continue;
                        float score = 1.2f * (1 - meanSlope / 9f);
                        score += 0.9f * (float)Math.Exp(-(dw - radius[t] - 18f) / 260f);
                        // rivers, river mouths and big rivers are classic town spots
                        int nearRiver = NearestRiver(i, j, (int)((radius[t] + 320f) / aCell));
                        if (nearRiver >= 0) score += 0.25f + 0.25f * WorldGenResult.Clamp((float)Math.Log(acc[nearRiver] / riverThresh) / 3f, 0, 1);
                        if (distOcean[k] < radius[t] + 260f) score += t == 0 ? 0.45f : 0.2f;   // harbour
                        float u = i / (M - 1f), v = j / (M - 1f);
                        score -= 0.35f * (float)Math.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f));
                        score -= alt / 500f;
                        score += 0.25f * jit.Get(u * 30f, v * 30f);
                        cands.Add(new KeyValuePair<float, int>(score, k));
                    }
                cands.Sort((a, b2) => b2.Key.CompareTo(a.Key));
                int placed = 0;
                foreach (var c in cands)
                {
                    if (placed >= counts[t]) break;
                    float x = (c.Value % M) * aCell, z = (c.Value / M) * aCell;
                    bool ok = true;
                    foreach (var o in R.settlements)
                    {
                        float need = Math.Max(spacing[t], spacing[Array.IndexOf(types, o.type)] * 0.75f);
                        if (Dist(x, z, o.x, o.z) < need) { ok = false; break; }
                    }
                    if (!ok) continue;
                    int k = c.Value;
                    var st = new Settlement
                    {
                        type = types[t], x = x, z = z, radius = radius[t], biome = (Biome)R.biome[k],
                        coastal = distOcean[k] < radius[t] + 260f,
                        riverside = NearestRiver(k % M, k / M, (int)((radius[t] + 320f) / aCell)) >= 0,
                    };
                    st.name = names.Settlement(st.coastal, st.riverside, t);
                    R.settlements.Add(st);
                    placed++;
                }
            }

            // starter: the friendliest place near the middle (a town if possible)
            Settlement starter = null; float bestS = float.MinValue;
            foreach (var st in R.settlements)
            {
                if (st.type == "city") continue;
                float u = st.x / S.worldSize, v = st.z / S.worldSize;
                float sc = -Dist(u, v, 0.5f, 0.5f) * 3f + (st.type == "town" ? 0.4f : 0);
                if (st.biome == Biome.Grassland || st.biome == Biome.Forest) sc += 1f;
                if (sc > bestS) { bestS = sc; starter = st; }
            }
            if (starter == null && R.settlements.Count > 0) starter = R.settlements[0];
            if (starter != null) starter.isStarter = true;
            starterPos = starter;

            float maxD = 1;
            foreach (var st in R.settlements) if (starter != null) maxD = Math.Max(maxD, Dist(st.x, st.z, starter.x, starter.z));
            foreach (var st in R.settlements)
                st.dangerTier = starter == null ? 1 : DangerTier(st.x, st.z, st.biome, maxD, 0);
            if (starter != null) starter.dangerTier = 1;

            // level the ground under each settlement
            foreach (var st in R.settlements)
            {
                float r = st.radius, outer = r * 1.7f;
                float sum = 0; int n = 0;
                ForCells(st.x, st.z, r, (i, d) => { sum += H[i]; n++; });
                float target = n > 0 ? sum / n : HeightAtFull(st.x, st.z);
                ForCells(st.x, st.z, outer, (i, d) =>
                {
                    float w = 1 - WorldGenResult.Smooth(r, outer, d);
                    H[i] = WorldGenResult.Lerp(H[i], target, w);
                });
                st.y = target;
            }
        }
        Settlement starterPos;

        int DangerTier(float x, float z, Biome bm, float maxD, int extra)
        {
            if (starterPos == null) return 1;
            float d = Dist(x, z, starterPos.x, starterPos.z) / maxD;
            int tier = 1 + (int)(d * 3.6f) + extra;
            if (bm == Biome.Snow || bm == Biome.Desert || bm == Biome.Swamp || bm == Biome.Mountain || bm == Biome.SnowPeak) tier++;
            return Math.Max(1, Math.Min(5, tier));
        }

        int NearestRiver(int i, int j, int r)
        {
            int best = -1; float bd = float.MaxValue;
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int ni = i + dx, nj = j + dz;
                    if (ni < 0 || nj < 0 || ni >= M || nj >= M) continue;
                    int n = nj * M + ni;
                    if (!river[n] && !lake[n]) continue;
                    float d = dx * dx + dz * dz;
                    if (d <= r * r && d < bd) { bd = d; best = n; }
                }
            return best;
        }

        // ---------------------------------------------------------------- caves
        void Caves()
        {
            var jit = new Rng(S.seed, 50);
            var names = new NameMaker(S.seed + 7);
            var cands = new List<KeyValuePair<float, int>>();
            var bestDir = new int[M * M];
            for (int j = 6; j < M - 6; j++)
                for (int i = 6; i < M - 6; i++)
                {
                    int k = j * M + i;
                    if (water[k] || distWater[k] < 20f) continue;
                    if (HA[k] < sea + 2f) continue;
                    if (SlopeA(i, j) > 22f) continue;          // the cave mouth needs walkable ground in front
                    float best = 0; int bd = -1;
                    for (int d = 0; d < 8; d++)
                    {
                        // a steep face rising right behind this spot
                        float h1 = HA[(j + DZ[d] * 2) * M + i + DX[d] * 2], h2 = HA[(j + DZ[d] * 5) * M + i + DX[d] * 5];
                        float rise2 = h1 - HA[k], rise5 = h2 - HA[k];
                        float run5 = 5 * aCell * DL[d];
                        if (rise5 < 22f || rise5 / run5 < 0.75f) continue;
                        float sc = rise5 + rise2;
                        if (sc > best) { best = sc; bd = d; }
                    }
                    if (bd < 0) continue;
                    bestDir[k] = bd;
                    float score = best / 60f + jit.Float() * 0.8f + mountainMask[k] * 0.3f + mesaMask[k] * 0.3f;
                    cands.Add(new KeyValuePair<float, int>(score, k));
                }
            cands.Sort((a, b) => b.Key.CompareTo(a.Key));
            float maxD = 1;
            foreach (var st in R.settlements) if (starterPos != null) maxD = Math.Max(maxD, Dist(st.x, st.z, starterPos.x, starterPos.z));
            foreach (var c in cands)
            {
                if (R.caves.Count >= S.caves) break;
                int k = c.Value, d = bestDir[k];
                float x = (k % M) * aCell, z = (k / M) * aCell;
                bool ok = true;
                foreach (var cv in R.caves) if (Dist(x, z, cv.x, cv.z) < 320f) { ok = false; break; }
                foreach (var st in R.settlements) if (Dist(x, z, st.x, st.z) < st.radius * 1.7f + 90f) { ok = false; break; }
                if (!ok) continue;
                // walk toward the cliff until the ground gets steep: that's the cliff foot
                float dl = DL[d], dirX = DX[d] / dl, dirZ = DZ[d] / dl;
                float px = x, pz = z;
                for (int s = 0; s < 40; s++)
                {
                    if (SlopeFull(px + dirX * 1.5f, pz + dirZ * 1.5f) > 36f) break;
                    px += dirX; pz += dirZ;
                }
                var bm = (Biome)R.biome[k];
                var cave = new Cave
                {
                    x = px, z = pz, y = HeightAtFull(px, pz),
                    yaw = (float)(Math.Atan2(-dirX, -dirZ) * 180 / Math.PI),   // mouth faces away from the cliff
                    biome = bm,
                };
                cave.dangerTier = DangerTier(px, pz, bm, maxD, 1);
                cave.name = names.Cave(bm, distOcean[k] < 150f);
                // small flat landing in front of the mouth
                float baseY = cave.y;
                ForCells(px - dirX * 5, pz - dirZ * 5, 9f, (i, dd) =>
                {
                    float w = 1 - WorldGenResult.Smooth(5f, 9f, dd);
                    if (H[i] > baseY) H[i] = WorldGenResult.Lerp(H[i], baseY, w * 0.5f);
                    else H[i] = WorldGenResult.Lerp(H[i], baseY, w);
                });
                R.caves.Add(cave);
            }
        }

        // ---------------------------------------------------------------- roads
        void Roads()
        {
            R.road = new byte[N * N];
            var st = R.settlements;
            if (st.Count < 2) return;
            // minimum spanning tree plus a few extra links so the network has loops
            var edges = new List<int[]>();
            var inTree = new bool[st.Count]; inTree[0] = true;
            for (int added = 1; added < st.Count; added++)
            {
                float best = float.MaxValue; int ba = -1, bb = -1;
                for (int a = 0; a < st.Count; a++) if (inTree[a])
                        for (int b = 0; b < st.Count; b++) if (!inTree[b])
                            {
                                float d = Dist(st[a].x, st[a].z, st[b].x, st[b].z);
                                if (d < best) { best = d; ba = a; bb = b; }
                            }
                inTree[bb] = true; edges.Add(new[] { ba, bb });
            }
            for (int a = 0; a < st.Count; a++)
            {
                if (st[a].type == "village") continue;
                // link towns and cities to their two nearest bigger neighbours
                var near = new List<KeyValuePair<float, int>>();
                for (int b = 0; b < st.Count; b++) if (b != a && st[b].type != "village") near.Add(new KeyValuePair<float, int>(Dist(st[a].x, st[a].z, st[b].x, st[b].z), b));
                near.Sort((p, q) => p.Key.CompareTo(q.Key));
                for (int n = 0; n < Math.Min(2, near.Count); n++)
                {
                    int b = near[n].Value;
                    if (near[n].Key > 2600f) break;
                    bool dup = false;
                    foreach (var e in edges) if ((e[0] == a && e[1] == b) || (e[0] == b && e[1] == a)) dup = true;
                    if (!dup) edges.Add(new[] { a, b });
                }
            }
            edges.Sort((p, q) => Dist(st[p[0]].x, st[p[0]].z, st[p[1]].x, st[p[1]].z).CompareTo(Dist(st[q[0]].x, st[q[0]].z, st[q[1]].x, st[q[1]].z)));

            var roadCell = new bool[M * M];
            var slope = new float[M * M];
            for (int j = 0; j < M; j++) for (int i = 0; i < M; i++) slope[j * M + i] = SlopeA(i, j);
            foreach (var e in edges)
            {
                var path = AStar(Cell(st[e[0]]), Cell(st[e[1]]), slope, roadCell);
                if (path == null) continue;
                foreach (int k in path) roadCell[k] = true;
                var pts = new List<float>();
                foreach (int k in path) { pts.Add((k % M) * aCell); pts.Add((k / M) * aCell); }
                var smooth = Chaikin(Chaikin(pts.ToArray()));
                R.roads.Add(smooth);
                bool major = st[e[0]].type != "village" && st[e[1]].type != "village";
                StampRoad(smooth, major ? 3.2f : 2.4f);
                FindBridges(path);
            }
        }

        int Cell(Settlement s) { return (int)Math.Round(s.z / aCell) * M + (int)Math.Round(s.x / aCell); }

        List<int> AStar(int start, int goal, float[] slope, bool[] roadCell)
        {
            var g = new Dictionary<int, float>();
            var from = new Dictionary<int, int>();
            var heap = new MinHeap(1024);
            g[start] = 0; heap.Push(0, start);
            int gi = goal % M, gj = goal / M;
            var closed = new HashSet<int>();
            while (heap.Count > 0)
            {
                float f; int c = heap.Pop(out f);
                if (c == goal) break;
                if (!closed.Add(c)) continue;
                int ci = c % M, cj = c / M;
                for (int d = 0; d < 8; d++)
                {
                    int ni = ci + DX[d], nj = cj + DZ[d];
                    if (ni < 1 || nj < 1 || ni >= M - 1 || nj >= M - 1) continue;
                    int n = nj * M + ni;
                    if (ocean[n] || lake[n]) continue;
                    float rise = Math.Abs(HA[n] - HA[c]) / (aCell * DL[d]);
                    float cost = DL[d] * (1 + 60f * rise * rise + (slope[n] > 25 ? 20f : 0));
                    if (CrossesRiver(c, n)) cost += 25f;         // bridges are expensive, cross rivers rarely
                    if (roadCell[n]) cost *= 0.45f;            // reuse existing roads
                    float ng = g[c] + cost;
                    float old;
                    if (g.TryGetValue(n, out old) && old <= ng) continue;
                    g[n] = ng; from[n] = c;
                    float h = 0.45f * (float)Math.Sqrt((ni - gi) * (ni - gi) + (nj - gj) * (nj - gj));
                    heap.Push(ng + h, n);
                }
            }
            if (!from.ContainsKey(goal)) return null;
            var path = new List<int>();
            for (int c = goal; c != start; c = from[c]) path.Add(c);
            path.Add(start); path.Reverse();
            return path;
        }

        // Stepping onto a river cell, or slipping diagonally between two river cells.
        bool CrossesRiver(int c, int n)
        {
            if (river[n]) return !river[c];
            int ci = c % M, cj = c / M, ni = n % M, nj = n / M;
            return ci != ni && cj != nj && river[cj * M + ni] && river[nj * M + ci];
        }

        static float[] Chaikin(float[] p)
        {
            int n = p.Length / 2;
            if (n < 3) return p;
            var o = new List<float> { p[0], p[1] };
            for (int i = 0; i < n - 1; i++)
            {
                float ax = p[i * 2], az = p[i * 2 + 1], bx = p[i * 2 + 2], bz = p[i * 2 + 3];
                o.Add(ax * 0.75f + bx * 0.25f); o.Add(az * 0.75f + bz * 0.25f);
                o.Add(ax * 0.25f + bx * 0.75f); o.Add(az * 0.25f + bz * 0.75f);
            }
            o.Add(p[(n - 1) * 2]); o.Add(p[(n - 1) * 2 + 1]);
            return o.ToArray();
        }

        void StampRoad(float[] pts, float hw)
        {
            int n = pts.Length / 2;
            // a gently smoothed height profile along the road, so it doesn't follow every bump
            var prof = new float[n];
            for (int i = 0; i < n; i++) prof[i] = HeightAtFull(pts[i * 2], pts[i * 2 + 1]);
            for (int pass = 0; pass < 3; pass++)
            {
                var p2 = (float[])prof.Clone();
                for (int i = 0; i < n; i++)
                {
                    float s = 0; int c = 0;
                    for (int d = -3; d <= 3; d++) { int q = i + d; if (q < 0 || q >= n) continue; s += prof[q]; c++; }
                    p2[i] = s / c;
                }
                prof = p2;
            }
            float outer = hw + 5f;
            for (int i = 0; i < n - 1; i++)
            {
                float ax = pts[i * 2], az = pts[i * 2 + 1], bx = pts[i * 2 + 2], bz = pts[i * 2 + 3];
                float sx = bx - ax, sz = bz - az, sl2 = sx * sx + sz * sz;
                if (sl2 < 1e-4f) continue;
                int x0 = Math.Max(0, (int)((Math.Min(ax, bx) - outer) / cell)), x1 = Math.Min(N - 1, (int)((Math.Max(ax, bx) + outer) / cell) + 1);
                int z0 = Math.Max(0, (int)((Math.Min(az, bz) - outer) / cell)), z1 = Math.Min(N - 1, (int)((Math.Max(az, bz) + outer) / cell) + 1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float wx = x * cell, wz = z * cell;
                        float t = WorldGenResult.Clamp(((wx - ax) * sx + (wz - az) * sz) / sl2, 0, 1);
                        float qx = ax + sx * t - wx, qz = az + sz * t - wz;
                        float d = (float)Math.Sqrt(qx * qx + qz * qz);
                        if (d > outer) continue;
                        int k = z * N + x;
                        byte rv = (byte)(255 * (1 - WorldGenResult.Smooth(hw - 0.8f, hw + 0.8f, d)));
                        if (rv > R.road[k]) R.road[k] = rv;
                        // don't fill in rivers or lakes where the road crosses them (a bridge goes there)
                        float wl = R.WaterLevelAt(wx, wz);
                        if (!float.IsNaN(wl) && H[k] < wl + 0.8f) continue;
                        float target = WorldGenResult.Lerp(prof[i], prof[i + 1], t);
                        if (target < sea + 0.6f) continue;
                        float w = 1 - WorldGenResult.Smooth(hw + 1f, outer, d);
                        H[k] = WorldGenResult.Lerp(H[k], target, w * 0.85f);
                    }
            }
        }

        void FindBridges(List<int> path)
        {
            var wet = new bool[path.Count];
            for (int s = 0; s < path.Count; s++)
                wet[s] = river[path[s]] || (s > 0 && CrossesRiver(path[s - 1], path[s]));
            int i = 0;
            while (i < path.Count)
            {
                if (!wet[i]) { i++; continue; }
                int a = i;
                while (i < path.Count && wet[i]) i++;
                int b = i - 1;
                int pa = path[Math.Max(0, a - 1)], pb = path[Math.Min(path.Count - 1, b + 1)];
                float ax = (pa % M) * aCell, az = (pa / M) * aCell, bx = (pb % M) * aCell, bz = (pb / M) * aCell;
                float cx = (ax + bx) / 2, cz = (az + bz) / 2;
                bool dup = false;
                foreach (var br in R.bridges) if (Dist(br.x, br.z, cx, cz) < 20f) dup = true;
                if (dup) continue;
                float lv = R.WaterLevelAt(cx, cz);
                float deckY = Math.Max(HeightAtFull(ax, az), HeightAtFull(bx, bz));
                if (!float.IsNaN(lv)) deckY = Math.Max(deckY, lv + 1.5f);
                R.bridges.Add(new Bridge
                {
                    x = cx, z = cz, y = deckY,
                    length = Dist(ax, az, bx, bz) + 6f,
                    yaw = (float)(Math.Atan2(bx - ax, bz - az) * 180 / Math.PI),
                });
            }
        }

        // ---------------------------------------------------------------- trees
        void Trees()
        {
            var rng = new Rng(S.seed, 60);
            var groves = new Noise(S.seed, 61);
            float sp = Math.Max(3f, S.treeSpacing), ws = S.worldSize;
            var R2 = R;
            for (float z = sp * 0.5f; z < ws - sp; z += sp)
                for (float x = sp * 0.5f; x < ws - sp; x += sp)
                {
                    float px = x + rng.Range(-0.45f, 0.45f) * sp, pz = z + rng.Range(-0.45f, 0.45f) * sp;
                    float roll = rng.Float(), pick = rng.Float(), sc = rng.Float(), rot = rng.Float();
                    // sample the biome a little off-centre so borders mix naturally
                    var bm = R2.BiomeAt(px + rng.Range(-20f, 20f), pz + rng.Range(-20f, 20f));
                    float dens; TreeKind kind;
                    switch (bm)
                    {
                        case Biome.Forest: dens = 0.62f; kind = pick < 0.85f ? TreeKind.Broadleaf : TreeKind.Conifer; break;
                        case Biome.PineForest: dens = 0.6f; kind = pick < 0.9f ? TreeKind.Conifer : TreeKind.Broadleaf; break;
                        case Biome.Snow: dens = 0.32f; kind = TreeKind.SnowPine; break;
                        case Biome.Swamp: dens = 0.38f; kind = pick < 0.7f ? TreeKind.SwampTree : TreeKind.Shrub; break;
                        case Biome.Grassland: dens = 0.07f; kind = pick < 0.55f ? TreeKind.Broadleaf : TreeKind.Shrub; break;
                        case Biome.Savanna: dens = 0.06f; kind = pick < 0.75f ? TreeKind.Shrub : TreeKind.Palm; break;
                        case Biome.Desert: dens = 0.012f; kind = TreeKind.Shrub; break;
                        case Biome.Beach: dens = 0.05f; kind = TreeKind.Palm; break;
                        case Biome.Mountain: dens = 0.06f; kind = TreeKind.Conifer; break;
                        default: continue;
                    }
                    if (bm == Biome.Beach && TemperatureAt(px, pz) < 0.55f) continue;   // no palms up north
                    float gv = groves.Fbm(px / ws * 18f, pz / ws * 18f, 3);
                    float g = bm == Biome.Grassland || bm == Biome.Savanna
                        ? WorldGenResult.Smooth(0.05f, 0.35f, gv) * 4f + 0.15f           // copses in open land
                        : WorldGenResult.Smooth(-0.35f, 0.05f, gv) * 1.25f;             // clearings in forests
                    if (roll >= dens * g * S.forestAmount) continue;

                    float h = R2.HeightAt(px, pz);
                    if (h < sea + 0.8f) continue;
                    float wl = R2.WaterLevelAt(px, pz);
                    if (!float.IsNaN(wl) && h < wl + 0.6f) continue;
                    if (R2.SlopeAt(px, pz) > 34f) continue;
                    if (R2.RoadAt(px, pz) > 0 || R2.RoadAt(px + 3, pz) > 0 || R2.RoadAt(px - 3, pz) > 0 || R2.RoadAt(px, pz + 3) > 0 || R2.RoadAt(px, pz - 3) > 0) continue;
                    if (Near(px, pz)) continue;
                    R.trees.Add(new TreePlacement { x = px, z = pz, kind = kind, scale = 0.75f + sc * 0.6f, rotation = rot * 360f });
                }
            R.log.Add("trees " + R.trees.Count);
        }

        bool Near(float x, float z)
        {
            foreach (var s in R.settlements) if (Dist(x, z, s.x, s.z) < s.radius * 1.15f + 8f) return true;
            foreach (var c in R.caves) if (Dist(x, z, c.x, c.z) < 16f) return true;
            foreach (var b in R.bridges) if (Dist(x, z, b.x, b.z) < b.length * 0.5f + 6f) return true;
            return false;
        }

        float TemperatureAt(float x, float z)
        {
            int i = (int)WorldGenResult.Clamp(x / aCell + 0.5f, 0, M - 1), j = (int)WorldGenResult.Clamp(z / aCell + 0.5f, 0, M - 1);
            return temperature[j * M + i];
        }

        // ---------------------------------------------------------------- helpers
        float HeightAtFull(float x, float z)
        {
            float fx = WorldGenResult.Clamp(x / cell, 0, N - 1.001f), fz = WorldGenResult.Clamp(z / cell, 0, N - 1.001f);
            int x0 = (int)fx, z0 = (int)fz; float tx = fx - x0, tz = fz - z0;
            int i = z0 * N + x0;
            return WorldGenResult.Lerp(WorldGenResult.Lerp(H[i], H[i + 1], tx), WorldGenResult.Lerp(H[i + N], H[i + N + 1], tx), tz);
        }

        float SlopeFull(float x, float z)
        {
            float d = cell;
            float gx = (HeightAtFull(x + d, z) - HeightAtFull(x - d, z)) / (2 * d);
            float gz = (HeightAtFull(x, z + d) - HeightAtFull(x, z - d)) / (2 * d);
            return (float)(Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI);
        }

        void ForCells(float cx, float cz, float r, Action<int, float> f)
        {
            int x0 = Math.Max(0, (int)((cx - r) / cell)), x1 = Math.Min(N - 1, (int)((cx + r) / cell) + 1);
            int z0 = Math.Max(0, (int)((cz - r) / cell)), z1 = Math.Min(N - 1, (int)((cz + r) / cell) + 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Dist(x * cell, z * cell, cx, cz);
                    if (d <= r) f(z * N + x, d);
                }
        }

        float SampleA(float[] a, float fx, float fz)
        {
            if (fx > M - 1.001f) fx = M - 1.001f; if (fz > M - 1.001f) fz = M - 1.001f;
            int x0 = (int)fx, z0 = (int)fz; float tx = fx - x0, tz = fz - z0;
            int i = z0 * M + x0;
            return WorldGenResult.Lerp(WorldGenResult.Lerp(a[i], a[i + 1], tx), WorldGenResult.Lerp(a[i + M], a[i + M + 1], tx), tz);
        }

        static float Dist(float ax, float az, float bx, float bz) { float dx = ax - bx, dz = az - bz; return (float)Math.Sqrt(dx * dx + dz * dz); }

        static float Percentile(float[] v, bool[] mask, float p)
        {
            var l = new List<float>();
            for (int i = 0; i < v.Length; i++) if (mask == null || mask[i]) l.Add(v[i]);
            if (l.Count == 0) return 0;
            l.Sort();
            return l[(int)WorldGenResult.Clamp(p * (l.Count - 1), 0, l.Count - 1)];
        }

        bool[] Not(bool[] a) { var b = new bool[a.Length]; for (int i = 0; i < a.Length; i++) b[i] = !a[i]; return b; }

        // Distance (m) on the analysis grid to the nearest true cell (two-pass chamfer).
        float[] Chamfer(bool[] src)
        {
            var d = new float[M * M];
            const float big = 1e9f;
            for (int k = 0; k < M * M; k++) d[k] = src[k] ? 0 : big;
            float a = aCell, b = aCell * 1.41421f;
            for (int j = 0; j < M; j++)
                for (int i = 0; i < M; i++)
                {
                    int k = j * M + i; float v = d[k];
                    if (i > 0) v = Math.Min(v, d[k - 1] + a);
                    if (j > 0) { v = Math.Min(v, d[k - M] + a); if (i > 0) v = Math.Min(v, d[k - M - 1] + b); if (i < M - 1) v = Math.Min(v, d[k - M + 1] + b); }
                    d[k] = v;
                }
            for (int j = M - 1; j >= 0; j--)
                for (int i = M - 1; i >= 0; i--)
                {
                    int k = j * M + i; float v = d[k];
                    if (i < M - 1) v = Math.Min(v, d[k + 1] + a);
                    if (j < M - 1) { v = Math.Min(v, d[k + M] + a); if (i < M - 1) v = Math.Min(v, d[k + M + 1] + b); if (i > 0) v = Math.Min(v, d[k + M - 1] + b); }
                    d[k] = v;
                }
            return d;
        }

        void Blur(float[] a, int passes)
        {
            var t = new float[a.Length];
            for (int p = 0; p < passes; p++)
            {
                for (int j = 0; j < M; j++)
                    for (int i = 0; i < M; i++)
                    {
                        int k = j * M + i;
                        t[k] = (a[k] * 2 + a[j * M + Math.Max(0, i - 1)] + a[j * M + Math.Min(M - 1, i + 1)]) * 0.25f;
                    }
                for (int j = 0; j < M; j++)
                    for (int i = 0; i < M; i++)
                    {
                        int k = j * M + i;
                        a[k] = (t[k] * 2 + t[Math.Max(0, j - 1) * M + i] + t[Math.Min(M - 1, j + 1) * M + i]) * 0.25f;
                    }
            }
        }

        List<List<int>> Components(bool[] mask)
        {
            var seen = new bool[M * M];
            var res = new List<List<int>>();
            var q = new Queue<int>();
            for (int s = 0; s < M * M; s++)
            {
                if (!mask[s] || seen[s]) continue;
                var comp = new List<int>();
                seen[s] = true; q.Enqueue(s);
                while (q.Count > 0)
                {
                    int c = q.Dequeue(); comp.Add(c);
                    int ci = c % M, cj = c / M;
                    for (int d = 0; d < 4; d++)
                    {
                        int ni = ci + DX[d], nj = cj + DZ[d];
                        if (ni < 0 || nj < 0 || ni >= M || nj >= M) continue;
                        int n = nj * M + ni;
                        if (mask[n] && !seen[n]) { seen[n] = true; q.Enqueue(n); }
                    }
                }
                res.Add(comp);
            }
            return res;
        }

        void RemoveSmall(bool[] land, bool value, int minSize)
        {
            var mask = value ? land : Not(land);
            foreach (var comp in Components(mask)) if (comp.Count < minSize) foreach (int k in comp) land[k] = !value;
        }

        // summed-area table for fast box averages
        double[] Integral(float[] a)
        {
            int W = M + 1; var s = new double[W * W];
            for (int j = 0; j < M; j++)
                for (int i = 0; i < M; i++)
                    s[(j + 1) * W + i + 1] = a[j * M + i] + s[j * W + i + 1] + s[(j + 1) * W + i] - s[j * W + i];
            return s;
        }

        float BoxMean(double[] s, int i, int j, int r)
        {
            int W = M + 1;
            int x0 = Math.Max(0, i - r), x1 = Math.Min(M - 1, i + r) + 1, z0 = Math.Max(0, j - r), z1 = Math.Min(M - 1, j + r) + 1;
            double sum = s[z1 * W + x1] - s[z0 * W + x1] - s[z1 * W + x0] + s[z0 * W + x0];
            return (float)(sum / ((x1 - x0) * (z1 - z0)));
        }
    }

    class MinHeap
    {
        float[] k; int[] v; int n;
        public MinHeap(int cap) { k = new float[cap]; v = new int[cap]; }
        public int Count { get { return n; } }
        public void Push(float key, int val)
        {
            if (n == k.Length) { Array.Resize(ref k, n * 2); Array.Resize(ref v, n * 2); }
            int i = n++;
            while (i > 0) { int p = (i - 1) / 2; if (k[p] <= key) break; k[i] = k[p]; v[i] = v[p]; i = p; }
            k[i] = key; v[i] = val;
        }
        public int Pop(out float key)
        {
            int top = v[0]; key = k[0];
            float lk = k[--n]; int lv = v[n];
            int i = 0;
            while (true)
            {
                int c = i * 2 + 1; if (c >= n) break;
                if (c + 1 < n && k[c + 1] < k[c]) c++;
                if (k[c] >= lk) break;
                k[i] = k[c]; v[i] = v[c]; i = c;
            }
            if (n > 0) { k[i] = lk; v[i] = lv; }
            return top;
        }
    }

    // Placeholder fantasy names. Rename freely in the scene or in the saved JSON.
    class NameMaker
    {
        readonly Rng r; readonly HashSet<string> used = new HashSet<string>();
        static readonly string[] Pre = { "Ash", "Bram", "Cold", "Dun", "Elder", "Fen", "Glen", "Hollow", "Iron", "King", "Lark", "Mill", "North", "Oak", "Pike",
            "Raven", "Stone", "Thorn", "Wester", "Wolf", "Yew", "Brack", "Cinder", "Dusk", "Ember", "Frost", "Gold", "Hearth", "Moss", "Rush", "Salt",
            "Silver", "Storm", "Tall", "Wren", "Amber", "Bright", "Copper", "Deep", "Fair", "Grey", "High", "Long", "Marsh", "Red", "Black", "White", "Sun",
            "Briar", "Crow", "Elm", "Hazel", "Rook", "Sorrow", "Tide", "Vale", "Wild", "Alder", "Barrow", "Clay" };
        static readonly string[] Land = { "ton", "holm", "wick", "stead", "bury", "dale", "field", "moor", "gate", "hall", "by", "thorpe", "ridge", "crest", "hurst", "ley" };
        static readonly string[] River = { "ford", "bridge", "brook", "water", "mere", "wash", "bank" };
        static readonly string[] Coast = { "haven", "port", "mouth", "strand", "cove", "harbour" };
        static readonly string[] CaveWord = { "Cave", "Cavern", "Grotto", "Hollow", "Den", "Mine", "Deeps", "Burrow" };
        static readonly string[] CaveMid = { "fang", "maw", "rock", "stone", "crag", "shade", "gloom", "" };
        public NameMaker(int seed) { r = new Rng(seed, 99); }
        string Pick(string[] a) { return a[r.Int(a.Length)]; }
        public string Settlement(bool coastal, bool riverside, int tier)
        {
            for (int tries = 0; tries < 50; tries++)
            {
                string suf = coastal && r.Float() < 0.7f ? Pick(Coast) : riverside && r.Float() < 0.6f ? Pick(River) : Pick(Land);
                string n = Pick(Pre) + suf;
                if (used.Add(n)) return n;
            }
            return "Settlement " + used.Count;
        }
        public string Cave(Biome b, bool sea)
        {
            for (int tries = 0; tries < 50; tries++)
            {
                string pre = b == Biome.Snow || b == Biome.SnowPeak ? (r.Float() < 0.5f ? "Frost" : "Rime")
                           : b == Biome.Desert ? (r.Float() < 0.5f ? "Sand" : "Sun")
                           : b == Biome.Swamp ? "Bog" : Pick(Pre);
                string n = sea ? pre + " Sea " + (r.Float() < 0.5f ? "Cave" : "Grotto") : pre + Pick(CaveMid) + " " + Pick(CaveWord);
                if (used.Add(n)) return n;
            }
            return "Cave " + used.Count;
        }
    }
}

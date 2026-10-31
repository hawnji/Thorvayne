// Thorvayne town planner: lays out each town's streets, square and houses from the generated world.
// A square in the middle; main streets that carry on the roads coming into town (plus extra streets so a town
// opens to several sides); a ring street in towns and cities. Houses stand along the streets and around the square,
// fronts to the street, never on a street, in water or on top of each other; each house gets a short path to its door.
// The streets, square and door paths are painted on the ground (the "path" layer) by the world builder.
// Pure C# like ThorvayneWorldGen.cs, and it only reads the generated world, so a seed's terrain doesn't change.
using System;
using System.Collections.Generic;

namespace Thorvayne.WorldGen
{
    public struct HousePlot
    {
        public float x, z, y, yaw;           // yaw in degrees around the up axis; the house's front faces the street
    }

    public class TownPlan
    {
        public Settlement settlement;
        public float plazaRadius;
        public List<float[]> paths = new List<float[]>();   // painted segments: x0, z0, x1, z1, half width
        public List<HousePlot> houses = new List<HousePlot>();
        public List<float> gateAngles = new List<float>();  // where main streets leave town (radians, 0 = +x, ccw toward +z)

        // How much of the path texture to show at a point (0..1).
        public float PathWeight(float x, float z)
        {
            var s = settlement;
            float dc = Dist(x, z, s.x, s.z);
            if (dc > s.radius * 1.15f) return 0;
            float best = 1 - TownPlanner.Smooth(plazaRadius - 0.6f, plazaRadius + 0.8f, dc);
            foreach (var p in paths)
            {
                float d = SegDist(x, z, p[0], p[1], p[2], p[3]);
                float w = 1 - TownPlanner.Smooth(p[4] - 0.6f, p[4] + 0.8f, d);
                if (w > best) best = w;
            }
            return best;
        }

        internal static float Dist(float ax, float az, float bx, float bz) { float dx = ax - bx, dz = az - bz; return (float)Math.Sqrt(dx * dx + dz * dz); }

        internal static float SegDist(float px, float pz, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz;
            float t = l2 > 1e-6f ? ((px - ax) * dx + (pz - az) * dz) / l2 : 0;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return Dist(px, pz, ax + dx * t, az + dz * t);
        }
    }

    public static class TownPlanner
    {
        // House footprint (Assets/Thorvayne/Buildings/House.fbx is 10.5 x 6.9 m, eaves a little wider)
        public const float HouseLength = 11f, HouseDepth = 7.6f;

        public static List<TownPlan> PlanAll(WorldGenResult r)
        {
            var plans = new List<TownPlan>();
            for (int i = 0; i < r.settlements.Count; i++)
                plans.Add(Plan(r, r.settlements[i], new Random(r.settings.seed * 7919 + i)));
            return plans;
        }

        class Street { public List<float> pts = new List<float>(); public float hw; }   // pts: x0, z0, x1, z1, ...

        static TownPlan Plan(WorldGenResult r, Settlement st, Random rnd)
        {
            var plan = new TownPlan { settlement = st };
            float R = st.radius;
            float plaza, hw, edge; float[] rings; int maxHouses, minStreets;
            switch (st.type)
            {
                case "city": plaza = 22f; hw = 2.6f; rings = new[] { R * 0.45f, R * 0.72f }; edge = R * 0.82f; maxHouses = 90; minStreets = 4; break;
                case "town": plaza = 13f; hw = 2.1f; rings = new[] { R * 0.58f }; edge = R * 0.95f; maxHouses = 30; minStreets = 3; break;
                default: plaza = 8f; hw = 1.7f; rings = new float[0]; edge = R * 0.95f; maxHouses = 12; minStreets = 2; break;
            }
            plan.plazaRadius = plaza;
            float outer = st.type == "city" ? R * 0.98f : edge;   // city streets run on through the wall gates

            // main streets: the roads into town, from where they cross the square's edge out to the town's edge
            var streets = new List<Street>();
            foreach (var road in r.roads)
            {
                int n = road.Length / 2;
                if (n < 2) continue;
                for (int end = 0; end < 2; end++)
                {
                    int i0 = end == 0 ? 0 : n - 1, step = end == 0 ? 1 : -1;
                    if (TownPlan.Dist(road[i0 * 2], road[i0 * 2 + 1], st.x, st.z) > R * 0.5f) continue;
                    var s = new Street { hw = hw };
                    for (int i = i0; i >= 0 && i < n; i += step)
                    {
                        float x = road[i * 2], z = road[i * 2 + 1], d = TownPlan.Dist(x, z, st.x, st.z);
                        if (d < plaza) continue;
                        s.pts.Add(x); s.pts.Add(z);
                        if (d > outer) break;
                    }
                    if (s.pts.Count < 4) continue;
                    // several roads often leave town the same way: one street is enough for them
                    bool same = false;
                    foreach (var o in streets) same |= AngleGap(ExitAngle(o, st), ExitAngle(s, st)) < 0.4f;
                    if (!same) streets.Add(s);
                }
            }
            // more streets in the directions furthest from the others, until the town opens to enough sides
            var angles = new List<float>();
            foreach (var s in streets) angles.Add(ExitAngle(s, st));
            while (streets.Count < minStreets)
            {
                float bestA = 0, bestGap = -1;
                for (int k = 0; k < 48; k++)
                {
                    float a = k / 48f * (float)Math.PI * 2f + (float)rnd.NextDouble() * 0.05f;
                    bool wet = false;
                    for (float d = plaza; d <= outer && !wet; d += 4f)
                        wet = r.IsWet(st.x + (float)Math.Cos(a) * d, st.z + (float)Math.Sin(a) * d);
                    if (wet) continue;
                    float gap = (float)Math.PI;
                    foreach (var b in angles) gap = Math.Min(gap, AngleGap(a, b));
                    if (gap > bestGap) { bestGap = gap; bestA = a; }
                }
                if (bestGap < 0.5f) break;
                var s = new Street { hw = hw * 0.85f };
                for (float d = plaza; d <= outer + 0.1f; d += Math.Min(10f, outer - plaza))
                {
                    s.pts.Add(st.x + (float)Math.Cos(bestA) * d); s.pts.Add(st.z + (float)Math.Sin(bestA) * d);
                }
                streets.Add(s); angles.Add(bestA);
            }
            plan.gateAngles.AddRange(angles);
            // ring streets (towns and cities), broken where they would cross water
            var ringStreets = new List<Street>();
            foreach (float ring in rings)
            {
                int segs = Math.Max(16, (int)(ring * 2 * Math.PI / 9f));
                Street cur = null;
                for (int k = 0; k <= segs; k++)
                {
                    float a = k / (float)segs * (float)Math.PI * 2f;
                    float x = st.x + (float)Math.Cos(a) * ring, z = st.z + (float)Math.Sin(a) * ring;
                    if (r.IsWet(x, z)) { cur = null; continue; }
                    if (cur == null) { cur = new Street { hw = hw * 0.8f }; ringStreets.Add(cur); }
                    cur.pts.Add(x); cur.pts.Add(z);
                }
                ringStreets.RemoveAll(s => s.pts.Count < 4);
            }
            var all = new List<Street>(streets); all.AddRange(ringStreets);
            foreach (var s in all)
                for (int i = 0; i + 3 < s.pts.Count; i += 2)
                    plan.paths.Add(new[] { s.pts[i], s.pts[i + 1], s.pts[i + 2], s.pts[i + 3], s.hw });
            var streetSegs = new List<float[]>(plan.paths);
            // roads that only pass by (not ending here) are kept clear of houses too
            foreach (var road in r.roads)
                for (int i = 0; i + 3 < road.Length; i += 2)
                    if (TownPlan.Dist(road[i], road[i + 1], st.x, st.z) < R + 10f || TownPlan.Dist(road[i + 2], road[i + 3], st.x, st.z) < R + 10f)
                        streetSegs.Add(new[] { road[i], road[i + 1], road[i + 2], road[i + 3], hw });

            // houses: around the square first, then along the main streets, then the ring
            var placed = new List<float[]>();   // oriented boxes: cx, cz, ux, uz (front direction), half length, half depth
            float setback = HouseDepth / 2f + 1.6f;
            int ringCount = Math.Max(4, (int)((plaza + setback) * 2 * Math.PI / (HouseLength + 3f)));
            float a0 = (float)rnd.NextDouble() * 6.283f;
            for (int k = 0; k < ringCount && plan.houses.Count < maxHouses; k++)
            {
                float a = a0 + k / (float)ringCount * (float)Math.PI * 2f, d = plaza + setback;
                float cx = st.x + (float)Math.Cos(a) * d, cz = st.z + (float)Math.Sin(a) * d;
                TryHouse(r, plan, placed, streetSegs, cx, cz, -(float)Math.Cos(a), -(float)Math.Sin(a), edge);
            }
            for (int pass = 0; pass < 2; pass++)
                foreach (var s in pass == 0 ? streets : ringStreets)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float along = 2f + (float)rnd.NextDouble() * 3f;
                        float walked = 0;
                        for (int i = 0; i + 3 < s.pts.Count && plan.houses.Count < maxHouses; i += 2)
                        {
                            float ax = s.pts[i], az = s.pts[i + 1], bx = s.pts[i + 2], bz = s.pts[i + 3];
                            float len = TownPlan.Dist(ax, az, bx, bz);
                            if (len < 0.01f) continue;
                            float dx = (bx - ax) / len, dz = (bz - az) / len, nx = -dz * side, nz = dx * side;
                            while (along - walked <= len && plan.houses.Count < maxHouses)
                            {
                                float t = along - walked, off = s.hw + setback;
                                float cx = ax + dx * t + nx * off, cz = az + dz * t + nz * off;
                                bool ok = TryHouse(r, plan, placed, streetSegs, cx, cz, -nx, -nz, edge);
                                along += ok ? HouseLength + 2f + (float)rnd.NextDouble() * 3f : 3f;
                            }
                            walked += len;
                        }
                    }
            return plan;
        }

        static float ExitAngle(Street s, Settlement st)
        {
            int n = s.pts.Count;
            return (float)Math.Atan2(s.pts[n - 1] - st.z, s.pts[n - 2] - st.x);
        }

        static float AngleGap(float a, float b)
        {
            float d = Math.Abs(a - b) % ((float)Math.PI * 2f);
            return d > Math.PI ? (float)Math.PI * 2f - d : d;
        }

        // Places a house facing (fx, fz) at (cx, cz) if it fits; adds its door path.
        static bool TryHouse(WorldGenResult r, TownPlan plan, List<float[]> placed, List<float[]> streets,
                             float cx, float cz, float fx, float fz, float edge)
        {
            var st = plan.settlement;
            float hl = HouseLength / 2f, hd = HouseDepth / 2f;
            if (TownPlan.Dist(cx, cz, st.x, st.z) + hl > edge) return false;
            if (TownPlan.Dist(cx, cz, st.x, st.z) < plan.plazaRadius + hd + 1f) return false;
            var box = new[] { cx, cz, fx, fz, hl, hd };
            foreach (var p in placed) if (BoxesOverlap(box, p, 1.2f)) return false;
            foreach (var s in streets) if (SegHitsBox(s, box, s[4] + 0.6f)) return false;
            // ground: dry and fairly level under the whole house
            float lo = float.MaxValue, hi = float.MinValue, sum = 0;
            for (int i = 0; i < 9; i++)
            {
                float u = (i % 3 - 1) * hl, v = (i / 3 - 1) * hd;
                float x = cx + fz * u + fx * v, z = cz - fx * u + fz * v;
                if (r.IsWet(x, z)) return false;
                float h = r.HeightAt(x, z);
                lo = Math.Min(lo, h); hi = Math.Max(hi, h); sum += h;
            }
            if (hi - lo > 1.0f) return false;     // more and the uphill side buries the wall above the stone base
            placed.Add(box);
            // floor at the average height, but never so high that the buried part of the stone base shows
            float y = Math.Min(sum / 9f, lo + 0.6f);
            plan.houses.Add(new HousePlot { x = cx, z = cz, y = y, yaw = (float)(Math.Atan2(fx, fz) * 180.0 / Math.PI) });
            float dx0 = cx + fx * hd, dz0 = cz + fz * hd, reach = 1.6f + 1.5f;
            plan.paths.Add(new[] { dx0, dz0, dx0 + fx * reach, dz0 + fz * reach, 1.0f });
            return true;
        }

        // Separating-axis test for two oriented boxes (cx, cz, front x, front z, half length, half depth), with a gap.
        static bool BoxesOverlap(float[] a, float[] b, float gap)
        {
            var axes = new[] { a[2], a[3], a[3], -a[2], b[2], b[3], b[3], -b[2] };
            for (int k = 0; k < 4; k++)
            {
                float ax = axes[k * 2], az = axes[k * 2 + 1];
                float d = Math.Abs((b[0] - a[0]) * ax + (b[1] - a[1]) * az);
                if (d > Extent(a, ax, az) + Extent(b, ax, az) + gap) return false;
            }
            return true;
        }

        static float Extent(float[] b, float ax, float az)
        {
            float fx = b[2], fz = b[3], sx = fz, sz = -fx;      // front and side directions
            return Math.Abs(fx * ax + fz * az) * b[5] + Math.Abs(sx * ax + sz * az) * b[4];
        }

        // Does the segment come within `margin` of the box?
        static bool SegHitsBox(float[] s, float[] b, float margin)
        {
            float fx = b[2], fz = b[3], sx = fz, sz = -fx;
            float hl = b[4] + margin, hd = b[5] + margin;
            // segment in the box's frame (u along the length, v toward the front)
            float u0 = (s[0] - b[0]) * sx + (s[1] - b[1]) * sz, v0 = (s[0] - b[0]) * fx + (s[1] - b[1]) * fz;
            float u1 = (s[2] - b[0]) * sx + (s[3] - b[1]) * sz, v1 = (s[2] - b[0]) * fx + (s[3] - b[1]) * fz;
            float t0 = 0, t1 = 1;
            return Clip(-hl - u0, u1 - u0, ref t0, ref t1) && Clip(u0 - hl, u0 - u1, ref t0, ref t1)
                && Clip(-hd - v0, v1 - v0, ref t0, ref t1) && Clip(v0 - hd, v0 - v1, ref t0, ref t1);
        }

        // Liang-Barsky step: narrows [t0, t1] to the part of the segment where t * p >= q; false when nothing is left
        static bool Clip(float q, float p, ref float t0, ref float t1)
        {
            if (Math.Abs(p) < 1e-6f) return q <= 0;
            float t = q / p;
            if (p > 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }

        public static float Smooth(float a, float b, float x)
        {
            float t = (x - a) / (b - a);
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return t * t * (3 - 2 * t);
        }
    }
}

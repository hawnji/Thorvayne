using System;
using System.IO;
using System.Text;
using Thorvayne.WorldGen;
class Program
{
    static void Main(string[] a)
    {
        int seed = int.Parse(a[0]); string outDir = a[1]; int size = a.Length > 2 ? int.Parse(a[2]) : 1600;
        Directory.CreateDirectory(outDir);
        var s = new WorldGenSettings { seed = seed };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = WorldGenerator.Generate(s, (st, t) => Console.Error.WriteLine("  .. " + st));
        Console.WriteLine("total ms " + sw.ElapsedMilliseconds);
        foreach (var l in r.log) Console.WriteLine(l);
        var img = r.RenderMap(size);
        using (var f = File.Create(Path.Combine(outDir, "map.ppm")))
        {
            var h = Encoding.ASCII.GetBytes("P6\n" + size + " " + size + "\n255\n");
            f.Write(h, 0, h.Length);
            for (int y = size - 1; y >= 0; y--) f.Write(img, y * size * 3, size * 3);   // north up
        }
        var sb = new StringBuilder();
        sb.Append("{\"worldSize\":" + s.worldSize + ",\"settlements\":[");
        for (int i = 0; i < r.settlements.Count; i++) { var t = r.settlements[i]; sb.AppendFormat("{0}{{\"name\":\"{1}\",\"type\":\"{2}\",\"x\":{3},\"z\":{4},\"y\":{5},\"tier\":{6},\"starter\":{7},\"biome\":\"{8}\"}}", i > 0 ? "," : "", t.name, t.type, t.x, t.z, t.y, t.dangerTier, t.isStarter ? "true" : "false", t.biome); }
        sb.Append("],\"caves\":[");
        for (int i = 0; i < r.caves.Count; i++) { var t = r.caves[i]; sb.AppendFormat("{0}{{\"name\":\"{1}\",\"x\":{2},\"z\":{3},\"y\":{4},\"yaw\":{5},\"tier\":{6},\"biome\":\"{7}\"}}", i > 0 ? "," : "", t.name, t.x, t.z, t.y, t.yaw, t.dangerTier, t.biome); }
        sb.Append("],\"bridges\":[");
        for (int i = 0; i < r.bridges.Count; i++) { var t = r.bridges[i]; sb.AppendFormat("{0}{{\"x\":{1},\"z\":{2},\"y\":{3},\"len\":{4},\"yaw\":{5}}}", i > 0 ? "," : "", t.x, t.z, t.y, t.length, t.yaw); }
        sb.Append("],\"trees\":[");
        for (int i = 0; i < r.trees.Count; i++) { var t = r.trees[i]; sb.AppendFormat("{0}[{1:0},{2:0},{3}]", i > 0 ? "," : "", t.x, t.z, (int)t.kind); }
        sb.Append("]}");
        File.WriteAllText(Path.Combine(outDir, "world.json"), sb.ToString(), Encoding.UTF8);
        // raw heights (downsampled x2) for a 3D preview
        int n2 = (r.N - 1) / 2 + 1; var hb = new byte[n2 * n2 * 4];
        for (int z = 0; z < n2; z++) for (int x = 0; x < n2; x++) Buffer.BlockCopy(BitConverter.GetBytes(r.height[(z * 2) * r.N + x * 2]), 0, hb, (z * n2 + x) * 4, 4);
        File.WriteAllBytes(Path.Combine(outDir, "height_" + n2 + ".f32"), hb);
    }
}

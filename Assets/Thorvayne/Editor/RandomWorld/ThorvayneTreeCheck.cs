using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Thorvayne.WorldGen.EditorTools
{
    // Thorvayne > Check Trees: reports why terrain trees might not show, and fixes what it safely can:
    // trees switched off on a terrain, a tree distance of 0, and trees left shrunk to nothing by woodcutting
    // (felled trees are hidden by scaling them to 0 and put back on Stop; a crash or a script reload can skip that).
    public static class ThorvayneTreeCheck
    {
        [MenuItem("Thorvayne/Check Trees")]
        static void Check()
        {
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            if (terrains.Length == 0) { Debug.LogWarning("Check Trees: no terrains in the open scene. Generate the world first."); return; }
            int total = 0, shrunk = 0, noPrefab = 0, fixedTerrains = 0;
            foreach (var t in terrains)
            {
                var td = t.terrainData;
                if (td == null) { Debug.LogWarning($"Check Trees: {t.name} has no terrain data."); continue; }
                bool changed = false;
                if (!t.drawTreesAndFoliage) { t.drawTreesAndFoliage = true; changed = true; }
                if (t.treeDistance < 100) { t.treeDistance = 800; t.treeBillboardDistance = 800; changed = true; }
                var protos = td.treePrototypes;
                noPrefab += protos.Count(p => p.prefab == null);
                var inst = td.treeInstances;
                total += inst.Length;
                bool fixedInst = false;
                for (int i = 0; i < inst.Length; i++)
                {
                    if (inst[i].widthScale > 0.01f && inst[i].heightScale > 0.01f) continue;
                    inst[i].widthScale = inst[i].heightScale = 1f;
                    shrunk++; fixedInst = true;
                }
                if (fixedInst) { td.SetTreeInstances(inst, true); EditorUtility.SetDirty(td); }
                if (changed) { EditorUtility.SetDirty(t); fixedTerrains++; }
            }
            // what each tree type is drawn with, from the first terrain (all tiles share the same tree types)
            var td0 = terrains[0].terrainData;
            var counts = new int[td0.treePrototypes.Length];
            foreach (var t in terrains)
                foreach (var ti in t.terrainData.treeInstances)
                    if (ti.prototypeIndex >= 0 && ti.prototypeIndex < counts.Length) counts[ti.prototypeIndex]++;
            var report = new System.Text.StringBuilder("Check Trees: tree types\n");
            for (int p = 0; p < td0.treePrototypes.Length; p++)
            {
                var prefab = td0.treePrototypes[p].prefab;
                report.Append($"  {p} {(prefab != null ? prefab.name : "(none)")}: {counts[p]} trees");
                if (prefab != null)
                {
                    var lod = prefab.GetComponent<LODGroup>();
                    report.Append(lod != null ? $", LODGroup {lod.lodCount} levels size {lod.size:0.##}" : ", no LODGroup");
                    foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        var mesh = mf != null ? mf.sharedMesh : null;
                        var mat = r.sharedMaterial;
                        report.Append($"\n      {r.name}: mesh {(mesh != null ? $"{mesh.name} {mesh.vertexCount} verts, size {mesh.bounds.size}" : "MISSING")}, " +
                                      $"pos {r.transform.localPosition} scale {r.transform.localScale}, " +
                                      $"material {(mat != null ? $"{mat.name} ({mat.shader.name})" : "MISSING")}, enabled {r.enabled}");
                    }
                }
                report.Append("\n");
            }
            Debug.Log(report.ToString());
            Debug.Log($"Check Trees: {terrains.Length} terrains, {total} trees. Put back {shrunk} shrunk trees, " +
                      $"switched trees back on for {fixedTerrains} terrains. Tree types with a missing prefab: {noPrefab}" +
                      (total == 0 ? ". There are no trees at all: generate the world again (seed 1234)." : "."));
        }
    }
}

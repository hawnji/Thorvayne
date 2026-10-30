# Turns a high-poly textured axe (e.g. a ComfyUI .glb) into the game axe held in the hero's hand:
# decimated to ~3k triangles, its colours baked onto a fresh texture, and placed in the grip's frame
# (origin where the hand holds it, Y up the handle toward the head, Z the way the blade's edge faces).
# Faces are split into two materials, AxeWood and AxeMetal (the head), so the game can tint the metal.
# Output: ThorvayneAxe.blend (object "AxeModel"), ThorvayneAxe_Albedo.png and ThorvayneAxe_Metal.png (light grey). add_axe.py puts it in the hand.
# Usage: python make_axe.py -- in.glb out_dir [length_m=0.75] [tris=3000]
import bpy, sys, math, os
import numpy as np
from mathutils import Vector
args = sys.argv[sys.argv.index('--')+1:]
src_path, out_dir = args[:2]
LENGTH = float(args[2]) if len(args) > 2 else 0.75
TRIS = int(args[3]) if len(args) > 3 else 3000
GRIP = 0.13          # metres from the end of the handle to where the hand holds it
TEX = 1024

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src_path)
sc = bpy.context.scene
axe = [o for o in sc.objects if o.type == 'MESH'][0]
for o in list(sc.objects):
    if o is not axe: bpy.data.objects.remove(o)
axe.parent = None
bpy.context.view_layer.objects.active = axe; axe.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
# ComfyUI meshes come split along every texture seam; weld them first, or decimating opens holes along the seams
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=1e-5)
bpy.ops.object.mode_set(mode='OBJECT')

# find the handle (the long thin part) and the blade (the point furthest from the handle's line)
P = np.array([v.co[:] for v in axe.data.vertices])
lo, hi_ = P.min(0), P.max(0); up = int(np.argmax(hi_ - lo))          # the longest side runs along the handle
t = (P[:, up] - lo[up]) / (hi_[up] - lo[up])
def stem(sel):                                                     # centre line of the vertices in sel
    Q = P[sel]; c = Q.mean(0); a = np.linalg.svd(Q - c, full_matrices=False)[2][0]; return c, a
c, a = stem(t < 0.5); c2, _ = stem(t > 0.5)                       # the head sits at one end
if np.dot(c2 - c, a) < 0: a = -a                                  # point it toward the head
proj = (P - c) @ a
r = (P - c) - np.outer(proj, a)
tip = int(np.argmax(np.linalg.norm(r, axis=1) * (proj > np.percentile(proj, 60))))
e = r[tip] / np.linalg.norm(r[tip])
a, e, c = Vector(a), Vector(e), Vector(c)
side = a.cross(e).normalized(); e = side.cross(a).normalized()
k = LENGTH / (proj.max() - proj.min())
grip = c + a * (proj.min() + GRIP / k)
print('handle', tuple(round(x, 3) for x in a), 'blade', tuple(round(x, 3) for x in e), 'scale', round(k, 3))
for v in axe.data.vertices:
    d = v.co - grip
    v.co = Vector((d.dot(side), d.dot(a), d.dot(e))) * k
axe.data.update()

hi = axe.copy(); hi.data = axe.data.copy(); sc.collection.objects.link(hi)   # full detail: bake source
mod = axe.modifiers.new('dec', 'DECIMATE'); mod.ratio = TRIS / len(axe.data.polygons)
bpy.ops.object.modifier_apply(modifier='dec')
axe.name = axe.data.name = 'AxeModel'
print('triangles', sum(len(p.vertices) - 2 for p in axe.data.polygons))

# colour texture: fresh UVs on the reduced axe, colours baked from the full-detail model
me = axe.data
while me.uv_layers: me.uv_layers.remove(me.uv_layers[0])
me.uv_layers.new(name='UVMap')
bpy.ops.object.select_all(action='DESELECT'); axe.select_set(True); bpy.context.view_layer.objects.active = axe
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004)
bpy.ops.object.mode_set(mode='OBJECT')
alb = bpy.data.images.new('ThorvayneAxe_Albedo', TEX, TEX)
bmat = bpy.data.materials.new('Bake'); bmat.use_nodes = True
bt = bmat.node_tree.nodes.new('ShaderNodeTexImage'); bt.image = alb; bmat.node_tree.nodes.active = bt
me.materials.clear(); me.materials.append(bmat)
sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
bk = sc.render.bake
bk.use_selected_to_active = True; bk.cage_extrusion = 0.006; bk.max_ray_distance = 0.02; bk.margin = 8
bk.use_pass_direct = False; bk.use_pass_indirect = False; bk.use_pass_color = True
hi.select_set(True); axe.select_set(True); bpy.context.view_layer.objects.active = axe
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
alb.filepath_raw = os.path.join(out_dir, 'ThorvayneAxe_Albedo.png'); alb.file_format = 'PNG'; alb.save()
bpy.data.objects.remove(hi)

# wood or metal: the head is everything up top that sticks out past the handle's thickness
V = np.array([v.co[:] for v in me.vertices])
dist = np.hypot(V[:, 0], V[:, 2]); y = V[:, 1]
radius = np.median(dist[(y > 0) & (y < 0.25)])                    # the handle around and just above the hand
y_head = y[dist > 2 * radius].min()
metal = []
for p in me.polygons:
    f = np.array(p.center[:]); metal.append(f[1] > y_head - 0.01 and np.hypot(f[0], f[2]) > 1.2 * radius)
near = [set() for _ in me.polygons]; by_edge = {}
for p in me.polygons:
    for ek in p.edge_keys: by_edge.setdefault(ek, []).append(p.index)
for fs in by_edge.values():
    for f in fs: near[f].update(fs)
for _ in range(2):
    metal = [sum(metal[n] for n in near[f]) * 2 > len(near[f]) for f in range(len(metal))]

# the metal gets its own light grey copy of the texture, so the game can colour it per axe (bronze, iron, rune...)
px = np.array(alb.pixels[:]).reshape(TEX, TEX, 4)
uv = me.uv_layers[0].data
lum = px[..., :3] @ np.array([0.3, 0.59, 0.11])
ml = []
for p, ism in zip(me.polygons, metal):
    if not ism: continue
    u = sum((uv[i].uv for i in p.loop_indices), Vector((0, 0))) / p.loop_total
    ml.append(lum[min(TEX - 1, int(u.y * TEX)), min(TEX - 1, int(u.x * TEX))])
gain = 0.85 / max(float(np.median(ml)), 1e-3)                    # typical metal ends up near white
g = np.clip(lum * gain, 0, 1)
gi = bpy.data.images.new('ThorvayneAxe_Metal', TEX, TEX)
gi.pixels[:] = np.dstack([g, g, g, np.ones_like(g)]).ravel()
gi.filepath_raw = os.path.join(out_dir, 'ThorvayneAxe_Metal.png'); gi.file_format = 'PNG'; gi.save()
print('handle radius', round(float(radius), 4), 'head from', round(float(y_head), 3), 'metal brightened x', round(gain, 2))

me.materials.clear()
for n, im in (('AxeWood', alb), ('AxeMetal', gi)):
    m = bpy.data.materials.new(n); m.use_nodes = True
    tx = m.node_tree.nodes.new('ShaderNodeTexImage'); tx.image = im
    m.node_tree.links.new(tx.outputs[0], m.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    me.materials.append(m)
for p, ism in zip(me.polygons, metal): p.material_index = int(ism)
print('metal faces', sum(metal), 'of', len(metal))
bpy.data.materials.remove(bmat)
for im in list(bpy.data.images):
    if im not in (alb, gi): bpy.data.images.remove(im)
bpy.data.orphans_purge(do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, 'ThorvayneAxe.blend'))

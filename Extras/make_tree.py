# Turns a high-poly textured tree (e.g. a ComfyUI .glb) into a game-ready terrain tree:
#   LOD0  - the tree decimated to ~20k triangles, its colours baked onto a fresh texture (close up)
#   LOD1  - two crossed cards showing pictures of the tree (far away, 4 triangles)
# Output: <name>.fbx (objects LOD0 and LOD1), <name>_Albedo.png, <name>_Impostor.png (with alpha).
# Usage: python make_tree.py -- in.glb out_dir Broadleaf [height_m=7] [tris=20000]
import bpy, sys, math, os
from mathutils import Vector
args = sys.argv[sys.argv.index('--')+1:]
src_path, out_dir, name = args[:3]
HEIGHT = float(args[3]) if len(args) > 3 else 7.0
TRIS = int(args[4]) if len(args) > 4 else 20000
TEX = 2048; IMP = 512
SINK = 0.04          # share of the height that goes below ground

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src_path)
sc = bpy.context.scene
tree = [o for o in sc.objects if o.type == 'MESH'][0]
for o in list(sc.objects):
    if o is not tree: bpy.data.objects.remove(o)
tree.parent = None
bpy.context.view_layer.objects.active = tree; tree.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
# ComfyUI meshes come split along every texture seam; weld them first, or decimating opens holes along the seams
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=1e-5)
bpy.ops.object.mode_set(mode='OBJECT')

# stand the trunk up straight: generated trees often lean, and with each tree turned a different way on the
# terrain the lean shows as trees tipped in random directions. Fit a line through the middle of the trunk
# (the box centre of thin slices between 6% and 30% of the height) and shear the tree so that line is vertical.
import numpy as np
P = np.array([v.co[:] for v in tree.data.vertices])
zmin, zmax = P[:, 2].min(), P[:, 2].max(); H = zmax - zmin
zs, xs, ys = [], [], []
for f in np.arange(0.06, 0.30, 0.02):
    sl = P[(P[:, 2] >= zmin + f * H) & (P[:, 2] < zmin + (f + 0.02) * H)]
    if len(sl) < 3: continue
    zs.append(sl[:, 2].mean()); xs.append((sl[:, 0].min() + sl[:, 0].max()) / 2); ys.append((sl[:, 1].min() + sl[:, 1].max()) / 2)
ax, bx = np.polyfit(zs, xs, 1); ay, by = np.polyfit(zs, ys, 1)
print('trunk lean', round(float(np.degrees(np.arctan(np.hypot(ax, ay)))), 1), 'degrees, straightened')
for v in tree.data.vertices:
    v.co.x -= ax * (v.co.z - zmin); v.co.y -= ay * (v.co.z - zmin)
cx, cy = ax * zmin + bx, ay * zmin + by               # the trunk's centre at the ground: the pivot

# real size, pivot at the bottom of the trunk, sunk a little so root tips that lift off the ground stay in it
k = HEIGHT / H
tree.location = (-cx * k, -cy * k, -zmin * k - SINK * HEIGHT); tree.scale = (k, k, k)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

hi = tree.copy(); hi.data = tree.data.copy(); sc.collection.objects.link(hi)   # full detail: bake source and far pictures
mod = tree.modifiers.new('dec', 'DECIMATE'); mod.ratio = TRIS / len(tree.data.polygons)
bpy.ops.object.modifier_apply(modifier='dec')
tree.name = tree.data.name = 'LOD0'
print('LOD0 triangles', sum(len(p.vertices) - 2 for p in tree.data.polygons))

# colour texture: fresh UVs on the reduced tree, colours baked from the full-detail model
# (reusing the original UVs smears the texture once the mesh is reduced)
me = tree.data
while me.uv_layers: me.uv_layers.remove(me.uv_layers[0])
me.uv_layers.new(name='UVMap')
bpy.ops.object.select_all(action='DESELECT'); tree.select_set(True); bpy.context.view_layer.objects.active = tree
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.003)
bpy.ops.object.mode_set(mode='OBJECT')
alb = bpy.data.images.new(name + '_Albedo', TEX, TEX)
lmat = bpy.data.materials.new('LOD0'); lmat.use_nodes = True
lt = lmat.node_tree.nodes.new('ShaderNodeTexImage'); lt.image = alb; lmat.node_tree.nodes.active = lt
me.materials.clear(); me.materials.append(lmat)
sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
bk = sc.render.bake
bk.use_selected_to_active = True; bk.cage_extrusion = 0.02; bk.max_ray_distance = 0.08; bk.margin = 8
bk.use_pass_direct = False; bk.use_pass_indirect = False; bk.use_pass_color = True
hi.select_set(True); tree.select_set(True); bpy.context.view_layer.objects.active = tree
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
alb.filepath_raw = os.path.join(out_dir, name + '_Albedo.png'); alb.file_format = 'PNG'; alb.save()

# impostor pictures of the full-detail model: flat colour (no lighting), front and side, transparent background
tree.hide_render = True
mat = hi.data.materials[0]
base = [n for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE' and any(l.to_socket.name == 'Base Color' for l in n.outputs[0].links)][0].image
V = hi.data.vertices
S = 1.02 * max(HEIGHT, 2 * max(abs(v.co.x) for v in V), 2 * max(abs(v.co.y) for v in V))
nt = mat.node_tree
for l in list(nt.links):
    if l.to_node.type == 'OUTPUT_MATERIAL': nt.links.remove(l)
em = nt.nodes.new('ShaderNodeEmission'); tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = base
nt.links.new(tx.outputs['Color'], em.inputs['Color'])
nt.links.new(em.outputs['Emission'], [n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL'][0].inputs['Surface'])
sc.render.engine = 'CYCLES'; sc.cycles.samples = 16; sc.cycles.use_denoising = False
sc.render.film_transparent = True; sc.view_settings.view_transform = 'Standard'
sc.render.resolution_x = sc.render.resolution_y = IMP
sc.render.image_settings.color_mode = 'RGBA'
cam_d = bpy.data.cameras.new('cam'); cam_d.type = 'ORTHO'; cam_d.ortho_scale = S
cam = bpy.data.objects.new('cam', cam_d); sc.collection.objects.link(cam); sc.camera = cam
shots = []
for i, (pos, look) in enumerate([((0, -50, S / 2), (0, 1, 0)), ((50, 0, S / 2), (-1, 0, 0))]):
    cam.location = pos; cam.rotation_euler = Vector(look).to_track_quat('-Z', 'Y').to_euler()
    p = os.path.join(out_dir, f'_imp{i}.png'); sc.render.filepath = p
    bpy.ops.render.render(write_still=True); shots.append(p)
a = bpy.data.images.load(shots[0]); b = bpy.data.images.load(shots[1])
imp = bpy.data.images.new(name + '_Impostor', 2 * IMP, IMP, alpha=True)
pa, pb = list(a.pixels), list(b.pixels); px = [0.0] * (2 * IMP * IMP * 4)
for y in range(IMP):
    row = y * 2 * IMP * 4
    px[row:row + IMP * 4] = pa[y * IMP * 4:(y + 1) * IMP * 4]
    px[row + IMP * 4:row + 2 * IMP * 4] = pb[y * IMP * 4:(y + 1) * IMP * 4]
imp.pixels = px
imp.filepath_raw = os.path.join(out_dir, name + '_Impostor.png'); imp.file_format = 'PNG'; imp.save()
for p in shots: os.remove(p)

# LOD1: two crossed cards (front card faces Y, side card faces X)
h = S / 2
verts = [(-h, 0, 0), (h, 0, 0), (h, 0, S), (-h, 0, S), (0, -h, 0), (0, h, 0), (0, h, S), (0, -h, S)]
me = bpy.data.meshes.new('LOD1'); me.from_pydata(verts, [], [(0, 1, 2, 3), (4, 5, 6, 7)])
uv = me.uv_layers.new(name='UVMap')
uvs = [(0, 0), (0.5, 0), (0.5, 1), (0, 1), (0.5, 0), (1, 0), (1, 1), (0.5, 1)]
for loop in me.loops: uv.data[loop.index].uv = uvs[loop.vertex_index]
lod1 = bpy.data.objects.new('LOD1', me); sc.collection.objects.link(lod1)

tree.hide_render = False; bpy.data.objects.remove(hi)
bpy.ops.object.select_all(action='DESELECT'); tree.select_set(True); lod1.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, name + '.fbx'), use_selection=True, object_types={'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    mesh_smooth_type='FACE', path_mode='STRIP', bake_anim=False)
print('size', S, 'exported', name)

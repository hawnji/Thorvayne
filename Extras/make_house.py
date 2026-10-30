# Turns a high-poly textured building (e.g. a ComfyUI .glb) into a game-ready house for the towns:
#   LOD0 - decimated to ~8k triangles, its colours baked onto a fresh texture
#   LOD1 - the same, reduced to a quarter (far away), sharing the texture
# The house is squared up to the axes (generated models come turned at an odd angle), its long side runs along X,
# the front (the side with the door) faces -Y (forward in Unity), and the pivot is the middle of the footprint at ground
# level, with the bottom SINK metres of the stone base below it.
# Output: <name>.fbx (objects LOD0 and LOD1) and <name>_Albedo.png.
# Usage: python make_house.py -- in.glb out_dir House [length_m=9] [front=0] [tris=8000]
#   front: quarter turns that bring the door side to the front. front=sides only renders <name>_sides.png
#   (the four sides, the one facing front first) to choose it.
import bpy, sys, math, os
import numpy as np
from mathutils import Vector
args = sys.argv[sys.argv.index('--')+1:]
src_path, out_dir, name = args[:3]
LENGTH = float(args[3]) if len(args) > 3 else 9.0
SIDES = len(args) > 4 and args[4] == 'sides'
FRONT = int(args[4]) if len(args) > 4 and not SIDES else 0
TRIS = int(args[5]) if len(args) > 5 else 8000
TEX = 2048
SINK = 0.9           # metres of the stone base that go below ground (the base is tall, and it hides uneven ground)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src_path)
sc = bpy.context.scene
house = [o for o in sc.objects if o.type == 'MESH'][0]
for o in list(sc.objects):
    if o is not house: bpy.data.objects.remove(o)
house.parent = None
bpy.context.view_layer.objects.active = house; house.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
# ComfyUI meshes come split along every texture seam; weld them first, or decimating opens holes along the seams
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=1e-5)
bpy.ops.object.mode_set(mode='OBJECT')

# square it up: turn it so the walls (the lowest quarter, below the roof) fill the smallest axis-aligned box
P = np.array([v.co[:] for v in house.data.vertices])
zmin, zmax = P[:, 2].min(), P[:, 2].max()
F = P[P[:, 2] < zmin + 0.25 * (zmax - zmin)][:, :2]
F = F[np.random.default_rng(0).choice(len(F), min(len(F), 20000), replace=False)]
best = None
for deg in np.arange(0, 90, 0.25):
    a = math.radians(deg); R2 = np.array([[math.cos(a), -math.sin(a)], [math.sin(a), math.cos(a)]])
    Q = F @ R2.T; ext = Q.max(0) - Q.min(0)
    if best is None or ext[0] * ext[1] < best[0]: best = (ext[0] * ext[1], deg, ext)
deg = best[1] + (90 if best[2][1] > best[2][0] else 0) + 90 * FRONT        # long side along X, then the chosen front
a = math.radians(deg); ca, sa = math.cos(a), math.sin(a)
Q = F @ np.array([[ca, -sa], [sa, ca]]).T
cx, cy = (Q.max(0) + Q.min(0)) / 2; length = (Q.max(0) - Q.min(0)).max()
k = LENGTH / length
for v in house.data.vertices:
    x, y = ca * v.co.x - sa * v.co.y, sa * v.co.x + ca * v.co.y
    v.co = Vector(((x - cx) * k, (y - cy) * k, (v.co.z - zmin) * k - SINK))
house.data.update()
W = (Q.max(0) - Q.min(0)) * k
print('turned', round(deg, 2), 'degrees; footprint', round(float(W[0]), 2), 'x', round(float(W[1]), 2), 'm, height', round(float((zmax - zmin) * k), 2), 'm')

def flat_colour(obj):
    """Shows the model's own texture without lighting (for pictures)."""
    for m in obj.data.materials:
        nt = m.node_tree
        img = [n for n in nt.nodes if n.type == 'TEX_IMAGE' and any(l.to_socket.name == 'Base Color' for l in n.outputs[0].links)]
        if not img: continue
        em = nt.nodes.new('ShaderNodeEmission'); nt.links.new(img[0].outputs['Color'], em.inputs['Color'])
        out = [n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL'][0]
        nt.links.new(em.outputs['Emission'], out.inputs['Surface'])

if SIDES:
    flat_colour(house)
    sc.render.engine = 'CYCLES'; sc.cycles.samples = 4; sc.view_settings.view_transform = 'Standard'
    sc.render.resolution_x, sc.render.resolution_y = 384, 300
    cd = bpy.data.cameras.new('cam'); cd.type = 'ORTHO'; cd.ortho_scale = LENGTH * 1.25
    cam = bpy.data.objects.new('cam', cd); sc.collection.objects.link(cam); sc.camera = cam
    shots = []
    for i, d in enumerate([(0, -1), (1, 0), (0, 1), (-1, 0)]):        # front, then going round
        dv = Vector((d[0], d[1], 0.15)).normalized()
        cam.location = Vector((0, 0, 3)) + dv * 40; cam.rotation_euler = (-dv).to_track_quat('-Z', 'Y').to_euler()
        p = os.path.join(out_dir, f'_side{i}.png'); sc.render.filepath = p; bpy.ops.render.render(write_still=True); shots.append(p)
    ims = [bpy.data.images.load(p) for p in shots]
    out = bpy.data.images.new(name + '_sides', 384 * 4, 300)
    px = np.zeros((300, 384 * 4, 4))
    for i, im in enumerate(ims): px[:, i * 384:(i + 1) * 384] = np.array(im.pixels[:]).reshape(300, 384, 4)
    out.pixels[:] = px.ravel(); out.filepath_raw = os.path.join(out_dir, name + '_sides.png'); out.file_format = 'PNG'; out.save()
    for p in shots: os.remove(p)
    sys.exit(0)

hi = house.copy(); hi.data = house.data.copy(); sc.collection.objects.link(hi)   # full detail: bake source
mod = house.modifiers.new('dec', 'DECIMATE'); mod.ratio = TRIS / len(house.data.polygons)
bpy.ops.object.modifier_apply(modifier='dec')
house.name = house.data.name = 'LOD0'
print('LOD0 triangles', sum(len(p.vertices) - 2 for p in house.data.polygons))

# colour texture: fresh UVs on the reduced house, colours baked from the full-detail model
me = house.data
while me.uv_layers: me.uv_layers.remove(me.uv_layers[0])
me.uv_layers.new(name='UVMap')
bpy.ops.object.select_all(action='DESELECT'); house.select_set(True); bpy.context.view_layer.objects.active = house
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.003)
bpy.ops.object.mode_set(mode='OBJECT')
alb = bpy.data.images.new(name + '_Albedo', TEX, TEX)
lmat = bpy.data.materials.new(name); lmat.use_nodes = True
lt = lmat.node_tree.nodes.new('ShaderNodeTexImage'); lt.image = alb; lmat.node_tree.nodes.active = lt
me.materials.clear(); me.materials.append(lmat)
sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
bk = sc.render.bake
bk.use_selected_to_active = True; bk.cage_extrusion = 0.03; bk.max_ray_distance = 0.1; bk.margin = 8
bk.use_pass_direct = False; bk.use_pass_indirect = False; bk.use_pass_color = True
hi.select_set(True); house.select_set(True); bpy.context.view_layer.objects.active = house
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
alb.filepath_raw = os.path.join(out_dir, name + '_Albedo.png'); alb.file_format = 'PNG'; alb.save()
bpy.data.objects.remove(hi)

# LOD1: the same house reduced to a quarter; decimating keeps the UVs, so it shares the texture
lod1 = house.copy(); lod1.data = house.data.copy(); sc.collection.objects.link(lod1)
lod1.name = lod1.data.name = 'LOD1'
mod = lod1.modifiers.new('dec', 'DECIMATE'); mod.ratio = 0.25
bpy.ops.object.select_all(action='DESELECT'); lod1.select_set(True); bpy.context.view_layer.objects.active = lod1
bpy.ops.object.modifier_apply(modifier='dec')
print('LOD1 triangles', sum(len(p.vertices) - 2 for p in lod1.data.polygons))

bpy.ops.object.select_all(action='DESELECT'); house.select_set(True); lod1.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, name + '.fbx'), use_selection=True, object_types={'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    mesh_smooth_type='FACE', path_mode='STRIP', bake_anim=False)
print('exported', name)

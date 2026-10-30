# Projects the colours of a textured copy of the character (e.g. the ComfyUI .glb) onto the rigged hero,
# gives the hero a clean UV layout, and re-exports the FBX plus a skin texture for Unity.
# Usage: python bake_skin.py -- ThorvayneHero_source.blend textured.glb out.blend out.fbx out_skin.png
import bpy, sys, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree
src, glb, out_blend, out_fbx, out_png = sys.argv[sys.argv.index('--')+1:][:5]
RES = 2048
bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene
rig = bpy.data.objects['Armature']; hero = bpy.data.objects['HeroMesh']
rig.animation_data.action = None
rig.data.pose_position = 'REST'
bpy.context.view_layer.update()

def world_bbox(o):
    dg = bpy.context.evaluated_depsgraph_get(); e = o.evaluated_get(dg)
    pts = [o.matrix_world @ v.co for v in e.data.vertices]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx

before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=glb)
src_obj = [o for o in bpy.data.objects if o not in before and o.type == 'MESH'][0]
bpy.ops.object.select_all(action='DESELECT'); src_obj.select_set(True); bpy.context.view_layer.objects.active = src_obj
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# line the textured copy up with the hero (same shape; Mixamo only changed scale and position)
hmn, hmx = world_bbox(hero); gmn, gmx = world_bbox(src_obj)
s = (hmx.z - hmn.z) / (gmx.z - gmn.z)
src_obj.scale = (s, s, s); bpy.ops.object.transform_apply(scale=True)
gmn, gmx = world_bbox(src_obj)
off = Vector(((hmn.x + hmx.x - gmn.x - gmx.x) / 2, (hmn.y + hmx.y - gmn.y - gmx.y) / 2, hmn.z - gmn.z))
src_obj.location = off; bpy.ops.object.transform_apply(location=True)
bvh = BVHTree.FromObject(src_obj, bpy.context.evaluated_depsgraph_get())
dg = bpy.context.evaluated_depsgraph_get(); he = hero.evaluated_get(dg)
d = sorted(bvh.find_nearest(hero.matrix_world @ v.co)[3] for v in list(he.data.vertices)[::20])
print('fit: median %.4f m, 95%% %.4f m, max %.4f m' % (d[len(d)//2], d[int(len(d)*0.95)], d[-1]))

# fresh, non-overlapping UVs on the hero
me = hero.data
while me.uv_layers: me.uv_layers.remove(me.uv_layers[0])
me.uv_layers.new(name='UVMap')
bpy.ops.object.select_all(action='DESELECT'); hero.select_set(True); bpy.context.view_layer.objects.active = hero
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004)
bpy.ops.object.mode_set(mode='OBJECT')

img = bpy.data.images.new('HeroSkin', RES, RES)
mat = hero.data.materials[0]; mat.use_nodes = True; nt = mat.node_tree
tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = img
nt.links.new(tex.outputs['Color'], nt.nodes['Principled BSDF'].inputs['Base Color'])
nt.nodes.active = tex

sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 1
bk = sc.render.bake
bk.use_selected_to_active = True; bk.cage_extrusion = 0.01; bk.max_ray_distance = 0.05; bk.margin = 8
bk.use_pass_direct = False; bk.use_pass_indirect = False; bk.use_pass_color = True
bpy.ops.object.select_all(action='DESELECT'); src_obj.select_set(True); hero.select_set(True); bpy.context.view_layer.objects.active = hero
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
img.filepath_raw = out_png; img.file_format = 'PNG'; img.save()
print('baked', out_png)

bpy.data.objects.remove(src_obj)
rig.data.pose_position = 'POSE'; rig.animation_data.action = bpy.data.actions['Idle']
bpy.ops.wm.save_as_mainfile(filepath=out_blend)
bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True); hero.select_set(True)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={'ARMATURE', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type='FACE', path_mode='STRIP')
print('exported', sorted(a.name for a in bpy.data.actions))

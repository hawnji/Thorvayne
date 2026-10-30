# Puts an axe (object "Axe") in the hero's right hand, parented to the RightHand bone, and re-exports
# the FBX. The axe is the model made by make_axe.py (ThorvayneAxe.blend), or two plain boxes without one.
# Unity shows or hides it (ThorvayneWoodcutting); it moves with the hand in every animation.
# The grip is worked out from the rest pose: palm facing down/in, fingers along the arm, axe head on the thumb side,
# handle tilted toward the fingers.
# Usage: python add_axe.py -- ThorvayneHero_source.blend out.blend out.fbx [ThorvayneAxe.blend]
import bpy, bmesh, sys
from mathutils import Vector, Matrix
args = sys.argv[sys.argv.index('--')+1:]
src, out_blend, out_fbx = args[:3]
model = args[3] if len(args) > 3 else None
bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene
rig = bpy.data.objects['Armature']; hero = bpy.data.objects['HeroMesh']
old = bpy.data.objects.get('Axe')
if old: bpy.data.objects.remove(old)
for n in ('AxeWood', 'AxeMetal'):                       # the old axe's materials, so the new ones keep their names
    if n in bpy.data.materials: bpy.data.materials.remove(bpy.data.materials[n])
rig.animation_data.action = None; rig.data.pose_position = 'REST'; bpy.context.view_layer.update()

P = 'mixamorig:'
def head(n): return rig.matrix_world @ rig.data.bones[P + n].head_local
hand, idx, ua = head('RightHand'), head('RightHandIndex1'), head('RightArm')
up = Vector((0, 0, 1)); fwd = Vector((0, -1, 0))          # the hero faces -Y
arm = (hand - ua).normalized(); palm = -(up - arm * up.dot(arm)).normalized()
fing = idx - hand; L = fing.length; fing = (fing - palm * fing.dot(palm)).normalized()
handle = fing.cross(palm).normalized()
if handle.dot(fwd) < 0: handle = -handle
# tilt the handle 45 degrees toward the fingers (a cocked wrist): the axe then points ahead at the end of the
# swing instead of straight up, and the blade edge faces the way the swing moves
handle = (handle + fing).normalized()
edge = (fing - handle * fing.dot(handle)).normalized()
side = handle.cross(edge)
grip = hand + fing * L * 0.6 + palm * 0.025
M = Matrix((side, handle, edge)).transposed().to_4x4(); M.translation = grip   # local X side, Y along handle, Z blade edge

if model:
    # already in the grip's frame: origin in the hand, Y up the handle, Z the blade's edge
    with bpy.data.libraries.load(model) as (src_data, dst):
        dst.objects = ['AxeModel']
    me = dst.objects[0].data; bpy.data.objects.remove(dst.objects[0])
else:
    me = bpy.data.meshes.new('Axe'); bm = bmesh.new()
    def box(center, size, mat):
        r = bmesh.ops.create_cube(bm, size=1.0)
        for v in r['verts']:
            v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2])) + Vector(center)
        for f in {f for v in r['verts'] for f in v.link_faces}: f.material_index = mat
    box((0, 0.23, 0), (0.035, 0.7, 0.035), 0)        # handle, wood
    box((0, 0.5, 0.07), (0.035, 0.16, 0.22), 1)      # head, metal
    bm.to_mesh(me); bm.free()
    for n, c in (('AxeWood', (0.45, 0.3, 0.15, 1)), ('AxeMetal', (0.62, 0.63, 0.66, 1))):
        m = bpy.data.materials.get(n) or bpy.data.materials.new(n); m.diffuse_color = c; me.materials.append(m)
me.name = 'Axe'
axe = bpy.data.objects.new('Axe', me); sc.collection.objects.link(axe)
axe.matrix_world = M
mw = axe.matrix_world.copy()
axe.parent = rig; axe.parent_type = 'BONE'; axe.parent_bone = P + 'RightHand'
axe.matrix_world = mw
bpy.context.view_layer.update()

rig.data.pose_position = 'POSE'; rig.animation_data.action = bpy.data.actions['Idle']
bpy.ops.wm.save_as_mainfile(filepath=out_blend)
bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True); hero.select_set(True); axe.select_set(True)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={'ARMATURE', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type='FACE', path_mode='STRIP')
print('exported with axe', sorted(a.name for a in bpy.data.actions))

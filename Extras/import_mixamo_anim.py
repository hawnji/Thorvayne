# Replaces clips on the hero with Mixamo animations (same mixamorig skeleton), then re-exports the FBX.
# Each Mixamo FBX is sampled frame by frame: bone rotations are copied as they are, and the hips' movement
# (measured in the world) is rescaled to the hero's height, with the feet put back on the ground.
# Download from Mixamo with "In Place" ticked (no forward travel).
# Usage: python import_mixamo_anim.py -- ThorvayneHero_source.blend out.blend out.fbx Idle=idle.fbx Walk=walk.fbx
import bpy, sys
from mathutils import Vector
args = sys.argv[sys.argv.index('--')+1:]
src, out_blend, out_fbx = args[:3]
jobs = [a.split('=', 1) for a in args[3:]]
bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene
rig = bpy.data.objects['Armature']; hero = bpy.data.objects['HeroMesh']
P = 'mixamorig:'
for b in rig.pose.bones: b.rotation_mode = 'QUATERNION'
def height(r):
    top = r.matrix_world @ r.data.bones[P + 'HeadTop_End'].head_local
    toe = r.matrix_world @ r.data.bones[P + 'LeftToe_End'].head_local
    return top.z - toe.z
H = height(rig)
hip_rest_inv = rig.data.bones[P + 'Hips'].matrix_local.to_3x3().inverted()
W_inv = rig.matrix_world.to_3x3().inverted()

for name, path in jobs:
    before_o, before_a = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False)
    new_objs = [o for o in bpy.data.objects if o not in before_o]
    srig = [o for o in new_objs if o.type == 'ARMATURE'][0]
    sact = [a for a in bpy.data.actions if a not in before_a][0]
    srig.animation_data.action = sact
    k = H / height(srig)                                            # source size -> hero size
    s_rest = srig.matrix_world @ srig.data.bones[P + 'Hips'].head_local
    f0, f1 = int(sact.frame_range[0]), int(sact.frame_range[1])

    old = bpy.data.actions.get(name)
    if old: bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    rig.animation_data_create(); rig.animation_data.action = act
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        for pb in rig.pose.bones:
            sb = srig.pose.bones.get(pb.name)
            if sb is None: continue
            pb.rotation_quaternion = sb.matrix_basis.to_quaternion()
            pb.keyframe_insert('rotation_quaternion', frame=f - f0 + 1)
            if pb.name == P + 'Hips':
                delta = (srig.matrix_world @ sb.head - s_rest) * k    # hips movement in the world, hero-sized
                pb.location = hip_rest_inv @ (W_inv @ delta)
                pb.keyframe_insert('location', frame=f - f0 + 1)
    # Mixamo rest poses aren't at the same height as the animation, so put the feet back on the ground:
    # the lowest foot over the whole clip ends where the hero's feet are in its rest pose.
    feet = [P + n for n in ('LeftToe_End', 'RightToe_End', 'LeftFoot', 'RightFoot')]
    ground = min((rig.matrix_world @ rig.data.bones[n].head_local).z for n in feet)
    hb = rig.pose.bones[P + 'Hips']; low = []; locs = []
    for f in range(1, f1 - f0 + 2):
        sc.frame_set(f)
        low.append(min((rig.matrix_world @ rig.pose.bones[n].head).z for n in feet)); locs.append(hb.location.copy())
    drop = hip_rest_inv @ (W_inv @ Vector((0, 0, min(low) - ground)))
    for i, loc in enumerate(locs):
        hb.location = loc - drop
        hb.keyframe_insert('location', frame=i + 1)
    print(name, 'from', path, 'frames', f1 - f0 + 1, 'scale', k, 'lowered by', round(min(low) - ground, 3))
    for o in new_objs: bpy.data.objects.remove(o)
    for a in [a for a in bpy.data.actions if a not in before_a and a is not act]: bpy.data.actions.remove(a)

rig.animation_data.action = bpy.data.actions['Idle']
bpy.ops.wm.save_as_mainfile(filepath=out_blend)
bpy.ops.object.select_all(action='DESELECT')
for o in (rig, hero, bpy.data.objects.get('Axe')):
    if o: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={'ARMATURE', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type='FACE', path_mode='STRIP')
print('exported', sorted(a.name for a in bpy.data.actions))

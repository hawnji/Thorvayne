# Adds a "Grip" clip to the hero: the right hand closed in a fist (the finger pose from the first frame of Idle),
# then re-exports the FBX. Unity plays it on a layer that only moves the right fingers while an axe is in hand.
# Usage: python add_grip.py -- ThorvayneHero_source.blend out.blend out.fbx
import bpy, sys
src, out_blend, out_fbx = sys.argv[sys.argv.index('--')+1:][:3]
bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene
rig = bpy.data.objects['Armature']; hero = bpy.data.objects['HeroMesh']
for b in rig.pose.bones: b.rotation_mode = 'QUATERNION'
rig.animation_data.action = bpy.data.actions['Idle']; sc.frame_set(1)
pose = {b.name: (b.location.copy(), b.rotation_quaternion.copy()) for b in rig.pose.bones}
old = bpy.data.actions.get('Grip')
if old: bpy.data.actions.remove(old)
act = bpy.data.actions.new('Grip'); act.use_fake_user = True
rig.animation_data.action = act
for f in (1, 2):
    for b in rig.pose.bones:
        b.location, b.rotation_quaternion = pose[b.name]
        b.keyframe_insert('location', frame=f); b.keyframe_insert('rotation_quaternion', frame=f)
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

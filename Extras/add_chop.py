# Adds the Chop (woodcutting) clip to the finished hero and re-exports the FBX.
# Usage: python add_chop.py -- ThorvayneHero_source.blend out.blend out.fbx   (bpy module, or blender -b -P)
import bpy, sys, math
from mathutils import Vector, Quaternion
args = sys.argv[sys.argv.index('--')+1:]
src, out_blend, out_fbx = args[:3]
bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene
rig = bpy.data.objects['Armature']

# same helpers as mixamo_build.py
P = 'mixamorig:'
B = {'Hips': 'Hips', 'Spine': 'Spine', 'Chest': 'Spine1', 'UpperChest': 'Spine2', 'Neck': 'Neck', 'Head': 'Head'}
for s in ('Left', 'Right'):
    B.update({s + 'Shoulder': s + 'Shoulder', s + 'UpperArm': s + 'Arm', s + 'LowerArm': s + 'ForeArm', s + 'Hand': s + 'Hand',
              s + 'UpperLeg': s + 'UpLeg', s + 'LowerLeg': s + 'Leg', s + 'Foot': s + 'Foot', s + 'Toes': s + 'ToeBase'})
pb = rig.pose.bones
for b in pb: b.rotation_mode = 'QUATERNION'
W = rig.matrix_world.to_3x3()
Winv = W.inverted()
def ax(v): return (W.normalized().inverted() @ Vector(v)).normalized()
X, Y, Z = ax((1, 0, 0)), ax((0, 1, 0)), ax((0, 0, 1))
top = rig.matrix_world @ rig.data.bones[P + 'HeadTop_End'].head_local
foot = rig.matrix_world @ rig.data.bones[P + 'LeftToe_End'].head_local
HF = (top.z - foot.z) / 1.8

def rot(bone, axis, deg):
    rest = rig.data.bones[bone].matrix_local.to_quaternion()
    return rest.inverted() @ Quaternion(axis, math.radians(deg)) @ rest

def pose(frame, spec, hips_z=0.0):
    for b in pb:
        q = Quaternion()
        for axis, deg in spec.get(b.name, []): q = rot(b.name, axis, deg) @ q
        b.rotation_quaternion = q
        b.keyframe_insert('rotation_quaternion', frame=frame)
    hb = pb[P + 'Hips']
    rest = rig.data.bones[P + 'Hips'].matrix_local.to_3x3()
    hb.location = rest.inverted() @ (Winv @ Vector((0, 0, hips_z * HF)))
    hb.keyframe_insert('location', frame=frame)

def add(sp, bone, axis, deg): sp.setdefault(P + B[bone], []).append((axis, deg))
_a = rig.matrix_world @ rig.data.bones[P + 'LeftArm'].head_local; _b = rig.matrix_world @ rig.data.bones[P + 'LeftForeArm'].head_local
REST_DROP = math.degrees(math.atan2(_a.z - _b.z, abs(_b.x - _a.x)))
def arms_down(sp, deg):
    d = deg - REST_DROP
    add(sp, 'LeftUpperArm', Y, d); add(sp, 'RightUpperArm', Y, -d)
def elbows(sp, l, r=None):
    add(sp, 'LeftLowerArm', Z, -l); add(sp, 'RightLowerArm', Z, l if r is None else r)

def make(name, frames, fn):
    old = bpy.data.actions.get(name)
    if old: bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name)
    rig.animation_data_create(); rig.animation_data.action = act
    for f in range(0, frames + 1, 2):
        sp, hz = fn(f / frames)
        pose(f + 1, sp, hz)
    act.use_fake_user = True
    return act

def smooth(x): x = min(1.0, max(0.0, x)); return x * x * (3 - 2 * x)
def lerp(a, b, t): return a + (b - a) * t

# One overhead axe swing with the right hand: slow wind-up, fast strike at t=0.68, recover.
STRIKE = 0.68
def raise_amount(t):
    if t < 0.55: return lerp(0.2, 1.0, smooth(t / 0.55))
    if t < STRIKE: u = (t - 0.55) / (STRIKE - 0.55); return 1.0 - u * u
    return lerp(0.0, 0.2, smooth((t - STRIKE) / (1 - STRIKE)))

def chop(t):
    a = raise_amount(t)
    sp = {}; arms_down(sp, 80)
    add(sp, 'RightUpperArm', X, -lerp(30, 160, a))
    add(sp, 'RightUpperArm', Y, -lerp(0, 15, a))
    elbows(sp, 35, lerp(15, 100, a))
    add(sp, 'LeftUpperArm', X, -20); add(sp, 'LeftUpperArm', Y, 10)
    add(sp, 'Spine', Z, lerp(14, -16, a)); add(sp, 'Chest', Z, lerp(6, -8, a))
    add(sp, 'Spine', X, lerp(12, -4, a)); add(sp, 'Head', X, lerp(-6, 4, a))
    add(sp, 'LeftUpperLeg', X, -14); add(sp, 'RightUpperLeg', X, 10)
    add(sp, 'LeftLowerLeg', X, 12); add(sp, 'RightLowerLeg', X, 14)
    add(sp, 'LeftFoot', X, 6); add(sp, 'RightFoot', X, -12)
    return sp, -0.035

sc.render.fps = 30
make('Chop', 54, chop)   # 1.8 s per swing; the strike lands at 68% (ThorvayneWoodcutting.strikeTime)
rig.animation_data.action = bpy.data.actions['Idle']
bpy.ops.wm.save_as_mainfile(filepath=out_blend)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={'ARMATURE', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type='FACE')
print('exported', sorted(a.name for a in bpy.data.actions))

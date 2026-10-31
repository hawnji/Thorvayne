# Adds the Chop (woodcutting) clip to the finished hero and re-exports the FBX.
# A one-handed side swing: the axe is drawn back over the right shoulder, swung round level at waist height, and
# stops dead in the trunk at the strike (68% of the swing), holds a moment, then is pulled out for the next swing.
# The axe's path is written down directly (where the grip is and which way the handle and blade point); the arms
# follows it with IK (the right hand holds the grip) and is baked into the clip; the left arm hangs loose and
# swings a little against the body's turn.
# The tree stands TREE_DIST metres in front of the feet (ThorvayneWoodcutting.chopDistance).
# Usage: python add_chop.py -- ThorvayneHero_source.blend out.blend out.fbx   (bpy module, or blender -b -P)
import bpy, sys, math
from mathutils import Vector, Quaternion, Matrix
args = sys.argv[sys.argv.index('--')+1:]
src, out_blend, out_fbx = args[:3]
bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene
rig = bpy.data.objects['Armature']; axe = bpy.data.objects['Axe']

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
def smooth(x): x = min(1.0, max(0.0, x)); return x * x * (3 - 2 * x)

# ---- the swing, as keys over the cycle (t from 0 to 1). The hero faces -Y, its right is -X.
# angle: where the grip is around the body, degrees from straight ahead toward the right side
# reach: grip distance from the body's middle (m), height: grip height (m), tilt: handle raised above level (degrees)
# twist: upper body turned to the left (+) or right (-), degrees
STRIKE = 0.68
TREE_DIST = 1.2
KEYS = [  # t, angle, reach, height, tilt, twist
    (0.00, 35, 0.46, 1.16, 0, -8),
    (0.50, 112, 0.34, 1.45, 45, -42),    # drawn back over the right shoulder
    (STRIKE, 15, 0.46, 1.15, -5, 2),     # in the trunk
    (0.71, 18, 0.46, 1.16, -4, 0),       # small bounce back off the wood
    (0.80, 16, 0.46, 1.15, -5, 1),       # settles, held a moment
    (1.00, 35, 0.46, 1.16, 0, -8),       # pulled out, ready again (same as t = 0)
]
def swing(t):
    for (t0, *a), (t1, *b) in zip(KEYS, KEYS[1:]):
        if t0 <= t <= t1:
            u = (t - t0) / (t1 - t0)
            # the swing itself speeds up all the way into the wood and stops there; everything else eases in and out
            k = u ** 1.7 if t0 == 0.50 else smooth(u)
            return [x + (y - x) * k for x, y in zip(a, b)]
    return KEYS[-1][1:]

HANDLE_TO_HEAD = 0.52   # grip to the middle of the axe head (make_axe.py: 0.75 m axe held 0.13 m from the end)

def axe_matrix(angle, reach, height, tilt):
    a, tl = math.radians(angle), math.radians(tilt)
    out = Vector((-math.sin(a), -math.cos(a), 0))            # from the body out through the grip
    handle = (out * math.cos(tl) + Vector((0, 0, 1)) * math.sin(tl)).normalized()
    edge = Vector((math.cos(a), -math.sin(a), 0))            # the way the head travels in the swing
    side = handle.cross(edge)
    m = Matrix((side, handle, edge)).transposed().to_4x4()
    m.translation = out * reach + Vector((0, 0, height))
    return m

# where the axe sits in the right hand (from the rest pose, where add_axe.py put it)
rig.data.pose_position = 'REST'; bpy.context.view_layer.update()
hand_rest = rig.matrix_world @ rig.data.bones[P + 'RightHand'].matrix_local
axe_in_hand = hand_rest.inverted() @ axe.matrix_world
hand_len = rig.data.bones[P + 'RightHand'].length
rig.data.pose_position = 'POSE'

# torso and legs: keyed directly
_a = rig.matrix_world @ rig.data.bones[P + 'LeftArm'].head_local; _b = rig.matrix_world @ rig.data.bones[P + 'LeftForeArm'].head_local
REST_DROP = math.degrees(math.atan2(_a.z - _b.z, abs(_b.x - _a.x)))   # how far the rest pose's arms already hang

def body(t):
    angle, reach, height, tilt, twist = swing(t)
    sp = {}
    # left arm: hangs a little out from the side, elbow soft, carried forward a little as the body winds up
    add(sp, 'LeftUpperArm', Y, 72 - REST_DROP); add(sp, 'LeftUpperArm', X, -6 + twist * 0.3)
    add(sp, 'LeftLowerArm', Z, -18)
    for bone, share in (('Spine', 0.3), ('Chest', 0.35), ('UpperChest', 0.35)): add(sp, bone, Z, twist * share)
    lean = 8 * smooth((STRIKE - abs(t - STRIKE)) / STRIKE) if t <= 0.9 else 8 * smooth((1 - t) / 0.1) * 0.3
    add(sp, 'Spine', X, lean - 3); add(sp, 'Head', Z, -twist * 0.5); add(sp, 'Head', X, -4)
    # feet apart, left foot forward, knees soft
    add(sp, 'LeftUpperLeg', X, -16); add(sp, 'LeftUpperLeg', Y, -6); add(sp, 'RightUpperLeg', X, 10); add(sp, 'RightUpperLeg', Y, 6)
    add(sp, 'LeftLowerLeg', X, 14); add(sp, 'RightLowerLeg', X, 16)
    add(sp, 'LeftFoot', X, 4); add(sp, 'RightFoot', X, -10)
    return sp, -0.04

FRAMES = 54   # 1.8 s at 30 fps (ThorvayneWoodcutting.swingTime)
sc.render.fps = 30
old = bpy.data.actions.get('Chop')
if old: bpy.data.actions.remove(old)
act = bpy.data.actions.new('Chop')
rig.animation_data_create(); rig.animation_data.action = act
for f in range(FRAMES + 1):
    sp, hz = body(f / FRAMES)
    pose(f + 1, sp, hz)

# arms: IK onto the axe's path, then baked
rig.pose.ik_solver = 'LEGACY'
targets = {}
for side in ('Right',):
    e = bpy.data.objects.new('IK_' + side, None); sc.collection.objects.link(e); targets[side] = e
    c = pb[P + side + 'Hand'].constraints.new('IK')
    c.target = e; c.chain_count = 3; c.use_rotation = True; c.use_stretch = False
arm_bones = [P + 'Right' + n for n in ('Arm', 'ForeArm', 'Hand')]
baked = []
for f in range(FRAMES + 1):
    sc.frame_set(f + 1)
    am = axe_matrix(*swing(f / FRAMES)[:4])
    hand = am @ axe_in_hand.inverted()                       # where the right hand bone must be to hold the axe there
    targets['Right'].matrix_world = hand @ Matrix.Translation((0, hand_len, 0))   # IK aims the bone's tip
    bpy.context.view_layer.update()
    baked.append({n: rig.convert_space(pose_bone=pb[n], matrix=pb[n].matrix, from_space='POSE', to_space='LOCAL').to_quaternion()
                  for n in arm_bones})
for side in ('Right',):
    b = pb[P + side + 'Hand']; b.constraints.remove(b.constraints[-1]); bpy.data.objects.remove(targets[side])
for f, qs in enumerate(baked):
    for n, q in qs.items():
        pb[n].rotation_quaternion = q
        pb[n].keyframe_insert('rotation_quaternion', frame=f + 1)
act.use_fake_user = True
print('Chop: side swing,', FRAMES + 1, 'frames, strike at', STRIKE)

rig.animation_data.action = bpy.data.actions['Idle']
bpy.ops.wm.save_as_mainfile(filepath=out_blend)
bpy.ops.object.select_all(action='DESELECT')
for o in (rig, bpy.data.objects['HeroMesh'], axe): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={'ARMATURE', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type='FACE', path_mode='STRIP')
print('exported', sorted(a.name for a in bpy.data.actions))

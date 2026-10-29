import bpy, sys, math
from mathutils import Vector, Quaternion
args = sys.argv[sys.argv.index('--')+1:]
src, out_blend, out_fbx = args[:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src, automatic_bone_orientation=False)
sc = bpy.context.scene
print('fps', sc.render.fps)
rig = bpy.data.objects['Armature']
mesh = [o for o in sc.objects if o.type == 'MESH'][0]
mesh.name = 'HeroMesh'
run = bpy.data.actions[0]; run.name = 'Run'; run.use_fake_user = True

def fix_run():
    rig.animation_data.action = run
    f0, f1 = int(run.frame_range[0]), int(run.frame_range[1])
    hb = rig.pose.bones['mixamorig:Hips']
    rest3 = rig.data.bones['mixamorig:Hips'].matrix_local.to_3x3()
    W3 = rig.matrix_world.to_3x3()
    ground = min((rig.matrix_world @ rig.data.bones['mixamorig:' + n].head_local).z for n in ('LeftToe_End', 'RightToe_End', 'LeftFoot', 'RightFoot'))
    frames = list(range(f0, f1 + 1)); D = []; low = []
    for f in frames:
        sc.frame_set(f)
        D.append(W3 @ (rest3 @ hb.location))
        low.append(min((rig.matrix_world @ rig.pose.bones['mixamorig:' + n].head).z for n in ('LeftToe_End', 'RightToe_End', 'LeftFoot', 'RightFoot')))
    shift = min(low) - ground
    mx = sum(d.x for d in D) / len(D)
    for i, f in enumerate(frames):
        d = D[i].copy(); t = i / (len(frames) - 1)
        d.x -= mx
        d.y -= D[0].y + (D[-1].y - D[0].y) * t      # remove forward travel (in place)
        d.z -= shift
        hb.location = rest3.inverted() @ (W3.inverted() @ d)
        hb.keyframe_insert('location', frame=f)
    print('run fixed: lifted by', shift, 'travel removed', D[-1].y - D[0].y)

# lighten the mesh (weights are kept by the decimate modifier)
bpy.context.view_layer.objects.active = mesh
mod = mesh.modifiers.new('dec', 'DECIMATE'); mod.ratio = 40000 / len(mesh.data.polygons)
bpy.ops.object.modifier_apply(modifier='dec')
print('decimated', len(mesh.data.vertices), len(mesh.data.polygons))
mat = bpy.data.materials.new('HeroSkin'); mat.diffuse_color = (0.78, 0.66, 0.55, 1)
mesh.data.materials.clear(); mesh.data.materials.append(mat)

P = 'mixamorig:'
B = {'Hips': 'Hips', 'Spine': 'Spine', 'Chest': 'Spine1', 'UpperChest': 'Spine2', 'Neck': 'Neck', 'Head': 'Head'}
for s in ('Left', 'Right'):
    B.update({s + 'Shoulder': s + 'Shoulder', s + 'UpperArm': s + 'Arm', s + 'LowerArm': s + 'ForeArm', s + 'Hand': s + 'Hand',
              s + 'UpperLeg': s + 'UpLeg', s + 'LowerLeg': s + 'Leg', s + 'Foot': s + 'Foot', s + 'Toes': s + 'ToeBase'})
pb = rig.pose.bones
for b in pb: b.rotation_mode = 'QUATERNION'
W = rig.matrix_world.to_3x3()
Winv = W.inverted()
def ax(v): return (W.normalized().inverted() @ Vector(v)).normalized()   # world axis -> armature space
X, Y, Z = ax((1, 0, 0)), ax((0, 1, 0)), ax((0, 0, 1))
top = rig.matrix_world @ rig.data.bones[P + 'HeadTop_End'].head_local
foot = rig.matrix_world @ rig.data.bones[P + 'LeftToe_End'].head_local
HF = (top.z - foot.z) / 1.8      # world units per "metre" of a 1.8 m character

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
# the bind pose may be an A-pose: measure how far the arms already hang below horizontal
_a = rig.matrix_world @ rig.data.bones[P + 'LeftArm'].head_local; _b = rig.matrix_world @ rig.data.bones[P + 'LeftForeArm'].head_local
REST_DROP = math.degrees(math.atan2(_a.z - _b.z, abs(_b.x - _a.x)))
print('rest arm drop', REST_DROP)
def arms_down(sp, deg):
    """deg = target angle of the upper arms below horizontal."""
    d = deg - REST_DROP
    add(sp, 'LeftUpperArm', Y, d); add(sp, 'RightUpperArm', Y, -d)
def elbows(sp, l, r=None):
    add(sp, 'LeftLowerArm', Z, -l); add(sp, 'RightLowerArm', Z, l if r is None else r)

def make(name, frames, fn):
    act = bpy.data.actions.new(name)
    rig.animation_data_create(); rig.animation_data.action = act
    for f in range(0, frames + 1, 2):
        sp, hz = fn(f / frames)
        pose(f + 1, sp, hz)
    act.use_fake_user = True
    return act

S = lambda t, ph=0.0: math.sin(2 * math.pi * (t + ph))
C = lambda t, ph=0.0: math.cos(2 * math.pi * (t + ph))

def idle(t):
    sp = {}; arms_down(sp, 80)
    add(sp, 'Chest', X, -1.5 * S(t)); add(sp, 'Spine', Z, 1.0 * S(t, 0.25)); add(sp, 'Head', X, 1.0 * S(t, 0.1))
    elbows(sp, 4)
    add(sp, 'LeftUpperArm', X, 4 + 2 * S(t)); add(sp, 'RightUpperArm', X, 4 + 2 * S(t))
    return sp, -0.004 + 0.004 * S(t, 0.25)

def walk(t):
    sp = {}; arms_down(sp, 80)
    s = S(t); leg, knee, arm, lean, bob, tw = 24, 42, 18, 3, 0.03, 4
    add(sp, 'LeftUpperLeg', X, -leg * s); add(sp, 'RightUpperLeg', X, leg * s)
    kl = knee * max(0.0, S(t, 0.15)) ** 1.2 + 6; kr = knee * max(0.0, -S(t, 0.15)) ** 1.2 + 6
    add(sp, 'LeftLowerLeg', X, kl); add(sp, 'RightLowerLeg', X, kr)
    add(sp, 'LeftFoot', X, -0.4 * leg * s - 0.3 * kl); add(sp, 'RightFoot', X, 0.4 * leg * s - 0.3 * kr)
    add(sp, 'LeftUpperArm', X, 3 + arm * s); add(sp, 'RightUpperArm', X, 3 - arm * s)
    elbows(sp, 5 + 8 * max(0, s), 5 + 8 * max(0, -s))
    add(sp, 'Spine', X, lean); add(sp, 'Spine', Z, tw * s); add(sp, 'Chest', Z, tw * s); add(sp, 'Hips', Z, -tw * 0.8 * s)
    add(sp, 'Head', X, -lean * 0.6)
    return sp, -bob * abs(C(t))

def jump(t):
    sp = {}; arms_down(sp, 55); w = S(t)
    add(sp, 'LeftUpperArm', X, -25 + 4 * w); add(sp, 'RightUpperArm', X, -25 + 4 * w)
    elbows(sp, 25)
    add(sp, 'LeftUpperLeg', X, -35 + 3 * w); add(sp, 'RightUpperLeg', X, -10 - 3 * w)
    add(sp, 'LeftLowerLeg', X, 70); add(sp, 'RightLowerLeg', X, 35)
    add(sp, 'Spine', X, 6)
    return sp, 0.05

def swim(t):
    sp = {}; s = S(t); c = C(t)
    add(sp, 'Hips', X, 25); add(sp, 'Spine', X, 5); add(sp, 'Head', X, -25)
    arms_down(sp, 15 + 25 * c)
    add(sp, 'LeftUpperArm', X, -55 + 20 * s); add(sp, 'RightUpperArm', X, -55 + 20 * s)
    elbows(sp, 20 + 25 * max(0, s))
    k = S(t * 2)
    add(sp, 'LeftUpperLeg', X, 12 * k); add(sp, 'RightUpperLeg', X, -12 * k)
    add(sp, 'LeftLowerLeg', X, 20 + 15 * max(0, k)); add(sp, 'RightLowerLeg', X, 20 + 15 * max(0, -k))
    return sp, 0.0

def fly(t):
    sp = {}; s = S(t)
    add(sp, 'Hips', X, 55); add(sp, 'Spine', X, 5 + 2 * s); add(sp, 'Head', X, -45)
    arms_down(sp, 80)
    add(sp, 'LeftUpperArm', X, 35 + 4 * s); add(sp, 'RightUpperArm', X, 35 + 4 * s)
    add(sp, 'LeftUpperLeg', X, 5 + 4 * s); add(sp, 'RightUpperLeg', X, 5 - 4 * s)
    add(sp, 'LeftLowerLeg', X, 15); add(sp, 'RightLowerLeg', X, 15)
    return sp, 0.0

fix_run()
sc.render.fps = 30
make('Idle', 60, idle); make('Walk', 32, walk); make('Jump', 30, jump); make('Swim', 40, swim); make('Fly', 40, fly)
rig.animation_data.action = bpy.data.actions['Idle']
bpy.ops.wm.save_as_mainfile(filepath=out_blend)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={'ARMATURE', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type='FACE')
print('exported', sorted(a.name for a in bpy.data.actions))

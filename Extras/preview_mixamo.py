import bpy, sys, math
from mathutils import Vector
args = sys.argv[sys.argv.index('--')+1:]
src, outdir = args[:2]
bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene
rig = bpy.data.objects['Armature']; mesh = bpy.data.objects['HeroMesh']
top = rig.matrix_world @ rig.data.bones['mixamorig:HeadTop_End'].head_local
foot = rig.matrix_world @ rig.data.bones['mixamorig:LeftToe_End'].head_local
H = top.z - foot.z; ground = foot.z
sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 12; sc.cycles.use_denoising = False
sc.render.resolution_x = 320; sc.render.resolution_y = 420
w = bpy.data.worlds.new('w'); w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1); sc.world = w
cam_d = bpy.data.cameras.new('cam'); cam_d.lens = 50; cam_d.clip_start = H * 0.01; cam_d.clip_end = H * 100
cam = bpy.data.objects.new('cam', cam_d); sc.collection.objects.link(cam); sc.camera = cam
target = Vector((0, 0, ground + H * 0.53))
ang = math.radians(-35)
cam.location = target + Vector((math.sin(ang) * -4.2, -math.cos(ang) * 4.2, 0.6)) * (H / 1.8)
cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
sun = bpy.data.lights.new('sun', 'SUN'); sun.energy = 3.5
so = bpy.data.objects.new('sun', sun); so.rotation_euler = (math.radians(50), 0, math.radians(-30)); sc.collection.objects.link(so)
bpy.ops.mesh.primitive_circle_add(radius=0.8 * H / 1.8, fill_type='NGON', location=(0, 0, ground))
g = bpy.context.active_object; gm = bpy.data.materials.new('g'); gm.use_nodes = True
gm.node_tree.nodes['Principled BSDF'].inputs[0].default_value = (0.3, 0.45, 0.25, 1); g.data.materials.append(gm)
m = bpy.data.materials['HeroSkin']; m.use_nodes = True; m.node_tree.nodes['Principled BSDF'].inputs[0].default_value = (0.78, 0.66, 0.55, 1)
shots = [('Idle', 1), ('Walk', 1), ('Walk', 9), ('Run', 1), ('Run', 13), ('Jump', 1), ('Swim', 10), ('Fly', 1)]
for i, (act, fr) in enumerate(shots):
    rig.animation_data.action = bpy.data.actions[act]
    sc.frame_set(fr)
    sc.render.filepath = f"{outdir}/shot_{i}_{act}_{fr}.png"
    bpy.ops.render.render(write_still=True)

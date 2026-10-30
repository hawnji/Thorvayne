# Renders the inventory icons: one per item in ThorvayneItems (axes from the real axe model with the head tinted
# per metal, logs as a small cut log in the log's colour). Transparent 128x128 PNGs named after the item id,
# with a dark outline and a soft shadow so they read on any slot. Output goes to Assets/Thorvayne/Resources/Icons.
# Usage: python make_icons.py -- Extras/ThorvayneAxe.blend Assets/Thorvayne/Characters out_dir   (needs Pillow in Blender's Python)
import bpy, sys, math, os
from mathutils import Vector, Matrix
axe_blend, tex_dir, out_dir = sys.argv[sys.argv.index('--')+1:][:3]
SIZE = 128

# keep in step with ThorvayneItems (Scripts/ThorvayneInventory.cs): id -> sRGB colour
AXES = {'bronze_axe': (0.72, 0.46, 0.24), 'iron_axe': (0.45, 0.45, 0.48), 'steel_axe': (0.72, 0.74, 0.77),
        'mithril_axe': (0.33, 0.38, 0.7), 'adamant_axe': (0.3, 0.55, 0.35), 'rune_axe': (0.3, 0.7, 0.82)}
LOGS = {'logs': (0.55, 0.38, 0.22), 'pine_logs': (0.66, 0.5, 0.3), 'palm_logs': (0.8, 0.66, 0.42),
        'swamp_logs': (0.35, 0.3, 0.2), 'frost_logs': (0.72, 0.8, 0.86)}
def lin(c): return tuple(x ** 2.2 for x in c) + (1,)

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.engine = 'CYCLES'; sc.cycles.samples = 48; sc.cycles.use_denoising = True
sc.render.resolution_x = sc.render.resolution_y = SIZE * 2
sc.render.film_transparent = True; sc.render.image_settings.color_mode = 'RGBA'
sc.view_settings.view_transform = 'Standard'
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.75, 0.8, 0.9, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.9
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun)
sun.data.energy = 3.5; sun.rotation_euler = (math.radians(40), math.radians(-25), math.radians(30))
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'

def frame(objs):
    # camera looks straight down -Y at the object, which is laid out diagonally in the XZ plane
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    mn = Vector([min(p[i] for p in pts) for i in range(3)]); mx = Vector([max(p[i] for p in pts) for i in range(3)])
    ctr = (mn + mx) / 2
    cam.location = ctr + Vector((0, -5, 0)); cam.rotation_euler = (math.radians(90), 0, 0)
    cam.data.ortho_scale = max(mx.x - mn.x, mx.z - mn.z) * 1.12

def shot(name):
    sc.render.filepath = os.path.join(out_dir, '_raw_' + name + '.png'); bpy.ops.render.render(write_still=True)

def tinted(mat, colour):
    nt = mat.node_tree; bsdf = nt.nodes['Principled BSDF']
    src = bsdf.inputs['Base Color'].links[0].from_socket
    mul = nt.nodes.new('ShaderNodeVectorMath'); mul.operation = 'MULTIPLY'
    nt.links.new(src, mul.inputs[0]); mul.inputs[1].default_value = lin(colour)[:3]
    nt.links.new(mul.outputs[0], bsdf.inputs['Base Color'])
    bsdf.inputs['Metallic'].default_value = 0.35; bsdf.inputs['Roughness'].default_value = 0.4

# ---- axes ----
with bpy.data.libraries.load(axe_blend) as (src, dst): dst.objects = ['AxeModel']
axe = dst.objects[0]; sc.collection.objects.link(axe)
for im in bpy.data.images:                                          # the axe's textures, wherever they are now
    if im.name.startswith('ThorvayneAxe_'): im.filepath = os.path.join(tex_dir, im.name.split('.')[0] + '.png'); im.reload()
# handle from bottom-left to top-right, blade facing up-left (Y is the handle, Z the blade edge in the model)
lay = Matrix(((0, 0, -1), (-1, 0, 0), (0, 1, 0))).to_4x4()          # model X,Y,Z -> into the view, up, left
axe.matrix_world = Matrix.Rotation(math.radians(45), 4, 'Y') @ lay   # then lean the handle to the right
bpy.context.view_layer.update()
frame([axe])
metal = axe.data.materials[1]; base_metal = metal.copy()
for id_, col in AXES.items():
    m = base_metal.copy(); tinted(m, col); axe.data.materials[1] = m
    shot(id_)
bpy.data.objects.remove(axe)

# ---- logs: a short tapered log with a cut end showing rings ----
bpy.ops.mesh.primitive_cylinder_add(vertices=14, radius=0.16, depth=0.9)
log = bpy.context.active_object
for v in log.data.vertices:
    k = 1.0 + 0.1 * (v.co.z / 0.45)
    v.co.x *= k; v.co.y *= k
bpy.ops.object.shade_smooth()
bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.05, depth=0.16, location=(0.17, 0, 0.1), rotation=(0, math.radians(70), 0))
stub = bpy.context.active_object
for o in (log, stub): o.select_set(True)
bpy.context.view_layer.objects.active = log; bpy.ops.object.join()
log.rotation_euler = (0, math.radians(55), math.radians(-25))
bpy.context.view_layer.update()
def log_mat(col):
    m = bpy.data.materials.new('log'); nt = m.node_tree; bsdf = nt.nodes['Principled BSDF']
    bsdf.inputs['Roughness'].default_value = 0.85
    # bark: the log's colour with dark streaks along the log; cut ends: lighter wood with rings
    tc = nt.nodes.new('ShaderNodeTexCoord'); geo = nt.nodes.new('ShaderNodeNewGeometry')
    noise = nt.nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value = 3
    sep = nt.nodes.new('ShaderNodeMapping'); sep.inputs['Scale'].default_value = (14, 14, 1.5)
    nt.links.new(tc.outputs['Object'], sep.inputs[0]); nt.links.new(sep.outputs[0], noise.inputs[0])
    bark = nt.nodes.new('ShaderNodeValToRGB'); nt.links.new(noise.outputs['Fac'], bark.inputs[0])
    c = lin(col); bark.color_ramp.elements[0].color = tuple(x * 0.35 for x in c[:3]) + (1,); bark.color_ramp.elements[1].color = c
    bark.color_ramp.elements[0].position = 0.35; bark.color_ramp.elements[1].position = 0.65
    wave = nt.nodes.new('ShaderNodeTexWave'); wave.wave_type = 'RINGS'; wave.rings_direction = 'Z'
    wave.inputs['Scale'].default_value = 2.2; wave.inputs['Distortion'].default_value = 2
    nt.links.new(tc.outputs['Object'], wave.inputs[0])
    ring = nt.nodes.new('ShaderNodeValToRGB'); nt.links.new(wave.outputs['Fac'], ring.inputs[0])
    end = lin(tuple(min(1, x * 0.5 + 0.45) for x in col))
    ring.color_ramp.elements[0].color = tuple(x * 0.7 for x in end[:3]) + (1,); ring.color_ramp.elements[1].color = end
    # cut end where the surface faces along the log
    sepn = nt.nodes.new('ShaderNodeVectorTransform'); sepn.vector_type = 'NORMAL'; sepn.convert_from = 'WORLD'; sepn.convert_to = 'OBJECT'
    nt.links.new(geo.outputs['Normal'], sepn.inputs[0])
    xyz = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(sepn.outputs[0], xyz.inputs[0])
    ab = nt.nodes.new('ShaderNodeMath'); ab.operation = 'ABSOLUTE'; nt.links.new(xyz.outputs['Z'], ab.inputs[0])
    gt = nt.nodes.new('ShaderNodeMath'); gt.operation = 'GREATER_THAN'; gt.inputs[1].default_value = 0.9; nt.links.new(ab.outputs[0], gt.inputs[0])
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'
    nt.links.new(gt.outputs[0], mix.inputs['Factor']); nt.links.new(bark.outputs[0], mix.inputs[6]); nt.links.new(ring.outputs[0], mix.inputs[7])
    nt.links.new(mix.outputs[2], bsdf.inputs['Base Color'])
    return m
frame([log])
for id_, col in LOGS.items():
    log.data.materials.clear(); log.data.materials.append(log_mat(col)); shot(id_)

# ---- finish: shrink, dark outline and drop shadow ----
from PIL import Image, ImageFilter, ImageChops
for id_ in list(AXES) + list(LOGS):
    raw = os.path.join(out_dir, '_raw_' + id_ + '.png')
    im = Image.open(raw).convert('RGBA')
    im = im.crop(im.getbbox())                                      # fill the icon: trim, square up, shrink
    side = max(im.size); sq = Image.new('RGBA', (side, side), (0, 0, 0, 0))
    sq.paste(im, ((side - im.width) // 2, (side - im.height) // 2)); im = sq.resize((SIZE - 12, SIZE - 12), Image.LANCZOS)
    canvas = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0)); canvas.paste(im, (6, 6))
    a = canvas.split()[3].point(lambda v: 255 if v > 90 else 0)
    outline = a.filter(ImageFilter.MaxFilter(3))
    shadow = a.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(2.5))
    shadow = ImageChops.offset(shadow, 3, 4).point(lambda v: v * 0.55)
    out = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
    out.paste(Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 255)), (0, 0), shadow)
    out.paste(Image.new('RGBA', (SIZE, SIZE), (20, 14, 8, 255)), (0, 0), outline)
    out.alpha_composite(canvas)
    out.save(os.path.join(out_dir, id_ + '.png')); os.remove(raw)
print('icons', sorted(f for f in os.listdir(out_dir) if f.endswith('.png')))

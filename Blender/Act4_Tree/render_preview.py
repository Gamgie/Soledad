# Preview renders of tree_crown.blend (blender -b tree_crown.blend --python render_preview.py -- <out_prefix>)
import bpy, math, sys, os
from mathutils import Vector
out = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else os.path.join(os.path.dirname(bpy.data.filepath), "preview")
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE_NEXT'
sc.render.resolution_x, sc.render.resolution_y = 960, 720
sc.render.film_transparent = False
w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes.get("Background"); bg.inputs[0].default_value = (0.45, 0.65, 0.85, 1); bg.inputs[1].default_value = 1.0
sun = bpy.data.objects.get("Sun") or bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN'))
if sun.name not in sc.collection.objects: sc.collection.objects.link(sun)
sun.data.energy = 4.0; sun.rotation_euler = (math.radians(55), 0, math.radians(140))
if not bpy.data.objects.get("Ground"):
    bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, -0.05)); g = bpy.context.active_object; g.name = "Ground"
    gm = bpy.data.materials.new("GroundMat"); gm.use_nodes = True; gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.08, 0.2, 0.04, 1); g.data.materials.append(gm)
cam = bpy.data.objects.get("PrevCam") or bpy.data.objects.new("PrevCam", bpy.data.cameras.new("PrevCam"))
if cam.name not in sc.collection.objects: sc.collection.objects.link(cam)
sc.camera = cam; cam.data.lens = 28
views = {"front": ((0, -24, 4.5), (0, 0, 5.5)), "side": ((20, 9, 3.0), (0, 0, 5.8)), "under": ((2.5, -4.0, 1.6), (0, 0.5, 6.5))}
for name, (p, t) in views.items():
    cam.location = Vector(p)
    d = Vector(t) - Vector(p); cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = f"{out}_{name}.png"
    bpy.ops.render.render(write_still=True)
print("RENDERED")

# Act 4: a light shadow caster for the scanned tree (blender -b tree_scan_proxy.blend --python make_shadow_proxy.py).
# The scan's ~2 M lit grains are drawn again in every shadow cascade (most of their GPU cost): in Unity they cast no
# shadow, and this proxy (the decimated scan + the grafted stems, shrunk a little inward so the grains do not shadow
# themselves) casts it instead, as a Shadows Only renderer. Exported to Assets/Acts/Act4_SeeThrough/Tree.
import bpy, bmesh, json, math, os
from mathutils import Vector

HERE = r"D:\Gamgie\Projects\Oxipital\Soledad\Blender\Act4_Tree"
OUT = r"D:\Gamgie\Projects\Oxipital\Soledad\Assets\Acts\Act4_SeeThrough\Tree\TreeShadowProxy.glb"
FACES = 80000
SHRINK = 0.02

scan = bpy.data.objects["TreeScan_proxy"]
bpy.context.view_layer.objects.active = scan
for o in bpy.context.scene.objects:
    o.select_set(o == scan)
m = scan.modifiers.new("dec", "DECIMATE"); m.ratio = FACES / len(scan.data.polygons)
bpy.ops.object.modifier_apply(modifier="dec")
d = scan.modifiers.new("shrink", "DISPLACE"); d.strength = -SHRINK; d.mid_level = 0
bpy.ops.object.modifier_apply(modifier="shrink")
scan.data.materials.clear()

bm = bmesh.new()
for ch in json.load(open(os.path.join(HERE, "stem_chains.json"))):
    P = [Vector(p) for p in ch["points"]]; R = ch["radii"]
    prev = None
    for k, (c, r) in enumerate(zip(P, R)):
        t = (P[min(k + 1, len(P) - 1)] - P[max(k - 1, 0)]).normalized()
        up = Vector((0, 0, 1)) if abs(t.z) < 0.95 else Vector((1, 0, 0))
        x = t.cross(up).normalized(); y = t.cross(x).normalized()
        ring = [bm.verts.new(c + (x * math.cos(a) + y * math.sin(a)) * (r - SHRINK)) for a in (2 * math.pi * j / 12 for j in range(12))]
        if prev:
            for j in range(12):
                bm.faces.new((prev[j], prev[(j + 1) % 12], ring[(j + 1) % 12], ring[j]))
        prev = ring
stems_me = bpy.data.meshes.new("StemsShadow"); bm.to_mesh(stems_me); bm.free()
stems = bpy.data.objects.new("StemsShadow", stems_me); bpy.context.scene.collection.objects.link(stems)

for o in bpy.context.scene.objects:
    o.select_set(o in (scan, stems))
bpy.context.view_layer.objects.active = scan
bpy.ops.object.join()
scan.name = "TreeShadowProxy"
for o in bpy.context.scene.objects:
    o.select_set(o == scan)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_materials='NONE')
print("RESULT", len(scan.data.polygons), round(os.path.getsize(OUT) / 1e6, 1), "MB")

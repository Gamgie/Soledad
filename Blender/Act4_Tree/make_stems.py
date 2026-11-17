# Act 4: the grafted stems merged into the scan as ONE Oxipital emitter mesh, "Tree_AF_Grafted.glb", so the whole tree
# is one point cloud (one Orb Group, moved by the force groups). Run after make_crown.py (which writes stem_chains.json):
#   blender -b --factory-startup --python make_stems.py
# The tube quads are sized so the stems have the scan's own triangle density (QUAD computed from it).
# Oxipital samples a random TRIANGLE, then a random point in it (Orb VFX ParticleSpawnPosition, placement Surface), so
# the particle density follows the triangle density: the tubes are tessellated with equal-area quads and the script
# prints how many particles give the scan's own density (set it as the stems group's maxInfiniteParticles).
# Texture: the scan's own atlas. Each tube vertex takes the UV of the scan's bark at the same angle, a little below the
# cut on the same stem (repeating every BARK_REPEAT metres), so the stems carry real bark of this tree.
import bpy, bmesh, json, math, os, time
import numpy as np
from mathutils import Vector, kdtree

HERE = r"D:\Gamgie\Projects\Oxipital\Soledad\Blender\Act4_Tree"
SCAN = r"E:\Projets\Oxipital\Asset\emitters\Tree_AF.glb"
OUT = r"D:\Gamgie\Projects\Oxipital\Soledad\Assets\StreamingAssets\emitters\Tree_AF_Grafted.glb"
QUAD = 0.03            # quad edge (m), replaced below by the size that matches the scan's triangle density
BARK_REPEAT = 0.9      # metres of scan bark below the cut reused along the stem
BARK_BELOW = 0.12      # start sampling this far below the cut (the cut edge is ragged)
PARTICLES_PER_TRI = 1900000 / 6870816.0

t0 = time.time()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SCAN)
scan = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
me = scan.data
nv = len(me.vertices)
co = np.empty(nv * 3, np.float32); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
# per-vertex UV from the loops
nl = len(me.loops)
lv = np.empty(nl, np.int32); me.loops.foreach_get("vertex_index", lv)
luv = np.empty(nl * 2, np.float32); me.uv_layers.active.data.foreach_get("uv", luv); luv = luv.reshape(-1, 2)
vuv = np.zeros((nv, 2), np.float32); vuv[lv] = luv
print("scan loaded", round(time.time() - t0, 1), "s")

tips = json.load(open(os.path.join(HERE, "stem_tips.json")))
chains = json.load(open(os.path.join(HERE, "stem_chains.json")))
cut_z = min(t["tip"][2] for t in tips)

# ---- triangle density of the scan on its stems (band below the cut)
band = (co[:, 2] > cut_z - 1.0) & (co[:, 2] < cut_z - 0.1)
nf = len(me.polygons)
fv = np.empty(nf * 3, np.int32); me.polygons.foreach_get("vertices", fv); fv = fv.reshape(-1, 3)
fin = band[fv[:, 0]] & band[fv[:, 1]] & band[fv[:, 2]]
a = co[fv[fin, 0]]; b = co[fv[fin, 1]]; c = co[fv[fin, 2]]
area = 0.5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)
tri_density = fin.sum() / max(area.sum(), 1e-6)
print("scan stems: %d tris on %.2f m2 -> %.0f tris/m2" % (fin.sum(), area.sum(), tri_density))
QUAD = math.sqrt(2.0 / tri_density)   # two triangles per quad
print("quad edge %.4f m" % QUAD)

# ---- KD tree of the scan's bark just below the cut
sel = np.nonzero((co[:, 2] > cut_z - BARK_BELOW - BARK_REPEAT - 0.1) & (co[:, 2] < cut_z - BARK_BELOW + 0.05))[0]
kd = kdtree.KDTree(len(sel))
for i, vi in enumerate(sel):
    kd.insert(Vector(co[vi]), i)
kd.balance()

def bark_uv(tip, axis, rad, ang_dir, h):
    # point on the scan stem, same angle, at depth below the cut
    d = BARK_BELOW + (h % BARK_REPEAT)
    p = Vector(tip) - Vector(axis) * d + ang_dir * rad
    _, i, _ = kd.find(p)
    return tuple(vuv[sel[i]])

# ---- tubes
bm = bmesh.new()
uvl = bm.loops.layers.uv.new("UVMap")
tube_area = 0.0
for ch in chains:
    P = [Vector(p) for p in ch["points"]]; R = ch["radii"]
    tip = tips[ch["stem"]]
    tip_p, tip_a, tip_r = tip["tip"], Vector(tip["axis"]).normalized(), tip["radius"]
    # resample the polyline every QUAD metres
    seg = [(P[i + 1] - P[i]).length for i in range(len(P) - 1)]
    L = sum(seg)
    n_rings = max(2, int(L / QUAD) + 1)
    rings_c, rings_r, rings_t = [], [], []
    for k in range(n_rings):
        s = L * k / (n_rings - 1); acc = 0.0
        for i, l in enumerate(seg):
            if acc + l >= s or i == len(seg) - 1:
                f = 0 if l < 1e-6 else min(1, (s - acc) / l)
                rings_c.append(P[i].lerp(P[i + 1], f)); rings_r.append(R[i] + (R[i + 1] - R[i]) * f)
                rings_t.append((P[i + 1] - P[i]).normalized())
                break
            acc += l
    # start a little inside the scan's cut
    rings_c.insert(0, rings_c[0] - rings_t[0] * 0.08); rings_r.insert(0, rings_r[0]); rings_t.insert(0, rings_t[0])
    sides = max(12, int(2 * math.pi * max(R) / QUAD))
    t = rings_t[0]
    up = Vector((0, 0, 1)) if abs(t.z) < 0.95 else Vector((1, 0, 0))
    x = t.cross(up).normalized()
    # orient x like the scan's frame so the bark angle matches
    prev = None; hacc = 0.0
    for k, (c, r, t) in enumerate(zip(rings_c, rings_r, rings_t)):
        if k > 0:
            q = rings_t[k - 1].rotation_difference(t); x = (q @ x); x = (x - t * x.dot(t)).normalized()
            hacc += (c - rings_c[k - 1]).length
        y = t.cross(x).normalized()
        ring = []
        for j in range(sides):
            ang = 2 * math.pi * j / sides
            dvec = x * math.cos(ang) + y * math.sin(ang)
            v = bm.verts.new(c + dvec * r)
            ring.append((v, bark_uv(tip_p, tip_a, tip_r, dvec, hacc)))
        if prev is not None:
            for j in range(sides):
                j1 = (j + 1) % sides
                quad = (prev[j], prev[j1], ring[j1], ring[j])
                f = bm.faces.new([q[0] for q in quad])
                for loop, q in zip(f.loops, quad):
                    loop[uvl].uv = q[1]
                tube_area += f.calc_area()
        prev = ring
bmesh.ops.triangulate(bm, faces=bm.faces[:])
tris = len(bm.faces)
stems_me = bpy.data.meshes.new("Tree_AF_Stems")
bm.to_mesh(stems_me); bm.free()
stems = bpy.data.objects.new("Tree_AF_Stems", stems_me)
bpy.context.scene.collection.objects.link(stems)
stems_me.materials.append(me.materials[0])

# join the stems into the scan: one mesh, one material (the scan's atlas)
for o in bpy.context.scene.objects:
    o.select_set(o in (scan, stems))
bpy.context.view_layer.objects.active = scan
bpy.ops.object.join()
scan.name = "Tree_AF_Grafted"
info = {"tube_area_m2": round(tube_area, 2), "tube_tris": tris, "scan_tri_density": round(float(tri_density)),
        "total_tris": len(scan.data.polygons), "quad_m": round(QUAD, 4)}
for o in bpy.context.scene.objects:
    o.select_set(o == scan)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_image_format='AUTO')
info["glb_mb"] = round(os.path.getsize(OUT) / 1e6, 1)
info["total_s"] = round(time.time() - t0, 1)
print("RESULT " + json.dumps(info))

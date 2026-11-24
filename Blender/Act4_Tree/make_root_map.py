# Act 4: the root map, a top view of the tree's roots (white roots on black) to place under the tree and emit particles
# from (Oxipital colours each particle from its emitter texture and, in Unlit Additive, kills the dark ones at spawn).
#   blender -b --factory-startup --python make_root_map.py
# 1. The scanned roots: from the scan's points (scan_points.npy, scan frame, Z up), the highest point per 2 cm cell
#    minus the local soil level (lower envelope, smoothed): what stands above the soil is root (or trunk at the centre).
# 2. Extension: the roots leaving the scanned plate (where the root mask crosses the plate's rim) grow on outward as a
#    2D space colonization towards attractors in a disc of EXTENT metres, thinning (pipe model) and branching, so the
#    map reaches well past the scanned ground.
# Output: RootMap.png (SIZE px, the map covers MAP_SIZE metres, centred on the scan's origin; +X right, +Y up in the
# image = scan +X / +Y, i.e. Unity +X / -Z... see the CLAUDE.md for the placement) and a preview over the scan.
import json, math, os, random
import numpy as np

HERE = r"D:\Gamgie\Projects\Oxipital\Soledad\Blender\Act4_Tree"
OUT_DIR = r"D:\Gamgie\Projects\Oxipital\Soledad\Assets\Acts\Act4_SeeThrough\Roots"
SIZE = 4096
MAP_SIZE = 24.0          # metres covered by the map (centred on the tree)
CELL = 0.01              # scan analysis cell (m)
ROOT_RELIEF = 0.02       # above the local mean height to count as root (m)
EDGE_BAND = 6            # cells: roots touching this band at the plate's edge are grown on
TRUNK_RADIUS = 0.75      # the trunk's foot, solid in the map (m)
EXTENT = 11.0            # radius the extended roots reach (m)
ATTRACTORS = 5000
INFLUENCE = 1.6
KILL = 0.35
STEP = 0.12
TIP_WIDTH = 0.012        # half width of the finest rootlets (m)
SEED = 11
rng = random.Random(SEED); np.random.seed(SEED)

p = np.load(os.path.join(HERE, "scan_points.npy"))
x, y, z = p[:, 0], p[:, 1], p[:, 2]
r = np.hypot(x, y)

# ---- 1. scanned roots: local relief (top of the cell minus the mean top within 31 cm) above ROOT_RELIEF
half = 2.8
n = int(2 * half / CELL)
ix = np.clip(((x + half) / CELL).astype(int), 0, n - 1)
iy = np.clip(((y + half) / CELL).astype(int), 0, n - 1)
low = z < -0.6           # the root plate, under the trunk's flare
top = np.full((n, n), -9.0, np.float32)
np.maximum.at(top, (iy[low], ix[low]), z[low])
has = top > -8.9

def box(a, k):
    c = np.cumsum(np.cumsum(np.pad(a, ((k + 1, k), (k + 1, k))), 0), 1)
    return c[2 * k + 1:, 2 * k + 1:] - c[:-2 * k - 1, 2 * k + 1:] - c[2 * k + 1:, :-2 * k - 1] + c[:-2 * k - 1, :-2 * k - 1]

hv = has.astype(np.float32)
mean = box(np.where(has, top, 0), 15) / np.maximum(box(hv, 15), 1)
rel = np.where(has, top - mean, 0)
root_mask = has & (rel > ROOT_RELIEF)
root_mask &= box(root_mask.astype(np.float32), 1) >= 4          # drop isolated cells
# the plate (where the scan has ground) and its edge band
plate = box(hv, 2) >= 12
edge = plate.copy()
for _ in range(EDGE_BAND):
    edge = edge & (box(edge.astype(np.float32), 1) >= 9)
edge_band = plate & ~edge
cy, cx = np.mgrid[0:n, 0:n]
gx = (cx + 0.5) * CELL - half; gy = (cy + 0.5) * CELL - half
gr = np.hypot(gx, gy)
trunk = (gr < TRUNK_RADIUS) & ~has                               # the trunk foot (no point under z -0.6 there)
print("scanned root cells %d of %d plate cells" % (int(root_mask.sum()), int(plate.sum())), "radius pct", np.percentile(gr[root_mask], [50, 90, 99]).round(2), "plate pct", np.percentile(gr[plate], [50, 90, 99]).round(2))

# ---- 2. seeds: in each angular sector, the outermost scanned root cell continues outward
SECTORS = 56
jj, ii = np.nonzero(root_mask & (gr > TRUNK_RADIUS + 0.3))
rr_c = gr[jj, ii]; aa_c = np.arctan2(gy[jj, ii], gx[jj, ii])
sec = ((aa_c + math.pi) / (2 * math.pi) * SECTORS).astype(int) % SECTORS
seeds = []
for sct in range(SECTORS):
    m = np.nonzero(sec == sct)[0]
    if len(m) < 30: continue
    k = m[np.argmax(rr_c[m])]
    j0, i0 = jj[k], ii[k]
    # width: root cells within 5 cm of the tip
    near = root_mask[max(0, j0 - 5):j0 + 6, max(0, i0 - 5):i0 + 6].sum()
    width = min(0.06, max(0.025, near * CELL * CELL / 0.1 * 0.5))
    seeds.append((float(gx[j0, i0]), float(gy[j0, i0]), width))
print("seed radii", sorted(round(math.hypot(s_[0], s_[1]), 2) for s_ in seeds)[:: 5])
print("seeds", len(seeds))

# ---- 3. space colonization outward (2D)
att = []
while len(att) < ATTRACTORS:
    a = rng.uniform(0, 2 * math.pi)
    rr = math.sqrt(rng.uniform(0, 1)) * EXTENT
    if rr < 1.9: continue
    # sparser far away, a few clumps
    if rng.random() > 1.0 - 0.55 * (rr / EXTENT): continue
    att.append((math.cos(a) * rr, math.sin(a) * rr))
att = np.array(att)
pos = [np.array([s[0], s[1]]) for s in seeds]
par = [-1] * len(seeds)
dirs = [np.array([s[0], s[1]]) / max(1e-6, math.hypot(s[0], s[1])) for s in seeds]
alive = np.ones(len(att), bool)
for it in range(220):
    P = np.array(pos); A = att[alive]
    if len(A) == 0: break
    idx = np.nonzero(alive)[0]
    d2 = ((A[:, None, :] - P[None, :, :]) ** 2).sum(-1)
    near = d2.argmin(1); nd = np.sqrt(d2[np.arange(len(A)), near])
    grow = {}
    for kk in range(len(A)):
        if nd[kk] < INFLUENCE:
            v = A[kk] - P[near[kk]]; grow.setdefault(near[kk], np.zeros(2)); grow[near[kk]] += v / (np.linalg.norm(v) + 1e-6)
    if not grow: break
    for nn, g in grow.items():
        d = g / (np.linalg.norm(g) + 1e-6)
        out = P[nn] / (np.linalg.norm(P[nn]) + 1e-6)
        d = d * 0.55 + dirs[nn] * 0.3 + out * 0.15          # keep heading outward, roots wander
        d += np.random.normal(0, 0.12, 2)
        d /= np.linalg.norm(d) + 1e-6
        pos.append(P[nn] + d * STEP * rng.uniform(0.8, 1.2)); par.append(nn); dirs.append(d)
    P = np.array(pos)
    dk = ((att[idx][:, None, :] - P[None, :, :]) ** 2).sum(-1).min(1)
    alive[idx[dk < KILL * KILL]] = False
N = len(pos)
children = [[] for _ in range(N)]
for i, q in enumerate(par):
    if q >= 0: children[q].append(i)
w = [0.0] * N
for i in reversed(range(N)):
    w[i] = TIP_WIDTH if not children[i] else sum(w[c] ** 2.4 for c in children[i]) ** (1 / 2.4)
# match each tree's root to its seed width
for si, s in enumerate(seeds):
    sc = s[2] / max(w[si], 1e-4)
    stack = [si]
    while stack:
        i = stack.pop()
        f = (w[i] - TIP_WIDTH) / max(w[si] - TIP_WIDTH, 1e-4)
        w[i] = max(TIP_WIDTH, w[i] * (1 + (sc - 1) * max(f, 0) ** 0.7))
        stack += children[i]
print("extension nodes", N)

# ---- 4. rasterize
img = np.zeros((SIZE, SIZE), np.float32)
px_m = MAP_SIZE / SIZE
def to_px(v):
    return (v[0] + MAP_SIZE / 2) / px_m, (v[1] + MAP_SIZE / 2) / px_m
def capsule(a, b, ra, rb, val=1.0):
    ax, ay = to_px(a); bx, by = to_px(b)
    ra_p, rb_p = ra / px_m + 0.7, rb / px_m + 0.7
    x0 = int(max(0, min(ax, bx) - max(ra_p, rb_p) - 2)); x1 = int(min(SIZE - 1, max(ax, bx) + max(ra_p, rb_p) + 2))
    y0 = int(max(0, min(ay, by) - max(ra_p, rb_p) - 2)); y1 = int(min(SIZE - 1, max(ay, by) + max(ra_p, rb_p) + 2))
    if x1 < x0 or y1 < y0: return
    yy, xx = np.mgrid[y0:y1 + 1, x0:x1 + 1].astype(np.float32) + 0.5
    dx, dy = bx - ax, by - ay
    L2 = dx * dx + dy * dy + 1e-6
    t = np.clip(((xx - ax) * dx + (yy - ay) * dy) / L2, 0, 1)
    d = np.hypot(xx - ax - t * dx, yy - ay - t * dy)
    rad = ra_p + (rb_p - ra_p) * t
    cov = np.clip(rad - d + 0.5, 0, 1) * val
    sub = img[y0:y1 + 1, x0:x1 + 1]
    np.maximum(sub, cov, out=sub)
for i in range(N):
    q = par[i]
    if q >= 0:
        capsule(pos[q], pos[i], w[q], w[i])
# scanned roots and trunk foot: the 1 cm mask resampled onto the map, softened by a 3 px blur
yy, xx = np.mgrid[0:SIZE, 0:SIZE]
mx = (xx + 0.5) * px_m - MAP_SIZE / 2; my = (yy + 0.5) * px_m - MAP_SIZE / 2
ci = np.floor((mx + half) / CELL).astype(int); cj = np.floor((my + half) / CELL).astype(int)
ok = (ci >= 0) & (ci < n) & (cj >= 0) & (cj < n)
scan_img = np.zeros((SIZE, SIZE), np.float32)
scan_img[ok] = (root_mask | trunk)[cj[ok], ci[ok]].astype(np.float32)
scan_img = box(scan_img, 1) / 9.0
img = np.maximum(img, np.clip(scan_img * 1.4, 0, 1))
# slight blur of the edges, brightness: thick roots brighter is in the coverage already
out = (np.clip(img, 0, 1) * 255).astype(np.uint8)

os.makedirs(OUT_DIR, exist_ok=True)
import bpy
def save(arr, name):
    h, w_ = arr.shape[:2]
    im = bpy.data.images.new(name, w_, h, alpha=False)
    if arr.ndim == 2:
        rgba = np.stack([arr, arr, arr, np.full_like(arr, 255)], -1)
    else:
        rgba = np.concatenate([arr, np.full(arr.shape[:2] + (1,), 255, np.uint8)], -1)
    im.pixels.foreach_set((rgba.astype(np.float32) / 255.0).ravel())
    im.filepath_raw = os.path.join(OUT_DIR, name); im.file_format = 'PNG'; im.save()
save(out, "RootMap.png")
json.dump({"size_px": SIZE, "map_size_m": MAP_SIZE, "extent_m": EXTENT, "seeds": len(seeds), "nodes": N,
           "center": "scan origin (Tree object origin)", "image_x": "+X", "image_y": "scan +Y (Unity -Z... see CLAUDE.md)"},
          open(os.path.join(OUT_DIR, "RootMap.json"), "w"), indent=1)
print("RESULT saved", round(float((out > 128).mean()) * 100, 2), "% white")
# preview for the scratchpad (1024 px)
prev = out.reshape(1024, SIZE // 1024, 1024, SIZE // 1024).max(axis=(1, 3))
import shutil
save(prev, "RootMap_preview.png")
shutil.move(os.path.join(OUT_DIR, "RootMap_preview.png"), os.path.join(HERE, "RootMap_preview.png"))

# ---- 5. emitter mesh for Oxipital: the map's cells that hold roots, following the hill DEPTH m under its surface,
# with RootMap.png as its texture (Oxipital colours the particles from the mesh texture: in Unlit Additive the black
# ones die at spawn, so particles only stay on the roots). Equal cells, so Oxipital's per-triangle sampling is even.
# Built in the scan's frame (Blender Z up), centred on the tree's axis; the hill top is at Z = 0 (Unity Y = 0).
GRID = 512
DEPTH = 0.4
HILL_R = 50.0
EMIT = os.path.join(HERE, "RootMap.glb")   # flat emitter, kept out of StreamingAssets (RootCloud replaces it)
blk = out.reshape(GRID, SIZE // GRID, GRID, SIZE // GRID).max(axis=(1, 3)) > 40
cell = MAP_SIZE / GRID
jj, ii = np.nonzero(blk)
verts, faces, uvs = [], [], []
def hill_z(px_, py_):
    rr_ = math.hypot(px_, py_)
    return math.sqrt(max(HILL_R * HILL_R - rr_ * rr_, 0)) - HILL_R - DEPTH
vid = {}
def vert(gi, gj):
    k_ = (gi, gj)
    if k_ not in vid:
        px_ = gi * cell - MAP_SIZE / 2; py_ = gj * cell - MAP_SIZE / 2
        vid[k_] = len(verts); verts.append((px_, py_, hill_z(px_, py_))); uvs.append((gi / GRID, gj / GRID))
    return vid[k_]
for j_, i_ in zip(jj, ii):
    a_, b_, c_, d_ = vert(i_, j_), vert(i_ + 1, j_), vert(i_ + 1, j_ + 1), vert(i_, j_ + 1)
    faces.append((a_, b_, c_, d_))
me = bpy.data.meshes.new("RootMap")
me.from_pydata(verts, [], faces)
uvl = me.uv_layers.new(name="UVMap")
loop_v = np.empty(len(me.loops), np.int32); me.loops.foreach_get("vertex_index", loop_v)
uv_arr = np.array(uvs, np.float32)[loop_v]
uvl.data.foreach_set("uv", uv_arr.ravel())
ob = bpy.data.objects.new("RootMap", me); bpy.context.scene.collection.objects.link(ob)
mat = bpy.data.materials.new("RootMap"); mat.use_nodes = True
tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
tex.image = bpy.data.images.load(os.path.join(OUT_DIR, "RootMap.png"))
mat.node_tree.links.new(tex.outputs["Color"], mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
me.materials.append(mat)
bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.quads_convert_to_tris(); bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.export_scene.gltf(filepath=EMIT, export_format='GLB', use_selection=True, export_image_format='AUTO')
print("RESULT emitter cells %d tris %d (%.1f%% of the grid), %.1f MB" % (len(faces), len(me.polygons), 100.0 * len(faces) / GRID / GRID, os.path.getsize(EMIT) / 1e6))

# ---- 6. RootCloud.glb: the roots as a 3D point-cloud emitter, close under the real roots
# Centre: the underside of the scanned soil plate (lowest point of each 1 cm cell) under each scanned root and under the
# trunk foot, as 2 cm quads. Beyond: the extended roots as tubes of their width, diving deeper into the hill with the
# distance (DIVE m per m, from the plate's underside at their seed). Quads of about QUAD m everywhere, so Oxipital's
# per-triangle sampling spreads the particles evenly. Scan frame (Blender Z up): the Roots group sits where Tree sits
# (TREE_LIFT above the hill top), so the hill surface is at hill_z - TREE_LIFT here. No texture: white particles.
TREE_LIFT = 1.63
DIVE = 0.11
QUAD = 0.02
CLOUD = r"D:\Gamgie\Projects\Oxipital\Soledad\Assets\StreamingAssets\emitters\RootCloud.glb"
bot = np.full((n, n), 9.0, np.float32)
np.minimum.at(bot, (iy[low], ix[low]), z[low])
hasb = bot < 8.9
under = (root_mask | (gr < TRUNK_RADIUS + 0.1)) & hasb
# 2 cm quads: a 2x2 block of 1 cm cells, at the block's lowest point
cverts, cfaces = [], []
for j0 in range(0, n - 1, 2):
    for i0 in range(0, n - 1, 2):
        blk2 = under[j0:j0 + 2, i0:i0 + 2]
        if not blk2.any(): continue
        zz = bot[j0:j0 + 2, i0:i0 + 2][blk2].min()
        x0_ = i0 * CELL - half; y0_ = j0 * CELL - half
        b0 = len(cverts)
        cverts += [(x0_, y0_, zz), (x0_ + 2 * CELL, y0_, zz), (x0_ + 2 * CELL, y0_ + 2 * CELL, zz), (x0_, y0_ + 2 * CELL, zz)]
        cfaces.append((b0, b0 + 3, b0 + 2, b0 + 1))     # facing down
centre_quads = len(cfaces)

def hill_surface(px_, py_):
    rr_ = math.hypot(px_, py_)
    return math.sqrt(max(HILL_R * HILL_R - rr_ * rr_, 0)) - HILL_R - TREE_LIFT

# z of every extension node: from its seed's underside, diving with the distance travelled
seed_of = list(range(N))
zn = [0.0] * N
dist = [0.0] * N
for i in range(N):
    q = par[i]
    if q < 0:
        sx, sy = pos[i]
        ci_ = int((sx + half) / CELL); cj_ = int((sy + half) / CELL)
        win = bot[max(0, cj_ - 3):cj_ + 4, max(0, ci_ - 3):ci_ + 4]
        zs = float(win[win < 8.9].min()) if (win < 8.9).any() else hill_surface(sx, sy) - 0.1
        zn[i] = min(zs, hill_surface(sx, sy) - 0.05)
    else:
        dist[i] = dist[q] + float(np.linalg.norm(pos[i] - pos[q]))
        target = hill_surface(*pos[i]) - 0.08 - DIVE * dist[i] + 0.04 * math.sin(dist[i] * 2.3 + i)
        zn[i] = min(target, zn[q] + 0.02)                # never climbs back up
tverts, tfaces = [], []
for i in range(N):
    q = par[i]
    if q < 0: continue
    a3 = np.array([pos[q][0], pos[q][1], zn[q]]); b3 = np.array([pos[i][0], pos[i][1], zn[i]])
    d3 = b3 - a3; L = float(np.linalg.norm(d3))
    if L < 1e-4: continue
    t3 = d3 / L
    up = np.array([0, 0, 1.0]) if abs(t3[2]) < 0.95 else np.array([1.0, 0, 0])
    ux = np.cross(t3, up); ux /= np.linalg.norm(ux); uy = np.cross(t3, ux)
    ra_, rb_ = w[q], w[i]
    sides = max(4, int(round(2 * math.pi * max(ra_, rb_) / QUAD)))
    rings_ = max(1, int(round(L / QUAD)))
    base = len(tverts)
    for k in range(rings_ + 1):
        f_ = k / rings_
        c3 = a3 + d3 * f_; rr_ = ra_ + (rb_ - ra_) * f_
        for s_ in range(sides):
            an = 2 * math.pi * s_ / sides
            tverts.append(tuple(c3 + (ux * math.cos(an) + uy * math.sin(an)) * rr_))
    for k in range(rings_):
        for s_ in range(sides):
            s1 = (s_ + 1) % sides
            v0 = base + k * sides
            tfaces.append((v0 + s_, v0 + s1, v0 + sides + s1, v0 + sides + s_))
off = len(cverts)
allv = cverts + tverts
allf = cfaces + [tuple(v + off for v in f_) for f_ in tfaces]
cm = bpy.data.meshes.new("RootCloud"); cm.from_pydata(allv, [], allf)
co_ = bpy.data.objects.new("RootCloud", cm); bpy.context.scene.collection.objects.link(co_)
bpy.ops.object.select_all(action='DESELECT'); co_.select_set(True); bpy.context.view_layer.objects.active = co_
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.quads_convert_to_tris(); bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.export_scene.gltf(filepath=CLOUD, export_format='GLB', use_selection=True, export_materials='NONE')
print("RESULT cloud: centre quads %d, tube quads %d, tris %d, deepest %.2f m under the hill top, %.1f MB" % (
    centre_quads, len(tfaces), len(cm.polygons), -(min(zn) + TREE_LIFT), os.path.getsize(CLOUD) / 1e6))

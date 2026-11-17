# Act 4 crown, grown on the cut stems of the Tree_AF scan (Blender 5.x, run with: blender -b tree_scan_proxy.blend --python make_crown.py)
# Space colonization (Runions et al. 2007) from the 7 stem tips (stem_tips.json) towards attractors spread in a rounded
# crown, pipe-model radii continuing each stem's radius, tube mesh for the wood, twig cards from the Meadow poplar atlas
# (T_Poplar_leaves_BC.tga) for the foliage. Blender coordinates (Z up), same frame as the imported scan.
import bpy, bmesh, json, math, os, random, sys
import numpy as np
from mathutils import Vector, Matrix, Quaternion

HERE = os.path.dirname(bpy.data.filepath) if bpy.data.filepath else r"D:\Gamgie\Projects\Oxipital\Soledad\Blender\Act4_Tree"
TEX = os.path.join(HERE, "textures")
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SEED = 7
rng = random.Random(SEED)
np.random.seed(SEED)

# ---------------------------------------------------------------- settings
CROWN_CENTER = Vector((-0.35, 0.15, 7.6))
CROWN_RADII = Vector((5.6, 5.4, 3.9))      # rounded crown, about 11 m wide, top near 11.5 m
CROWN_BOTTOM = 4.4                          # no attractor under this height (the stems stay bare)
ATTRACTORS = 3200
INFLUENCE = 2.2
KILL = 0.42
STEP = 0.24
ITERATIONS = 140
TWIG_RADIUS = 0.011
PIPE_EXP = 2.4
CARD_SIZE = (0.6, 1.05)
CARDS_PER_TIP = 7
CARD_SCATTER = 0.18                         # cards spread around their twig tip (m): leaf clumps
LOBES = 11                                  # clumps of foliage making the silhouette
LOBE_RADIUS = (2.1, 3.3)
DROOP = 0.9                                 # outer thin branches sag by up to this much (m)
NORMAL_OUTWARD = 0.7                        # leaf normals bent towards "out of the crown" (soft volumetric shading)

tips = json.load(open(os.path.join(HERE, "stem_tips.json")))

# ---------------------------------------------------------------- attractors in clumps (lobes), denser towards each lobe's shell
lobes = []
for i in range(LOBES):
    # spread the lobes over the upper half of a squashed sphere, one above each stem first
    if i < len(tips):
        t = tips[i]
        tip = Vector(t["tip"]); a = Vector(t["axis"]).normalized()
        out = Vector((tip.x - CROWN_CENTER.x, tip.y - CROWN_CENTER.y, 0)).normalized() if (tip.xy - CROWN_CENTER.xy).length > 0.2 else Vector((0, 0, 0))
        c = tip + a * rng.uniform(2.6, 3.6) + out * rng.uniform(1.2, 2.2)
    else:
        th = rng.uniform(0, 2 * math.pi); el = rng.uniform(0.15, 1.1)
        c = CROWN_CENTER + Vector((math.cos(th) * math.cos(el) * CROWN_RADII.x * 0.6, math.sin(th) * math.cos(el) * CROWN_RADII.y * 0.6, math.sin(el) * CROWN_RADII.z * 0.55))
    c.z = max(c.z, CROWN_BOTTOM + 1.6)
    lobes.append((c, rng.uniform(*LOBE_RADIUS)))
att = []
while len(att) < ATTRACTORS:
    c, r = lobes[rng.randrange(len(lobes))]
    v = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)))
    l = v.length
    if l > 1 or l < 0.45:
        continue
    p = c + Vector((v.x, v.y, v.z * 0.8)) * r
    if p.z < CROWN_BOTTOM:
        continue
    att.append(p)
att = np.array([tuple(a) for a in att])

# ---------------------------------------------------------------- nodes
pos = []; parent = []; dirs = []; root_of = []
for i, t in enumerate(tips):
    p = Vector(t["tip"]); a = Vector(t["axis"]).normalized()
    # start a little inside the cut so the wood overlaps the scan
    pos.append(p - a * 0.15); parent.append(-1); dirs.append(a); root_of.append(i)
    pos.append(p + a * 0.05); parent.append(len(pos) - 2); dirs.append(a); root_of.append(i)

alive = np.ones(len(att), bool)
for it in range(ITERATIONS):
    P = np.array([tuple(p) for p in pos])
    A = att[alive]
    if len(A) == 0:
        break
    idx_alive = np.nonzero(alive)[0]
    # nearest node for every attractor
    d2 = ((A[:, None, :] - P[None, :, :]) ** 2).sum(-1)
    nearest = d2.argmin(1)
    nd = np.sqrt(d2[np.arange(len(A)), nearest])
    grow = {}
    for k in range(len(A)):
        if nd[k] < INFLUENCE:
            n = nearest[k]
            v = Vector(A[k]) - pos[n]
            grow.setdefault(n, Vector()).__iadd__(v.normalized())
    if not grow:
        break
    for n, g in grow.items():
        d = g.normalized()
        # stems keep their direction near the cut, and branches keep some of their parent's
        depth = (pos[n] - Vector(tips[root_of[n]]["tip"])).length
        keep = 0.55 if depth < 1.2 else 0.25
        d = (d * (1 - keep) + dirs[n] * keep).normalized()
        d.z += 0.08                                           # a touch of phototropism
        d.normalize()
        q = pos[n] + d * STEP * rng.uniform(0.85, 1.15)
        pos.append(q); parent.append(n); dirs.append(d); root_of.append(root_of[n])
    P = np.array([tuple(p) for p in pos])
    # kill reached attractors
    dk = ((att[idx_alive][:, None, :] - P[None, :, :]) ** 2).sum(-1).min(1)
    alive[idx_alive[dk < KILL * KILL]] = False

N = len(pos)
children = [[] for _ in range(N)]
for i, p in enumerate(parent):
    if p >= 0:
        children[p].append(i)

# ---------------------------------------------------------------- pipe-model radii, scaled per stem to the scan's radius
radius = [0.0] * N
order = sorted(range(N), key=lambda i: -len(str(i)))  # placeholder, replaced below
# post-order traversal
def post(i, out):
    stack = [(i, False)]
    while stack:
        n, done = stack.pop()
        if done:
            out.append(n)
        else:
            stack.append((n, True))
            for c in children[n]:
                stack.append((c, False))
roots = [i for i in range(N) if parent[i] < 0]
for r in roots:
    seq = []
    post(r, seq)
    for n in seq:
        if not children[n]:
            radius[n] = TWIG_RADIUS
        else:
            radius[n] = sum(radius[c] ** PIPE_EXP for c in children[n]) ** (1 / PIPE_EXP)
    # scale the subtree (smoothly, thin twigs untouched) so the root matches the scan's stem
    target = tips[root_of[r]]["radius"]
    s = target / max(radius[r], 1e-4)
    for n in seq:
        f = (radius[n] - TWIG_RADIUS) / max(radius[r] - TWIG_RADIUS, 1e-4)
        radius[n] = radius[n] * (1 + (s - 1) * max(f, 0) ** 0.6)

# ---------------------------------------------------------------- thin outer branches sag (a smooth field: parents and children move together)
for n in range(N):
    hd = (Vector((pos[n].x, pos[n].y)) - CROWN_CENTER.xy).length
    thin = 1 - min(1, radius[n] / 0.07)
    pos[n] = pos[n] - Vector((0, 0, DROOP * min(1, (hd / 5.5) ** 2) * thin ** 1.5))

# ---------------------------------------------------------------- distance along the branches from the stem (bark v)
BARK_TILE = 1.2       # metres of branch per bark texture repeat
BARK_WRAP = 1.0       # repeats around a branch
vdist = [0.0] * N
for n in range(N):    # parents are always created before their children
    if parent[n] >= 0:
        vdist[n] = vdist[parent[n]] + (pos[n] - pos[parent[n]]).length

# ---------------------------------------------------------------- wood mesh: continuous tubes
# Each chain follows the thickest child at every fork; a side branch starts a new chain at its parent node. Rings are
# shared between consecutive segments (one ring per node, oriented by the mean of the incoming and outgoing
# directions, parallel-transported so the tube does not twist), so a bend has no gap. A side branch's first ring sits
# inside its parent, so the joint is closed.
bm = bmesh.new()
uv_layer = bm.loops.layers.uv.new("UVMap")
main_child = [max(children[n], key=lambda c: radius[c]) if children[n] else -1 for n in range(N)]
chains = []
starts = [r for r in roots] + [c for n in range(N) for c in children[n] if c != main_child[n]]
for st in starts:
    chain = [parent[st]] if parent[st] >= 0 else []
    n = st
    while n >= 0:
        chain.append(n); n = main_child[n]
    if len(chain) >= 2:
        chains.append(chain)

# The grafted stems (from each cut up to STEM_LENGTH along the main chain, while still thick) become particles like the
# scan (make_stems.py builds them as a second Oxipital emitter, "Tree_AF_Stems"): saved to stem_chains.json and left
# out of the crown's wood, whose chain then starts where the particle stem ends.
STEM_LENGTH = 2.6
STEM_MIN_RADIUS = 0.55      # fraction of the stem's radius at the cut
stem_chains = []
for ci, chain in enumerate(chains):
    if parent[chain[0]] >= 0:
        continue
    r_cut = radius[chain[0]]
    k = 0
    while k + 1 < len(chain) and vdist[chain[k + 1]] <= STEM_LENGTH and radius[chain[k + 1]] >= STEM_MIN_RADIUS * r_cut:
        k += 1
    if k < 1:
        continue
    stem_chains.append({"stem": root_of[chain[0]],
                        "points": [list(pos[n]) for n in chain[:k + 1]],
                        "radii": [radius[n] for n in chain[:k + 1]],
                        "v": [vdist[n] for n in chain[:k + 1]]})
    chains[ci] = chain[k:]
json.dump(stem_chains, open(os.path.join(HERE, "stem_chains.json"), "w"), indent=1)

def sides_for(r):
    return 16 if r > 0.15 else (12 if r > 0.06 else (8 if r > 0.025 else 5))

for chain in chains:
    first = chain[0]
    lateral = parent[chain[1]] == first and main_child[first] != chain[1]
    r0 = radius[chain[1]] if lateral else radius[first]
    sides = sides_for(max(r0, radius[chain[1]]))
    # tangents
    tans = []
    for i, n in enumerate(chain):
        din = (pos[n] - pos[chain[i - 1]]).normalized() if i > 0 else None
        dout = (pos[chain[i + 1]] - pos[n]).normalized() if i + 1 < len(chain) else None
        t = (din if dout is None else dout if din is None else (din + dout))
        tans.append(t.normalized() if t.length > 1e-6 else Vector((0, 0, 1)))
    # parallel transport frame
    t0 = tans[0]
    up = Vector((0, 0, 1)) if abs(t0.z) < 0.95 else Vector((1, 0, 0))
    x = t0.cross(up).normalized()
    rings = []
    for i, n in enumerate(chain):
        if i > 0:
            q = tans[i - 1].rotation_difference(tans[i])
            x = (q @ x)
            x = (x - tans[i] * x.dot(tans[i])).normalized()
        y = tans[i].cross(x).normalized()
        r = r0 if i == 0 else radius[n]
        c = pos[n]
        if i == 0 and lateral:
            c = c - tans[0] * min(radius[first] * 0.6, 0.15)    # sink the joint into the parent branch
        ring = []
        for k in range(sides):
            ang = 2 * math.pi * k / sides
            ring.append(bm.verts.new(c + (x * math.cos(ang) + y * math.sin(ang)) * r))
        rings.append(ring)
    for i in range(len(chain) - 1):
        va = vdist[chain[i]] / BARK_TILE
        vb = vdist[chain[i + 1]] / BARK_TILE
        if i == 0 and lateral:
            va = vb - (pos[chain[1]] - pos[first]).length / BARK_TILE
        ra, rb = rings[i], rings[i + 1]
        for k in range(sides):
            k1 = (k + 1) % sides
            f = bm.faces.new((ra[k], ra[k1], rb[k1], rb[k]))
            u0, u1 = k / sides * BARK_WRAP, (k + 1) / sides * BARK_WRAP
            for loop, uv in zip(f.loops, ((u0, va), (u1, va), (u1, vb), (u0, vb))):
                loop[uv_layer].uv = uv
    # cap the twig ends
    end = rings[-1]
    if len(end) >= 3 and radius[chain[-1]] > 0.004:
        try:
            bm.faces.new(end)
        except ValueError:
            pass
wood_me = bpy.data.meshes.new("Crown_Wood")
bm.to_mesh(wood_me); bm.free()
for poly in wood_me.polygons:
    poly.use_smooth = True
wood = bpy.data.objects.new("Crown_Wood", wood_me)
bpy.context.scene.collection.objects.link(wood)

# ---------------------------------------------------------------- foliage cards from the atlas
# Twig regions in the 4096 atlas (u0, v0, u1, v1 in 0..1, v up), base of the twig at the bottom edge (bv)
REGIONS = [
    # (u0, v0, u1, v1, base_u, base_v)   base = where the twig attaches
    (0.46, 0.54, 0.80, 1.00, 0.47, 0.60),   # big leafy spray, top
    (0.75, 0.43, 1.00, 1.00, 0.77, 0.47),   # hanging spray, right
    (0.34, 0.27, 0.69, 0.55, 0.34, 0.37),   # middle twig
    (0.49, 0.01, 1.00, 0.40, 0.70, 0.02),   # bottom right cluster
    (0.08, 0.53, 0.24, 0.70, 0.17, 0.53),   # small left cluster
    (0.33, 0.00, 0.54, 0.28, 0.42, 0.00),   # bottom middle twig
]
bm = bmesh.new()
uv_layer = bm.loops.layers.uv.new("UVMap")
col_layer = bm.loops.layers.color.new("Col")   # r = hue/age variation, g = height in the crown, b = card id
tips_nodes = [n for n in range(N) if not children[n] or radius[n] < 0.03]
cards = 0
for n in tips_nodes:
    if pos[n].z < CROWN_BOTTOM - 0.3:
        continue
    count = CARDS_PER_TIP if not children[n] else 2
    for _ in range(count):
        reg = REGIONS[rng.randrange(len(REGIONS))]
        u0, v0, u1, v1, bu, bv = reg
        w_uv, h_uv = (u1 - u0), (v1 - v0)
        size = rng.uniform(*CARD_SIZE)
        aspect = w_uv / h_uv
        h = size; w = size * aspect
        # card up axis: the branch direction, bent outward and down a little (leaves droop)
        out = (pos[n] - CROWN_CENTER); out.z *= 0.5; out.normalize()
        up = (dirs[n] * 0.5 + out * 0.6 + Vector((rng.uniform(-.4, .4), rng.uniform(-.4, .4), rng.uniform(-.5, .2)))).normalized()
        side = up.cross(Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-0.3, 1)))).normalized()
        if side.length < 0.1:
            continue
        nrm = side.cross(up).normalized()
        if nrm.dot(out) < 0:                         # front face looks out of the crown
            side = -side; nrm = -nrm
        # the base point of the twig in the region sits near the node (leaf clumps)
        bx = (bu - u0) / w_uv * w; by = (bv - v0) / h_uv * h
        jitter = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * CARD_SCATTER
        origin = pos[n] + jitter - side * bx - up * by
        corners = [origin, origin + side * w, origin + side * w + up * h, origin + up * h]
        uvs = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
        vs = [bm.verts.new(c) for c in corners]
        f = bm.faces.new(vs)
        var = rng.random()
        hgt = (pos[n].z - CROWN_BOTTOM) / (CROWN_CENTER.z + CROWN_RADII.z - CROWN_BOTTOM)
        for loop, uv in zip(f.loops, uvs):
            loop[uv_layer].uv = uv
            loop[col_layer] = (var, max(0, min(1, hgt)), 0, 1)
        cards += 1
leaves_me = bpy.data.meshes.new("Crown_Leaves")
bm.to_mesh(leaves_me); bm.free()
# Normals bent towards "out of the crown": the canopy shades as one soft volume, light on the sunny side
loop_normals = []
for poly in leaves_me.polygons:
    fn = poly.normal
    for li in poly.loop_indices:
        vpos = leaves_me.vertices[leaves_me.loops[li].vertex_index].co
        o = (vpos - CROWN_CENTER); o.z *= 0.7
        o.normalize()
        loop_normals.append((fn * (1 - NORMAL_OUTWARD) + o * NORMAL_OUTWARD).normalized())
leaves_me.normals_split_custom_set(loop_normals)
leaves = bpy.data.objects.new("Crown_Leaves", leaves_me)
bpy.context.scene.collection.objects.link(leaves)

# ---------------------------------------------------------------- materials (preview; Unity gets its own)
def img(name):
    p = os.path.join(TEX, name)
    i = bpy.data.images.get(name) or bpy.data.images.load(p)
    return i
m = bpy.data.materials.new("Crown_Bark"); m.use_nodes = True
nt = m.node_tree; b = nt.nodes["Principled BSDF"]
t = nt.nodes.new("ShaderNodeTexImage"); t.image = img("T_Poplar_bark_01_BC.tga")
nt.links.new(t.outputs["Color"], b.inputs["Base Color"]); b.inputs["Roughness"].default_value = 0.85
wood.data.materials.append(m)
m = bpy.data.materials.new("Crown_Leaves"); m.use_nodes = True
nt = m.node_tree; b = nt.nodes["Principled BSDF"]
t = nt.nodes.new("ShaderNodeTexImage"); t.image = img("T_Poplar_leaves_BC.tga")
nt.links.new(t.outputs["Color"], b.inputs["Base Color"]); nt.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
b.inputs["Roughness"].default_value = 0.45
try:
    b.inputs["Transmission Weight"].default_value = 0.15
except Exception:
    pass
m.surface_render_method = 'DITHERED' if hasattr(m, "surface_render_method") else None
m.use_backface_culling = False
leaves.data.materials.append(m)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "tree_crown.blend"))
# Export the crown alone for Unity (glTFast import: same frame as the scan loaded by Oxipital)
for o in bpy.context.scene.objects:
    o.select_set(o.name in ("Crown_Wood", "Crown_Leaves"))
bpy.ops.export_scene.gltf(filepath=os.path.join(HERE, "Crown.glb"), export_format='GLB', use_selection=True,
                          export_image_format='NONE', export_vertex_color='ACTIVE', export_attributes=True)
info = {"nodes": N, "wood_faces": len(wood_me.polygons), "cards": cards, "leaf_tris": cards * 2,
        "attractors_left": int(alive.sum()), "max_z": max(p.z for p in pos)}
print("RESULT " + json.dumps(info))

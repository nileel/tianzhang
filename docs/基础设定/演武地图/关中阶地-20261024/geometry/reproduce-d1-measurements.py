# D1 measurement reproduction, 2026-10-24.
# Run only via blender_lab.execute_blender_code in the saved temporary
# d1-measured-size-comparison.blend. No FBX export, file overwrite or CLI Blender.
# Load with exec(compile(open(path, encoding="utf-8").read(), path, "exec")).
# Returns JSON-serializable result; does not mutate any scene or write outputs.
import bpy, json, math, hashlib
import numpy as np
from mathutils import Vector, Quaternion, Matrix
from mathutils.bvhtree import BVHTree
from pathlib import Path
evidence = Path(r"D:\Temp\TianZhang-Blender\d1-measure-20261024")
imports = json.loads((evidence / "01-import-state.json").read_text(encoding="utf-8"))
for key, entry in imports.items():
    assert hashlib.sha256(Path(entry["path"]).read_bytes()).hexdigest() == entry["sha256"], key
scene = bpy.data.scenes["D1_Measure_20261024"]
def mesh(key):
    return next(o for o in scene.objects if o.type == "MESH" and o.name.startswith("D1_" + key + "_"))
tile = mesh("Tile")
original_matrix = Matrix(next(e for e in imports["Tile"]["objects"] if e["type"] == "MESH")["matrix_world"])
q = Quaternion((.1757585685908413, -.17575856859084127, .6849152688960135, .6849152688960136))
p = Vector((.24881442194815115, -.8945829622159506, -.4738227693251703))
tile_vertices = []
for vertex in tile.data.vertices:
    b = original_matrix @ vertex.co
    tile_vertices.append(q @ Vector((-b.x, b.z, -b.y)) * 1.9770544758933135 + p + Vector((0, .34, 0)))
faces = [list(f.vertices) for f in tile.data.polygons]
bvh = BVHTree.FromPolygons(tile_vertices, faces, all_triangles=True)
a = np.array([list(v) for v in tile_vertices])
def vertical(x, z):
    top = bvh.ray_cast(Vector((x, 10, z)), Vector((0, -1, 0)))[0]
    bottom = bvh.ray_cast(Vector((x, -10, z)), Vector((0, 1, 0)))[0]
    return {"top": float(top.y) if top else None, "bottom": float(bottom.y) if bottom else None}
center = vertical(0, 0)
edges = []
for angle in range(0, 360, 60):
    r = math.radians(angle)
    n = np.array([math.cos(r), math.sin(r)])
    tangent = np.array([-math.sin(r), math.cos(r)])
    samples = []
    for radius in [.45, .475, .49, .5]:
        for lateral in [-.25, 0, .25]:
            xz = n * radius + tangent * lateral
            samples.append({"radius": radius, "lateral": lateral, **vertical(float(xz[0]), float(xz[1]))})
    edges.append({"normal_deg": angle, "projection_min": float((a[:, [0, 2]] @ n).min()), "projection_max": float((a[:, [0, 2]] @ n).max()), "vertical_samples": samples})
norms = np.array([[math.cos(math.radians(r)), math.sin(math.radians(r))] for r in range(0, 360, 60)])
models = {}
for key in ["Player", "Beast"]:
    raw = np.array([list(v.co) for v in mesh(key).data.vertices])
    scale = 1.4 if key == "Player" else min(1.8 / .99951172, .9 / .45556642)
    vertices = raw * scale
    rows = []
    for facing, yaw in enumerate([90, 150, 210, 270, 330, 30]):
        r = math.radians(yaw)
        c, s = math.cos(r), math.sin(r)
        rotated = vertices @ np.array([[c, 0, -s], [0, 1, 0], [s, 0, c]])
        forward, side = np.array([s, c]), np.array([c, -s])
        centers = [np.array([0., 0.])] if key == "Player" else [forward * .5, -forward * .5]
        overruns = np.min(np.stack([np.max((rotated[:, [0, 2]] - center) @ norms.T, axis=1) - .5 for center in centers]), axis=0)
        rows.append({"facing": facing, "unity_yaw": yaw, "world_size_x_y_z": np.ptp(rotated, axis=0).tolist(), "path_lateral_width": float(np.ptp(rotated[:, [0, 2]] @ side)), "path_axial_length": float(np.ptp(rotated[:, [0, 2]] @ forward)), "vertices_projected_outside_ideal_occupied_hex_union": int((overruns > 1e-6).sum()), "max_halfplane_overrun": float(overruns.max()), "vertex_count": len(raw)})
    models[key] = {"raw_bounds_min": raw.min(axis=0).tolist(), "raw_bounds_max": raw.max(axis=0).tolist(), "runtime_scale": scale, "runtime_size_width_height_length": np.ptp(vertices, axis=0).tolist(), "six_facing": rows}
midpoints = [e["vertical_samples"][10] for e in edges]
edge_summary = []
for i, e in enumerate(edges):
    middle = midpoints[i]
    opposite = midpoints[(i + 3) % 6]
    edge_summary.append({"normal_deg": e["normal_deg"], "top_y": middle["top"], "bottom_y": middle["bottom"], "edge_top_minus_center_y": middle["top"] - center["top"], "same_height_current_minus_opposite_edge_y": middle["top"] - opposite["top"], "high_neighbor_edge_minus_low_edge_y_at_rise_075": .75 + opposite["top"] - middle["top"], "bottom_y_after_rise_075": middle["bottom"] + .75, "downward_coverage_below_low_center_after_rise_075": center["top"] - middle["bottom"] - .75})
result = {"scope": "D1 isolated actual-mesh measurement only; not D3.1/D3.2 acceptance or locomotion proof", "tile": {"bounds_min": a.min(axis=0).tolist(), "bounds_max": a.max(axis=0).tolist(), "center_downray": center, "six_edge_samples": edges}, "models": models, "edge_midpoint_summary": edge_summary, "sources_sha256_unchanged": True}

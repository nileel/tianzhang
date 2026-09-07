"""Analyze saved body BakeMesh vs registered proxies; never equate coverage gaps with cloth penetration."""
import argparse
import json
import re
from pathlib import Path
import runpy

import numpy as np

ROOT = Path(__file__).resolve().parent
proxy_distance = runpy.run_path(str(ROOT.parent / 'isolation-03' / 'analyze_isolation.py'))['proxy_distance']


def points(values):
    return np.array([[v[k] for k in 'xyz'] for v in values], dtype=float).reshape(-1, 3)


def analyze(report):
    result = {'purpose': 'body surface coverage, NOT observed cloth/body penetration', 'snapshots': []}
    for f in report['frames']:
        s = f.get('skinning') or {}
        if not s.get('bodySurfaces'):
            continue
        centers, radii = points(s['bodyProxyCenters']), s['bodyProxyRadii']
        snap = {'time': f['time'], 'parts': [], 'pinned_targets_inside_torso_over_5mm': 0}
        for body in s['bodySurfaces']:
            world, local = points(body['worldVertices']), points(body['characterLocalVertices'])
            assert len(world) == len(local) == len(body['dominantBone'])
            assert body['bakeVsCpuMaxMeters'] < .00001, 'Body BakeMesh/CPU correspondence failed'
            names = np.array([body['boneNames'][i] for i in body['dominantBone']])
            distances = proxy_distance(world, centers[0], centers[1], radii[0], radii[1])
            part = {'name': body['rendererName'], 'mesh': body['meshName'], 'vertices': len(world),
                    'triangles': len(body['triangles']) // 3, 'bake_cpu_error_m': body['bakeVsCpuMaxMeters'],
                    'bone_regions': []}
            for bone in sorted(set(names)):
                if not any(word in bone.lower() for word in ('spine', 'pelvis', 'clavicle', 'neck')):
                    continue
                selected = names == bone
                ids = np.flatnonzero(selected)
                worst = ids[np.argmax(distances[selected])]
                part['bone_regions'].append({'bone': bone, 'count': int(selected.sum()),
                    'local_min': local[selected].min(axis=0).tolist(), 'local_max': local[selected].max(axis=0).tolist(),
                    'outside_over_5mm': int((distances[selected] > .005).sum()),
                    'maximum_uncovered_distance_m': float(max(0, distances[selected].max())),
                    'worst_vertex': int(worst), 'worst_local': local[worst].tolist()})
            snap['parts'].append(part)
        targets = points(s['cpuWorld'])[s['pinnedIndices']]
        distances = proxy_distance(targets, centers[0], centers[1], radii[0], radii[1])
        snap['pinned_targets_inside_torso_over_5mm'] = int((distances < -.005).sum())
        snap['fixed_target_maximum_depth_m'] = float(max(0, -distances.min()))
        result['snapshots'].append(snap)
    return result


def surface_distance_and_winding(point, triangles):
    a, b, c = triangles[:, 0], triangles[:, 1], triangles[:, 2]
    ab, ac, ap = b - a, c - a, point - a
    normal = np.cross(ab, ac)
    n2 = (normal * normal).sum(axis=1)
    aa, bb, cc = (ab * ab).sum(axis=1), (ab * ac).sum(axis=1), (ac * ac).sum(axis=1)
    pa, pc = (ap * ab).sum(axis=1), (ap * ac).sum(axis=1)
    denominator = np.maximum(aa * cc - bb * bb, 1e-25)
    u, v = (cc * pa - bb * pc) / denominator, (aa * pc - bb * pa) / denominator
    plane = (ap * normal).sum(axis=1) ** 2 / np.maximum(n2, 1e-25)
    distances = [np.where((u >= 0) & (v >= 0) & (u + v <= 1) & (n2 > 1e-20), plane, np.inf)]
    for start, end in [(a, b), (b, c), (c, a)]:
        edge = end - start
        t = np.clip(((point - start) * edge).sum(axis=1) / np.maximum((edge * edge).sum(axis=1), 1e-25), 0, 1)
        distances.append(((point - start - t[:, None] * edge) ** 2).sum(axis=1))
    x, y, z = a - point, b - point, c - point
    lx, ly, lz = np.linalg.norm(x, axis=1), np.linalg.norm(y, axis=1), np.linalg.norm(z, axis=1)
    numerator = (x * np.cross(y, z)).sum(axis=1)
    denominator = lx * ly * lz + (x * y).sum(axis=1) * lz + (y * z).sum(axis=1) * lx + (z * x).sum(axis=1) * ly
    winding = np.arctan2(numerator, denominator).sum() / (2 * np.pi)
    return float(np.sqrt(np.min(distances))), float(winding)


def constraint_conflicts(report, coefficients):
    checks = []
    for f in report['frames']:
        if f['time'] not in (0, 3):
            continue
        s = f['skinning']
        body = next(b for b in s['bodySurfaces'] if b['rendererName'] == 'SuperHero_Male')
        vertices = points(body['worldVertices'])
        topology = np.array(body['triangles']).reshape(-1, 3)
        triangles = vertices[topology]
        _, mapping = np.unique(vertices, axis=0, return_inverse=True)
        welded = mapping[topology]
        edges = np.concatenate([welded[:, [0, 1]], welded[:, [1, 2]], welded[:, [2, 0]]])
        _, edge_ids, counts = np.unique(np.sort(edges, axis=1), axis=0, return_inverse=True, return_counts=True)
        assert np.all(counts == 2), 'Welded body is not a closed manifold'
        assert np.all(np.bincount(edge_ids, weights=np.where(edges[:, 0] < edges[:, 1], 1, -1)) == 0), 'Body orientation mismatch'
        assert np.all(np.linalg.norm(np.cross(triangles[:, 1] - triangles[:, 0],
                                             triangles[:, 2] - triangles[:, 0]), axis=1) > 1e-12)
        assert set(s['pinnedIndices']) == set(np.flatnonzero(coefficients <= .0001))
        targets = points(s['cpuWorld'])
        checked = []
        for index in [415, 454, 359]:
            distance, winding = surface_distance_and_winding(targets[index], triangles)
            inside = abs(winding) > .9
            checked.append({'source_index': index, 'max_distance_m': float(coefficients[index]),
                'winding_number': winding, 'inside_body': bool(inside), 'distance_to_body_surface_m': distance,
                'minimum_motion_shortfall_m': max(0, distance - coefficients[index]) if inside else 0})
        checks.append({'time': f['time'], 'weld_rule': 'exactly equal world positions; no geometric tolerance',
            'welded_vertices': int(mapping.max() + 1), 'triangles': len(topology),
            'boundary_edges': int((counts == 1).sum()), 'nonmanifold_edges': int((counts > 2).sum()),
            'targets': checked})
    return checks


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('report', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--scene', type=Path, help='Saved baseline scene, used only to read the 456 maxDistance values')
    parser.add_argument('--samples-output', type=Path, help='Preserve only t=0 and t=3 raw frames for independent replay of analysis')
    args = parser.parse_args()
    report = json.loads(args.report.read_text(encoding='utf-8-sig'))
    result = analyze(report)
    coefficients = None
    if args.scene:
        coefficients = np.array([float(x) for x in re.findall(r'maxDistance: ([^\r\n]+)',
                                args.scene.read_text(encoding='utf-8-sig'))])
        assert len(coefficients) == 456 and report['releaseDiagnosedTorsoPins']
        released = json.loads((ROOT.parent / 'isolation-03' / 'analysis.json').read_text())[
            'fixed_target_proxy_overlap'][0]['source_mesh_indices']
        coefficients[released] = coefficients.max()
    elif 'effectiveMaxDistances' in report:
        coefficients = np.array(report['effectiveMaxDistances'])
    if coefficients is not None:
        result['confirmed_body_constraint_conflicts'] = constraint_conflicts(report, coefficients)
    if args.samples_output:
        samples = {k: v for k, v in report.items() if k not in ('frames', 'steps')}
        samples['frames'] = [f for f in report['frames'] if f['time'] in (0, 3)]
        samples['selectionNote'] = 'Two raw same-frame samples from the 96-frame body-probe run; no simulated state changed.'
        if coefficients is not None:
            samples['effectiveMaxDistances'] = coefficients.tolist()
            samples['effectiveMaxDistancesSource'] = ('Reconstructed from the frozen saved scene maxDistance coefficients '
                'plus the validated Isolation 03 28-vertex release mask; NOT directly sampled per-vertex at runtime. '
                'The resulting pinned index set is checked against each raw runtime snapshot.')
        args.samples_output.write_text(json.dumps(samples, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    serialized = json.dumps(result, ensure_ascii=False, indent=2) + '\n'
    if args.output:
        args.output.write_text(serialized, encoding='utf-8')
    else:
        print(serialized)

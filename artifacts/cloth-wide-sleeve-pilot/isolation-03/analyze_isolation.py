"""Recompute the bounded ABC diagnosis from the preserved Unity snapshots; no project asset writes."""
import json
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent


def points(values):
    return np.array([[v['x'], v['y'], v['z']] for v in values], dtype=float).reshape(-1, 3)


def proxy_distance(vertices, a, b, ra, rb):
    # The continuous union of interpolated spheres; not two isolated endpoint spheres.
    def distance(t):
        centers = a + t[:, None] * (b - a)
        return np.linalg.norm(vertices - centers, axis=1) - (ra + t * (rb - ra))
    lo = np.zeros(len(vertices))
    hi = np.ones(len(vertices))
    for _ in range(40):
        left, right = (2 * lo + hi) / 3, (lo + 2 * hi) / 3
        choose_left = distance(left) < distance(right)
        hi = np.where(choose_left, right, hi)
        lo = np.where(choose_left, lo, left)
    return np.minimum.reduce([distance(np.zeros(len(vertices))), distance(np.ones(len(vertices))),
                              distance((lo + hi) / 2)])


def analyze():
    reports = {g: json.loads((ROOT / f'runtime-{g}.json').read_text(encoding='utf-8')) for g in 'ABC'}
    for g, r in reports.items():
        assert len(r['frames']) == 96 and len(r['steps']) == 480
        assert [f['simulationFrame'] for f in r['frames']] == list(range(0, 480, 5))
        assert all(s['rootStepMeters'] == 0 and s['rootRotationStepDegrees'] == 0 for s in r['steps'])
        assert sum(f['clothEnabled'] for f in r['frames']) == (0 if g == 'A' else 95)
        assert r['colliderPairs'] == (0 if g == 'B' else 3)
        assert r['testedDirectionYaw'] == [150.0]
        for key in ['vertexCount', 'pinnedVertices', 'bendingStiffness', 'stretchingStiffness',
                    'maximumDistance', 'selfCollisionVertices']:
            assert r[key] == reports['A'][key]

    result = {'input_invariants': 'pass', 'cpu_ABC_max_difference_m': 0.0,
              'A_Bake_CPU_max_error_m': max(f['skinning']['bakeVsCpuMaxMeters'] for f in reports['A']['frames']),
              'static_comparisons': [], 'fixed_target_proxy_overlap': []}
    for index in range(96):
        skin_a = reports['A']['frames'][index]['skinning']
        if not skin_a.get('cpuWorld'):
            continue
        reference = points(skin_a['cpuWorld'])
        for g in 'BC':
            actual = points(reports[g]['frames'][index]['skinning']['cpuWorld'])
            result['cpu_ABC_max_difference_m'] = max(result['cpu_ABC_max_difference_m'],
                                                    float(np.linalg.norm(actual - reference, axis=1).max()))
    for index in [12, 24, 84]:
        for g in 'BC':
            s = reports[g]['frames'][index]['skinning']
            cpu = points(s['cpuWorld'])[s['pinnedIndices']]
            candidate = points(s['clothCandidateWorld'])
            indexed = np.linalg.norm(candidate[s['pinnedIndices']] - cpu, axis=1)
            nearest = np.linalg.norm(cpu[:, None, :] - candidate[None, :, :], axis=2).min(axis=1)
            result['static_comparisons'].append({'group': g, 'time': index / 12,
                'pinned_indexed_max_m': float(indexed.max()), 'pinned_over_5mm': int((indexed > .005).sum()),
                'nearest_any_cloth_vertex_max_m': float(nearest.max())})

    proof = json.loads((ROOT / 'runtime-C-collider-proof.json').read_text(encoding='utf-8'))
    for index in [0, 12, 24, 84]:
        s = proof['frames'][index]['skinning']
        cpu = points(s['cpuWorld'])[s['pinnedIndices']]
        centers, radii = points(s['bodyProxyCenters']), s['bodyProxyRadii']
        distances = np.array([proxy_distance(cpu, centers[p * 2], centers[p * 2 + 1],
                                            radii[p * 2], radii[p * 2 + 1]) for p in range(3)])
        depth = np.maximum(0, -distances.min(axis=0))
        result['fixed_target_proxy_overlap'].append({'time': index / 12,
            'cloth_enabled': proof['frames'][index]['clothEnabled'],
            'fixed_targets_inside_over_5mm': int((depth > .005).sum()), 'maximum_depth_m': float(depth.max()),
            'counts_torso_upperarm_forearm': (distances < -.005).sum(axis=1).tolist(),
            'source_mesh_indices': np.array(s['pinnedIndices'])[depth > .005].tolist()})
    result['limitations'] = [
        'Proxy overlap is between CPU skinning targets and registered proxy geometry, not triangle/body penetration.',
        'Candidate Cloth coordinate/index agreement is demonstrated for static B only; dynamic timing is not calibrated.',
        'BakeMesh in enabled-Cloth B/C is not an independent skinning reference.',
        'The internal solver priority/order and natural wide-sleeve feasibility remain unproven.']
    return result


if __name__ == '__main__':
    output = analyze()
    (ROOT / 'analysis.json').write_text(json.dumps(output, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(output, ensure_ascii=False, indent=2))

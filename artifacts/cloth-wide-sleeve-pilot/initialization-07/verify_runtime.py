"""Check saved Unity evidence; body distances below refer to CPU skinning, not simulated Cloth."""
from pathlib import Path
import argparse
import hashlib
import json
import runpy
import numpy as np

ROOT = Path(__file__).resolve().parent
TEMP = Path('D:/Temp/TianZhang-Blender')
OLD = TEMP / 'cloth-wide-sleeve-range-06-capture/runtime-report.json'
RAW = TEMP / 'cloth-wide-sleeve-initialization-07-visible-capture/runtime-report.json'
OPEN = TEMP / 'cloth-wide-sleeve-initialization-07-validation/open-pose.json'
BODY = runpy.run_path(str(ROOT.parent / 'torso-contact-04/analyze_body_coverage.py'))
points = BODY['points']
rotation = runpy.run_path(str(ROOT.parent / 'sleeve-range-06/verify_runtime.py'))['rotation']


def read(path):
    return json.loads(path.read_text('utf-8-sig'))


def write(name, value):
    (ROOT / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def check_open():
    s = read(OPEN)
    old = read(ROOT.parent / 'sleeve-range-06/runtime-body-samples.json')
    body = next(b for b in s['bodySurfaces'] if b['rendererName'] == 'SuperHero_Male')
    vertices, topology = points(body['worldVertices']), np.array(body['triangles']).reshape(-1, 3)
    _, mapping = np.unique(vertices, axis=0, return_inverse=True)
    welded = mapping[topology]
    edges = np.concatenate([welded[:, [0, 1]], welded[:, [1, 2]], welded[:, [2, 0]]])
    _, ids, counts = np.unique(np.sort(edges, axis=1), axis=0, return_inverse=True, return_counts=True)
    assert np.all(counts == 2)
    assert np.all(np.bincount(ids, weights=np.where(edges[:, 0] < edges[:, 1], 1, -1)) == 0)
    values = np.array([BODY['surface_distance_and_winding'](q, vertices[topology]) for q in points(s['cpuWorld'])])
    inside = np.abs(values[:, 1]) > .9
    outside_root = np.array(old['sleeveRangeProof']['axisFractions']) > .17
    assert not s['clothEnabled'] and s['bakeVsCpuMaxMeters'] < .00001
    assert not (inside & outside_root).any(), 'Open-pose sleeve belly still has vertices inside the body.'
    result = {'source': str(OPEN), 'sourceSha256': hashlib.sha256(OPEN.read_bytes()).hexdigest(),
              'bodyClosedAndOrientedAfterExactWeld': True, 'bakeVsCpuMaxMeters': s['bakeVsCpuMaxMeters'],
              'nonRootVerticesInsideBody': int((inside & outside_root).sum()),
              'nonRootMinimumSurfaceDistanceMeters': float(values[outside_root, 0].min()),
              'rootIntersections': [{'index': int(i), 'depthMeters': float(values[i, 0]),
                                     'maxDistance': old['effectiveMaxDistances'][i]} for i in np.flatnonzero(inside)],
              'boundary': 'Per-vertex CPU skinning/body surface test; root overlaps are retained, not hidden. '
                          'Not a triangle intersection, cloth/body penetration, or solver convergence guarantee.'}
    write('open-pose-proof.json', result)
    print(json.dumps(result, ensure_ascii=False))


def check_runtime():
    raw, old = read(RAW), read(OLD)
    assert raw['initializeFromOpenPose'] and raw['expandSleeveRange'] and raw['correctTorsoTransition']
    assert not raw['releaseDiagnosedTorsoPins'] and raw['isolationMode'] == 'Retest02'
    assert raw['initializationSeconds'] == 4 and raw['durationSeconds'] == 96
    assert raw['capturedFrames'] == len(raw['frames']) == 1152 and len(raw['steps']) == 5760
    for key in ['effectiveMaxDistances', 'sleeveRangeProof', 'testedDirectionYaw', 'simulationHz', 'captureHz',
                'pinnedVertices', 'colliderPairs', 'selfCollisionVertices', 'bendingStiffness',
                'stretchingStiffness', 'maximumDistance', 'tacticalOrthographicSize', 'rendererLossyScale', 'cameraEuler']:
        assert raw[key] == old[key], key
    reset_indices = [s['simulationFrame'] for s in raw['steps'] if s['reset']]
    assert reset_indices == list(range(0, 5760, 960))
    assert all(s['clothEnabled'] == (s['simulationFrame'] % 960 != 0) for s in raw['steps'])
    assert sum(not f['clothEnabled'] for f in raw['frames']) == 6
    max_root = max_cpu = max_proxy = max_radius = 0.0
    cpu_pairs = 0
    for d in range(6):
        for k in range(720):
            new, prior = raw['steps'][d*960+240+k], old['steps'][d*720+k]
            max_root = max(max_root, float(np.linalg.norm(points([new['rootPosition']])-points([prior['rootPosition']]))))
        for k in range(144):
            new, prior = raw['frames'][d*192+48+k], old['frames'][d*144+k]
            assert new['action'] == prior['action'] and new['direction'] == prior['direction']
            if prior['clothEnabled']:
                max_radius = max(max_radius, float(np.max(np.abs(np.array(new['measurement']['colliderRadii']) -
                                                               np.array(prior['measurement']['colliderRadii'])))))
                max_proxy = max(max_proxy, float(np.max(np.abs(points(new['measurement']['colliderCenters']) -
                                                              points(prior['measurement']['colliderCenters'])))))
            a, b = new.get('skinning') or {}, prior.get('skinning') or {}
            if b.get('cpuWorld'):
                assert a.get('cpuWorld'), (d, k)
                cpu_pairs += 1
                max_cpu = max(max_cpu, float(np.max(np.linalg.norm(points(a['cpuWorld'])-points(b['cpuWorld']), axis=1))))
                assert a['pinnedIndices'] == b['pinnedIndices']
    assert max(max_root, max_cpu, max_proxy) < .0001, 'Same-action input mismatch exceeds 0.1 mm.'
    assert max_radius < .000001, 'World-radius float/scale difference exceeds 1 micrometer.'
    initial = raw['frames'][192]['skinning']
    saved = read(OPEN)
    def local(s):
        b = next(b for b in s['bodySurfaces'] if b['rendererName'] == 'SuperHero_Male')
        return (points(s['cpuWorld'])-points([b['characterPosition']])) @ rotation(b['characterRotation'])
    open_error = float(np.max(np.linalg.norm(local(initial)-local(saved), axis=1)))
    assert not raw['frames'][192]['clothEnabled'] and open_error < .00001
    result = {'source': str(RAW), 'sourceSha256': hashlib.sha256(RAW.read_bytes()).hexdigest(),
              'frames': 1152, 'simulationSteps': 5760, 'resetSteps': reset_indices,
              'clothOffSteps': reset_indices, 'initializationSecondsPerDirection': 4,
              'unchangedMaxDistances': 456, 'unchangedPinnedVertices': raw['pinnedVertices'],
              'sameActionCpuSnapshotPairs': cpu_pairs, 'sameActionCpuMaxErrorMeters': max_cpu,
              'sameActionProxyCenterMaxComponentErrorMeters': max_proxy,
              'sameActionWorldRadiusMaxErrorMeters': max_radius,
              'sameActionRootMaxErrorMeters': max_root, 'openPoseVsEditModeMaxErrorMeters': open_error,
              'openPoseMusclesFromActualStep0': {k: raw['steps'][0][k] for k in ['armUp', 'armForward', 'forearm']},
              'boundary': 'Same-action inputs use 07 cycleTime=06 trialTime+4. Float time quantization is measured. '
                          'Old raw Cloth index/world conversion is uncalibrated; no penetration-depth claim.'}
    write('runtime-input-proof.json', result)
    compact = {k: v for k, v in raw.items() if k != 'frames'}
    compact['frames'] = [{k: v for k, v in f.items() if k != 'skinning'} for f in raw['frames']]
    compact['rawEvidencePath'] = str(RAW)
    (ROOT / 'runtime-report.json').write_text(json.dumps(compact, separators=(',', ':'))+'\n', encoding='utf-8')
    selected = {k: v for k, v in raw.items() if k not in ('frames', 'steps')}
    selected['frames'] = [raw['frames'][i] for i in [192, 240, 252, 276]]
    selected['selectionNote'] = 'Unmodified raw snapshots: yaw150 cycle0/4/5/7 (open/rest/rest/raised); not a second simulation.'
    (ROOT / 'runtime-body-samples.json').write_text(json.dumps(selected, separators=(',', ':'))+'\n', encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', action='store_true')
    args = parser.parse_args()
    if args.runtime:
        check_runtime()
    else:
        check_open()

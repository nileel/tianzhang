"""Reproduce the one authored torso candidate from prior real runtime snapshots; no parameter search."""
import argparse
import json
from pathlib import Path
import re
import runpy
import numpy as np

ROOT = Path(__file__).resolve().parent
previous = runpy.run_path(str(ROOT.parent / 'torso-contact-04' / 'analyze_body_coverage.py'))
points, distance = previous['points'], previous['proxy_distance']
REFERENCE = np.array([[.01, .995, .025], [.01, 1.43, .025]])
RADII = [.18, .225]


def rotate(q, p):
    q = np.array(q, float)
    q /= np.linalg.norm(q)
    return p + 2 * np.cross(q[:3], np.cross(q[:3], p) + q[3] * p)


def quat(q):
    return [q[k] for k in 'xyzw']


def compact(raw):
    source = json.loads((ROOT.parent / 'torso-contact-04' / 'body-surface-samples.json').read_text())
    result = {'coefficientSource': source['effectiveMaxDistancesSource'],
              'baselineCoefficients': source['effectiveMaxDistances'], 'snapshots': []}
    for f in raw['frames']:
        s = f.get('skinning') or {}
        if not s.get('bodySurfaces'):
            continue
        b = next(b for b in s['bodySurfaces'] if b['rendererName'] == 'SuperHero_Male')
        ids = [b['boneNames'].index(n) for n in ['spine_02', 'neck_01']]
        names = [b['boneNames'][i] for i in b['dominantBone']]
        region = [i for i, n in enumerate(names) if n.startswith(('spine', 'pelvis', 'neck', 'clavicle_l'))]
        result['snapshots'].append({'time': f['time'], 'cpuWorld': points(s['cpuWorld']).tolist(),
            'characterPosition': points([b['characterPosition']])[0].tolist(),
            'characterRotation': quat(b['characterRotation']), 'characterScale': points([b['characterScale']])[0].tolist(),
            'bonePositions': points(b['bonePositionsLocal'])[ids].tolist(),
            'boneRotations': [quat(b['boneRotationsLocal'][i]) for i in ids],
            'bodyWorldRegion': points(b['worldVertices'])[region].tolist(), 'bodyRegions': [names[i] for i in region],
            'originalProxyCenters': points(s['bodyProxyCenters']).tolist(), 'originalProxyRadii': s['bodyProxyRadii']})
    assert len(result['snapshots']) == 11
    return result


def derive(data):
    frames = data['snapshots']
    first = frames[0]
    offsets = [rotate([-q[0], -q[1], -q[2], q[3]], p - b)
               for q, p, b in zip(first['boneRotations'], REFERENCE, np.array(first['bonePositions']))]
    required = np.zeros(456)
    per_frame = []
    for f in frames:
        centers = np.array([rotate(q, o) + b for q, o, b in zip(f['boneRotations'], offsets, f['bonePositions'])])
        centers = rotate(f['characterRotation'], centers * np.array(f['characterScale'])) + f['characterPosition']
        d = distance(np.array(f['cpuWorld']), centers[0], centers[1], *RADII)
        required = np.maximum(required, np.maximum(0, .015 - d))
        body = np.array(f['bodyWorldRegion'])
        covered = distance(body, centers[0], centers[1], *RADII)
        c, r = np.array(f['originalProxyCenters']), f['originalProxyRadii']
        for i in [2, 4]:
            covered = np.minimum(covered, distance(body, c[i], c[i + 1], r[i], r[i + 1]))
        per_frame.append({'time': f['time'], 'candidateCentersWorld': centers.tolist(),
            'combinedBodyRegionOutsideOver5mm': int((covered > .005).sum()),
            'maximumBodyRegionGapMeters': float(max(0, covered.max()))})
    baseline = np.array(data['baselineCoefficients'])
    ids = np.flatnonzero(required > baseline + 1e-8)
    values = np.ceil(required[ids] * 1000) / 1000
    result = baseline.copy()
    result[ids] = np.maximum(result[ids], values)
    helper = (ROOT.parents[2] / 'src/Assets/Tests/ClothWideSleevePilot/Runtime/ClothWideSleeveTorsoCorrection.cs').read_text()
    parsed_ids = [int(x) for x in re.findall(r'\d+', re.search(r'int\[\] Indices =\s*\{(.*?)\}', helper, re.S)[1])]
    parsed_values = [float(x) for x in re.findall(r'(\.\d+)f', re.search(r'float\[\] Distances =\s*\{(.*?)\}', helper, re.S)[1])]
    assert parsed_ids == ids.tolist() and np.array_equal(parsed_values, values), 'Authored mask differs from derivation'
    assert len(ids) == 71 and (result <= .0001).sum() == 29 and np.all(result >= required - 1e-8)
    return {'method': 'maximum over 11 recorded poses of max(0, 0.015 - proxy signed distance), only increases, ceil to 1mm',
        'scope': 'individual target-sphere reachability, not simultaneous Cloth feasibility or measured cloth penetration',
        'referenceCharacterLocal': REFERENCE.tolist(), 'radii': RADII, 'boneLocalOffsets': [o.tolist() for o in offsets],
        'changes': [{'index': int(i), 'baseline': float(baseline[i]), 'required': float(required[i]), 'candidate': float(result[i])} for i in ids],
        'remainingFixedIndices': np.flatnonzero(result <= .0001).tolist(), 'effectiveMaxDistances': result.tolist(),
        'coverage': per_frame}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--raw', type=Path)
    parser.add_argument('--inputs', type=Path, default=ROOT / 'constraint-inputs.json')
    parser.add_argument('--output', type=Path, default=ROOT / 'constraint-derivation.json')
    args = parser.parse_args()
    if args.raw:
        data = compact(json.loads(args.raw.read_text(encoding='utf-8-sig')))
        args.inputs.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
    else:
        data = json.loads(args.inputs.read_text())
    result = derive(data)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print('PASS: authored 71-value mask equals derivation; 29 pins retained; maximum', max(x['required'] for x in result['changes']))

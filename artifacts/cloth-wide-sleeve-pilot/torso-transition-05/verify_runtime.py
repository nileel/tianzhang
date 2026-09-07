"""Validate applied inputs and observed target feasibility, not uncalibrated simulated penetration."""
import json
from pathlib import Path
import runpy
import numpy as np

ROOT = Path(__file__).resolve().parent
raw_path = Path('D:/Temp/TianZhang-Blender/cloth-wide-sleeve-transition-05-capture/runtime-report.json')
raw = json.loads(raw_path.read_text())
expected = json.loads((ROOT / 'constraint-derivation.json').read_text())
old = json.loads((ROOT.parent / 'torso-contact-04' / 'body-surface-samples.json').read_text())
analysis = runpy.run_path(str(ROOT.parent / 'torso-contact-04' / 'analyze_body_coverage.py'))
points = analysis['points']
assert raw['correctTorsoTransition'] and not raw['releaseDiagnosedTorsoPins']
assert raw['capturedFrames'] == len(raw['frames']) == 864 and len(raw['steps']) == 4320
assert raw['testedDirectionYaw'] == [90, 150, 210, 270, 330, 30]
coefficients = np.array(raw['effectiveMaxDistances'])
assert np.max(np.abs(coefficients - expected['effectiveMaxDistances'])) < 1e-7
assert raw['pinnedVertices'] == 29 and raw['colliderPairs'] == 3 and raw['selfCollisionVertices'] == 360
assert abs(raw['bendingStiffness'] - .70) < 1e-7 and abs(raw['stretchingStiffness'] - .88) < 1e-7
for i, f in enumerate(raw['frames']):
    assert f['captureIndex'] == i and f['simulationFrame'] == i * 5
    assert abs(f['time'] - i / 12) < 1e-5 and f['direction'] == i // 144

snapshots = [f for f in raw['frames'] if f.get('skinning') and f['skinning'].get('bodySurfaces')]
assert len(snapshots) == 15
for wanted in [295 / 60, 5.5, 475 / 60]:
    assert any(abs(f['trialTime'] - wanted) < 1e-5 for f in snapshots)
checks = []
selected = []
target_checks = []
for f in snapshots:
    s = f['skinning']
    assert set(s['pinnedIndices']) == set(expected['remainingFixedIndices'])
    assert max(b['bakeVsCpuMaxMeters'] for b in s['bodySurfaces']) < 1e-5
    centers = points(s['bodyProxyCenters'])
    d = analysis['proxy_distance'](points(s['cpuWorld']), centers[0], centers[1], *s['bodyProxyRadii'][:2])
    shortfall = np.maximum(0, .015 - d) - coefficients
    assert shortfall.max() < 1e-6
    target_checks.append({'absoluteTime': f['time'], 'trialTime': f['trialTime'],
                          'maximumPositiveShortfallMeters': float(max(0, shortfall.max()))})
    if f['trialTime'] not in (0, 3):
        continue
    prior = next(x for x in old['frames'] if x['time'] == f['trialTime'])['skinning']
    c, pc = points(s['bodyProxyCenters']), points(prior['bodyProxyCenters'])
    assert np.max(np.abs(c[2:] - pc[2:])) < 1e-5
    assert np.max(np.abs(np.array(s['bodyProxyRadii'][2:]) - prior['bodyProxyRadii'][2:])) < 1e-6
    assert np.max(np.abs(points(s['cpuWorld']) - points(prior['cpuWorld']))) < 1e-5
    assert np.max(np.abs(np.array(s['bodyProxyRadii'][:2]) - [.18, .225])) < 1e-6
    ex = next(x for x in expected['coverage'] if x['time'] == f['trialTime'])
    assert np.max(np.abs(c[:2] - ex['candidateCentersWorld'])) < 1e-5
    checks.append({'absoluteTime': f['time'], 'trialTime': f['trialTime'],
        'unchangedArmCenterMaxErrorMeters': float(np.max(np.abs(c[2:] - pc[2:]))),
        'unchangedSkinningTargetMaxErrorMeters': float(np.max(np.abs(points(s['cpuWorld']) - points(prior['cpuWorld'])))),
        'actualTorsoCenters': c[:2].tolist(), 'actualTorsoRadii': s['bodyProxyRadii'][:2]})
    selected.append(f)

proof = analysis['constraint_conflicts']({'frames': [dict(f, time=f['trialTime']) for f in selected]}, coefficients)
assert all(t['minimum_motion_shortfall_m'] == 0 for p in proof for t in p['targets'])
old_full = json.loads((ROOT.parent / 'isolation-03' / 'runtime-pin-release-01.json').read_text())
arm_error = 0
arm_samples = 0
for f, prior in zip(raw['frames'], old_full['frames']):
    assert f['simulationFrame'] == prior['simulationFrame']
    if not f['clothEnabled']:
        continue
    a, b = f['measurement'], prior['measurement']
    arm_error = max(arm_error, float(np.max(np.abs(points(a['colliderCenters'])[2:] - points(b['colliderCenters'])[2:]))),
                    float(np.max(np.abs(np.array(a['colliderRadii'][2:]) - b['colliderRadii'][2:]))))
    arm_samples += 1
assert arm_error == 0 and arm_samples == 858
summary = {k: v for k, v in raw.items() if k != 'frames'}
summary['frames'] = [{k: v for k, v in f.items() if k != 'skinning'} for f in raw['frames']]
(ROOT / 'runtime-report.json').write_text(json.dumps(summary, indent=2) + '\n')
samples = {k: v for k, v in raw.items() if k not in ('frames', 'steps')}
samples['frames'] = selected
samples['selectionNote'] = 'Unmodified raw frames at absolute t12/t15 (local t0/t3) from the single 864-frame candidate run.'
samples['effectiveMaxDistancesSource'] = 'Directly recorded from sleeveCloth.coefficients in this runtime; not reconstructed.'
(ROOT / 'runtime-body-samples.json').write_text(json.dumps(samples, indent=2) + '\n')
result = {'appliedInputsMatch': True, 'samePoseChecks': checks, 'bodyTargetConstraintChecks': proof,
    'allSnapshotTargetSphereChecks': target_checks, 'unchangedArmAllFrames': {'samples': arm_samples, 'maximumComponentError': arm_error},
    'coverageAllBodySnapshots': analysis['analyze'](raw),
    'coordinateFlagPassed': sum(f['measurement']['coordinateProofValid'] for f in raw['frames'] if f['clothEnabled'] and f.get('measurement')),
    'coordinateFlagSampleCount': sum(bool(f.get('measurement')) for f in raw['frames'] if f['clothEnabled']),
    'maximumPinnedCandidateErrorMeters': max(f['measurement']['pinnedMaxErrorMeters'] for f in raw['frames'] if f['clothEnabled'] and f.get('measurement')),
    'boundary': 'Reachability is individual movement-sphere geometry only. The old coordinate flag is not full index/surface calibration; no simulated penetration claim.'}
(ROOT / 'runtime-input-proof.json').write_text(json.dumps(result, indent=2) + '\n')
print('PASS: 864 frames, 4320 steps, 15 body snapshots, applied mask/proxies and unchanged same-pose arms/targets; old three target shortfalls now zero.')
print('Old partial coordinate flag:', result['coordinateFlagPassed'], '/', result['coordinateFlagSampleCount'],
      'max pin candidate error', result['maximumPinnedCandidateErrorMeters'])

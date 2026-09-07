"""Verify Range 06 inputs against FBX/05 snapshots; no Unity run or simulated-penetration claim."""
from pathlib import Path
import hashlib
import importlib.util
import json
import numpy as np

ROOT = Path(__file__).resolve().parent
RAW = Path('D:/Temp/TianZhang-Blender/cloth-wide-sleeve-range-06-capture/runtime-report.json')
PRIOR = Path('D:/Temp/TianZhang-Blender/cloth-wide-sleeve-transition-05-capture/runtime-report.json')
FBX = ROOT.parents[2] / 'src/Assets/Tests/ClothWideSleevePilot/Models/TZ_ClothWideSleevePilot_v001.fbx'
PARSER = Path('D:/Tools/Blender/5.2.0/5.2/scripts/addons_core/io_scene_fbx/fbx2json.py')


def points(values):
    return np.array([[v[k] for k in 'xyz'] for v in values], dtype=float)


def rotation(q):
    x, y, z, w = [q[k] for k in 'xyzw']
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                     [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                     [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])


def child(element, key):
    return next((e for e in element.elems if e.id == key), None)


def object_name(element):
    return element.props[1].split(b'\x00')[0].decode()


def verify():
    raw = json.loads(RAW.read_text('utf-8-sig'))
    old = json.loads(PRIOR.read_text('utf-8-sig'))
    assert raw['expandSleeveRange'] and raw['correctTorsoTransition'] and not raw['releaseDiagnosedTorsoPins']
    assert raw['isolationMode'] == 'Retest02'
    assert raw['capturedFrames'] == len(raw['frames']) == 864 and len(raw['steps']) == 4320
    assert raw['steps'] == old['steps']
    for key in ['testedDirectionYaw', 'simulationHz', 'captureHz', 'durationSeconds', 'pinnedVertices',
                'colliderPairs', 'selfCollisionVertices', 'bendingStiffness', 'stretchingStiffness',
                'tacticalOrthographicSize', 'rendererLossyScale', 'cameraEuler']:
        assert raw[key] == old[key], key
    assert raw['pinnedVertices'] == 29 and raw['selfCollisionVertices'] == 360

    previous = np.array(old['effectiveMaxDistances'])
    actual = np.array(raw['effectiveMaxDistances'])
    p = raw['sleeveRangeProof']
    d, t, y = [np.array(p[k]) for k in ['radialDistances', 'axisFractions', 'radialUpOffsets']]
    eligible = (previous > .0001) & (t > .17) & (y < 0)
    expected = np.where(eligible, np.maximum(previous, 2*d), previous)
    assert np.max(np.abs(actual - expected)) < 1e-7
    changed = actual != previous
    assert int(changed.sum()) == p['changedVertices'] == 249
    assert not changed[t <= .17].any() and not changed[y >= 0].any()
    assert np.array_equal(actual <= .0001, previous <= .0001)

    # Use the installed Blender distribution's binary parser only; never call its JSON writer or launch Blender.
    spec = importlib.util.spec_from_file_location('fbx_readonly', PARSER)
    parser = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(parser)
    tree, version = parser.parse(str(FBX))
    fbx_hash = hashlib.sha256(FBX.read_bytes()).hexdigest()
    assert fbx_hash == '5bd14d613a68881ed5202f03ce6a494535382dbb541c5bc4310cc6a30e0367a4'
    objects = child(tree, b'Objects')
    by_id = {e.props[0]: e for e in objects.elems}
    connections = [e.props for e in child(tree, b'Connections').elems]
    geometry = next(e for e in objects.elems if e.id == b'Geometry' and
                    object_name(e) == 'TZ_LeftWideSleeve_ClothMesh')
    skin_id = next(c[1] for c in connections if c[2] == geometry.props[0] and by_id[c[1]].id == b'Deformer')
    clusters = [by_id[c[1]] for c in connections if c[2] == skin_id]
    vertices = np.array(child(geometry, b'Vertices').props[0]).reshape(-1, 3)
    clusters = [c for c in clusters if child(c, b'Indexes') and len(child(c, b'Indexes').props[0])]
    weights = {}
    for c in clusters:
        weights[object_name(c)] = np.zeros(456)
        weights[object_name(c)][child(c, b'Indexes').props[0]] = child(c, b'Weights').props[0]
    assert set(weights) == {'clavicle_l', 'upperarm_l', 'lowerarm_l', 'hand_l'}
    assert all(np.ptp(w[k*24:(k+1)*24]) == 0 for w in weights.values() for k in range(19))

    snapshots = [f for f in raw['frames'] if f.get('skinning') and f['skinning'].get('bodySurfaces')]
    assert len(snapshots) == 15
    max_cpu_error = max_proxy_error = 0.0
    for f, prior in zip(raw['frames'], old['frames']):
        for key in ['captureIndex', 'simulationFrame', 'time', 'trialTime', 'direction', 'yaw',
                    'action', 'clothEnabled', 'rootPosition', 'rootRotation']:
            assert f[key] == prior[key], (f['captureIndex'], key)
        if f['clothEnabled']:
            for key in ['colliderCenters', 'colliderRadii']:
                assert f['measurement'][key] == prior['measurement'][key]
        s = f.get('skinning')
        if not s or not s.get('cpuWorld'):
            continue
        previous_s = prior['skinning']
        max_cpu_error = max(max_cpu_error, float(np.abs(points(s['cpuWorld'])-points(previous_s['cpuWorld'])).max()))
        max_proxy_error = max(max_proxy_error, float(np.abs(points(s['bodyProxyCenters'])-
                                                            points(previous_s['bodyProxyCenters'])).max()))
        assert s['pinnedIndices'] == previous_s['pinnedIndices']
    assert max_cpu_error == max_proxy_error == 0

    mappings = []
    match_errors = []
    for f in [s for s in snapshots if s['trialTime'] in (0, 3)]:
        s = f['skinning']
        b = next(b for b in s['bodySurfaces'] if b['rendererName'] == 'SuperHero_Male')
        cpu = (points(s['cpuWorld'])-points([b['characterPosition']])[0]) @ rotation(b['characterRotation'])
        predicted = np.zeros((456, 3))
        for c in clusters:
            name = object_name(c)
            bind = np.array(child(c, b'Transform').props[0]).reshape(4, 4).T
            local = (np.c_[vertices, np.ones(456)] @ bind.T)[:, :3] * [-1, 1, 1]
            bi = b['boneNames'].index(name)
            transformed = local @ rotation(b['boneRotationsLocal'][bi]).T + points(b['bonePositionsLocal'])[bi]
            predicted += transformed * weights[name][:, None]
        distances = np.linalg.norm(predicted[:, None, :]-cpu[None, :, :], axis=2)
        mapping = distances.argmin(axis=1)
        assert len(set(mapping.tolist())) == 456 and distances.min(axis=1).max() < 1e-6
        mappings.append(mapping)
        match_errors.append(float(distances.min(axis=1).max()))
    assert np.array_equal(*mappings)
    mapping = mappings[0]
    bind_points = {}
    for c in clusters:
        link = np.array(child(c, b'TransformLink').props[0]).reshape(4, 4).T
        bind_points[object_name(c)] = link[:3, 3] * [-1, 1, 1]
    # Exact frozen export has (x,z,y) in character bind space, checked by TransformLink * Transform previously.
    source_rest = vertices[:, [0, 2, 1]]
    shoulder = bind_points['upperarm_l']
    axis = bind_points['hand_l'] - shoulder
    source_t = (source_rest-shoulder) @ axis / np.dot(axis, axis)
    radial = source_rest-shoulder-source_t[:, None]*axis
    radial_error = float(np.abs(np.linalg.norm(radial, axis=1)-d[mapping]).max())
    axis_fraction_error = float(np.abs(source_t-t[mapping]).max())
    assert radial_error < 1e-6 and axis_fraction_error < 1e-6
    assert np.max(np.abs(radial[:, 1]-y[mapping])) < 1e-6

    result = {'appliedFormulaMatches': True, 'changedVertices': int(changed.sum()),
              'rootBandChanged': int(changed[t <= .17].sum()), 'upperSurfaceChanged': int(changed[y >= 0].sum()),
              'retainedPins': raw['pinnedVertices'], 'maximumDistance': float(actual.max()),
              'fbxSha256': fbx_hash, 'fbxVersion': version, 'fbxToRuntimeCpuMaxErrorsMeters': match_errors,
              'bindRadialMaxErrorMeters': radial_error, 'bindAxisFractionMaxError': axis_fraction_error,
              'all15SnapshotCpuTargetMaxDifferenceMeters': max_cpu_error,
              'all858EnabledFrameProxiesIdentical': True, 'all4320StepsIdentical': True,
              'fullSnapshotCount': len(snapshots), 'changedIndices': np.where(changed)[0].tolist(),
              'sourceToUnityMapping': mapping.tolist(),
              'oldPartialCoordinateFlagPassed': sum(f['measurement']['coordinateProofValid'] for f in raw['frames'] if f['clothEnabled']),
              'boundary': 'Input and individual motion-sphere verification only; not full solver reachability, cloth/body penetration, or visual acceptance.'}
    (ROOT / 'runtime-input-proof.json').write_text(json.dumps(result, indent=2)+'\n')
    compact = dict(raw)
    compact['frames'] = [{k: v for k, v in f.items() if k != 'skinning'} for f in raw['frames']]
    (ROOT / 'runtime-report.json').write_text(json.dumps(compact, indent=2)+'\n')
    selected = {k: v for k, v in raw.items() if k not in ('frames', 'steps')}
    selected['frames'] = [f for f in snapshots if f['trialTime'] in (0, 3)]
    selected['selectionNote'] = 'Unmodified full raw frames t12/t15; coefficients are directly recorded at runtime.'
    (ROOT / 'runtime-body-samples.json').write_text(json.dumps(selected, indent=2)+'\n')
    print(json.dumps({k: v for k, v in result.items() if k not in ('changedIndices', 'sourceToUnityMapping')}, indent=2))


if __name__ == '__main__':
    verify()

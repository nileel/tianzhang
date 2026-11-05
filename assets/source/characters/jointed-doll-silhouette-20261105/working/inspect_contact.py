"""Check actual posed sleeve triangles against the body and robe panels."""
import bpy
import numpy as np
from mathutils.bvhtree import BVHTree


def hand_passage(scene, side):
    """Project the rigid hand silhouette through the entry-to-wrist passage only."""
    sign = 1 if side == 'L' else -1
    sleeve, body = scene.objects['sleeve.' + side].data, scene.objects['Body'].data
    cloth = np.array([v.co[:] for v in sleeve.vertices])
    points = np.array([v.co[:] for v in body.vertices])
    for values in (cloth, points):
        values[:, 0] *= sign
        values[:, 2] += .07 * (values[:, 0] - .447)
    cloth_tree = BVHTree.FromPolygons(cloth.tolist(), [p.vertices[:] for p in sleeve.polygons])
    hand_tree = BVHTree.FromPolygons(points.tolist(), [p.vertices[:] for p in body.polygons
        if all(points[i, 0] > .69 for i in p.vertices)])
    hand_points = points[points[:, 0] >= .69]
    low, high = hand_points.min(axis=0), hand_points.max(axis=0)
    sampled = blocked = 0
    for y in np.arange(low[1], high[1] + .0015, .0015):
        for z in np.arange(low[2], high[2] + .0015, .0015):
            if hand_tree.ray_cast((.15, y, z), (1, 0, 0))[0] is not None:
                sampled += 1
                blocked += int(cloth_tree.ray_cast((.35, y, z), (1, 0, 0), .38)[0] is not None)
    return {'handSilhouetteRays': sampled, 'blockedEntryToWristRays': blocked,
            'gridMm': 1.5, 'axisSegmentM': [.35, .73],
            'scope': 'Bind-pose hand silhouette through the elbow entry to wrist opening; hanging lining beyond this interval is intentionally excluded.'}


def inspect(scene):
    rig = scene.objects['JointedDollRig']
    saved_action, saved_frame = rig.animation_data.action, scene.frame_current
    targets = ['Body'] + [p + side for p in ('front_panel.', 'rear_panel.') for side in ('L', 'R')]
    events, frame_counts = [], {}
    try:
        for action in sorted(bpy.data.actions, key=lambda action: action.name):
            rig.animation_data.action = action
            frames = range(int(action.frame_range.x), int(action.frame_range.y) + 1)
            frame_counts[action.name] = len(frames)
            for frame in frames:
                scene.frame_set(frame)
                deps = bpy.context.evaluated_depsgraph_get()
                trees = {name: BVHTree.FromObject(scene.objects[name], deps)
                         for name in targets + ['sleeve.L', 'sleeve.R']}
                for name in ['sleeve.L', 'sleeve.R']:
                    for other in targets + (['sleeve.R'] if name == 'sleeve.L' else []):
                        pairs = trees[name].overlap(trees[other])
                        if pairs:
                            events.append([action.name, frame, name, other, len(pairs)])
    finally:
        rig.animation_data.action = saved_action
        scene.frame_set(saved_frame)
    return {'sampledFrames': frame_counts, 'intersectingObjectFramePairs': events,
            'scope': 'Three authored clips at integer source frames; no claim for other motions, interpolation, self-intersection or fabric simulation.'}

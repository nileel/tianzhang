"""Verify both hand silhouettes through the bore and posed contacts on the delivered rig."""
import bpy
import numpy as np
from mathutils.bvhtree import BVHTree


def arm_coordinates(points, sign):
    # Remove the measured slope of the straight forearm in the bind pose.
    points = np.array(points, dtype=float)
    along = sign * points[:, 0] - .447
    points[:, 1] += .024 * along
    points[:, 2] += .070 * along
    return points


def hand_passage(scene, side):
    sign = 1 if side == 'L' else -1
    sleeve = scene.objects['sleeve.' + side].data
    body = scene.objects['Body'].data
    cloth_points = arm_coordinates([v.co[:] for v in sleeve.vertices], sign)
    body_points = arm_coordinates([v.co[:] for v in body.vertices], sign)
    cloth = BVHTree.FromPolygons(cloth_points.tolist(), [p.vertices[:] for p in sleeve.polygons])
    hand = BVHTree.FromPolygons(body_points.tolist(), [
        p.vertices[:] for p in body.polygons if p.center.x * sign > .69
    ])
    hand_points = body_points[body_points[:,0] * sign >= .69]
    low, high = hand_points.min(axis=0), hand_points.max(axis=0)
    direction = (sign, 0, 0)
    def ray_hits(tree, y, z):
        return tree.ray_cast((.15 * sign, y, z), direction)[0] is not None
    rays = blocked = 0
    for y in np.arange(low[1], high[1]+.0015, .0015):
        for z in np.arange(low[2], high[2]+.0015, .0015):
            if ray_hits(hand, y, z):
                rays += 1
                blocked += int(ray_hits(cloth, y, z))
    blocked_vertices = sum(ray_hits(cloth, y, z) for x, y, z in hand_points)
    return {'handWidthMm':round(1000*(high[1]-low[1]),1), 'handVertices':len(hand_points),
            'blockedVertexRays':blocked_vertices, 'handSilhouetteRays':rays,
            'blockedSilhouetteRays':blocked, 'gridMm':1.5}


def inspect(scene):
    rig = scene.objects['JointedDollRig']
    saved_action = rig.animation_data.action
    saved_frame = scene.frame_current
    panel_names = [p + side for p in ('front_panel.', 'rear_panel.') for side in ('L','R')]
    targets = ['Body'] + panel_names
    events = []
    frame_counts = {}
    try:
        for action in sorted(bpy.data.actions, key=lambda action: action.name):
            rig.animation_data.action = action
            frames = range(int(action.frame_range.x), int(action.frame_range.y) + 1)
            frame_counts[action.name] = len(frames)
            for frame in frames:
                scene.frame_set(frame)
                deps = bpy.context.evaluated_depsgraph_get()
                trees = {name: BVHTree.FromObject(scene.objects[name], deps)
                         for name in targets + ['sleeve.L','sleeve.R']}
                for name in ['sleeve.L','sleeve.R']:
                    for other in targets + (['sleeve.R'] if name == 'sleeve.L' else []):
                        pairs = trees[name].overlap(trees[other])
                        if pairs:
                            events.append([action.name, frame, name, other, len(pairs)])
    finally:
        rig.animation_data.action = saved_action
        scene.frame_set(saved_frame)
    return {'sampledFrames':frame_counts, 'intersectingObjectFramePairs':events,
            'bindPoseHandPassage':{side:hand_passage(scene, side) for side in ('L','R')}}

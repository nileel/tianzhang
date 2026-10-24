"""Supplementary design verification; writes only beside this temporary script."""
import contextlib
import copy
import hashlib
import heapq
import importlib.util
import io
import json
from collections import Counter, deque
from pathlib import Path

OUT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('candidate', OUT / 'build_and_verify.py')
m = importlib.util.module_from_spec(spec)
with contextlib.redirect_stdout(io.StringIO()):
    spec.loader.exec_module(m)

checks = []


def check(name, value):
    assert value, name
    checks.append(name)


def describe(p):
    front, facing = p
    rear = m.step(front, (facing + 3) % 6)
    return dict(front=m.key(front), rear=m.key(rear), facing=facing,
                frontHeight=m.cells[front]['heightLevel'], rearHeight=m.cells[rear]['heightLevel'])


def trace(pose, direction, forbidden=()):
    destination = m.pair_step(pose, direction, forbidden)
    if destination is None:
        return None
    a, facing = pose
    delta = (direction - facing) % 6
    sign = 1 if delta <= 3 else -1
    if not m.turns(pose, direction, sign, forbidden):
        assert delta == 3 and m.turns(pose, direction, -1, forbidden)
        sign = -1
    rotations = []
    while facing != direction:
        nxt = (facing + sign) % 6
        old_rear = m.step(a, (facing + 3) % 6)
        new_rear = m.step(a, (nxt + 3) % 6)
        assert m.legal_edge(old_rear, new_rear, forbidden, 2)
        assert m.placement((a, nxt), forbidden)
        rotations.append(dict(pose=describe((a, nxt)), rearSweep=m.edge_key(old_rear, new_rear)))
        facing = nxt
    rear = m.step(a, (direction + 3) % 6)
    b = destination[0]
    assert m.legal_edge(a, b, forbidden, 2) and m.legal_edge(rear, a, forbidden, 2)
    return dict(fromPose=describe(pose), turnSign=sign if rotations else 0,
                intermediateTurns=rotations, frontMove=m.edge_key(a, b),
                rearMove=m.edge_key(rear, a), toPose=describe(destination),
                frontMovementUnits=m.move_units(a, b),
                connectionId=m.edges[a, b]['connectionId'])


def pair_route(seed, forbidden=(), required_connection=None):
    assert m.placement(seed, forbidden)
    start = (seed, required_connection is None)
    dist, prev, queue = {start: 0}, {}, [(0, start)]
    while queue:
        cost, state = heapq.heappop(queue)
        if cost != dist[state]:
            continue
        pose, used = state
        if pose[0] == m.GOAL and used and describe(pose)['rearHeight'] == 2:
            states = [state]
            while states[-1] != start:
                states.append(prev[states[-1]])
            poses = [s[0] for s in reversed(states)]
            steps = [trace(a, b[1], forbidden) for a, b in zip(poses, poses[1:])]
            return dict(seed=describe(seed), forbiddenConnections=list(forbidden),
                        steps=len(steps), movementEquivalentDistance=cost / 2,
                        requiredConnection=required_connection,
                        poses=[describe(p) for p in poses], movements=steps)
        for d in range(6):
            nxt = m.pair_step(pose, d, forbidden)
            if nxt is None:
                continue
            used_next = used or m.edges[pose[0], nxt[0]]['connectionId'] == required_connection
            ns, nc = (nxt, used_next), cost + m.move_units(pose[0], nxt[0])
            if nc < dist.get(ns, 10**9):
                dist[ns], prev[ns] = nc, state
                heapq.heappush(queue, (nc, ns))
    return None


def reach(seed, forbidden=()):
    found, queue = {seed}, deque([seed])
    while queue:
        a = queue.popleft()
        for d in range(6):
            b = m.pair_step(a, d, forbidden)
            if b is not None and b not in found:
                found.add(b)
                queue.append(b)
    return dict(seed=describe(seed), reachablePoses=len(found),
                anchorHeights=sorted({m.cells[p[0]]['heightLevel'] for p in found}))


def single_reach(forbidden=()):
    found, queue = {m.START}, deque([m.START])
    while queue:
        a = queue.popleft()
        for d in range(6):
            b = m.step(a, d)
            if m.legal_edge(a, b, forbidden) and b not in found:
                found.add(b)
                queue.append(b)
    return dict(cellCount=len(found), heights=sorted({m.cells[p]['heightLevel'] for p in found}))


baseline = json.loads((OUT / 'baseline' / 'map-candidate.json').read_text(encoding='utf-8'))
check('100 cells unchanged', baseline['cells'] == m.candidate['cells'] and len(m.cells) == 100)
check('28 directed overrides unchanged', baseline['sparseDirectedEdgeOverrides'] == m.overrides and len(m.overrides) == 28)
check('marker records unchanged (baseline generator differs only in ordering)',
      {x['id']:x for x in baseline['markers']} == {x['id']:x for x in m.markers})
check('522 edges, three heights unchanged', len(m.edges) == 522 and Counter(c['heightLevel'] for c in m.cells.values()) == {0:30, 1:58, 2:12})
if not m.PAIR_ONLY:
    check('single main and alternate routes preserved', m.report['mainWitness']['steps'] == m.report['alternateWitnessWithLeftRampBlocked']['steps'] == 15)
    check('single both gates closed blocks high goal', m.shortest(('left_ramp', 'front_right_stairs')) is None)
    check('single high stair closed blocks high goal', m.shortest(('rear_right_stairs',)) is None)
    check('single one-way drop is directional', m.legal_edge((6,8), (5,8)) and not m.legal_edge((5,8), (6,8)))

slope_pose = ((1,3), 5)
front, rear = (1,3), (1,2)
check('approved one-level slope placement both facing directions', m.placement(slope_pose) and m.placement(((1,2), 2)))
check('open cliff cannot hold pair', not m.placement(((0,3), 5)))
check('one-way drop cannot hold cross-height pair', not m.placement(((6,8),0)))
check('entity wall cannot hold pair', not m.placement(((5,4),0)))
check('closed connection cannot hold pair', not m.placement(slope_pose, ('left_ramp',)))

original = copy.deepcopy(m.edges[rear, front])
for label, field, value in [('one-way slope rejected', 'allowsMovement', False),
                            ('reverse non-slope rejected', 'edgeKind', 'flat'),
                            ('slope-stairs internal kind mismatch rejected', 'edgeKind', 'stairs'),
                            ('internal connectionId mismatch rejected', 'connectionId', 'front_right_stairs'),
                            ('reverse clearance rank 1 rejected', 'clearanceLimit', 1)]:
    m.edges[rear, front][field] = value
    check(label, not m.placement(slope_pose))
    m.edges[rear, front] = copy.deepcopy(original)
removed = m.edges.pop((rear, front))
check('missing reverse edge rejected', not m.placement(slope_pose))
m.edges[rear, front] = removed
height = m.cells[front]['heightLevel']
m.cells[front]['heightLevel'] = 2
check('two-level pair rejected', not m.placement(slope_pose))
m.cells[front]['heightLevel'] = height
m.cells[rear]['blocksGroundMove'] = True
check('blocked rear rejected', not m.placement(slope_pose))
m.cells[rear]['blocksGroundMove'] = False
flat_pose = ((3,6),0)
flat_reverse = ((2,6),(3,6))
old_allow = m.edges[flat_reverse]['allowsMovement']
m.edges[flat_reverse]['allowsMovement'] = False
check('same-height one-way internal rule retained', m.placement(flat_pose))
m.edges[flat_reverse]['allowsMovement'] = old_allow
sweep = ((2,6),(3,5))
check('sweep test fixture is originally legal', m.turns(flat_pose, 5, -1))
m.edges[sweep]['clearanceLimit'] = 1
check('rear sweep checks body clearance', not m.turns(flat_pose, 5, -1))
m.edges[sweep]['clearanceLimit'] = 0

seed = ((1,1),5)
main = pair_route(seed, required_connection='left_ramp')
alternate = pair_route(seed, forbidden=('left_ramp',))
check('pair full route through left slope', main is not None)
check('pair full route with left slope blocked', alternate is not None)
check('pair all low-to-mid connectors closed blocks goal', pair_route(seed, ('left_ramp','front_right_stairs')) is None)
check('pair all high stairs closed blocks goal', pair_route(seed, ('rear_right_stairs',)) is None)

trace_count = 0
turn_count = 0
rejections_requiring_full_step = []
fallback_180 = []
long_turn_forbidden = []
for pose in m.poses:
    for d in range(6):
        move = trace(pose,d)
        if move:
            trace_count += 1
            turn_count += len(move['intermediateTurns'])
            if (d - pose[1]) % 6 == 3 and move['turnSign'] == -1:
                fallback_180.append(dict(pose=describe(pose), direction=d))
        else:
            a=pose[0]
            dest=(m.step(a,d),d)
            rear=m.step(a,(d+3)%6)
            endpoint_ok=m.placement(dest) and m.legal_edge(a,dest[0],body_scale=2) and m.legal_edge(rear,a,body_scale=2)
            if endpoint_ok:
                rejections_requiring_full_step.append(dict(pose=describe(pose), direction=d))
                delta=(d-pose[1])%6
                if delta not in (0,3) and m.turns(pose,d,-1 if delta<=3 else 1):
                    long_turn_forbidden.append(dict(pose=describe(pose), direction=d))
check('all accepted transitions replay every intermediate turn and rear edge', trace_count == 1632)
check('endpoint-only acceptance is prevented by intermediate checks', bool(rejections_requiring_full_step))
check('180 alternate shortest rotation retained', bool(fallback_180))
check('non-180 long rotation fallback remains prohibited', bool(long_turn_forbidden))
check('old boundary seed still reaches exactly 16 same-height poses', reach(((2,0),0))['reachablePoses'] == 16)

result = dict(mapVersion=m.VERSION, status='passed', testCount=len(checks), tests=checks,
              pairPoseCount=len(m.poses), crossHeightPoseCount=sum(describe(p)['frontHeight'] != describe(p)['rearHeight'] for p in m.poses),
              pairTransitionCount=trace_count, checkedIntermediateTurnCount=turn_count,
              pairCrossHeightTransitionCount=len(m.cross_height),
              pairMainWitness=main, pairAlternateWitnessWithLeftRampBlocked=alternate,
              pairMainReachability=reach(seed), pairBlockedReachability=reach(seed,('left_ramp',)),
              singleChecksExecuted=not m.PAIR_ONLY,
              singleReachability=None if m.PAIR_ONLY else single_reach(),
              singleBlockedReachability=None if m.PAIR_ONLY else single_reach(('left_ramp',)),
              endpointOnlyFalsePositiveCount=len(rejections_requiring_full_step),
              endpointOnlyFalsePositiveExample=rejections_requiring_full_step[0],
              alternative180Count=len(fallback_180), alternative180Example=fallback_180[0],
              prohibitedLongRotationCount=len(long_turn_forbidden), prohibitedLongRotationExample=long_turn_forbidden[0],
              boundarySeedWarning='((2,0), facing 0) is a legal but boundary-turn-locked hypothetical seed. Use ((1,1), facing 5), rear=(1,0), for slope route proof; no production spawn changed.',
              scope='Offline design graph and definition only. No geometric support, surface contact, body collision, Unity runtime, import or balance acceptance claimed.')
(OUT / 'pair-verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
for filename,route in [('pair-main-route.txt',main),('pair-alternate-route.txt',alternate)]:
    lines=[f"steps={route['steps']}; movementEquivalentDistance={route['movementEquivalentDistance']}; forbidden={route['forbiddenConnections']}"]
    for i,p in enumerate(route['poses']):
        lines.append(f"{i:02d} front={p['front']} H{p['frontHeight']} rear={p['rear']} H{p['rearHeight']} facing={p['facing']}")
        if i<len(route['movements']):
            move=route['movements'][i]
            for turn in move['intermediateTurns']:
                t=turn['pose']
                lines.append(f"   turn: front={t['front']} H{t['frontHeight']} rear={t['rear']} H{t['rearHeight']} facing={t['facing']}; rearSweep={turn['rearSweep']}")
            lines.append(f"   move: front={move['frontMove']}; rear={move['rearMove']}; units={move['frontMovementUnits']}; connection={move['connectionId']}")
    (OUT/filename).write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k not in ('pairMainWitness','pairAlternateWitnessWithLeftRampBlocked','tests')},ensure_ascii=False,indent=2))
print('Main path:', [(p['front'],p['facing']) for p in main['poses']])
print('Alternate path:', [(p['front'],p['facing']) for p in alternate['poses']])

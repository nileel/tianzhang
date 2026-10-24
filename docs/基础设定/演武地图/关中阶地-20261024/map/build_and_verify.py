"""Build a planning candidate, then verify its graph without invoking Unity.

This is NOT an importer or a replacement runtime spatial service. Its pair
transition check mirrors the currently documented turn-then-follow rule to
expose the unresolved same-height placement incompatibility.
"""
import heapq
import json
from collections import Counter, deque
from pathlib import Path

OUT = Path(__file__).resolve().parent
DIRS = ((1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1))
VERSION = "guanzhong-jindan-v3-d1-candidate-20261024.1"


def step(p, d):
    return p[0] + DIRS[d][0], p[1] + DIRS[d][1]


def key(p):
    return f"{p[0]}:{p[1]}"


def edge_key(a, b):
    return f"{key(a)}>{key(b)}"


def level(q, r):
    return 0 if r <= 2 else 2 if q >= 6 and r >= 7 else 1


water = {(q, r) for q in (4, 5, 6) for r in (1, 2)}
mud = {(3, 1), (7, 1)}
rocks = {(4, 5), (6, 4), (2, 5)}
cells = {}
for q in range(10):
    for r in range(10):
        p = (q, r)
        cells[p] = dict(q=q, r=r, heightLevel=level(q, r),
                        surfaceKind="shallow_water" if p in water else "mud_bank" if p in mud else "grass_earth",
                        blocksGroundMove=p in rocks, blocksLineOfSight=False,
                        cellMovementBurdenUnits=1 if p in water or p in mud else 0)

edges = {}
for a in cells:
    for d in range(6):
        b = step(a, d)
        if b not in cells:
            continue
        same = cells[a]["heightLevel"] == cells[b]["heightLevel"]
        edges[a, b] = dict(fromCell=key(a), toCell=key(b),
                           edgeKind="flat" if same else "open_cliff",
                           metricDistanceUnits=2, edgeMovementBurdenUnits=0,
                           allowsMovement=same, allowsEffects=True,
                           clearanceLimit=0, connectionId=None)

overrides = []


def override(a, b, kind, allow, connection, burden=0, effects=True):
    assert (a, b) in edges
    assert edges[a, b]["connectionId"] is None, "Duplicate directed override"
    e = edges[a, b]
    e.update(edgeKind=kind, allowsMovement=allow, connectionId=connection,
             edgeMovementBurdenUnits=burden, allowsEffects=effects)
    overrides.append(dict(e))


def join_band(qs, lower_r, kind, connection):
    # Include BOTH six-neighbor orientations across the row boundary.
    low = {(q, lower_r) for q in qs}
    high = {(q, lower_r + 1) for q in qs}
    for a in sorted(low):
        for b in sorted(high):
            if (a, b) not in edges:
                continue
            assert cells[b]["heightLevel"] - cells[a]["heightLevel"] == 1
            override(a, b, kind, True, connection, burden=1)
            override(b, a, kind, True, connection, burden=1)


join_band(range(1, 4), 2, "slope", "left_ramp")
join_band(range(7, 9), 2, "stairs", "front_right_stairs")
join_band(range(7, 9), 6, "stairs", "rear_right_stairs")
override((6, 8), (5, 8), "one_way_drop", True, "high_to_mid_drop")
override((5, 8), (6, 8), "one_way_drop", False, "high_to_mid_drop")
for a, b in (((4, 4), (5, 4)), ((5, 4), (5, 5))):
    override(a, b, "entity_wall", False, "mid_sealed_wall", effects=False)
    override(b, a, "entity_wall", False, "mid_sealed_wall", effects=False)

START, GOAL = (1, 0), (8, 8)
main_route = [(1, r) for r in range(7)] + [(q, 6) for q in range(2, 8)] + [(7, 7), (7, 8), (8, 8)]
alternate_route = [(q, 0) for q in range(1, 9)] + [(8, r) for r in range(1, 9)]


def legal_edge(a, b, forbidden=()):
    e = edges.get((a, b))
    return e is not None and e["allowsMovement"] and e["connectionId"] not in forbidden and not cells[b]["blocksGroundMove"]


def move_units(a, b):
    e = edges[a, b]
    return e["metricDistanceUnits"] + e["edgeMovementBurdenUnits"] + cells[b]["cellMovementBurdenUnits"]


def route_summary(route, forbidden=()):
    assert route[0] == START and route[-1] == GOAL
    assert all(legal_edge(a, b, forbidden) for a, b in zip(route, route[1:]))
    units = sum(move_units(a, b) for a, b in zip(route, route[1:]))
    return dict(cells=[key(p) for p in route], steps=len(route)-1,
                metricDistance=len(route)-1, movementEquivalentDistance=units / 2,
                connections=[edges[a, b]["connectionId"] for a, b in zip(route, route[1:])
                             if edges[a, b]["connectionId"]])


def shortest(forbidden=()):
    dist, prev, queue = {START: 0}, {}, [(0, START)]
    while queue:
        cost, a = heapq.heappop(queue)
        if cost != dist[a]:
            continue
        if a == GOAL:
            route = [a]
            while route[-1] != START:
                route.append(prev[route[-1]])
            return route_summary(list(reversed(route)), forbidden)
        for d in range(6):
            b = step(a, d)
            if not legal_edge(a, b, forbidden):
                continue
            nxt = cost + move_units(a, b)
            if nxt < dist.get(b, 10**9):
                dist[b], prev[b] = nxt, a
                heapq.heappush(queue, (nxt, b))
    return None


def placement(pose):
    a, facing = pose
    rear = step(a, (facing + 3) % 6)
    if a not in cells or rear not in cells or cells[a]["blocksGroundMove"] or cells[rear]["blocksGroundMove"]:
        return False
    if cells[a]["heightLevel"] != cells[rear]["heightLevel"]:
        return False
    f, r = edges.get((a, rear)), edges.get((rear, a))
    return f is not None and r is not None and (f["allowsMovement"] or r["allowsMovement"]) and all(
        e["clearanceLimit"] == 0 or e["clearanceLimit"] >= 2 for e in (f, r))


def turns(pose, target, sign):
    a, facing = pose
    while facing != target:
        nxt = (facing + sign) % 6
        old_rear, new_rear = step(a, (facing + 3) % 6), step(a, (nxt + 3) % 6)
        if not legal_edge(old_rear, new_rear) or not placement((a, nxt)):
            return False
        facing = nxt
    return True


def pair_step(pose, direction):
    a, facing = pose
    delta = (direction - facing) % 6
    if not turns(pose, direction, 1 if delta <= 3 else -1):
        if delta != 3 or not turns(pose, direction, -1):
            return None
    b, rear = step(a, direction), step(a, (direction + 3) % 6)
    destination = b, direction
    if not placement(destination) or not legal_edge(a, b) or not legal_edge(rear, a):
        return None
    return destination


poses = [(a, d) for a in cells for d in range(6) if placement((a, d))]
pair_graph = {}
for pose in poses:
    pair_graph[pose] = [nxt for d in range(6) if (nxt := pair_step(pose, d)) is not None]
cross_height = [(a, b) for a, ns in pair_graph.items() for b in ns
                if cells[a[0]]["heightLevel"] != cells[b[0]]["heightLevel"]]
seeds = (((2, 0), 0), ((4, 6), 0), ((8, 8), 0))
seed_reports = []
for seed in seeds:
    assert placement(seed)
    found, queue = {seed}, deque([seed])
    while queue:
        for nxt in pair_graph[queue.popleft()]:
            if nxt not in found:
                found.add(nxt)
                queue.append(nxt)
    seed_reports.append(dict(anchor=key(seed[0]), facing=seed[1],
                             startHeight=cells[seed[0]]["heightLevel"],
                             reachablePoses=len(found),
                             reachableHeights=sorted({cells[p[0]]["heightLevel"] for p in found})))

assert len(cells) == 100 and len(edges) == 522
assert all(abs(cells[a]["heightLevel"] - cells[b]["heightLevel"]) <= 1 for a, b in edges)
assert Counter(c["heightLevel"] for c in cells.values()) == {0: 30, 1: 58, 2: 12}
assert len({edge_key(a, b) for a, b in edges}) == 522
assert len({e["fromCell"]+">"+e["toCell"] for e in overrides}) == len(overrides)
assert not cross_height
assert shortest() and shortest(("left_ramp",))
assert shortest(("left_ramp", "front_right_stairs")) is None
assert shortest(("rear_right_stairs",)) is None
assert legal_edge((6, 8), (5, 8)) and not legal_edge((5, 8), (6, 8))
for (a, b), e in edges.items():
    assert (b, a) in edges and e["metricDistanceUnits"] == 2
    if e["edgeKind"] in ("slope", "stairs"):
        assert move_units(a, b) == move_units(b, a) == 3

markers = [
    dict(id="player_spawn", kind="spawn", cell=key(START)),
    dict(id="high_goal", kind="objective", cell=key(GOAL)),
    dict(id="fire_domain_support", kind="public_planned_support", cell="7:8"),
    dict(id="space_lock_support", kind="public_planned_support", cell="9:7"),
    dict(id="reveal_support", kind="public_planned_support", cell="6:9"),
    dict(id="conceal_support", kind="public_planned_support", cell="9:9"),
    dict(id="mid_low_cover_a", kind="low_cover", cell="3:4"),
    dict(id="mid_low_cover_b", kind="low_cover", cell="7:5"),
] + [dict(id="rock_"+key(p), kind="rock_obstacle", cell=key(p)) for p in sorted(rocks)]
candidate = dict(
    schema="tianzhang.d1.fixed-map-candidate", schemaVersion=1, mapVersion=VERSION,
    status="candidate_pending_double_cell_rule_and_geometry",
    targetImage="assets/generated-scene-art/guanzhong-jindan-demo/terrain-target-20261024/terrain-target-v3.png",
    productionEntry=dict(csv="src/Assets/DataConfig/EnvironmentProfiles.csv",
                         plannedProfileId="env_guanzhong_jindan_demo",
                         currentImporterSupportsCandidate=False,
                         existingProfileMustRemain="env_guanzhong_wild"),
    grid=dict(qMin=0, qMax=9, rMin=0, rMax=9, unitsPerRange=2,
              heightLevels=[0, 1, 2], worldCenterFormula="x=q+0.5*r; z=0.8660254*r+1",
              neighborDirections=[list(d) for d in DIRS],
              visualHeightMeters=None, visualHeightStatus="measured_candidate_not_frozen",
              candidateHeightStepMeters=0.75, candidateNominalTopMeters=[0.34, 1.09, 1.84],
              geometryEvidence="../geometry/04-candidate-dimensions.json"),
    defaultEdgeRule=dict(sameHeight="flat", differentHeight="open_cliff", metricDistanceUnits=2,
                         note="Generate every topology neighbor in both directions, then apply sparse overrides."),
    movementBurden=dict(unitsPerRange=2, flatExtraUnits=0, slopeAndStairsExtraUnits=1,
                        slopeAndStairsBidirectional=True,
                        shallowWaterAndMudCellExtraUnits=1,
                        deepWaterCellExtraUnits=2, demoDeepWaterGroundAllowed=True,
                        interpretation="Movement d_e = metric units + directed-edge extra units + destination-cell extra units. Attack metric remains 2 units per edge.",
                        currentRuntimeGap="Current runtime supports destination-cell burden only; D2 must consume configured edge burden without changing attack metric."),
    cells=list(cells.values()), sparseDirectedEdgeOverrides=overrides, markers=markers,
    placementNotes=["Support and cover markers do not create occupancy or block movement in this D1 graph.",
                    "Their future scene-object type, material and interaction contracts remain D4/D9-D11 work.",
                    "Pair validation seeds are separate hypothetical snapshots, not simultaneous production spawns.",
                    "Same-height pair placements cannot cross levels under the current follow-the-front step rule.",
                    "Layer heights and slope/stair dimensions remain candidates awaiting confirmation and geometry validation; this definition is not supported by the current production importer."])
report = dict(mapVersion=VERSION, checks="passed",
              cellCount=len(cells), directedEdgeCount=len(edges),
              directLowToHighEdgeCount=sum(abs(cells[a]["heightLevel"] - cells[b]["heightLevel"]) == 2 for a, b in edges),
              heightCounts=dict(Counter(c["heightLevel"] for c in cells.values())),
              surfaceCounts=dict(Counter(c["surfaceKind"] for c in cells.values())),
              sparseDirectedOverrideCount=len(overrides),
              edgeKindCounts=dict(Counter(e["edgeKind"] for e in edges.values())),
              mainWitness=route_summary(main_route),
              alternateWitnessWithLeftRampBlocked=route_summary(alternate_route, ("left_ramp",)),
              minimumCostRoute=shortest(), minimumCostRouteWithLeftRampBlocked=shortest(("left_ramp",)),
              noLowToMidConnectionsMeansNoGoal=True, noHighStairsMeansNoGoal=True,
              oneWayDropForwardLegalReverseIllegal=True,
              slopeAndStairBothDirectionsRawMovementUnits=3,
              pairLegalPoseCountsByHeight=dict(Counter(cells[p[0]]["heightLevel"] for p in poses)),
              pairLegalTransitionCount=sum(map(len, pair_graph.values())),
              pairCrossHeightTransitionCount=len(cross_height), pairReachability=seed_reports,
              pairInvariantProof="Every move ends with rear=old anchor. Same-height destination placement therefore forces H(new anchor)=H(old anchor). Turns preserve the anchor. Induction forbids changing height on any path, regardless of ramp width.",
              verificationLimits=["Offline candidate graph only; no current Unity importer/runtime acceptance claimed.",
                                  "No geometry, line-of-sight or visual-body collision verification.",
                                  "No actual AP/MP/CT balance claim; reports movement-equivalent distance only.",
                                  "Double-cell cross-height movement remains unresolved, not passed."])
for name, data in (("map-candidate.json", candidate), ("verification.json", report)):
    (OUT / name).write_text(json.dumps(data, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
print(json.dumps(report, ensure_ascii=False, indent=2))

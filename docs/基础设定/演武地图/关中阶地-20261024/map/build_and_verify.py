"""Build a planning candidate, then verify its graph without invoking Unity.

This is NOT an importer or a replacement runtime spatial service. Its pair
transition check mirrors the currently documented turn-then-follow rule to
verify the approved one-level bidirectional slope/stair placement exception.
"""
import heapq
import hashlib
import json
import math
from collections import Counter, deque
from pathlib import Path

OUT = Path(__file__).resolve().parent
DIRS = ((1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1))
VERSION = "guanzhong-jindan-v3-d1-supported-map-20261024.4"
GEOMETRY_RELEVANT_SHA256 = "683cd4fada56b816d81c0fda6e95e7f7b6c28531c86358551e9f863539262df8"
GEOMETRY_SCOPE = dict(
    acceptance="D1 fixed design accepted for the explicitly recorded geometry scope; production importer and D3 runtime presentation are not implemented here",
    evidence="../geometry/long-transition/11-lightweight-summary-manifest.json",
    sampledLegalStationaryPairPoses=390,checkedDynamicWitnessRoutes=2,dynamicPoseSamples=1295,
    conservativelyBoundedAdjacentIntervals=1240,
    minimumHigherForeignSupportSideClearanceMeters=.6931065097879338,
    continuousInterpolationCurvatureTrianglePenetrationUpperBoundMeters=.0005370580000357836,
    designToleranceMeters=.002,
    limitation="The two recorded dynamic routes are verified; this is not a continuous geometry proof for all 993 graph-legal transitions. Individual foot animation and runtime scene realization belong to subsequent D3 presentation.")


def step(p, d):
    return p[0] + DIRS[d][0], p[1] + DIRS[d][1]


def key(p):
    return f"{p[0]}:{p[1]}"


def edge_key(a, b):
    return f"{key(a)}>{key(b)}"


ROW_METERS = math.sqrt(3) / 2
GROUND_Y = 0.340433121
LEVEL_STEP = 0.75
coords = ({(q, -1) for q in range(1, 9)}
          | {(q, r) for q in range(10) for r in range(8)}
          | {(q, r) for q in range(3, 10) for r in range(8, 16)})
bands = {
    "left_ramp": dict(qs=[0, 1, 2, 3], playableQs=[1, 2], startRow=1, endRow=6, lowerLevel=0),
    "right_pair_ramp": dict(qs=[6, 7, 8, 9], playableQs=[7, 8], startRow=1, endRow=6, lowerLevel=0),
    "high_pair_ramp": dict(qs=[3, 4, 5, 6], playableQs=[4, 5], startRow=8, endRow=13, lowerLevel=1),
}


def region(p):
    return next((name for name, b in bands.items()
                 if p[0] in b["qs"] and b["startRow"] <= p[1] <= b["endRow"]), None)


def level(q, r):
    band = region((q, r))
    if band:
        b = bands[band]
        return b["lowerLevel"] + int(r > (b["startRow"] + b["endRow"]) / 2)
    if (q, r) == (8, 9):
        return 1  # Mid-stair physical landing is half-rise; rule H remains integer.
    return 0 if r <= 3 else 2 if r >= 9 else 1


def world(p):
    return p[0] + 0.5 * p[1], 1 + ROW_METERS * p[1]


def support_y(p):
    if p in stair_cells:
        st = stair_layout[stair_cells[p]]
        return GROUND_Y + LEVEL_STEP * (st["lowerLevel"] + (p[1] - st["startRow"]) / 2)
    profile = region(p)
    if profile is None:
        return GROUND_Y + LEVEL_STEP * level(*p)
    b = bands[profile]
    t = (p[1] - b["startRow"]) / (b["endRow"] - b["startRow"])
    return GROUND_Y + LEVEL_STEP * (b["lowerLevel"] + (1 - math.cos(math.pi * t)) / 2)


water = {(4, 0), (5, 0), (4, 1), (5, 1), (4, 2), (5, 5)}
mud = {(3, 0), (6, 0)}
rocks = {(0, 0), (4, 3), (9, 15)}
stair_layout = {
    "front_right_stairs": dict(q=5, startRow=2, endRow=4, lowerLevel=0),
    "rear_right_stairs": dict(q=8, startRow=8, endRow=10, lowerLevel=1),
}
stair_cells = {(st["q"], r): name for name, st in stair_layout.items()
               for r in range(st["startRow"], st["endRow"]+1)}
shoulders = {p for p in coords if region(p) and p[0] not in bands[region(p)]["playableQs"]}
cells = {}
for p in sorted(coords):
    q, r = p
    physical = region(p)
    cells[p] = dict(q=q, r=r, heightLevel=level(q, r),
                    surfaceKind="ramp_shoulder" if p in shoulders else "shallow_water" if p in water else "mud_bank" if p in mud else "grass_earth",
                    blocksGroundMove=p in rocks or p in shoulders, blocksLineOfSight=False,
                    cellMovementBurdenUnits=1 if p in water or p in mud else 0,
                    physicalSurfaceId=stair_cells.get(p, physical or "flat_H"+str(level(q,r))),
                    supportHeightMetersAtCenter=support_y(p),
                    supportHeightStatus="declared_stair_landing_sample" if p in stair_cells else "declared_profile_sample",
                    physicalSupportOnly=p in shoulders,
                    groundStopAllowed=p not in rocks and p not in shoulders)

edges = {}
for a in cells:
    for d in range(6):
        b = step(a, d)
        if b not in cells:
            continue
        same = cells[a]["heightLevel"] == cells[b]["heightLevel"]
        kind, allow, conn, burden = ("flat" if same else "open_cliff"), same, None, 0
        ar, br = region(a), region(b)
        physical_class = "flat_same_level" if same else "nominal_terrace_cliff"
        if ar and ar == br:
            conn = ar
            kind, allow, burden = ("slope", True, 1) if a[1] != b[1] else ("flat", True, 0)
            physical_class = "profile_internal_incline" if a[1] != b[1] else "profile_internal_isohypse"
        elif ar or br:
            conn = ar or br
            inside, outside = (a, b) if ar else (b, a)
            band = bands[conn]
            lower = inside[1] == band["startRow"] and outside[1] == band["startRow"] - 1
            upper = inside[1] == band["endRow"] and outside[1] == band["endRow"] + 1
            allow = (lower or upper) and cells[a]["heightLevel"] == cells[b]["heightLevel"]
            kind, burden = ("flat", 0) if allow else ("open_cliff", 0)
            physical_class = ("lower_portal" if lower else "upper_portal") if allow else "sealed_profile_side"
        edges[a, b] = dict(fromCell=key(a), toCell=key(b), edgeKind=kind,
                          metricDistanceUnits=2, edgeMovementBurdenUnits=burden,
                          allowsMovement=allow, allowsEffects=True, clearanceLimit=0,
                          connectionId=conn, physicalEdgeClass=physical_class)


def set_edge(a,b,kind,allow,connection,burden=0,effects=True,clearance=0):
    assert (a,b) in edges
    edges[a,b].update(edgeKind=kind,allowsMovement=allow,connectionId=connection,
                      edgeMovementBurdenUnits=burden,allowsEffects=effects,
                      clearanceLimit=clearance,physicalEdgeClass="explicit_"+kind)


# True stairs have three legal horizontal stopping platforms and two four-step flights.
for (a,b), e in edges.items():
    sa, sb = stair_cells.get(a), stair_cells.get(b)
    if not (sa or sb):
        continue
    assert all(region(p) is None for p in (a,b) if p in stair_cells)
    connection = sa or sb
    if sa and sa == sb:
        set_edge(a,b,"stairs",True,connection,burden=1,clearance=1)
        e["physicalEdgeClass"] = "stair_internal_flight"
    else:
        inside,outside = (a,b) if sa else (b,a)
        st = stair_layout[connection]
        lower = inside[1] == st["startRow"] and outside[1] == st["startRow"]-1
        upper = inside[1] == st["endRow"] and outside[1] == st["endRow"]+1
        allow = (lower or upper) and cells[a]["heightLevel"] == cells[b]["heightLevel"] and region(outside) is None
        set_edge(a,b,"flat" if allow else "open_cliff",allow,connection,clearance=1 if allow else 0)
        e["physicalEdgeClass"] = ("stair_lower_portal" if lower else "stair_upper_portal") if allow else "sealed_surface_side"
set_edge((9,9),(9,8),"one_way_drop",True,"high_to_mid_drop")
set_edge((9,8),(9,9),"one_way_drop",False,"high_to_mid_drop")
for a,b in (((8,14),(9,14)),((8,15),(9,15))):
    set_edge(a,b,"entity_wall",False,"mid_sealed_wall",effects=False)
    set_edge(b,a,"entity_wall",False,"mid_sealed_wall",effects=False)

overrides=[]
for (a,b),e in edges.items():
    same=cells[a]["heightLevel"]==cells[b]["heightLevel"]
    if not (e["edgeKind"] == ("flat" if same else "open_cliff") and e["allowsMovement"] == same
            and e["allowsEffects"] and e["edgeMovementBurdenUnits"]==0 and e["clearanceLimit"]==0 and e["connectionId"] is None):
        overrides.append(dict(e))
START, GOAL = (4, 0), (8, 12)


def legal_edge(a, b, forbidden=(), body_scale=1):
    e = edges.get((a, b))
    return e is not None and e["allowsMovement"] and e["connectionId"] not in forbidden and not cells[b]["blocksGroundMove"] and (e["clearanceLimit"] == 0 or e["clearanceLimit"] >= body_scale)


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


def placement(pose, forbidden=()):
    a, facing = pose
    rear = step(a, (facing + 3) % 6)
    if a not in cells or rear not in cells or cells[a]["blocksGroundMove"] or cells[rear]["blocksGroundMove"]:
        return False
    f, r = edges.get((a, rear)), edges.get((rear, a))
    if f is None or r is None or any(e["connectionId"] in forbidden for e in (f, r)):
        return False
    if not all(e["clearanceLimit"] == 0 or e["clearanceLimit"] >= 2 for e in (f, r)):
        return False
    delta_height = abs(cells[a]["heightLevel"] - cells[rear]["heightLevel"])
    if delta_height == 0:
        return f["allowsMovement"] or r["allowsMovement"]
    return (delta_height == 1 and f["edgeKind"] == r["edgeKind"]
            and f["connectionId"] == r["connectionId"] and all(
                e["edgeKind"] in ("slope", "stairs") and e["allowsMovement"] for e in (f, r)))


def turns(pose, target, sign, forbidden=()):
    a, facing = pose
    while facing != target:
        nxt = (facing + sign) % 6
        old_rear, new_rear = step(a, (facing + 3) % 6), step(a, (nxt + 3) % 6)
        if not legal_edge(old_rear, new_rear, forbidden, 2) or not placement((a, nxt), forbidden):
            return False
        facing = nxt
    return True


def pair_step(pose, direction, forbidden=()):
    a, facing = pose
    delta = (direction - facing) % 6
    if not turns(pose, direction, 1 if delta <= 3 else -1, forbidden):
        if delta != 3 or not turns(pose, direction, -1, forbidden):
            return None
    b, rear = step(a, direction), step(a, (direction + 3) % 6)
    destination = b, direction
    if not placement(destination, forbidden) or not legal_edge(a, b, forbidden, 2) or not legal_edge(rear, a, forbidden, 2):
        return None
    return destination


def describe(pose):
    front, facing = pose
    rear = step(front, (facing + 3) % 6)
    return dict(front=key(front), rear=key(rear), facing=facing,
                frontHeight=cells[front]["heightLevel"], rearHeight=cells[rear]["heightLevel"],
                frontPhysicalSurface=cells[front]["physicalSurfaceId"],
                rearPhysicalSurface=cells[rear]["physicalSurfaceId"],
                frontSupportY=support_y(front), rearSupportY=support_y(rear))


def trace(pose, direction, forbidden=()):
    destination = pair_step(pose, direction, forbidden)
    if destination is None:
        return None
    a, facing = pose
    delta = (direction - facing) % 6
    sign = 1 if delta <= 3 else -1
    if not turns(pose, direction, sign, forbidden):
        assert delta == 3 and turns(pose, direction, -1, forbidden)
        sign = -1
    rotations = []
    while facing != direction:
        nxt = (facing + sign) % 6
        old_rear, new_rear = step(a, (facing + 3) % 6), step(a, (nxt + 3) % 6)
        assert legal_edge(old_rear, new_rear, forbidden, 2) and placement((a, nxt), forbidden)
        rotations.append(dict(pose=describe((a, nxt)), rearSweep=edge_key(old_rear, new_rear)))
        facing = nxt
    rear = step(a, (direction + 3) % 6)
    return dict(fromPose=describe(pose), intermediateTurns=rotations,
                frontMove=edge_key(a, destination[0]), rearMove=edge_key(rear, a),
                toPose=describe(destination), frontMovementUnits=move_units(a, destination[0]),
                connectionId=edges[a, destination[0]]["connectionId"])


def pair_route(seed, forbidden=(), required_connection=None):
    assert placement(seed, forbidden)
    initial = (seed, required_connection is None)
    dist, prev, queue = {initial: 0}, {}, [(0, initial)]
    while queue:
        cost, state = heapq.heappop(queue)
        if cost != dist[state]:
            continue
        pose, used = state
        if pose[0] == GOAL and used and describe(pose)["rearHeight"] == 2:
            path = [state]
            while path[-1] != initial:
                path.append(prev[path[-1]])
            pp = [p[0] for p in reversed(path)]
            return dict(steps=len(pp)-1, movementEquivalentDistance=cost/2,
                        forbiddenConnections=list(forbidden), requiredConnection=required_connection,
                        poses=[describe(p) for p in pp],
                        movements=[trace(a,b[1],forbidden) for a,b in zip(pp,pp[1:])])
        for direction in range(6):
            nxt = pair_step(pose, direction, forbidden)
            if nxt is None:
                continue
            connection = edges[pose[0],nxt[0]]
            crossed_up = connection["connectionId"] == required_connection and connection["edgeKind"] == "slope" and cells[nxt[0]]["heightLevel"] > cells[pose[0]]["heightLevel"]
            ns = (nxt, used or crossed_up)
            nc = cost + move_units(pose[0],nxt[0])
            if nc < dist.get(ns,10**9):
                dist[ns],prev[ns]=nc,state
                heapq.heappush(queue,(nc,ns))
    return None


def single_route(required_connections=(), forbidden=()):
    initial=(START,frozenset())
    dist,prev,queue={initial:0},{},[(0,START,())]
    while queue:
        cost,a,used_t=heapq.heappop(queue)
        used=frozenset(used_t)
        state=(a,used)
        if cost!=dist.get(state):
            continue
        if a==GOAL and set(required_connections)<=used:
            route=[state]
            while route[-1]!=initial:
                route.append(prev[route[-1]])
            return route_summary([p[0] for p in reversed(route)],forbidden)
        for d in range(6):
            b=step(a,d)
            if not legal_edge(a,b,forbidden):
                continue
            conn=edges[a,b]["connectionId"]
            crossed_up=edges[a,b]["edgeKind"] in ("slope","stairs") and cells[b]["heightLevel"]>cells[a]["heightLevel"]
            nu=used|({conn} if conn in required_connections and crossed_up else set())
            ns,nc=(b,frozenset(nu)),cost+move_units(a,b)
            if nc<dist.get(ns,10**9):
                dist[ns],prev[ns]=nc,state
                heapq.heappush(queue,(nc,b,tuple(sorted(nu))))
    return None


checks=[]
def check(name,condition):
    assert condition,name
    checks.append(name)


poses=[(a,d) for a in cells for d in range(6) if placement((a,d))]
graph={p:[n for d in range(6) if (n:=pair_step(p,d)) is not None] for p in poses}
seed=(START,0)
main=pair_route(seed,required_connection="left_ramp")
alternate=pair_route(seed,forbidden=("left_ramp",),required_connection="right_pair_ramp")
single_main=single_route(("left_ramp","high_pair_ramp"),("front_right_stairs","rear_right_stairs","right_pair_ramp"))
single_alt=single_route(("front_right_stairs","rear_right_stairs"),("left_ramp",))
check("144 support cells with 105 stoppable cells and 36 support-only shoulders",len(cells)==144 and sum(c["groundStopAllowed"] for c in cells.values())==105 and len(shoulders)==36)
check("every shoulder has explicit terrain block and shared ramp support",all(cells[p]["surfaceKind"]=="ramp_shoulder" and cells[p]["blocksGroundMove"] and not cells[p]["blocksLineOfSight"] and cells[p]["physicalSurfaceId"]==region(p) for p in shoulders))
check("all directed neighbor edges have reverse records",all((b,a) in edges for a,b in edges))
check("three integer height levels only",set(c["heightLevel"] for c in cells.values())=={0,1,2})
check("no adjacent two-level jumps",all(abs(cells[a]["heightLevel"]-cells[b]["heightLevel"])<=1 for a,b in edges))
check("three bands have 72 uniquely covered support cells",sum(sum(region(p)==name for p in cells) for name in bands)==72 and all(sum(p[0] in b["qs"] and b["startRow"]<=p[1]<=b["endRow"] for b in bands.values())<=1 for p in cells))
check("all side exits explicitly closed",all(not e["allowsMovement"] and e["edgeKind"]=="open_cliff" for e in edges.values() if e["physicalEdgeClass"]=="sealed_profile_side"))
check("all incline edges charge one extra unit even at same rule height",all(e["edgeKind"]=="slope" and e["edgeMovementBurdenUnits"]==1 for e in edges.values() if e["physicalEdgeClass"]=="profile_internal_incline"))
check("all profile horizontal edges are flat and free of slope burden",all(e["edgeKind"]=="flat" and e["edgeMovementBurdenUnits"]==0 for e in edges.values() if e["physicalEdgeClass"]=="profile_internal_isohypse"))
check("all slopes have matching bidirectional group kind cost clearance",all(all(edges[b,a][k]==e[k] for k in ("edgeKind","connectionId","allowsMovement","clearanceLimit","edgeMovementBurdenUnits")) for (a,b),e in edges.items() if e["edgeKind"]=="slope"))
check("stairs clearance 1 excludes rank2 pairs",all(e["clearanceLimit"]==1 for e in edges.values() if e["edgeKind"]=="stairs"))
check("one-way drop remains directional",legal_edge((9,9),(9,8)) and not legal_edge((9,8),(9,9)))
check("three rock obstacles are disjoint from all physical transition profiles",len(rocks)==3 and not rocks & (shoulders | set(stair_cells)) and all(region(p) is None for p in rocks))
check("two wall spans retain movement and effects blocking",sum(e["edgeKind"]=="entity_wall" for e in edges.values())==4 and all(not e["allowsMovement"] and not e["allowsEffects"] for e in edges.values() if e["edgeKind"]=="entity_wall"))
check("pair route through left ramp reaches relocated high goal",main is not None)
check("pair route with left ramp closed reaches relocated high goal",alternate is not None)
check("single slope route reaches relocated high goal",single_main is not None)
check("single route traverses both true stairs when left ramp closed",single_alt is not None)
check("closing all lower connectors blocks single",shortest(("left_ramp","right_pair_ramp","front_right_stairs")) is None)
check("closing all upper connectors blocks single",shortest(("high_pair_ramp","rear_right_stairs")) is None)
check("closing both pair lower slopes blocks pair",pair_route(seed,("left_ramp","right_pair_ramp")) is None)
check("closing high pair slope blocks pair despite single stairs",pair_route(seed,("high_pair_ramp",)) is None)
check("goal remains H2 actual flat plateau",cells[GOAL]["heightLevel"]==2 and region(GOAL) is None)
check("three stair platforms all stop legally with unique stair authority",all(cells[p]["groundStopAllowed"] and cells[p]["physicalSurfaceId"]==name for p,name in stair_cells.items()))
check("stair sides are closed even when logical heights match",all(not e["allowsMovement"] and e["edgeKind"]=="open_cliff" for e in edges.values() if e["physicalEdgeClass"]=="sealed_surface_side"))
check("six water and two mud burdens retained",len(water)==6 and len(mud)==2 and all(cells[p]["cellMovementBurdenUnits"]==1 for p in water|mud))
check("all ramp portals join at a fully flat shared edge",all(e["edgeKind"]=="flat" and cells[a]["heightLevel"]==cells[b]["heightLevel"] for (a,b),e in edges.items() if e["physicalEdgeClass"] in ("lower_portal","upper_portal")))
check("stair portals never attach to another inclined support profile",all(region(a) is None and region(b) is None for (a,b),e in edges.items() if e["physicalEdgeClass"] in ("stair_lower_portal","stair_upper_portal")))
single_reachable={START}
queue=deque([START])
while queue:
    a=queue.popleft()
    for d in range(6):
        b=step(a,d)
        if b not in single_reachable and legal_edge(a,b):
            single_reachable.add(b)
            queue.append(b)
check("all 105 stoppable cells reachable from common single start",single_reachable=={p for p,c in cells.items() if c["groundStopAllowed"]})
for field, value, test_name in (("edgeKind","stairs","mixed reverse slope/stairs body edge is rejected"),
                                ("connectionId","different_ramp","mixed reverse connection identity is rejected")):
    pose=((1,4),5)
    rear=step(pose[0],2)
    assert placement(pose) and cells[pose[0]]["heightLevel"]!=cells[rear]["heightLevel"]
    edge=edges[rear,pose[0]]
    old=edge[field]
    edge[field]=value
    check(test_name,not placement(pose))
    edge[field]=old
trace_count=turn_count=0
for p in poses:
    for d in range(6):
        t=trace(p,d)
        if t is not None:
            trace_count+=1
            turn_count+=len(t["intermediateTurns"])
check("every accepted pair transition and intermediate turn replayed",trace_count==sum(map(len,graph.values())))

profile_defs=[]
for name,b in bands.items():
    covered=[key(p) for p in cells if region(p)==name]
    z0,z1=world((0,b["startRow"]))[1],world((0,b["endRow"]))[1]
    lower=[e for e in edges.values() if e["connectionId"]==name and e["physicalEdgeClass"]=="lower_portal"]
    upper=[e for e in edges.values() if e["connectionId"]==name and e["physicalEdgeClass"]=="upper_portal"]
    sides=[e for (a,b),e in edges.items() if (region(a)==name) != (region(b)==name)
           and not e["allowsMovement"]]
    boundary={edge_key(a,b) for a,b in edges if (region(a)==name)!=(region(b)==name)}
    ports={e["fromCell"]+">"+e["toCell"] for e in lower+upper}
    sealed={e["fromCell"]+">"+e["toCell"] for e in sides}
    check(name+" boundary is exactly disjoint open portals and closed sides",boundary==ports|sealed and not ports&sealed)
    profile_defs.append(dict(id=name,kind="half_cosine_C1_worldZ",coveredCells=covered,
        stoppableCells=[key(p) for p in cells if region(p)==name and p not in shoulders],
        shoulderCells=[key(p) for p in cells if region(p)==name and p in shoulders],
        worldZStart=z0,worldZEnd=z1,horizontalRunMeters=z1-z0,
        lowerY=GROUND_Y+b["lowerLevel"]*LEVEL_STEP,upperY=GROUND_Y+(b["lowerLevel"]+1)*LEVEL_STEP,
        formula="Y=lowerY+0.75*(1-cos(pi*clamp((worldZ-worldZStart)/(worldZEnd-worldZStart),0,1)))/2",
        ends="Zero derivative at both endpoints; flat extension at endpoint Y outside the ramp domain.",
        supportDomain="Unique support across the union of four-column covered hexes; inner two columns are stoppable, outer two are non-stoppable ramp_shoulder with blocksGroundMove=true and blocksLineOfSight=false. Clip original Tile tops/walls inside this footprint. Only listed lower/upper shared-edge portals are open.",
        shoulderPresentation="D3 must visibly present the shoulder as non-traversable edge earthwork or gravel, not indistinguishable empty flat ground. The body swept-clearance region remains the same smooth support profile: no railing or protruding stones inside it. Decorations may occupy only the outer shoulder region after finite geometry establishes clearance.",
        logicalHeightQuantization="lowerLevel below the worldZ midpoint; upperLevel above it; no half-level state. heightLevel alone remains rule input.",
        lowerPortalEdges=[e["fromCell"]+">"+e["toCell"] for e in lower],
        upperPortalEdges=[e["fromCell"]+">"+e["toCell"] for e in upper],
        closedSideEdges=[e["fromCell"]+">"+e["toCell"] for e in sides],
        minimumStraightClearWidthMeters=1.0,
        turning="Use enumerated full pair poses/turns and finite profile shoulders plus horizontal platforms; no untested full-circle turning claim.",
        geometryStatus="D1 fixed design accepted: all 390 legal stationary pair poses and two dynamic witness routes (1295 samples,1240 conservatively bounded intervals). This is not geometry validation of all 993 graph-legal dynamic transitions; see shared geometry evidence and D3 runtime obligations."))
stair_defs=[]
for name,layout in stair_layout.items():
    q,r=layout["q"],layout["startRow"]
    owned=[(q,rr) for rr in range(r,r+3)]
    all_boundary=[(a,b,e) for (a,b),e in edges.items() if (stair_cells.get(a)==name) != (stair_cells.get(b)==name)]
    check(name+" boundary is exactly explicit stair portals or closed sides",all(e["physicalEdgeClass"] in ("stair_lower_portal","stair_upper_portal") if e["allowsMovement"] else e["edgeKind"]=="open_cliff" for a,b,e in all_boundary))
    stair_defs.append(dict(id=name,kind="true_stair_two_flights_three_landings",coveredCells=[key(p) for p in owned],
        lowerLevel=layout["lowerLevel"],upperLevel=layout["lowerLevel"]+1,clearanceLimit=1,
        riseMeters=.75,worldZStart=world((q,r))[1],worldZEnd=world((q,r+2))[1],
        worldZPortSeparationMeters=2*ROW_METERS,gridCenterPathLengthMeters=2.0,
        lowerY=GROUND_Y+layout["lowerLevel"]*LEVEL_STEP,
        platformCenters=[dict(cell=key(p),worldX=world(p)[0],worldZ=world(p)[1],supportY=support_y(p),heightLevel=cells[p]["heightLevel"],groundStopAllowed=True) for p in owned],
        endPlatformCenterToFlightMeters=.30352540378443865,middlePlatformWorldZDepthMeters=.5,
        flightCount=2,stepsPerFlight=4,stepRiseMeters=.09375,stepRunWorldZMeters=.078125,
        flightWorldZRunMeters=.3125,
        flightStartOffsetsWorldZ=[.30352540378443865,1.1160254037844386],
        risers=[dict(worldZ=world((q,r))[1]+start+i*.078125,supportYAfter=GROUND_Y+(layout["lowerLevel"]+.5*flight)*LEVEL_STEP+i*.09375)
                for flight,start in enumerate((.30352540378443865,1.1160254037844386)) for i in range(1,5)],
        stairSurfaceDefinition="At each riser worldZ, height increases by .09375; constant horizontal tread between consecutive risers. Before the first riser use lowerY, after the last use lowerY+.75. Profile domain is exactly the union of three covered hexes; vertical faces are the actual risers, with no hidden slope support.",
        lowerPortalEdges=[edge_key(a,b) for a,b,e in all_boundary if e["physicalEdgeClass"]=="stair_lower_portal"],
        upperPortalEdges=[edge_key(a,b) for a,b,e in all_boundary if e["physicalEdgeClass"]=="stair_upper_portal"],
        closedSideEdges=[edge_key(a,b) for a,b,e in all_boundary if not e["allowsMovement"]],
        geometryStatus="D1 shared true-stair dimensions fixed: three stopping platforms validated in six facings, and actual player stepping sampled with lower-foot support and no body penetration. Finite domain is the declared three-hex footprint. Subsequent D3 runtime presentation must implement the measured stepping behavior.",
        movingSupport="Each tested traversed pose has an actual lower foot support and no body penetration; simultaneous two-foot contact is required at the three stopping platforms, not during stepping. Subsequent D3 runtime presentation supplies foot lifting animation.",
        supportOwnership="The three covered hexes bind this one stair authority, including both endpoint landings, midpoint landing and true treads. Clip original Tile tops inside the footprint. No hidden slope collider or second top. WorldZ profile is shared for all facings; axial center travel is 2m although worldZ separation is sqrt(3)m.",
        logicalHeightQuantization="Midpoint platform remains at lowerLevel, upper platform at upperLevel; intermediate physical .375m rise is not a new rule height."))

candidate={'schema': 'tianzhang.d1.fixed-map-candidate',
 'schemaVersion': 1,
 'mapVersion': VERSION,
 'status': 'd1_fixed_design_verified_scope_explicit',
 'targetImage': 'assets/generated-scene-art/guanzhong-jindan-demo/terrain-target-20261024/terrain-target-v3.png',
 'productionEntry': {'csv': 'src/Assets/DataConfig/EnvironmentProfiles.csv',
                     'plannedProfileId': 'env_guanzhong_jindan_demo',
                     'currentImporterSupportsCandidate': False,
                     'existingProfileMustRemain': 'env_guanzhong_wild'},
 'environmentProfileEnvelope': {'profileId': 'env_guanzhong_jindan_demo',
                                'queryLimits': 'unitsPerRange=2;maxQueryRange=24',
                                'surfacePrototypeRefs': 'surface_grassland|surface_loess',
                                'phenomenonChannels': 'airflow=wind+gust;visibility=mist+haze;temperature=heat+cold;precipitation=rain+drizzle;suspendedHazard=ash+dust;cloudDischarge=storm+lightning',
                                'phenomenonPairs': 'airflow:wind+gust>gust|visibility:mist+haze>haze|temperature:heat+cold>cold|precipitation:rain+drizzle>drizzle|suspendedHazard:ash+dust>ash|cloudDischarge:storm+lightning>lightning',
                                'elementRelationRefs': 'element_wood|element_fire|element_earth|element_metal|element_water',
                                'spiritRecoveryEnvironment': 'ordinary',
                                'edgeClearanceLimits': 'none'},
 'grid': {'qMin': 0,
          'qMax': 9,
          'rMin': 0,
          'rMax': 9,
          'unitsPerRange': 2,
          'heightLevels': [0, 1, 2],
          'worldCenterFormula': 'x=q+0.5*r; z=0.8660254*r+1',
          'neighborDirections': [[1, 0], [1, -1], [0, -1], [-1, 0], [-1, 1], [0, 1]],
          'visualHeightMeters': 0.75,
          'visualHeightMeaning': 'Meters per nominal integer plateau height; continuous ramp and true-stair support is separately explicit.',
          'visualHeightStatus': 'fixed_D1_dimensions_with_explicit_geometry_scope',
          'geometryEvidence': '../geometry/long-transition/11-lightweight-summary-manifest.json'},
 'defaultEdgeRule': {'sameHeight': {'edgeKind': 'flat', 'allowsMovement': True},
                     'differentHeight': {'edgeKind': 'open_cliff', 'allowsMovement': False},
                     'metricDistanceUnits': 2,
                     'edgeMovementBurdenUnits': 0,
                     'allowsEffects': True,
                     'clearanceLimit': 0,
                     'connectionId': None,
                     'generation': 'Generate all six-neighbor directed edges in both directions, then apply sparse '
                                   'overrides. Missing reverse records are invalid, not inferred.'},
 'movementBurden': {'unitsPerRange': 2,
                    'flatExtraUnits': 0,
                    'slopeAndStairsExtraUnits': 1,
                    'slopeAndStairsBidirectional': True,
                    'shallowWaterAndMudCellExtraUnits': 1,
                    'deepWaterCellExtraUnits': 2,
                    'demoDeepWaterGroundAllowed': True,
                    'interpretation': 'Movement d_e = metric units + directed-edge extra units + destination-cell '
                                      'extra units. Attack metric remains 2 units per edge.',
                    'currentRuntimeGap': 'Current runtime supports destination-cell burden only; D2 must consume '
                                         'configured edge burden without changing attack metric.'},
 'placementNotes': ['Support and cover markers do not create occupancy or block movement in this D1 graph.',
                    'Their future scene-object type, material and interaction contracts remain D4/D9-D11 work.',
                    'Pair validation seeds are separate hypothetical snapshots, not simultaneous production spawns.',
                    'Pair placements retain the same-height rule; abs(deltaHeight)=1 is allowed only with explicit '
                    'slope/stairs edges in both directions, matching edgeKind and connectionId, both movement-enabled '
                    'and both clearance-qualified. Turn-then-follow, every 60-degree intermediate pose and rear sweep '
                    'stay mandatory.',
                    'Layer heights and slope/stair dimensions are fixed for D1 under the recorded geometry scope. '
                    'The current production importer does not yet support this definition; D2 implements the existing data chain and subsequent D3 implements runtime presentation.']}
candidate.update(mapVersion=VERSION,status="d1_fixed_design_verified_scope_explicit",cells=list(cells.values()),
                 sparseDirectedEdgeOverrides=overrides,directedEdges=list(edges.values()),
                 physicalSurfaceProfiles=profile_defs,physicalStairConnections=stair_defs)
candidate["grid"].update(qMin=0,qMax=9,rMin=-1,rMax=15,shape="explicit coordinate set; bounding rectangle includes absent cells",
                         cellCount=len(cells),groundStopCellCount=sum(c["groundStopAllowed"] for c in cells.values()),supportOnlyShoulderCellCount=len(shoulders),heightStepMeters=.75,nominalSupportTopMeters=[GROUND_Y+i*.75 for i in range(3)],
                         visualHeightStatus="fixed D1 plateau heights and declared continuous support; exact acceptance scope is in geometryValidation")
candidate["grid"].update(neighborCenterDistanceMeters=1.0,hexOrientation="pointy axial on Unity XZ; Unity Y is elevation",
                         hexCornerOffsetsXZ=[[0,1/math.sqrt(3)],[.5,.5/math.sqrt(3)],[.5,-.5/math.sqrt(3)],
                                             [0,-1/math.sqrt(3)],[-.5,-.5/math.sqrt(3)],[-.5,.5/math.sqrt(3)]])
candidate["physicalSurfaceAuthority"]="One support function per XZ plane position in each occupied column. Profile-covered cells replace default tile tops and their hidden walls inside the footprint. All four ramp columns, including non-stoppable shoulders, use exactly the same worldZ profile. Rule movement and heights never sample Unity Transform. Adjacent physical mismatches remain closed even when rule height matches. Three true-stair stopping platforms use the same explicit stair support authority; its intermediate physical level does not create a rule height."
candidate["markers"]=[dict(id="player_spawn",kind="spawn",cell=key(START)),dict(id="high_goal",kind="objective",cell=key(GOAL)),
    dict(id="fire_domain_support",kind="public_planned_support",cell="7:12"),dict(id="space_lock_support",kind="public_planned_support",cell="9:12"),
    dict(id="reveal_support",kind="public_planned_support",cell="4:15"),dict(id="conceal_support",kind="public_planned_support",cell="7:15"),
    dict(id="mid_low_cover_a",kind="low_cover",cell="4:5"),dict(id="mid_low_cover_b",kind="low_cover",cell="9:7")]+[
    dict(id="rock_"+key(p),kind="rock_obstacle",cell=key(p)) for p in sorted(rocks)]
candidate["changeScope"]=["Whole-layout replacement after the 121-cell candidate failed finite sidewall clearance; no inherited route cost or physical acceptance. Total144 support cells =105 stoppable +36 non-stoppable ramp shoulders +3 rocks.",
    "Low/mid ramps: four physical columns q0..3 andq6..9 at rows1..6; inner q1..2/q7..8 stoppable. High ramp:q3..6 rows8..13, innerq4..5 stoppable. Each retains measured .75m rise/4.330127m worldZ half-cosine run.",
    "Full low platform row0, full middle platform row7, full upper platform rows14..15 q3..9; central rows4..6 q4..5 and rightrow8 give middle approach support. No second top or wall inside any profile footprint.",
    "Front-right true stair:q5 rows2/3/4; rear-right:q8 rows8/9/10. Three horizontal stopping platforms, two four-step flights, clearance1. No new only-pass rule or half-height rule state.",
    "Three rocks relocate to0:0,4:3,9:15; two blocked wall spans relocate to8:14<->9:14 and8:15<->9:15; their movement/effect rules retained. Central rock4:3 replaces the otherwise isolated low cell; leaving4:2 free connects every stoppable cell. Subsequent D3 scene objects must remain within the recorded D1 clearance boundary and may not intrude into validated route sweeps.",
    "The one-way high-to-mid drop moves to9:9>9:8, reverse blocked. Six water cells/two mud cells retain destination burden1 and are moved off support-only shoulders.",
    "Common single/pair seed4:0,facing0,rear3:0; target8:12. Four public support markers and capability witness coordinates relocate explicitly below.",
    "Inclining internal edges charge slope burden1 both ways including same-rule-height segments; isohypse edges are flat. Closed side edges, shoulder blocking, all body/turn/sweep rules and new path costs are independently enumerated."]
candidate["coordinateMigration"]={"markers":[dict(id=i,old=o,new=n) for i,o,n in [
    ("player_spawn","1:0","4:0"),("high_goal","8:8","8:12"),
    ("fire_domain_support","7:8","7:12"),("space_lock_support","9:7","9:12"),
    ("reveal_support","6:9","4:15"),("conceal_support","9:9","7:15"),
    ("mid_low_cover_a","3:4","4:5"),("mid_low_cover_b","7:5","9:7")]],
    "visibilityWitness":{"old":["6:8","7:8","8:8","9:8"],"new":["4:14","5:14","6:14","7:14"],"metricUnits":6},
    "scaleWitness":{"old":["7:8","8:8","9:8"],"new":["5:14","6:14","7:14"],"metricUnits":4},
    "sealEdge":{"old":"8:7>9:7","new":"8:12>9:12","metricUnits":2},
    "migrationBasis":"Marker old values refer to the committed original 100-cell candidate, not the rejected 121-cell experiment or old hypothetical pair seed2:0.",
    "parentResponsibility":"Update direct ability/assembly coordinate examples to these exact witnesses; no new ability rule or runtime range is introduced."}
for witness in (candidate["coordinateMigration"]["visibilityWitness"],candidate["coordinateMigration"]["scaleWitness"]):
    points=[tuple(map(int,s.split(':'))) for s in witness["new"]]
    check("relocated capability witness "+str(witness["metricUnits"])+" units retains flat H2 effects and movement",all(cells[p]["heightLevel"]==2 and cells[p]["physicalSurfaceId"]=="flat_H2" for p in points) and all(legal_edge(a,b) and edges[a,b]["allowsEffects"] and edges[a,b]["edgeKind"]=="flat" for a,b in zip(points,points[1:])) and sum(edges[a,b]["metricDistanceUnits"] for a,b in zip(points,points[1:]))==witness["metricUnits"])
grid_keys=['worldCenterFormula','neighborDirections','heightStepMeters','nominalSupportTopMeters','neighborCenterDistanceMeters','hexOrientation','hexCornerOffsetsXZ']
ramp_keys=['id','kind','coveredCells','stoppableCells','shoulderCells','worldZStart','worldZEnd','horizontalRunMeters','lowerY','upperY','formula','lowerPortalEdges','upperPortalEdges','closedSideEdges','minimumStraightClearWidthMeters']
stair_keys=['id','kind','coveredCells','lowerLevel','upperLevel','clearanceLimit','riseMeters','worldZStart','worldZEnd','worldZPortSeparationMeters','gridCenterPathLengthMeters','lowerY','platformCenters','endPlatformCenterToFlightMeters','middlePlatformWorldZDepthMeters','flightCount','stepsPerFlight','stepRiseMeters','stepRunWorldZMeters','flightWorldZRunMeters','flightStartOffsetsWorldZ','risers','lowerPortalEdges','upperPortalEdges','closedSideEdges']
geometry_relevant=dict(grid={k:candidate['grid'][k] for k in grid_keys},cells=candidate['cells'],directedEdges=candidate['directedEdges'],markers=candidate['markers'],ramps=[{k:r[k] for k in ramp_keys} for r in profile_defs],stairs=[{k:r[k] for k in stair_keys} for r in stair_defs])
geometry_digest=hashlib.sha256(json.dumps(geometry_relevant,sort_keys=True,separators=(',',':')).encode()).hexdigest()
assert geometry_digest == GEOMETRY_RELEVANT_SHA256, "Geometry-relevant input changed; the recorded evidence cannot be reused without appropriate review."
candidate['geometryRelevantDataUnchanged']=True
candidate['geometryRelevantDataSha256']=geometry_digest
candidate['geometryValidation']=GEOMETRY_SCOPE
report=dict(mapVersion=VERSION,scope="Complete offline graph validation plus accepted D1 geometry scope:390 stationary poses and two dynamic witness routes; not all993 graph transitions",checks="passed",testCount=len(checks),tests=checks,
            cellCount=len(cells),groundStopCellCount=sum(c["groundStopAllowed"] for c in cells.values()),supportOnlyShoulderCellCount=len(shoulders),directedEdgeCount=len(edges),overrideCount=len(overrides),heightCounts=dict(Counter(c["heightLevel"] for c in cells.values())),
            edgeKindCounts=dict(Counter(e["edgeKind"] for e in edges.values())),pairLegalPoseCount=len(poses),pairLegalTransitionCount=trace_count,
            singleReachableCellCount=len(single_reachable),
            checkedIntermediateTurnCount=turn_count,pairCrossHeightTransitionCount=sum(cells[p[0]]["heightLevel"]!=cells[n[0]]["heightLevel"] for p,ns in graph.items() for n in ns),
            singleMainWitness=single_main,singleBothStairsWitnessWithLeftClosed=single_alt,
            pairMainWitness=main,pairAlternateWitnessWithLeftClosed=alternate,
            physicalGeometryAcceptance=True,geometryValidation=GEOMETRY_SCOPE,
            geometryRelevantDataUnchanged=True,geometryRelevantDataSha256=geometry_digest)
for name,data in [("map-candidate.json",candidate),("verification.json",report),
                  ("pair-main-route.json",main),("pair-alternate-route.json",alternate),
                  ("physical-profiles.json",dict(profiles=profile_defs,stairs=stair_defs,cells=list(cells.values()),directedEdges=list(edges.values()))),
                  ("all-legal-pair-poses.json",[describe(p) for p in poses])]:
    (OUT/name).write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print(json.dumps({k:v for k,v in report.items() if k not in ("tests","pairMainWitness","pairAlternateWitnessWithLeftClosed")},ensure_ascii=False,indent=2))
print("Pair main:",main["steps"],main["movementEquivalentDistance"],[(p["front"],p["facing"]) for p in main["poses"]])
print("Pair alternative:",alternate["steps"],alternate["movementEquivalentDistance"],[(p["front"],p["facing"]) for p in alternate["poses"]])

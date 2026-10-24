"""Design-only CSV encoding on the existing EnvironmentProfiles entry."""
import csv
import io
import json
from pathlib import Path

OUT=Path(__file__).resolve().parent
c=json.loads((OUT/'map-candidate.json').read_text(encoding='utf-8'))
old_columns=['profileId','queryLimits','battlefieldCells','surfacePrototypeRefs','phenomenonChannels','phenomenonPairs','elementRelationRefs','spiritRecoveryEnvironment','edgeClearanceLimits']
new_columns=['mapSchemaVersion','mapVersion','directedEdgeOverrides','physicalSurfaceProfiles','mapMarkers']
columns=old_columns+new_columns
profile=c['environmentProfileEnvelope']
row={k:profile.get(k,'') for k in old_columns}
row['battlefieldCells']='|'.join(f"{x['q']}:{x['r']}@{int(x['blocksGroundMove'])}@{int(x['blocksLineOfSight'])}@{x['heightLevel']}@{x['cellMovementBurdenUnits']}@{x['surfaceKind']}@{x['physicalSurfaceId']}" for x in c['cells'])
row['mapSchemaVersion']='2'
row['mapVersion']=c['mapVersion']
row['directedEdgeOverrides']='|'.join('@'.join([x['fromCell']+'>'+x['toCell'],x['edgeKind'],str(x['metricDistanceUnits']),str(x['edgeMovementBurdenUnits']),str(int(x['allowsMovement'])),str(int(x['allowsEffects'])),str(x['clearanceLimit']),x['connectionId'] or 'none']) for x in c['sparseDirectedEdgeOverrides'])
physical={'flatProfiles':[{'id':f'flat_H{i}','kind':'flat','supportY':c['grid']['nominalSupportTopMeters'][i]} for i in range(3)],
          'gridGeometry':{k:c['grid'][k] for k in ('worldCenterFormula','neighborCenterDistanceMeters','hexOrientation','hexCornerOffsetsXZ')},
          'rampProfiles':c['physicalSurfaceProfiles'],'stairConnections':c['physicalStairConnections']}
row['physicalSurfaceProfiles']=json.dumps(physical,ensure_ascii=False,separators=(',',':'))
row['mapMarkers']=json.dumps(c['markers'],ensure_ascii=False,separators=(',',':'))
with (OUT/'EnvironmentProfiles.candidate.csv').open('w',encoding='utf-8',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=columns,lineterminator='\n')
    writer.writeheader();writer.writerow(row)
with (OUT/'EnvironmentProfiles.candidate.csv').open(encoding='utf-8',newline='') as f:
    parsed=list(csv.DictReader(f))
assert len(parsed)==1 and parsed[0]==row
assert len(parsed[0]['battlefieldCells'].split('|'))==len(c['cells'])
assert len(parsed[0]['directedEdgeOverrides'].split('|'))==len(c['sparseDirectedEdgeOverrides'])
assert json.loads(parsed[0]['physicalSurfaceProfiles'])==physical
assert json.loads(parsed[0]['mapMarkers'])==c['markers']
decoded_cells={}
for record in parsed[0]['battlefieldCells'].split('|'):
    pos,ground,los,h,burden,surface,physical_id=record.split('@')
    assert pos not in decoded_cells
    decoded_cells[pos]=dict(blocksGroundMove=bool(int(ground)),blocksLineOfSight=bool(int(los)),
                           heightLevel=int(h),cellMovementBurdenUnits=int(burden),surfaceKind=surface,physicalSurfaceId=physical_id)
for cell in c['cells']:
    decoded=decoded_cells[f"{cell['q']}:{cell['r']}"]
    assert all(decoded[k]==cell[k] for k in decoded)
edge_fields=['edgeKind','metricDistanceUnits','edgeMovementBurdenUnits','allowsMovement','allowsEffects','clearanceLimit','connectionId']
decoded_edges={}
for pos,cell in decoded_cells.items():
    q,r=map(int,pos.split(':'))
    for dq,dr in c['grid']['neighborDirections']:
        dest=f'{q+dq}:{r+dr}'
        if dest not in decoded_cells:
            continue
        same=cell['heightLevel']==decoded_cells[dest]['heightLevel']
        decoded_edges[pos+'>'+dest]=dict(edgeKind='flat' if same else 'open_cliff',metricDistanceUnits=2,
            edgeMovementBurdenUnits=0,allowsMovement=same,allowsEffects=True,clearanceLimit=0,connectionId=None)
for record in parsed[0]['directedEdgeOverrides'].split('|'):
    pair,kind,metric,burden,move,effects,clearance,connection=record.split('@')
    assert pair in decoded_edges
    decoded_edges[pair]=dict(edgeKind=kind,metricDistanceUnits=int(metric),edgeMovementBurdenUnits=int(burden),
        allowsMovement=bool(int(move)),allowsEffects=bool(int(effects)),clearanceLimit=int(clearance),connectionId=None if connection=='none' else connection)
assert len(decoded_edges)==len(c['directedEdges'])
assert all(decoded_edges[e['fromCell']+'>'+e['toCell']]=={k:e[k] for k in edge_fields} for e in c['directedEdges'])
support_ids={p['id'] for group in ('flatProfiles','rampProfiles','stairConnections') for p in physical[group]}
assert all(x['physicalSurfaceId'] in support_ids for x in decoded_cells.values())
assert all(e['connectionId'] for e in decoded_edges.values() if e['edgeKind'] in ('slope','stairs'))
contract={
 'status':'d1_encoding_and_geometry_values_fixed_production_importer_not_implemented',
 'productionEntry':'src/Assets/DataConfig/EnvironmentProfiles.csv',
 'consumerChain':'WorldContentImporter -> EnvironmentProfileDefinition/Asset -> CombatEntryAdapter -> existing SpatialQueryBoardFactory/SpatialQueryBoard; rendering reads the same declared physical profiles',
 'newRuntimeLoader':False,
 'existingColumns':old_columns,'appendedColumns':new_columns,
 'versionSelection':'Missing or blank mapSchemaVersion means version 1. Explicit 2 requires every v2 field. Other values are content errors; no runtime fallback.',
 'version1':{
   'battlefieldCells':'q:r@blocksGroundMove@blocksLineOfSight; ordered numeric q then r, unique q:r.',
   'newFieldDefaults':{'heightLevel':0,'cellMovementBurdenUnits':0,'surfaceKind':'none','physicalSurfaceId':'legacy_flat','directedEdgeOverrides':'none','physicalSurfaceProfiles':'legacy existing presentation unchanged','mapMarkers':'[]','mapVersion':'legacy'},
   'csvMigration':'Append the five new columns once to the existing header. Existing v1 rows append five empty CSV cells; their original nine field values remain byte-for-byte semantically unchanged.',
   'edgeRules':'Preserve old generated all-flat neighbor edges and existing edgeClearanceLimits exactly; no new geometry or body behavior is inferred for old rows.'},
 'version2':{
   'battlefieldCells':'q:r@groundBlocked(0|1)@losBlocked(0|1)@integerHeightLevel@nonnegativeCellBurdenUnits@surfaceKind@physicalSurfaceId; records joined with |, numeric q/r order, no duplicate coordinates.',
   'directedEdgeOverrides':'fromQ:fromR>toQ:toR@edgeKind@positiveMetricDistanceUnits@nonnegativeEdgeBurdenUnits@allowsMovement(0|1)@allowsEffects(0|1)@nonnegativeClearanceLimit@connectionIdOrNone; records joined with |. Endpoint pairs must be unique topology neighbors. slope and stairs require a non-empty named connectionId; none is invalid for those kinds. Cross-height pair support requires the same non-empty ID and same slope/stairs kind in both directions.',
   'defaultEdges':'Generate every existing six-neighbor directed pair. Same logical H: flat/allowMovement=1; different H: open_cliff/allowMovement=0. Both effects=1, metric=unitsPerRange, edgeBurden=0, clearance=0, connectionId=null. Then apply explicit directed overrides.',
   'edgeClearanceLimits':'Must be none in version 2 because directedEdgeOverrides is the sole clearance source. Version 1 behavior remains unchanged.',
   'physicalSurfaceProfiles':'One RFC8259 JSON object stored as a normal quoted CSV cell, with gridGeometry object and flatProfiles/rampProfiles/stairConnections arrays. Grid geometry defines actual hex corners and centers. The CSV writer doubles inner quotes. Cell physicalSurfaceId refs and each connection footprint resolve uniquely here. D1 dimensions are fixed under the explicitly recorded geometry scope; production acceptance additionally requires D2 importer implementation and D3 runtime realization.',
   'rampShoulders':'Each ramp shoulder appears as a normal battlefieldCells record with groundBlocked=1, losBlocked=0 and surfaceKind=ramp_shoulder; its physicalSurfaceId is the same ramp as the inner pathway. rampProfiles.shoulderCells is the explicit physical support-only set; no additional runtime occupancy state. D3 must distinguish non-traversable shoulders visually while keeping the measured body swept-clearance region free of raised decoration.',
   'stairStops':'All three stair platform cells use groundBlocked=0 and one physicalSurfaceId. Their exact physical platform elevations and true riser geometry live in stairConnections. The midpoint stays at integer lowerLevel as declared; no half-height gameplay state or pass-only cell is introduced.',
   'mapMarkers':'RFC8259 JSON array stored as a normal quoted CSV cell. IDs unique, cells present. Marker data does not imply blockers or occupancy.',
   'mapVersion':'Non-empty stable version ID; changed map input invalidates map-dependent cached paths.'},
 'geometryGate':'D1 geometry values are fixed. Evidence covers390 stationary pair poses and two dynamic witness routes:1295 samples and1240 conservatively bounded adjacent intervals. It does not prove all993 graph-legal dynamic transitions. This is a design-only candidate CSV; the current production importer is not implemented for v2. D2 must extend the existing data chain and D3 must realize the declared geometry and foot animation.',
 'geometryRelevantDataUnchanged':c['geometryRelevantDataUnchanged'],
 'geometryRelevantDataSha256':c['geometryRelevantDataSha256'],
 'tests':{'csvRoundTrip':True,'cellRecordCount':len(c['cells']),'overrideRecordCount':len(c['sparseDirectedEdgeOverrides']),'physicalJsonRoundTrip':True,'markerJsonRoundTrip':True,
          'decodedCellsMatch':True,'decodedAllDirectedEdgesMatch':True,'decodedEdgeCount':len(decoded_edges),'allSupportIdsResolve':True,'slopeStairsRequireNamedConnection':True},
 'noProductionWrites':True}
(OUT/'d2-import-contract.json').write_text(json.dumps(contract,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(contract['tests'],ensure_ascii=False))

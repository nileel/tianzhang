"""Read the existing profile envelope and document D2's minimum data contract."""
import csv
import hashlib
import json
from pathlib import Path

OUT=Path(__file__).resolve().parent
ROOT=Path(r'D:\天章游戏开发')
SOURCE=ROOT/'src/Assets/DataConfig/EnvironmentProfiles.csv'
candidate=json.loads((OUT/'map-candidate.json').read_text(encoding='utf-8'))
with SOURCE.open(encoding='utf-8-sig',newline='') as f:
    profiles=list(csv.DictReader(f))
base=next(p for p in profiles if p['profileId']=='env_guanzhong_wild')
envelope={k:base[k] for k in ('queryLimits','surfacePrototypeRefs','phenomenonChannels','phenomenonPairs','elementRelationRefs','spiritRecoveryEnvironment')}
envelope['profileId']='env_guanzhong_jindan_demo'
envelope['edgeClearanceLimits']='none'
cell_fields=['q','r','heightLevel','surfaceKind','blocksGroundMove','blocksLineOfSight','cellMovementBurdenUnits']
edge_fields=['fromCell','toCell','edgeKind','metricDistanceUnits','edgeMovementBurdenUnits','allowsMovement','allowsEffects','clearanceLimit','connectionId']
checks=[]
def check(name,value):
    assert value,name
    checks.append(name)
check('each of 100 cell rows has all 7 fields',len(candidate['cells'])==100 and all(set(cell_fields)<=set(c) for c in candidate['cells']))
check('each of 28 sparse directed edge rows has all 9 fields',len(candidate['sparseDirectedEdgeOverrides'])==28 and all(set(edge_fields)<=set(e) for e in candidate['sparseDirectedEdgeOverrides']))
check('profile envelope has every unchanged required importer field',all(envelope[k] for k in envelope))
check('query limits retain existing K2 and range24',envelope['queryLimits']=='unitsPerRange=2;maxQueryRange=24')
check('all marker locations name existing map cells',all(x['cell'] in {f"{c['q']}:{c['r']}" for c in candidate['cells']} for x in candidate['markers']))
default_rule=dict(sameHeight=dict(edgeKind='flat',allowsMovement=True),
                  differentHeight=dict(edgeKind='open_cliff',allowsMovement=False),
                  metricDistanceUnits=2,edgeMovementBurdenUnits=0,
                  allowsEffects=True,clearanceLimit=0,connectionId=None,
                  generation='Generate all six-neighbor directed edges in both directions, then apply sparse overrides. Missing reverse records are invalid, not inferred.')
check('final candidate includes complete exact environment envelope',candidate['environmentProfileEnvelope']==envelope)
check('final candidate includes complete exact default edge fields',candidate['defaultEdgeRule']==default_rule)
contract=dict(status='design_field_values_complete_for_D2_implementation_not_currently_importable',
              productionEntry='src/Assets/DataConfig/EnvironmentProfiles.csv',
              plannedProfileId='env_guanzhong_jindan_demo',
              existingProfileMustRemain='env_guanzhong_wild',
              copiedProfileEnvelope=envelope,
              copiedEnvelopeSource=dict(path=str(SOURCE),profileId=base['profileId'],sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest()),
              copyMode='These are explicit D2 output row values copied from the existing profile. Do not add a runtime profile inheritance mechanism.',
              mapFieldSources=dict(battlefieldCells='map-candidate.json:cells',directedEdgeOverrides='map-candidate.json:sparseDirectedEdgeOverrides',markers='map-candidate.json:markers',version='map-candidate.json:mapVersion'),
              requiredCellFields=cell_fields,requiredDirectedEdgeFields=edge_fields,
              defaultDirectedEdgeRule=default_rule,
              pairPlacementRule=dict(footprint='AdjacentPair',bodyScaleRank=2,
                                     sameHeight='Keep current rule: both internal directed records exist; at least one allows movement; both clearance limits pass.',
                                     differentHeight='Only abs(deltaHeight)=1; both internal records explicitly slope/stairs with matching edgeKind and connectionId, movement allowed, clearance passed. Open cliffs, drops, missing reverse and walls cannot hold a cross-height body.',
                                     movement='Keep turn-then-follow and every shortest 60-degree intermediate pose and rear sweep. Only 180 degrees may try the other three-step direction. No independent turn action.',
                                     presentation='User-approved body follows slope; this document supplies no geometric-support or collision proof.'),
              movementAccounting='Front translation costs metricDistanceUnits + edgeMovementBurdenUnits + destination cellMovementBurdenUnits. Rear/internal/sweep edges check eligibility only. Attack metric remains unchanged.',
              surfaceKindMeaning='Fixed-map terrain classification, not a reference to an already existing SurfacePrototype. Per-cell burden is explicit, so D2 movement is not dependent on adding D8 water prototypes.',
              markerScope='Spawn/objective/support/cover markers are retained design locations; support and cover do not implicitly create occupancy or blockers. Encounter actor composition remains D5.',
              knownCurrentCodeGaps=[
                  'WorldContentImporter.ParseBattlefieldCells accepts q:r@blocksGroundMove@blocksLineOfSight only; height and cell burden are absent.',
                  'WorldContentImporter.BuildDirectedEdges creates flat all-allowed edges; only clearance overrides are supported. Edge kind, directed allows flags and edge burden require D2 projection.',
                  'EnvironmentBattlefieldCell lacks height/burden; EnvironmentDirectedEdge lacks kind/burden. EnvironmentProfileAsset must round-trip the added data.',
                  'CombatEntryAdapter creates tiles without height; SpatialQueryBoardFactory hardcodes cell movement burden 0 and omits edge kind/burden.',
                  'SpatialQueryBoard.InspectEdge and InspectPlacement reject every different-height pair; movement costs consume destination-cell burden only.'
              ],
              physicalGeometry='Height meter values, connector occupied/stoppable cells and interfaces are supplied by the separate D1 geometry decision. They are not derived from graph route legality.',
              noProductionWrites=True,checks=checks,testCount=len(checks))
(OUT/'d2-import-contract.json').write_text(json.dumps(contract,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(testCount=len(checks),checks=checks,status=contract['status']),ensure_ascii=False,indent=2))

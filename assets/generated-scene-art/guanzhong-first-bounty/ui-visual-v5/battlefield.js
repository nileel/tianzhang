import * as T from '../ui-visual-v3/vendor/three.module.js';
const design = await fetch(new URL('../ui-visual-v3/scene-data.json', import.meta.url)).then(r => {if(!r.ok)throw new Error('Missing scene-data.json');return r.json();});

// Design-only geometry. No Unity rules, pathfinder, or battle simulation is called.
export function createBattlefield(host, labels, onPick, units) {
  const scene = new T.Scene();
  scene.background = new T.Color('#dce1d5');
  scene.fog = new T.Fog('#dce1d5', 24, 55);
  const camera = new T.OrthographicCamera(-12, 12, 6.75, -6.75, .1, 100);
  const renderer = new T.WebGLRenderer({ antialias: true, alpha: false });
  renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = T.PCFSoftShadowMap;
  renderer.outputColorSpace = T.SRGBColorSpace;
  renderer.toneMapping = T.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1;
  host.appendChild(renderer.domElement);
  scene.add(new T.HemisphereLight('#f7f8ed', '#607368', 1.7));
  const sun = new T.DirectionalLight('#fff9ed', 2.0);
  sun.position.set(-9, 16, 10); sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  Object.assign(sun.shadow.camera, { left: -12, right: 12, top: 12, bottom: -12, near: 1, far: 45 });
  sun.shadow.normalBias = .035; scene.add(sun);
  const board = new T.Group(); scene.add(board);
  const meshTargets = [], cells = [], cellMap = new Map(), gridLines = [];
  const overlays = new T.Group(); scene.add(overlays);
  const hl = r => design.heightBands.find(b => r <= b.maxR).height;
  const yAt = h => design.baseHeight + h * design.renderHeightStep;
  const xyz = (q, r) => new T.Vector3(Math.sqrt(3) * (q + r / 2), yAt(hl(r)), -1.5 * r);
  const mat = color => new T.MeshStandardMaterial({ color, roughness: 1, flatShading: true });
  const side = mat('#84968a'), road = mat('#c6bd9f');
  const tops = ['#aeb69a', '#afb69b', '#adb59b'].map(mat);
  const stone = mat('#aab0a1');
  for (let q = -design.radius; q <= design.radius; q++) for (let r = -design.radius; r <= design.radius; r++) {
    if (Math.max(Math.abs(q), Math.abs(r), Math.abs(q+r)) > design.radius) continue;
    const h = hl(r), p = xyz(q,r), isRoad = q === 0 || (r === -2 && q === 1);
    const cell = { q, r, height: h, position: p, key: `${q},${r}` };
    const mesh = new T.Mesh(new T.CylinderGeometry(1, 1, p.y, 6, 1), [side, isRoad ? road : tops[h], side]);
    mesh.position.set(p.x, p.y/2, p.z); mesh.castShadow = true; mesh.receiveShadow = true;
    mesh.userData.cell = cell; board.add(mesh); meshTargets.push(mesh);
    cells.push(cell); cellMap.set(cell.key, cell);
    const points = Array.from({length:7},(_,i)=>new T.Vector3(p.x+Math.sin(i*Math.PI/3),p.y+.013,p.z+Math.cos(i*Math.PI/3)));
    const line = new T.Line(new T.BufferGeometry().setFromPoints(points), new T.LineBasicMaterial({color:'#64786b',transparent:true,opacity:.16}));
    scene.add(line); gridLines.push(line);
  }
  const stairs = design.stairEdges;
  const stairTraces = new Map(), stairProfiles = [];
  for (const [a,b] of stairs) {
    const low=xyz(...a), high=xyz(...b), d=high.clone().sub(low); d.y=0; const angle=Math.atan2(d.x,d.z);
    const trace=[low.clone().add(new T.Vector3(0,.1,0))], profile=[];
    for(let i=0;i<7;i++) {
      // Complete the rise on the low side of the shared edge (t=.5).
      const start=.07+i*.44/7,end=.07+(i+1)*.44/7,t=(start+end)/2;
      const p=low.clone().lerp(high,t),top=low.y+(i+1)/7*(high.y-low.y);
      const s=new T.Mesh(new T.BoxGeometry(.85,top+.04,d.length()*(end-start)+.008),stone);
      s.position.set(p.x,(top-.04)/2,p.z);s.rotation.y=angle;s.castShadow=true;s.receiveShadow=true;scene.add(s);
      const startPoint=low.clone().lerp(high,start),endPoint=low.clone().lerp(high,end);
      startPoint.y=top+.1;endPoint.y=top+.1;trace.push(startPoint,endPoint);
      profile.push({start,end,top});
    }
    trace.push(high.clone().add(new T.Vector3(0,.1,0)));
    stairTraces.set(`${a}|${b}`,trace);stairTraces.set(`${b}|${a}`,[...trace].reverse());stairProfiles.push({edge:[a,b],steps:profile});
  }
  function rock(q,r,scale=1) {
    const p=xyz(q,r), rock=new T.Mesh(new T.DodecahedronGeometry(.47,0),mat('#71867c'));
    rock.position.set(p.x+.12,p.y+.24*scale,p.z-.04);rock.scale.set(1.3*scale,.8*scale,scale);rock.rotation.set(.15,q*.44,.1);
    rock.castShadow=true;rock.receiveShadow=true;scene.add(rock);
  }
  function pine(q,r,scale=1) {
    const p=xyz(q,r), tree=new T.Group();tree.position.copy(p);
    const trunk=new T.Mesh(new T.CylinderGeometry(.05,.10,1.8,5),mat('#657566'));trunk.position.y=.9;tree.add(trunk);
    for(const [y,s,x] of [[1.25,.9,.25],[1.72,.67,-.16],[2.05,.42,.06]]) {
      const crown=new T.Mesh(new T.IcosahedronGeometry(s,1),mat('#426b60'));crown.scale.set(1.15,.22,.8);crown.position.set(x,y,0);crown.castShadow=true;tree.add(crown);
    }
    tree.scale.setScalar(scale);scene.add(tree);
  }
  pine(-3,2,1.12);pine(0,3,.95);pine(3,-1,.84);rock(-2,3,1.35);rock(3,-2,.7);rock(-3,1,.65);
  const ground=new T.Mesh(new T.PlaneGeometry(150,150),mat('#dce1d5'));ground.rotation.x=-Math.PI/2;ground.position.y=-.05;ground.receiveShadow=true;scene.add(ground);
  const gradient=new T.DataTexture(new Uint8Array([80,145,205,255]),4,1,T.RedFormat);gradient.needsUpdate=true;
  const toon=color=>new T.MeshToonMaterial({color,gradientMap:gradient});
  function heroAt(q,r) {
    const root=new T.Group();root.position.copy(xyz(q,r));
    const robe=new T.Mesh(new T.CylinderGeometry(.17,.32,.88,7),toon('#263f54'));robe.position.y=.52;root.add(robe);
    const head=new T.Mesh(new T.SphereGeometry(.145,8,6),toon('#d5c5a8'));head.position.y=1.12;root.add(head);
    const hair=new T.Mesh(new T.SphereGeometry(.155,8,6),toon('#253d3b'));hair.position.set(0,1.20,-.025);hair.scale.y=.65;root.add(hair);
    const tail=new T.Mesh(new T.BoxGeometry(.16,.55,.12),toon('#263b3a'));tail.position.set(0,.87,-.16);root.add(tail);
    const collar=new T.Mesh(new T.BoxGeometry(.11,.32,.02),toon('#e7e5d1'));collar.position.set(.025,.82,.18);collar.rotation.z=.28;root.add(collar);
    for(const x of [-1,1]) {const sleeve=new T.Mesh(new T.CylinderGeometry(.12,.19,.48,6),toon('#314c5c'));sleeve.position.set(x*.25,.72,.015);sleeve.rotation.z=x*.18;root.add(sleeve);}
    root.traverse(o=>{if(o.isMesh)o.castShadow=true;});scene.add(root);return root;
  }
  function beastAt(q,r) {
    const root=new T.Group();root.position.copy(xyz(q,r));
    for(const [x,y,z,s] of [[0,.43,0,.4],[.23,.48,.08,.34],[-.24,.48,-.04,.34],[0,.39,.44,.24]]) {
      const part=new T.Mesh(new T.DodecahedronGeometry(s,0),toon('#6c7e6e'));part.position.set(x,y,z);part.scale.set(1.25,.9,1.1);root.add(part);
    }
    for(const x of [-.3,.3])for(const z of [-.28,.28]){const leg=new T.Mesh(new T.BoxGeometry(.18,.28,.2),toon('#626f60'));leg.position.set(x,.15,z);root.add(leg);}
    root.traverse(o=>{if(o.isMesh)o.castShadow=true;});scene.add(root);return root;
  }
  // Only presentation fixtures below this line; terrain above is inherited unchanged.
  const actors = new Map();
  for (const unit of units) {
    const root = unit.side === 'enemy' ? beastAt(...unit.position) : heroAt(...unit.position);
    root.traverse(o => { if (o.isMesh) o.userData.unitId = unit.id; });
    const ring = new T.Mesh(new T.RingGeometry(.43,.49,48),new T.MeshBasicMaterial({color:unit.side==='enemy'?'#a45340':'#c5dbb8',side:T.DoubleSide}));
    ring.rotation.x=-Math.PI/2;ring.position.y=.035;root.add(ring);
    const el=document.createElement('button');el.className=`world-label unit-label ${unit.side}`;el.dataset.unit=unit.id;
    el.setAttribute('aria-label',`战场单位：${unit.name}`);el.innerHTML=`<span>${unit.tag}</span><i><b style="width:${unit.hp/unit.maxHp*100}%"></b></i>`;
    el.addEventListener('click',()=>onPick({kind:'unit',id:unit.id}));labels.append(el);
    actors.set(unit.id,{unit,root,ring,el});
  }
  const mark=(q,r,color,opacity=.4)=>{
    const p=xyz(q,r),m=new T.Mesh(new T.CircleGeometry(.97,6),new T.MeshBasicMaterial({color,transparent:true,opacity,depthWrite:false,side:T.DoubleSide}));
    m.rotation.set(-Math.PI/2,0,Math.PI/6);m.position.set(p.x,p.y+.04,p.z);overlays.add(m);
  };
  function clear(){while(overlays.children.length){const m=overlays.children[0];overlays.remove(m);m.geometry?.dispose();m.material?.dispose();}}
  function highlight(id){actors.forEach((a,key)=>{a.ring.scale.setScalar(key===id?1.35:1);a.el.classList.toggle('selected',key===id);});render();}
  function range(cells,color='#91b5b2'){clear();cells.forEach(([q,r])=>mark(q,r,color,.48));render();}
  function path(){
    clear();design.route.forEach(([q,r])=>mark(q,r,'#a5cfb9',.58));
    const points=[xyz(...design.route[0]).add(new T.Vector3(0,.1,0))];
    for(let i=1;i<design.route.length;i++)points.push(...(stairTraces.get(`${design.route[i-1]}|${design.route[i]}`)||[xyz(...design.route[i]).add(new T.Vector3(0,.1,0))]));
    overlays.add(new T.Line(new T.BufferGeometry().setFromPoints(points),new T.LineBasicMaterial({color:'#fff1c7'})));render();
  }
  function cameraView(view='default') {
    if(view==='side')camera.position.set(12,7,14);else camera.position.set(2.2,12.8,17.7);
    camera.lookAt(.35,.7,0);camera.updateProjectionMatrix();render();
  }
  const pick=new T.Raycaster();
  renderer.domElement.addEventListener('click',e=>{
    const rect=renderer.domElement.getBoundingClientRect();pick.setFromCamera(new T.Vector2((e.clientX-rect.left)/rect.width*2-1,-(e.clientY-rect.top)/rect.height*2+1),camera);
    const hit=pick.intersectObjects([...actors.values()].filter(a=>a.root.visible).map(a=>a.root),true)[0];
    if(hit?.object.userData.unitId){onPick({kind:'unit',id:hit.object.userData.unitId});return;}
    const cell=pick.intersectObjects(meshTargets)[0]?.object.userData.cell;
    if(cell)onPick({kind:'tile',q:cell.q,r:cell.r,height:cell.height});
  });
  function render(){
    renderer.render(scene,camera);
    actors.forEach(a=>{const p=a.root.position.clone().add(new T.Vector3(0,a.unit.side==='enemy'?1.08:1.55,0)).project(camera);a.el.style.left=`${(p.x*.5+.5)*100}%`;a.el.style.top=`${(-p.y*.5+.5)*100}%`;});
  }
  function resize(){const w=host.clientWidth,h=host.clientHeight;renderer.setSize(w,h,false);camera.left=-7*w/h;camera.right=7*w/h;camera.top=7;camera.bottom=-7;camera.updateProjectionMatrix();render();}
  function setVisible(ids){actors.forEach((a,id)=>{a.root.visible=ids.includes(id);a.el.hidden=!a.root.visible;});render();}
  function reset(){actors.forEach(a=>a.root.position.copy(xyz(...a.unit.position)));clear();highlight(null);cameraView();}
  cameraView();new ResizeObserver(resize).observe(host);resize();
  return {highlight,range,path,cameraView,setVisible,reset,
    setCurrent:id=>{actors.forEach((a,key)=>a.el.classList.toggle('acting',key===id));},
    clear:()=>{clear();render();},grid:on=>{gridLines.forEach(l=>l.material.opacity=on?.45:.16);render();},
    movePlayer:()=>{actors.get('p').root.position.copy(xyz(0,1));render();},
    updateHealth:(id,hp)=>{const a=actors.get(id);a.el.querySelector('b').style.width=`${hp/a.unit.maxHp*100}%`;},
    evidence:()=>({renderer:'WebGL / three@0.180.0',tileCount:cells.length,heights:[...new Set(cells.map(c=>c.height))],stairs,stairProfiles,actors:actors.size})};
}

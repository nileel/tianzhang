// Capture the authorized static proposal, never Unity runtime.
const {chromium}=require('playwright');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const {pathToFileURL}=require('node:url');
(async()=>{
 const sandbox={};vm.createContext(sandbox);
 vm.runInContext(fs.readFileSync(path.join(__dirname,'storyboards.js'),'utf8')+';this.data=sequences;',sandbox);
 const data=sandbox.data;
 const only=new Set((process.argv[2]||'').split(',').filter(Boolean));
 const selected=only.size?data.filter(s=>only.has(s.id)):data;
 if(!selected.length)throw new Error('No matching storyboard group');
 const prior=only.size?JSON.parse(fs.readFileSync(path.join(__dirname,'review-check.json'),'utf8')):null;
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 const page=await browser.newPage();
 const errors=[],failedRequests=[],results=[],evidence=[];
 page.on('pageerror',e=>errors.push(String(e)));
 page.on('requestfailed',r=>failedRequests.push(r.url()));
 for(const [width,height] of [[1920,1080],[1366,768]]){
  await page.setViewportSize({width,height});
  const output=path.join(__dirname,'visuals',String(width));fs.mkdirSync(output,{recursive:true});
  for(const s of selected)for(let i=0;i<s.frames.length;i++){
   await page.goto(pathToFileURL(path.join(__dirname,'index.html')).href+'?sequence='+s.id+'&frame='+i);
   await page.evaluate(()=>document.fonts.ready);
   await page.waitForFunction(()=>document.querySelector('#scene-art')?.naturalWidth>0);
   const metrics=await page.evaluate(()=>{
    const selectors='.review,.topbar,.player,.timeline,.objective,.context,.dock,.scene-crop,.scene-note';
    const rects=[...document.querySelectorAll(selectors)].map(e=>({name:e.className,x:e.getBoundingClientRect().x,y:e.getBoundingClientRect().y,w:e.getBoundingClientRect().width,h:e.getBoundingClientRect().height}));
    const out=rects.filter(r=>r.x<0||r.y<0||r.x+r.w>innerWidth+1||r.y+r.h>innerHeight+1).map(r=>r.name);
    const textClip=[...document.querySelectorAll('button,h3,.breadcrumb,.review')].filter(e=>e.scrollWidth>e.clientWidth+2||e.scrollHeight>e.clientHeight+2).map(e=>e.textContent.trim());
    const scene=rects.find(r=>r.name==='scene-crop');
    const collisions=rects.filter(r=>/^(panel |dock|timeline|topbar)/.test(r.name)&&r.x<scene.x+scene.w&&r.x+r.w>scene.x&&r.y<scene.y+scene.h&&r.y+r.h>scene.y).map(r=>r.name);
    return {out,textClip,sceneCollisions:collisions,contextScroll:document.querySelector('.context').scrollHeight>document.querySelector('.context').clientHeight,images:[...document.querySelectorAll('#app img')].map(im=>({loaded:im.naturalWidth>0,width:im.naturalWidth,height:im.naturalHeight})),scene};
   });
   await page.screenshot({path:path.join(output,s.id+'-'+String(i+1).padStart(2,'0')+'.png')});
   results.push({resolution:[width,height],sequence:s.id,frame:i+1,...metrics});
  }
 }
 // Navigation and each source photograph must remain reviewable.
 for(let j=0;!prior&&j<data.length;j++){
  await page.selectOption('#sequence',String(j));
  const nav={sequence:data[j].id};
  await page.locator('#next').click();nav.next=await page.locator('#frame').inputValue()==='1';
  await page.locator('#previous').click();nav.previous=await page.locator('#frame').inputValue()==='0';
  for(const which of ['1','2']){
   await page.locator('[data-evidence="'+which+'"]').click();
   await page.waitForFunction(()=>document.querySelector('#evidence-image').complete&&document.querySelector('#evidence-image').naturalWidth>0);
   evidence.push({...nav,which,...await page.locator('#evidence-image').evaluate(i=>({width:i.naturalWidth,height:i.naturalHeight,src:i.getAttribute('src')}))});
   await page.locator('#close-evidence').click();
  }
 }
 const checkedThisRun=results.length;
 if(prior){results.push(...prior.results.filter(r=>!only.has(r.sequence)));evidence.push(...prior.evidence);}
 const report={kind:'V2 browser proposal review only; no Unity execution',date:'2026-10-30',screenCount:results.length,checkedThisRun,errors,failedRequests,evidence,results};
 fs.writeFileSync(path.join(__dirname,'review-check.json'),JSON.stringify(report,null,2)+'\n');
 const bad=results.filter(r=>r.out.length||r.textClip.length||r.sceneCollisions.length||r.images.some(i=>!i.loaded));
 await browser.close();
 console.log(JSON.stringify({screenCount:results.length,errors,failedRequests,layoutFailures:bad,sourceImages:evidence.length,navigationPassed:evidence.every(e=>e.next&&e.previous),scrollingDetails:results.filter(r=>r.contextScroll).map(r=>r.resolution[0]+':'+r.sequence+':'+r.frame)},null,2));
 if(bad.length||errors.length||failedRequests.length||evidence.some(e=>!e.next||!e.previous))process.exitCode=1;
})().catch(e=>{console.error(e);process.exit(1)});

// Actual public WebGL rendering from the declared Mine 3 reference fixture, in an isolated browser profile.
// Never touches the user's browser data. Uses the same Chromium dependency as smoke-macos.cjs.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..');
const {chromium}=require(path.join(root,'google-play-publish-2026-10-17-complete-integrations/google-play-publish/tools/agent-browser/node_modules/playwright-core'));
const arg=n=>{let i=process.argv.indexOf(n);return i<0?null:process.argv[i+1]};
const url=arg('--url')||'https://sites.aimulticast.org/sargsyan/cat-miners/';
const reports=path.join(root,arg('--reports')||'Docs/Reports/webgl-collapsed-mine-20261021');
async function read(page){return page.evaluate(async()=>{
 const names=(await indexedDB.databases()).map(d=>d.name).filter(n=>n.includes('/idbfs'));let all=[];
 for(const name of names){const db=await new Promise((ok,no)=>{let q=indexedDB.open(name);q.onsuccess=()=>ok(q.result);q.onerror=()=>no(q.error)});
 if(db.objectStoreNames.contains('FILE_DATA')) await new Promise((ok,no)=>{let q=db.transaction('FILE_DATA').objectStore('FILE_DATA').openCursor();q.onerror=()=>no(q.error);q.onsuccess=()=>{let c=q.result;if(!c)return ok();if(String(c.key).endsWith('/CatOutpostV2/main/state.json')){let e=JSON.parse(new TextDecoder().decode(c.value.contents)),s=JSON.parse(e.Payload);all.push({name,key:c.key,entry:c.value,commit:e.CommitSequence,zone:s.Run.Zone,max:s.Run.MapFinal?.MaxHp,remaining:s.Run.Targets.at(-1).RemainingHpUnits,ticks:s.Run.Now})}c.continue()}});db.close()}
 all.sort((a,b)=>b.commit-a.commit);return all[0]||null;
})}
(async()=>{let browser;const result={at:new Date().toISOString(),url,method:'declared-Mine3-reference-in-isolated-Chromium',realMobileDevice:false},exceptions=[];
try{
 browser=await chromium.launch({executablePath:'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',headless:true,args:['--use-angle=swiftshader','--enable-unsafe-swiftshader','--autoplay-policy=no-user-gesture-required']});
 const context=await browser.newContext({viewport:{width:540,height:960},deviceScaleFactor:1}),page=await context.newPage();page.on('pageerror',e=>exceptions.push(e.message));
 await page.goto(url,{waitUntil:'domcontentloaded',timeout:120000});await page.waitForFunction(()=>!!window.unityInstance,{timeout:180000});await page.waitForTimeout(7000);
 assert.equal(await page.evaluate(()=>config.productVersion),'0.1.8');
 const existing=await read(page);assert(existing,'browser must have created its main save');
 // Stop Unity before replacing ONLY this throwaway browser's main file.
 await page.goto(new URL('StreamingAssets/UnityServicesProjectConfiguration.json',url).href,{waitUntil:'load'});
 let payload=fs.readFileSync(path.join(root,'Docs/Reports/collapsed-mine/browser-reference-mine3.json'),'utf8');
 // Preserve 64-bit integer text: changing only the UTC field avoids JSON.parse rounding the RNG state.
 payload=payload.replace(/"LastProcessedUtcMs":\d+/, '"LastProcessedUtcMs":'+Date.now());
 const envelope=JSON.parse(fs.readFileSync(path.join(root,'Docs/Reports/final-excavations/save-check-3/CatOutpostV2/main/state.json'),'utf8'));
 envelope.Payload=payload;envelope.PayloadSha256=crypto.createHash('sha256').update(payload).digest('hex');envelope.CommitSequence=100;envelope.CatalogHash=null;
 await page.evaluate(async({name,key,text})=>{
 const db=await new Promise((ok,no)=>{let q=indexedDB.open(name);q.onsuccess=()=>ok(q.result);q.onerror=()=>no(q.error)});
 await new Promise((ok,no)=>{let tx=db.transaction('FILE_DATA','readwrite'),store=tx.objectStore('FILE_DATA');let q=store.openCursor();q.onsuccess=()=>{let c=q.result;if(!c){store.put({timestamp:new Date(),mode:33206,contents:new TextEncoder().encode(text)},key);return}if(String(c.key).startsWith(key))c.delete();c.continue()};tx.oncomplete=ok;tx.onerror=()=>no(tx.error)});db.close();
 },{name:existing.name,key:existing.key,text:JSON.stringify(envelope)});
 await page.goto(url,{waitUntil:'domcontentloaded',timeout:120000});await page.waitForFunction(()=>!!window.unityInstance,{timeout:180000});await page.waitForTimeout(5000);await page.keyboard.press('Escape');
 let box=await page.locator('#unity-canvas').boundingBox();
 for(let i=0;i<6;i++){
  await page.mouse.move(box.x+box.width*.5,box.y+box.height*.30);await page.mouse.down();
  await page.mouse.move(box.x+box.width*.5,box.y+box.height*.78,{steps:18});await page.mouse.up();await page.waitForTimeout(150);
 }
 await page.waitForTimeout(1500);
 await page.screenshot({path:path.join(reports,'public-excavation-mine3.png')});
 const first=await read(page);assert.equal(first.zone,3);assert.equal(first.max,82521);
 await page.waitForTimeout(10000);const later=await read(page);assert(later.remaining<first.remaining,'actual WebGL cats must damage the fixed-HP target');
 await page.reload({waitUntil:'domcontentloaded',timeout:120000});await page.waitForFunction(()=>!!window.unityInstance,{timeout:180000});await page.waitForTimeout(5000);
 const reload=await read(page);assert.equal(reload.zone,3);assert.equal(reload.max,first.max);assert(reload.remaining<=later.remaining,'partial giant HP survives browser reload');
 result.version='0.1.8';result.before={zone:first.zone,max:first.max,remaining:first.remaining};result.after={zone:later.zone,max:later.max,remaining:later.remaining};result.afterReload={zone:reload.zone,max:reload.max,remaining:reload.remaining};result.success=true;
}catch(e){result.success=false;result.failure=e.message;process.exitCode=1}
finally{result.pageExceptions=exceptions;if(exceptions.length){result.success=false;process.exitCode=1}fs.writeFileSync(path.join(reports,'public-excavation-browser.json'),JSON.stringify(result,null,2)+'\n');if(browser)await browser.close();console.log(JSON.stringify(result))}
})().catch(e=>{console.error(e.message);process.exitCode=1});

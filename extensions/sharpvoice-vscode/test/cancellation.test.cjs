const {test}=require('node:test');
const assert=require('node:assert/strict');
const fixture=require('./editor-fixture.cjs');
async function waitFor(predicate){for(let n=0;n<30&&!predicate();n++)await new Promise(r=>setImmediate(r));assert.ok(predicate(),'request reached the delayed boundary');}

for(const action of ['command','panel'])test(`${action} cancellation prevents a delayed code request from starting`,async()=>{
 const f=fixture();try{
  const d=f.document('unchanged');f.activate(d);let release;
  f.controller.channel.connect=()=>new Promise(r=>release=r);
  const pending=action==='panel'?f.controller.panelIntent({action:'start',mode:'edit'}):f.controller.start('edit');
  await waitFor(()=>release);
  assert.equal(f.views.at(-1).state,'opening','Pending startup must expose a cancellable busy state');
  if(action==='panel')await f.controller.panelIntent({action:'cancel'});else f.controller.cancel();
  release();await pending;
  assert.equal(f.frames.some(x=>x.type==='start'),false);
  assert.equal(f.controller.pendingId,undefined);assert.equal(d.text,'unchanged');
 }finally{f.controller.dispose();}
});

test('panel cancellation invalidates Ask AI before connection completes',async()=>{
 const f=fixture();try{
  f.activate(f.document('unchanged'));let release;
  f.controller.channel.connect=()=>new Promise(r=>release=r);
  const pending=f.controller.panelIntent({action:'send',mode:'ask',text:'Explain'});
  await waitFor(()=>release);await f.controller.panelIntent({action:'cancel'});release();await pending;
  assert.equal(f.frames.some(x=>x.type==='workspace_ask'),false);
 }finally{f.controller.dispose();}
});

for(const event of ['cancel','edit','close'])test(`clarification cannot send stale context after ${event} during context collection`,async()=>{
 const f=fixture();try{
  const d=f.document('unchanged');f.activate(d);await f.controller.start('edit','Improve');const id=f.controller.pendingId;
  await f.controller.receive({type:'result',id,result:{kind:'clarify',text:'',question:'Which style?'}});
  let release;f.controller.project.collect=()=>new Promise(r=>release=()=>r({related_sources:[],project_guidance:'',guidance_files:[]}));
  const pending=f.controller.clarify('Concise');await waitFor(()=>release);
  if(event==='cancel')f.controller.cancel();else if(event==='edit')f.change(d,[{rangeOffset:0,rangeLength:0,text:'manual '}]);else d.isClosed=true;
  release();await pending;assert.equal(f.frames.some(x=>x.type==='clarify'),false);
 }finally{f.controller.dispose();}
});

for(const event of ['cancel','edit','close'])test(`shortcut validation cannot send stale context after ${event} during context collection`,async()=>{
 const f=fixture();try{
  const d=f.document('unchanged');f.activate(d);await f.controller.start('edit','Document');const id=f.controller.pendingId;
  let release;f.controller.project.collect=()=>new Promise(r=>release=()=>r({related_sources:[],project_guidance:'',guidance_files:[]}));
  const pending=f.controller.receive({type:'validate',id,instruction:'document'});await waitFor(()=>release);
  if(event==='cancel')f.controller.cancel();else if(event==='edit')f.change(d,[{rangeOffset:0,rangeLength:0,text:'manual '}]);else d.isClosed=true;
  release();await pending;assert.equal(f.frames.some(x=>x.type==='validated'),false);
 }finally{f.controller.dispose();}
});

test('normal panel Ask exposes the Escape cancellation command while processing',async()=>{
 const f=fixture();try{
  await f.controller.receive({type:'workspace',mode:'ask',state:'processing'});
  assert.equal(f.commands.filter(x=>x[0]==='setContext'&&x[1]==='sharpvoice.sessionActive').at(-1)?.[2],true);
  f.controller.cancel();assert.equal(f.frames.at(-1).type,'workspace_cancel');
  await f.controller.receive({type:'workspace',mode:'ask',state:'idle'});
  assert.equal(f.commands.filter(x=>x[0]==='setContext'&&x[1]==='sharpvoice.sessionActive').at(-1)?.[2],false);
 }finally{f.controller.dispose();}
});

test('late validation from a canceled request cannot cancel its replacement',async()=>{
 const f=fixture();try{
  const d=f.document('unchanged');f.activate(d);await f.controller.start('edit','Improve prose');const oldId=f.controller.pendingId;
  const collect=f.controller.project.collect.bind(f.controller.project);let release;
  f.controller.project.collect=()=>new Promise(r=>release=()=>r({related_sources:[],project_guidance:'',guidance_files:[]}));
  const pending=f.controller.receive({type:'validate',id:oldId,instruction:'Improve prose',state:'initial'});await waitFor(()=>release);
  f.controller.cancel();f.controller.project.collect=collect;await f.controller.start('edit','Try another approach');const nextId=f.controller.pendingId;
  release();await pending;
  assert.equal(f.controller.pendingId,nextId);assert.ok(nextId);
  assert.equal(f.frames.some(x=>x.type==='cancel'&&x.id===nextId),false);
  assert.equal(f.frames.some(x=>x.type==='validated'&&x.id===oldId),false);
 }finally{f.controller.dispose();}
});

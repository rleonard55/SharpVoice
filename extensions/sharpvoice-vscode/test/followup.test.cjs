const {test}=require('node:test');const assert=require('node:assert/strict');
const {validateResult}=require('../out/model');const {parsePanelIntent}=require('../out/panelModel');
test('clarification choices are bounded literal replies only',()=>{
 const r={kind:'clarify',text:'',question:'Which style?',choices:['XML docs','Plain comments']};
 assert.deepEqual(validateResult(r,'edit',0,2),r);
 for(const choices of [null,[''],['a','a'],['a'.repeat(201)],['a','b','c','d','e'],[42]])assert.throws(()=>validateResult({...r,choices},'edit',0,2));
 assert.throws(()=>validateResult({...r,kind:'replace',text:'code',question:''},'edit',0,2));
});
test('choice intents carry only a question identity and bounded index',()=>{
 assert.deepEqual(parsePanelIntent({action:'choice',id:'question:1',index:0}),{action:'choice',id:'question:1',index:0});
 for(const intent of [{action:'choice',id:'',index:0},{action:'choice',id:'q',index:-1},{action:'choice',id:'q',index:4},{action:'choice',id:'q',index:0,text:'execute'}])assert.throws(()=>parsePanelIntent(intent));
});
const fixture=require('./editor-fixture.cjs');
test('workspace refresh preserves code question and buttons submit exactly once',async()=>{const f=fixture();try{
 const d=f.document('old');f.activate(d);await f.controller.start('edit','Change it');const id=f.controller.pendingId;
 await f.controller.receive({type:'result',id,result:{kind:'clarify',text:'',question:'Which style?',choices:['Compact','Expanded']}});
 const prompt=f.views.at(-1).follow_up;assert.equal(prompt.question,'Which style?');
 await f.controller.receive({type:'workspace',state:'clarify',sequence:1});assert.equal(f.views.at(-1).follow_up.id,prompt.id);
 await Promise.all([f.controller.panelIntent({action:'choice',id:prompt.id,index:1}),f.controller.panelIntent({action:'choice',id:prompt.id,index:1})]);
 assert.equal(f.frames.filter(x=>x.type==='clarify').length,1);assert.equal(f.frames.at(-1).instruction,'Expanded');assert.equal(d.text,'old');
 await f.controller.receive({type:'result',id,result:{kind:'clarify',text:'',question:'How much?',choices:['A little','A lot']}});
 await f.controller.panelIntent({action:'choice',id:prompt.id,index:0});assert.equal(f.frames.filter(x=>x.type==='clarify').length,1);
}finally{f.controller.dispose();}});
test('general quick replies preserve current file context and reject stale question identities',async()=>{const f=fixture();try{
 const d=f.document('source');f.activate(d);const conversation={id:'a'.repeat(32),scope:{domain:'editor_ask'},turns:[{id:'b'.repeat(32),status:'Needs Clarification',response:'Which style?',choices:['Brief','Detailed']}]};
 await f.controller.receive({type:'workspace',state:'idle',mode:'ask',include_file:true,conversation});const prompt=f.views.at(-1).follow_up;assert.ok(prompt);
 await f.controller.panelIntent({action:'choice',id:prompt.id+'stale',index:0});assert.equal(f.frames.filter(x=>x.type==='workspace_ask').length,0);
 await Promise.all([f.controller.panelIntent({action:'choice',id:prompt.id,index:1}),f.controller.panelIntent({action:'choice',id:prompt.id,index:1})]);const asks=f.frames.filter(x=>x.type==='workspace_ask');assert.equal(asks.length,1);assert.equal(asks[0].instruction,'Detailed');assert.equal(asks[0].include_file,true);assert.ok(asks[0].context);assert.equal(asks[0].turn_id,conversation.turns[0].id);
}finally{f.controller.dispose();}});
test('canceling a code clarification never exposes its historical question as a general Ask prompt',async()=>{const f=fixture();try{
 const d=f.document('old');f.activate(d);await f.controller.start('edit','Change it');const id=f.controller.pendingId;
 await f.controller.receive({type:'result',id,result:{kind:'clarify',text:'',question:'Which style?',choices:['Compact','Expanded']}});
 await f.controller.receive({type:'workspace',state:'clarify',conversation:{id:'a'.repeat(32),scope:{domain:'code'},turns:[{id:'b'.repeat(32),response:'Which style?',status:'Needs Clarification',choices:['Compact','Expanded']}]}});
 f.controller.cancel();await f.controller.receive({type:'workspace',state:'idle',conversation:{id:'a'.repeat(32),scope:{domain:'code'},turns:[{id:'b'.repeat(32),response:'Which style?',status:'Needs Clarification',choices:['Compact','Expanded']}]}});
 assert.equal(f.views.at(-1).follow_up,undefined);
}finally{f.controller.dispose();}});
test('interrupted connection does not permanently consume an unsent suggested reply',async()=>{const f=fixture();try{
 const d=f.document('source');f.activate(d);const conversation={id:'a'.repeat(32),scope:{domain:'general'},turns:[{id:'b'.repeat(32),status:'Needs Clarification',response:'Which style?',choices:['Brief','Detailed']}]};
 await f.controller.receive({type:'workspace',state:'idle',mode:'ask',conversation});const prompt=f.views.at(-1).follow_up;
 let release;f.controller.channel.connect=()=>new Promise(r=>release=r);const pending=f.controller.panelIntent({action:'choice',id:prompt.id,index:0});f.controller.hidePanel();release();await pending;
 assert.equal(f.frames.filter(x=>x.type==='workspace_ask').length,0);
 f.controller.channel.connect=async()=>{};await f.controller.panelIntent({action:'choice',id:prompt.id,index:0});assert.equal(f.frames.filter(x=>x.type==='workspace_ask').length,1);
}finally{f.controller.dispose();}});

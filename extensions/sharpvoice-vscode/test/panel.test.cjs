const {test}=require('node:test');const assert=require('node:assert/strict');
const {parsePanelIntent,PanelState}=require('../out/panelModel');
test('panel accepts only bounded named actions, never commands or arbitrary arguments',()=>{
 assert.deepEqual(parsePanelIntent({action:'send',text:'Make it vegetarian',mode:'ask',includeFile:false}),{action:'send',text:'Make it vegetarian',mode:'ask',includeFile:false});
 for(const input of [{action:'executeCommand',command:'evil'},{action:'send',text:'x'.repeat(4001)},{action:'select',id:'../file'},{action:'apply',arguments:['evil']},null])assert.throws(()=>parsePanelIntent(input));
});
test('panel drops stale snapshots/drafts and forgets content on host restart',()=>{
 const state=new PanelState();state.accept({type:'workspace',host_id:'a',conversation:{id:'c',turns:[{request:'Recipe',response:'Beef'}]},conversations:[]});
 assert.equal(state.snapshot.conversation.turns[0].response,'Beef');
 state.accept({type:'draft',host_id:'a',id:'turn',sequence:2,message:'beans'});
 state.accept({type:'draft',host_id:'a',id:'turn',sequence:1,message:'stale'});assert.equal(state.draft,'beans');
 state.accept({type:'workspace',host_id:'b',conversations:[]});assert.equal(state.snapshot.conversation,undefined);assert.equal(state.draft,'');
 assert.equal(state.accept({type:'draft',host_id:'a',id:'turn',sequence:5,message:'late'}),false);
});
test('out of order snapshots cannot replace a newer completed state',()=>{
 const state=new PanelState();state.accept({type:'workspace',host_id:'a',sequence:2,state:'idle',message:'Answered'});
 assert.equal(state.accept({type:'workspace',host_id:'a',sequence:1,state:'recording',message:'Old recording'}),false);
 assert.equal(state.snapshot.message,'Answered');
});
test('panel accepts explicit local batch review actions',()=>{
 for(const action of ['accept','revert'])assert.deepEqual(parsePanelIntent({action}),{action});
 assert.throws(()=>parsePanelIntent({action:'revert',uri:'file:///elsewhere'}));
});

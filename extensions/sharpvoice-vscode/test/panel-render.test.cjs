const {test}=require('node:test');const assert=require('node:assert/strict');const fs=require('node:fs');const vm=require('node:vm');
test('sidebar renders provider text literally and keeps all conversation state out of persistence',()=>{
 class Element{children=[];listeners={};value='';checked=false;hidden=false;_text='';set textContent(value){this._text=value;}get textContent(){return this._text;}set innerHTML(_){throw new Error('Raw HTML rendering is forbidden');}addEventListener(name,fn){this.listeners[name]=fn;}append(...nodes){this.children.push(...nodes);}appendChild(node){this.children.push(node);}replaceChildren(...nodes){this.children=[...nodes];}}
 const nodes=new Map();const el=id=>{if(!nodes.has(id))nodes.set(id,new Element());return nodes.get(id);};el('mode').value='ask';let receive;const posted=[];
 const context={document:{getElementById:el,createElement:()=>new Element()},window:{addEventListener:(name,fn)=>{if(name==='message')receive=fn;}},acquireVsCodeApi:()=>({postMessage:message=>posted.push(message)}),compromised:false};
 vm.runInNewContext(fs.readFileSync(require('node:path').join(__dirname,'../media/panel.js'),'utf8'),context);
 const hostile='<img src=x onerror="compromised=true"><script>compromised=true</script>';
 receive({data:{snapshot:{type:'workspace',host_id:'a',state:'idle',conversation:{id:'a'.repeat(32),scope:{domain:'general'},turns:[{request:'Recipe',response:hostile,status:'Answered'}]},conversations:[]}}});
 assert.equal(el('turns').children[0].children[3].textContent,hostile);assert.equal(context.compromised,false);
 receive({data:{snapshot:{type:'workspace',host_id:'a',state:'idle',pending_changes:2,review_file:'test.cs',review_conflicts:1}}});
 assert.equal(el('accept').hidden,false);assert.equal(el('revert').hidden,false);assert.equal(el('send').disabled,false);assert.match(el('pending').textContent,/2/);
 el('accept').listeners.click();assert.equal(posted.at(-1).action,'accept');el('revert').listeners.click();assert.equal(posted.at(-1).action,'revert');
 receive({data:{snapshot:{type:'workspace',host_id:'a',state:'processing',pending_changes:2}}});assert.equal(el('accept').disabled,true);assert.equal(el('revert').disabled,true);
 receive({data:{snapshot:{type:'workspace',host_id:'a',state:'idle',pending_changes:0}}});assert.equal(el('accept').hidden,true);assert.equal(el('revert').hidden,true);
 receive({data:{snapshot:{type:'workspace',host_id:'a',state:'clarify',follow_up:{id:'q:1',question:hostile,choices:['Yes',hostile]}}}});
 assert.equal(el('questionCard').hidden,false);assert.equal(el('questionText').textContent,hostile);assert.equal(el('choices').children.length,2);assert.equal(el('choices').children[1].textContent,hostile);
 el('voice').listeners.click();assert.equal(posted.at(-1).action,'reply');
 const oldButton=el('choices').children[0];oldButton.listeners.click();assert.equal(posted.at(-1).id,'q:1');assert.equal(posted.at(-1).index,0);assert.equal(el('choices').children[1].disabled,true);
 receive({data:{snapshot:{type:'workspace',host_id:'a',state:'idle'}}});assert.equal(el('questionCard').hidden,true);
 receive({data:{snapshot:{type:'workspace',host_id:'a',state:'idle',review_items:[{id:'h:1',file:'a.cs',line:3,preview:hostile,conflicted:false}],recovery_available:1,project_files:['AGENTS.md'],verification:'Task exited with code 0'}}});
 assert.equal(el('changes').children.length,1);assert.equal(el('recovery').hidden,false);assert.equal(el('verification').textContent,'Task exited with code 0');
 el('changes').children[0].children[1].listeners.click();assert.equal(posted.at(-1).action,'acceptChange');assert.equal(posted.at(-1).id,'h:1');
 el('fix').listeners.click();assert.equal(posted.at(-1).action,'fix');
 el('reply').value='Make it vegetarian';el('send').listeners.click();assert.equal(posted.at(-1).text,'Make it vegetarian');assert.equal(posted.at(-1).includeFile,false);
 receive({data:{snapshot:{type:'workspace',host_id:'b',state:'idle',conversations:[]}}});assert.equal(el('reply').value,'');assert.equal(el('turns').children.length,0);
});

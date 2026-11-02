const {test}=require('node:test');const assert=require('node:assert/strict');const fs=require('node:fs');const vm=require('node:vm');
test('sidebar renders provider text literally and keeps all conversation state out of persistence',()=>{
 class Element{children=[];listeners={};value='';checked=false;hidden=false;_text='';set textContent(value){this._text=value;}get textContent(){return this._text;}set innerHTML(_){throw new Error('Raw HTML rendering is forbidden');}addEventListener(name,fn){this.listeners[name]=fn;}append(...nodes){this.children.push(...nodes);}appendChild(node){this.children.push(node);}replaceChildren(...nodes){this.children=[...nodes];}}
 const nodes=new Map();const el=id=>{if(!nodes.has(id))nodes.set(id,new Element());return nodes.get(id);};el('mode').value='ask';let receive;const posted=[];
 const context={document:{getElementById:el,createElement:()=>new Element()},window:{addEventListener:(name,fn)=>{if(name==='message')receive=fn;}},acquireVsCodeApi:()=>({postMessage:message=>posted.push(message)}),compromised:false};
 vm.runInNewContext(fs.readFileSync(require('node:path').join(__dirname,'../media/panel.js'),'utf8'),context);
 const hostile='<img src=x onerror="compromised=true"><script>compromised=true</script>';
 receive({data:{snapshot:{type:'workspace',host_id:'a',state:'idle',conversation:{id:'a'.repeat(32),scope:{domain:'general'},turns:[{request:'Recipe',response:hostile,status:'Answered'}]},conversations:[]}}});
 assert.equal(el('turns').children[0].children[3].textContent,hostile);assert.equal(context.compromised,false);
 el('reply').value='Make it vegetarian';el('send').listeners.click();assert.equal(posted.at(-1).text,'Make it vegetarian');assert.equal(posted.at(-1).includeFile,false);
 receive({data:{snapshot:{type:'workspace',host_id:'b',state:'idle',conversations:[]}}});assert.equal(el('reply').value,'');assert.equal(el('turns').children.length,0);
});

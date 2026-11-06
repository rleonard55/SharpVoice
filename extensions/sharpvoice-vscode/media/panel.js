'use strict';
const api=acquireVsCodeApi();const el=id=>document.getElementById(id);let current={},draft='';
const options=()=>({mode:el('mode').value,includeFile:el('includeFile').checked});
const send=action=>api.postMessage(action);
for(const action of ['new','mode'])el(action==='mode'?'mode':'new').addEventListener(action==='mode'?'change':'click',()=>send({action,...options()}));
el('includeFile').addEventListener('change',()=>send({action:'mode',...options()}));
for(const action of ['fix','explainError','testThis','runTests','related','editRelated','shortcuts','explainChanges','acceptWorkspace','revertWorkspace','resume','discardRecovery','nextChange','previousChange','cancel','review','accept','revert','clear','move','copy'])el(action).addEventListener('click',()=>send({action}));
el('delete').addEventListener('click',()=>{if(current.conversation)send({action:'delete',id:current.conversation.id});});
el('conversations').addEventListener('change',()=>{if(el('conversations').value)send({action:'select',id:el('conversations').value});});
el('voice').addEventListener('click',()=>send(current.state==='recording'?{action:'stop'}:current.state==='arming'?{action:'cancel'}:current.follow_up?{action:'reply'}:{action:'start',...options()}));
function submit(){const text=el('reply').value;if(text.trim())send({action:'send',text,...options()});}
el('send').addEventListener('click',submit);el('reply').addEventListener('keydown',e=>{if(e.key==='Enter'&&(e.ctrlKey||e.metaKey)){e.preventDefault();submit();}});
function render(snapshot,live){const changedHost=current.host_id&&current.host_id!==snapshot.host_id;current=snapshot;draft=live;const state=snapshot.state??'idle';const busy=['arming','recording','processing','receiving','preparing_apply','answering','applying','opening','busy'].includes(state);
 if(changedHost)el('reply').value='';
 if(snapshot.mode)el('mode').value=snapshot.mode;el('includeFile').checked=snapshot.include_file===true;
 el('status').textContent=snapshot.message??'Ready';el('model').textContent=snapshot.model??'Your voice. Your words.';el('scope').textContent=snapshot.context?`${snapshot.context.name} · ${snapshot.context.language} · ${snapshot.context.target_kind??'Selected Context'}`:snapshot.conversation?.scope?.domain==='general'?'General Ask AI · No Document Attached':'';
 el('projectFiles').textContent=(snapshot.project_files??[]).length?'Context / guidance: '+snapshot.project_files.join(', '):'';
 el('verification').textContent=snapshot.verification??'';el('verification').hidden=!snapshot.verification;
 el('recovery').hidden=!(snapshot.recovery_available>0);
 el('changes').replaceChildren();el('changeList').hidden=!(snapshot.review_items?.length);
 for(const item of snapshot.review_items??[]){const row=document.createElement('section');row.className='change';
  const jump=document.createElement('button');jump.textContent=`${item.file}:${item.line} · ${item.conflicted?'Manual conflict · ':''}${item.preview}`;jump.disabled=busy||state==='clarify';jump.addEventListener('click',()=>send({action:'jumpChange',id:item.id}));row.appendChild(jump);
  for(const action of ['acceptChange','revertChange']){const button=document.createElement('button');button.textContent=action==='acceptChange'?'Accept':'Revert';button.disabled=busy||state==='clarify'||action==='revertChange'&&item.conflicted;button.addEventListener('click',()=>send({action,id:item.id}));row.appendChild(button);}el('changes').appendChild(row);
 }
 for(const id of ['fix','explainError','testThis','runTests','related','editRelated','shortcuts','explainChanges','acceptWorkspace','revertWorkspace','resume','discardRecovery','nextChange','previousChange'])el(id).disabled=busy||state==='clarify';
 el('conversations').replaceChildren();for(const c of snapshot.conversations??[]){const o=document.createElement('option');o.value=c.id;o.textContent=c.title;o.selected=c.id===snapshot.conversation?.id;el('conversations').appendChild(o);}
 const view=el('turns');const follow=view.scrollHeight-view.scrollTop-view.clientHeight<60;const oldScroll=view.scrollTop;view.replaceChildren();for(const turn of snapshot.conversation?.turns??[]){const card=document.createElement('section');card.className='turn';const heading=document.createElement('strong');heading.textContent='You';const q=document.createElement('p');q.textContent=turn.request;card.append(heading,q);if(turn.response){const label=document.createElement('strong');label.className='answer';label.textContent='SharpVoice';const a=document.createElement('p');a.textContent=turn.response;card.append(label,a);}const status=document.createElement('small');status.textContent=[turn.status,turn.model].filter(Boolean).join(' · ');card.append(status);view.appendChild(card);}
 const prompt=snapshot.follow_up;el('questionCard').hidden=!prompt;el('questionText').textContent=prompt?.question??'';el('choices').replaceChildren();
 for(const [index,text]of (prompt?.choices??[]).entries()){
  const button=document.createElement('button');button.textContent=text;button.disabled=busy;
  const questionId=prompt.id;
  button.addEventListener('click',()=>{if(current.follow_up?.id!==questionId||button.disabled)return;for(const child of el('choices').children)child.disabled=true;send({action:'choice',id:questionId,index});});
  el('choices').appendChild(button);
 }
 if(follow)view.scrollTop=view.scrollHeight;else view.scrollTop=oldScroll;
 el('omitted').hidden=!snapshot.conversation?.omitted;el('draft').hidden=!draft;el('draft').textContent=draft?'Live Draft · '+draft:'';
 el('voice').className=state==='recording'?'primary recording':'primary';
 el('status').className=snapshot.follow_up?'needs-answer':state==='recording'?'recording-status':'';
 el('voice').textContent=state==='recording'?'Stop Recording':state==='arming'?'Cancel Recording':snapshot.follow_up?'Reply by Voice':'Speak';el('voice').disabled=busy&&!['arming','recording'].includes(state);el('send').disabled=busy;el('reply').disabled=busy;el('cancel').disabled=['idle','busy','applying'].includes(state);const pending=snapshot.pending_changes??0;el('pending').hidden=!pending;el('pending').textContent=pending?`${snapshot.review_file??'Current file'} · ${pending} pending change${pending===1?'':'s'}${snapshot.review_conflicts?' · '+snapshot.review_conflicts+' overlap manual edits':''}`:'';
 el('accept').hidden=!pending;el('revert').hidden=!pending;el('review').hidden=!pending;for(const id of ['accept','revert','review'])el(id).disabled=busy||state==='clarify';
 for(const id of ['mode','includeFile','conversations','new'])el(id).disabled=busy||state==='clarify';
 el('delete').disabled=busy||state==='clarify'||!snapshot.conversation;el('clear').disabled=busy||state==='clarify'||!(snapshot.conversations?.length);
 el('copy').disabled=!(snapshot.conversation?.turns??[]).some(turn=>turn.response);
}
window.addEventListener('message',event=>{if(event.data&&event.data.snapshot)render(event.data.snapshot,event.data.draft??'');});send({action:'ready'});

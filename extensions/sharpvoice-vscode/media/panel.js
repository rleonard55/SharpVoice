'use strict';
const api=acquireVsCodeApi();const el=id=>document.getElementById(id);let current={},draft='';
const options=()=>({mode:el('mode').value,includeFile:el('includeFile').checked});
const send=action=>api.postMessage(action);
for(const action of ['new','mode'])el(action==='mode'?'mode':'new').addEventListener(action==='mode'?'change':'click',()=>send({action,...options()}));
el('includeFile').addEventListener('change',()=>send({action:'mode',...options()}));
for(const action of ['cancel','review','apply','clear','move','copy'])el(action).addEventListener('click',()=>send({action}));
el('delete').addEventListener('click',()=>{if(current.conversation)send({action:'delete',id:current.conversation.id});});
el('conversations').addEventListener('change',()=>{if(el('conversations').value)send({action:'select',id:el('conversations').value});});
el('voice').addEventListener('click',()=>send(current.state==='recording'?{action:'stop'}:current.state==='arming'?{action:'cancel'}:current.state==='clarify'?{action:'reply'}:{action:'start',...options()}));
function submit(){const text=el('reply').value;if(text.trim())send({action:'send',text,...options()});}
el('send').addEventListener('click',submit);el('reply').addEventListener('keydown',e=>{if(e.key==='Enter'&&(e.ctrlKey||e.metaKey)){e.preventDefault();submit();}});
function render(snapshot,live){const changedHost=current.host_id&&current.host_id!==snapshot.host_id;current=snapshot;draft=live;const state=snapshot.state??'idle';const busy=['arming','recording','processing','applying','opening','busy'].includes(state);
 if(changedHost)el('reply').value='';
 if(snapshot.mode)el('mode').value=snapshot.mode;el('includeFile').checked=snapshot.include_file===true;
 el('status').textContent=snapshot.message??'Ready';el('model').textContent=snapshot.model??'Your voice. Your words.';el('scope').textContent=snapshot.context?`${snapshot.context.name} · ${snapshot.context.language} · ${snapshot.context.target_kind??'Selected Context'}`:snapshot.conversation?.scope?.domain==='general'?'General Ask AI · No Document Attached':'';
 el('conversations').replaceChildren();for(const c of snapshot.conversations??[]){const o=document.createElement('option');o.value=c.id;o.textContent=c.title;o.selected=c.id===snapshot.conversation?.id;el('conversations').appendChild(o);}
 const view=el('turns');const follow=view.scrollHeight-view.scrollTop-view.clientHeight<60;const oldScroll=view.scrollTop;view.replaceChildren();for(const turn of snapshot.conversation?.turns??[]){const card=document.createElement('section');card.className='turn';const heading=document.createElement('strong');heading.textContent='You';const q=document.createElement('p');q.textContent=turn.request;card.append(heading,q);if(turn.response){const label=document.createElement('strong');label.className='answer';label.textContent='SharpVoice';const a=document.createElement('p');a.textContent=turn.response;card.append(label,a);}const status=document.createElement('small');status.textContent=[turn.status,turn.model].filter(Boolean).join(' · ');card.append(status);view.appendChild(card);}
 if(snapshot.result?.question){const q=document.createElement('section');q.className='turn';q.textContent=snapshot.result.question;view.appendChild(q);}
 if(follow)view.scrollTop=view.scrollHeight;else view.scrollTop=oldScroll;
 el('omitted').hidden=!snapshot.conversation?.omitted;el('draft').hidden=!draft;el('draft').textContent=draft?'Live Draft · '+draft:'';
 el('voice').textContent=state==='recording'?'Stop Recording':state==='arming'?'Cancel Recording':state==='clarify'?'Reply by Voice':'Speak';el('voice').disabled=busy&&!['arming','recording'].includes(state);el('send').disabled=busy;el('reply').disabled=busy;el('cancel').disabled=['idle','busy','applying'].includes(state);el('apply').hidden=state!=='review';el('review').hidden=state!=='review';
 for(const id of ['mode','includeFile','conversations'])el(id).disabled=busy;
}
window.addEventListener('message',event=>{if(event.data&&event.data.snapshot)render(event.data.snapshot,event.data.draft??'');});send({action:'ready'});

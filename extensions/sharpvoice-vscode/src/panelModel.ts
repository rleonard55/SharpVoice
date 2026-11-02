import {Frame} from './model';
export interface Turn {id:string;request:string;response:string;status:string;model:string;omitted?:boolean}
export interface Conversation {id:string;title:string;turns:Turn[];omitted:boolean;retired?:boolean;scope:{domain:string;binding:string;provider:string}}
export interface ConversationInfo {id:string;title:string;domain:string;retired:boolean}
export type PanelIntent={action:string;text?:string;mode?:string;includeFile?:boolean;id?:string};
export function parsePanelIntent(value:unknown):PanelIntent {
 if(!value||typeof value!=='object'||Array.isArray(value))throw new Error('Invalid SharpVoice action.');
 const v=value as Record<string,unknown>;const action=v.action;
 if(typeof action!=='string'||!['send','start','stop','cancel','apply','review','reply','new','select','delete','clear','move','copy','mode'].includes(action))throw new Error('Unsupported SharpVoice action.');
 const allowed=action==='send'?['action','text','mode','includeFile']:['start','new','mode'].includes(action)?['action','mode','includeFile']:['select','delete'].includes(action)?['action','id']:['action'];
 if(Object.keys(v).some(k=>!allowed.includes(k)))throw new Error('Unexpected SharpVoice action argument.');
 const result:PanelIntent={action};
 if(action==='send') {if(typeof v.text!=='string'||!v.text.trim()||v.text.length>4000)throw new Error('Enter a reply of 1–4,000 characters.');result.text=v.text;}
 if(['send','start','new','mode'].includes(action)) {if(v.mode!==undefined&&!['ask','dictate','edit'].includes(String(v.mode)))throw new Error('Invalid voice mode.');if(v.includeFile!==undefined&&typeof v.includeFile!=='boolean')throw new Error('Invalid context option.');result.mode=String(v.mode??'ask');result.includeFile=v.includeFile===true;}
 if(['select','delete'].includes(action)){if(typeof v.id!=='string'||!/^[a-f0-9]{32}$/.test(v.id))throw new Error('Invalid conversation identifier.');result.id=v.id;}
 return result;
}
export class PanelState {
 snapshot:Frame={type:'workspace'};draft='';private sequence=0;private draftTurn='';private snapshotSequence=0;
 accept(frame:Frame):boolean {
  if(frame.type==='workspace'){
   if(frame.host_id!==this.snapshot.host_id){this.draft='';this.sequence=0;this.draftTurn='';this.snapshotSequence=0;}
   else if(frame.sequence!==undefined&&frame.sequence<=this.snapshotSequence)return false;
   if(frame.sequence!==undefined)this.snapshotSequence=frame.sequence;
   this.snapshot=frame;if(frame.state!=='recording'){this.draft='';this.sequence=0;this.draftTurn='';}return true;
  }
  if(frame.type==='draft'){
   if(frame.host_id!==this.snapshot.host_id||this.snapshot.state&&this.snapshot.state!=='recording'||this.snapshot.turn_id&&this.snapshot.turn_id!==frame.id)return false;
   if(frame.id===this.draftTurn&&(frame.sequence??0)<=this.sequence)return false;
   this.draftTurn=frame.id??'';this.sequence=frame.sequence??0;this.draft=frame.message??'';return true;
  }
  return false;
 }
}

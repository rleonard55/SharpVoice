export interface Snapshot { uri:string; version:number; language:string; name:string; text:string; start:number; end:number; diagnostics:string; indent:string; eol:string }
export interface RelatedSource {name:string;language:string;text:string}
export interface ReviewItem {id:string;uri:string;file:string;line:number;conflicted:boolean;preview:string}
export interface Context {related_sources?:RelatedSource[];project_guidance?:string;guidance_files?:string[]; uri:string; version:number; language:string; name:string; context_text:string; start:number; end:number; source_text:string; diagnostics:string; indent:string; eol:string; target_kind?:string; cursor_offset?:number }
export interface Result {kind:'insert'|'replace'|'clarify'|'unsupported';text:string;question:string;choices?:string[]}
import type {Conversation,ConversationInfo} from './panelModel';
export interface FollowUp {id:string;question:string;choices:string[]}
export interface Frame {review_items?:ReviewItem[];recovery_available?:number;project_files?:string[];verification?:string;follow_up?:FollowUp;pending_changes?:number;review_conflicts?:number;review_file?:string;type:string;id?:string;version?:number;mode?:string;context?:Context;instruction?:string;result?:Result;model?:string;message?:string;token?:string;host_id?:string;client_id?:string;workspace?:string;conversation_id?:string;turn_id?:string;sequence?:number;conversation?:Conversation;conversations?:ConversationInfo[];state?:string;include_file?:boolean}
export function captureContext(s:Snapshot,scope:string):Context {
 if(!/^(file|untitled):/.test(s.uri)||s.version<1||!Number.isInteger(s.start)||!Number.isInteger(s.end)||s.start<0||s.end<s.start||s.end>s.text.length) throw new Error('Select a single region in a local editable file.');
 const source=s.text.slice(s.start,s.end); if(source.length>32000)throw new Error('Select a smaller target (32,000 characters maximum).');
 let text=source;
 if(scope==='document'){if(s.text.length>32000)throw new Error('Document exceeds 32,000 characters; choose Nearby Code or a smaller selection.');text=s.text;}
 else if(scope==='nearby'){
  const {start,end}=nearbyRange(s);
  text=s.text.slice(start,end);
 }
 return {uri:s.uri,version:s.version,language:s.language,name:s.name,context_text:text,start:s.start,end:s.end,source_text:source,diagnostics:s.diagnostics.slice(0,4000),indent:s.indent.slice(0,16),eol:s.eol};
}
function nearbyRange(s:Snapshot):{start:number;end:number} {
 const spare=32000-(s.end-s.start);let start=Math.max(0,s.start-Math.floor(spare/2));let end=Math.min(s.text.length,start+32000);start=Math.max(0,end-32000);
 if(start>0){const line=s.text.indexOf('\n',start);start=line>=0&&line<s.start?line+1:start;}
 if(end<s.text.length){const line=s.text.lastIndexOf('\n',end);end=line>=s.end?line:end;}
 if(start>0&&/[\uDC00-\uDFFF]/.test(s.text[start]??'')&&/[\uD800-\uDBFF]/.test(s.text[start-1]))start++;
 if(end<s.text.length&&/[\uD800-\uDBFF]/.test(s.text[end-1]??''))end--;
 return {start,end};
}
export function captureTarget(s:Snapshot,scope:string,mode:string):{snapshot:Snapshot;context:Context} {
 if(!['selection','nearby','document'].includes(scope))throw new Error('Choose a valid SharpVoice Context setting.');
 // Validate the original cursor/selection before choosing a larger, already-allowed target.
 captureContext(s,scope);
 let snapshot=s;let kind=s.start===s.end?'cursor':'selection';
 if(s.start===s.end&&s.text.length>0&&scope!=='selection'){
  const range=scope==='document'?{start:0,end:s.text.length}:nearbyRange(s);
  snapshot={...s,...range};kind=range.start===0&&range.end===s.text.length?'document':'nearby';
 }
 if(mode==='edit'&&snapshot.start===snapshot.end&&scope==='selection')throw new Error('Selection Only context is enabled. Select a target or choose Nearby Code in SharpVoice Context settings.');
 const context=captureContext(snapshot,scope);context.target_kind=kind;
 if(kind!=='selection')context.cursor_offset=s.start-snapshot.start;
 return {snapshot,context};
}
export function validateResult(raw:unknown,mode:string,start:number,end:number):Result {
 if(!raw||typeof raw!=='object')throw new Error('Invalid code response.');
 const r=raw as Result;
 if(Object.keys(r).some(k=>!['kind','text','question','choices'].includes(k))||!['insert','replace','clarify','unsupported'].includes(r.kind)||typeof r.text!=='string'||r.text.length>200000||typeof r.question!=='string'||r.question.length>2000)throw new Error('Malformed code response.');
 if(r.choices!==undefined&&(!Array.isArray(r.choices)||r.choices.length>4||r.choices.some(c=>typeof c!=='string'||!c.trim()||c.length>200)||new Set(r.choices.map(c=>c.trim().toLowerCase())).size!==r.choices.length||r.kind!=='clarify'&&r.choices.length>0))throw new Error('Invalid clarification choices.');
 if(r.kind==='clarify'||r.kind==='unsupported'){if(r.text||!r.question.trim())throw new Error('Missing clarification.');}
 else if(r.question||(r.kind==='insert'&&(mode!=='dictate'||start!==end||!r.text.trim())))throw new Error('Invalid insertion scope.');
 return r;
}
export function isCurrent(a:{uri:string;version:number},b:{uri:string;version:number}):boolean{return a.uri===b.uri&&a.version===b.version;}
export class FrameReader {
 private pending=Buffer.alloc(0);
 push(bytes:Buffer):Frame[]{
  this.pending=Buffer.concat([this.pending,bytes]);const frames:Frame[]=[];
  for(;;){const end=this.pending.indexOf(10);if(end<0){if(this.pending.length>1048576)throw new Error('Editor frame too large.');break;}
   if(end>1048576)throw new Error('Editor frame too large.');
   const decoder=new TextDecoder('utf-8',{fatal:true});const frame=JSON.parse(decoder.decode(this.pending.subarray(0,end)));
   if(!frame||typeof frame.type!=='string'||frame.version!==undefined&&frame.version!==9)throw new Error('Incompatible editor frame.');
   frames.push(frame);this.pending=this.pending.subarray(end+1);
  }return frames;
 }
}

export type EditorShortcut='document'|'comment'|'uncomment'|'explain'|'format';
export function editorShortcut(text:string):EditorShortcut|undefined{
 const command=text.trim().replace(/[.!?]+$/,'').trimEnd().toLowerCase();
 return command==='document'||command==='comment'||command==='uncomment'||command==='explain'||command==='format'?command:undefined;
}
/** The snapshot here is the selection at request start, never a later selection. */
export function shortcutTarget(initial:Snapshot,shortcut:EditorShortcut,scope:string):{snapshot:Snapshot;context:Context}|undefined{
 if(initial.start===initial.end){
  if(shortcut==='comment'||shortcut==='uncomment')return undefined;
  if(shortcut==='format')return captureTarget(initial,'nearby','dictate');
  return captureTarget({...initial,start:0,end:0},'document','dictate');
 }
 return captureTarget(initial,shortcut==='comment'||shortcut==='uncomment'||shortcut==='format'?'selection':scope,'dictate');
}

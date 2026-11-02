import * as vscode from 'vscode';
import {randomUUID} from 'node:crypto';
import * as path from 'node:path';
import * as fs from 'node:fs/promises';
import {Bridge} from './bridge';
import {VoicePanel} from './panel';
import {PanelIntent} from './panelModel';
import {captureTarget,Context,Frame,isCurrent,Result,Snapshot,validateResult} from './model';

export interface Channel {connect():Promise<void>;send(frame:Frame):void;dispose():void}
interface Session {id:string;mode:string;snapshot:Snapshot;context:Context;editor:vscode.TextEditor;range:vscode.Range;state:string;result?:Result;model?:string;originalUri?:vscode.Uri;proposedUri?:vscode.Uri;rounds:number;timer:NodeJS.Timeout;applying:boolean;readyTimer?:NodeJS.Timeout;applyReady?:(ready:boolean)=>void}
export class VoiceEditorController implements vscode.Disposable,vscode.TextDocumentContentProvider {
 private panel?:VoicePanel;private workspace:Frame={type:'workspace',state:'idle'};private source?:vscode.TextEditor;private askContext?:Context;
 private panelEpoch=0;private pendingCopy?:{id:string;host?:string;client?:string;conversation:string;turn:string;text:string;sequence:number;timer:NodeJS.Timeout};
 private session?:Session; private starting=false; private disposed=false;
 private readonly channel:Channel; private readonly disposables:vscode.Disposable[]=[];
 private readonly documents=new Map<string,string>();
 private readonly status=vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right,90);
 constructor(factory?:(receive:(frame:Frame)=>void,disconnected:()=>void)=>Channel, private readonly focused:()=>boolean=()=>vscode.window.state.focused,
  private readonly clipboard:(text:string)=>PromiseLike<void>=text=>vscode.env.clipboard.writeText(text),
  private readonly commit:(editor:vscode.TextEditor,range:vscode.Range,text:string)=>PromiseLike<boolean>=(editor,range,text)=>editor.edit(edit=>edit.replace(range,text),{undoStopBefore:true,undoStopAfter:true})) {
  this.channel=(factory??((receive,disconnected)=>new Bridge(receive,disconnected)))(frame=>{void this.receive(frame).catch(e=>this.fail(e));},()=>{this.panelEpoch++;this.clearCopy();const committing=this.session?.applying;this.workspace={type:'workspace',state:'idle',message:committing?'Edit Outcome Unknown · Check the document before retrying':'Disconnected · Open SharpVoice to reconnect'};this.panel?.update(this.workspace);if(this.session){this.clear();void vscode.window.showWarningMessage(committing?'SharpVoice disconnected during Apply. Check the document before retrying.':'SharpVoice disconnected. The document was left unchanged.');}});
  this.disposables.push(vscode.workspace.registerTextDocumentContentProvider('sharpvoice-preview',this),this.status,
   vscode.workspace.onDidChangeTextDocument(e=>{if(this.askContext&&e.document.uri.toString()===this.askContext.uri&&e.document.version!==this.askContext.version){try{this.channel.send({type:'workspace_cancel'});}catch{}this.askContext=undefined;}const s=this.session;if(s&&!s.applying&&e.document.uri.toString()===s.snapshot.uri&&e.document.version!==s.snapshot.version)this.cancel('Document changed; retry the voice edit.');}),
   vscode.workspace.onDidCloseTextDocument(d=>{const s=this.session;if(s&&(d.uri.toString()===s.snapshot.uri||d.uri.toString()===s.proposedUri?.toString()))this.cancel();}),
   vscode.window.onDidChangeWindowState(state=>{if(!this.focused())this.hidePanel();}),
   vscode.window.onDidChangeActiveTextEditor(editor=>{if(editor&&['file','untitled'].includes(editor.document.uri.scheme))this.source=editor;const s=this.session;if(!s){this.displayIdle();if(editor&&this.panel?.visible&&['code','editor_ask'].includes(this.workspace.conversation?.scope.domain??'')){if(this.workspace.state==='idle')void this.syncDocument().catch(e=>this.fail(e));else this.hidePanel();}return;}if(s.applying)return;if(editor&&editor.document.uri.toString()!==s.snapshot.uri&&editor.document.uri.toString()!==s.originalUri?.toString()&&editor.document.uri.toString()!==s.proposedUri?.toString())this.cancel('Editor changed; voice edit cancelled.');}),
   vscode.window.tabGroups.onDidChangeTabs(()=>{const s=this.session;if(s?.state==='review'&&s.proposedUri&&!vscode.window.tabGroups.all.some(g=>g.tabs.some(t=>t.input instanceof vscode.TabInputTextDiff&&t.input.modified.toString()===s.proposedUri!.toString())))this.cancel();})
  );
  this.source=vscode.window.activeTextEditor;this.displayIdle();
 }
 attachPanel(panel:VoicePanel):void{this.panel=panel;}
 private workspaceKey():string{return (vscode.workspace.workspaceFolders?.map(f=>f.uri.toString()).sort().join('|')??'local-window').slice(0,8192);}
 async sync():Promise<void>{await this.channel.connect();this.channel.send({type:'workspace_sync',workspace:this.workspaceKey()});}
 async open():Promise<void>{await this.panel?.show();await this.sync();}
 hidePanel():void{this.panelEpoch++;this.clearCopy();if(this.session?.applying)return;if(this.session)this.cancel();else {try{this.channel.send({type:'workspace_cancel'});}catch{}}}
 private async fileContext():Promise<Context>{
  const editor=vscode.window.activeTextEditor??this.source;this.eligible(editor);const doc=editor.document;
  const snapshot:Snapshot={uri:doc.uri.toString(),version:doc.version,language:doc.languageId,name:path.basename(doc.fileName),text:doc.getText(),start:doc.offsetAt(editor.selection.start),end:doc.offsetAt(editor.selection.end),diagnostics:'',indent:editor.options.insertSpaces===false?'\t':' '.repeat(Math.min(16,Number(editor.options.tabSize)||4)),eol:doc.eol===vscode.EndOfLine.CRLF?'\r\n':'\n'};
  return captureTarget(snapshot,vscode.workspace.getConfiguration('sharpvoice').get<string>('context','nearby'),'dictate').context;
 }
 private async syncDocument():Promise<void>{
  if(this.session||!this.focused()||this.workspace.state!=='idle')return;const context=await this.fileContext();
  await this.channel.connect();if(this.session)return;
  this.channel.send({type:'workspace_mode',workspace:this.workspaceKey(),mode:this.workspace.mode??'ask',include_file:this.workspace.include_file,context});
 }
 private clearCopy():void {if(this.pendingCopy)clearTimeout(this.pendingCopy.timer);this.pendingCopy=undefined;}
 async copyAnswer():Promise<void>{
  const turn=this.workspace.conversation?.turns.filter(t=>t.response).at(-1);const conversation=this.workspace.conversation?.id;if(!turn||!conversation)return;
  this.clearCopy();const id=randomUUID();const copy={id,host:this.workspace.host_id,client:this.workspace.client_id,conversation,turn:turn.id,text:'',sequence:0,timer:setTimeout(()=>this.clearCopy(),10000)};this.pendingCopy=copy;
  try{this.channel.send({type:'workspace_copy',id,workspace:this.workspaceKey(),conversation_id:conversation,turn_id:turn.id});}catch(e){this.clearCopy();throw e;}
 }
 async panelIntent(intent:PanelIntent):Promise<void>{
  const epoch=this.panelEpoch;
  if(intent.action==='move'){await this.panel?.move();return;}
  if(intent.action==='copy'){await this.copyAnswer();return;}
  if(intent.action==='review'){const s=this.session;if(s?.state==='review')await vscode.commands.executeCommand('vscode.diff',s.originalUri,s.proposedUri,'SharpVoice · Review Changes',{preview:true});return;}
  if(intent.action==='apply'){await this.apply();return;}
  if(intent.action==='cancel'){if(this.session)this.cancel();else this.channel.send({type:'workspace_cancel'});return;}
  if(intent.action==='stop'){if(this.session)this.stop();else this.channel.send({type:'workspace_stop'});return;}
  if(this.session?.state==='clarify'&&['reply','start','send'].includes(intent.action)){await this.clarify(intent.text);return;}
  if(['start','send'].includes(intent.action)&&intent.mode!=='ask') {await this.start(intent.mode??'dictate',intent.text,epoch);return;}
  await this.channel.connect();if(epoch!==this.panelEpoch||this.disposed)return;
  if(['mode','new','select','delete','clear'].includes(intent.action)){
   if(this.session)this.cancel();
   const context=(intent.mode!==undefined&&intent.mode!=='ask')||intent.includeFile?await this.fileContext():undefined;
   if(epoch!==this.panelEpoch||this.disposed)return;this.channel.send({type:'workspace_'+intent.action,workspace:this.workspaceKey(),mode:intent.mode,context,include_file:intent.includeFile,conversation_id:intent.id});return;
  }
  const context=intent.includeFile?await this.fileContext():undefined;if(epoch!==this.panelEpoch||this.disposed)return;this.askContext=context;
  this.channel.send({type:intent.action==='send'?'workspace_ask':'workspace_voice',workspace:this.workspaceKey(),instruction:intent.text,context,include_file:intent.includeFile});
 }
 provideTextDocumentContent(uri:vscode.Uri):string{return this.documents.get(uri.toString())??'';}
 get controlLabel():string{return this.status.text;}
 get controlCommand():string|vscode.Command|undefined{return this.status.command;}
 private displayIdle():void{
  if(this.session)return;
  const editor=vscode.window.activeTextEditor;
  if(this.disposed||vscode.env.remoteName||!vscode.workspace.isTrusted){this.status.hide();return;}
  this.status.text='$(mic) SharpVoice';this.status.command='sharpvoice.open';this.status.tooltip='Open the SharpVoice conversation panel.';this.status.show();
 }
 get pendingId():string|undefined{return this.session?.id;}
 get pendingState():string|undefined{return this.session?.state;}
 private contexts():void{const s=this.session;for(const [key,value]of Object.entries({sessionActive:!!s,recording:s?.state==='arming'||s?.state==='recording',canApply:s?.state==='review',needsClarification:s?.state==='clarify'}))void vscode.commands.executeCommand('setContext','sharpvoice.'+key,value);}
 private display(text:string,command='sharpvoice.cancel'):void{this.contexts();this.status.text='$(mic) '+text;this.status.command=command;this.status.tooltip='SharpVoice · '+text;this.status.show();this.panel?.update({...this.workspace,type:'workspace',sequence:undefined,state:this.session?.state??'idle',message:text,result:this.session?.result});}
 private eligible(editor:vscode.TextEditor|undefined):asserts editor is vscode.TextEditor {
  if(process.platform!=='win32'||vscode.env.remoteName||!vscode.workspace.isTrusted||!this.focused()||!editor||editor.selections.length!==1||!['file','untitled'].includes(editor.document.uri.scheme)||vscode.workspace.fs.isWritableFileSystem(editor.document.uri.scheme)===false)throw new Error('Use one cursor in a local editable file in trusted Windows VS Code.');
 }
 async start(mode:string,instruction?:string,panelEpoch?:number):Promise<void>{
  const pending=()=>this.disposed||panelEpoch!==undefined&&panelEpoch!==this.panelEpoch;
  if(this.starting)return;
  if(this.session){if(['arming','recording'].includes(this.session.state)&&this.session.mode===mode){this.stop();return;}throw new Error('Apply or cancel the current voice operation first.');}
  this.starting=true;
  try{
   let editor=vscode.window.activeTextEditor??this.source;this.eligible(editor);
   editor=await vscode.window.showTextDocument(editor.document,{preview:false,preserveFocus:false,viewColumn:editor.viewColumn,selection:editor.selection});if(pending())return;this.eligible(editor);const document=editor.document;const version=document.version;
   if(document.uri.scheme==='file'){const file=await fs.open(document.uri.fsPath,'r+');await file.close();if(pending())return;}
   let range: vscode.Range=editor.selection;
   this.eligible(vscode.window.activeTextEditor);if(vscode.window.activeTextEditor!==editor||document.version!==version)throw new Error('Document changed while choosing the target. Try again.');
   const text=document.getText();if(text.length>2_000_000)throw new Error('This first version supports documents up to 2 million characters.');
   let snapshot:Snapshot={uri:document.uri.toString(),version,language:document.languageId,name:path.basename(document.fileName),text,start:document.offsetAt(range.start),end:document.offsetAt(range.end),diagnostics:vscode.languages.getDiagnostics(document.uri).filter(d=>!!d.range.intersection(range)).map(d=>`${d.severity}: ${d.message}`).join('\n'),indent:editor.options.insertSpaces===false?'\t':' '.repeat(Math.min(16,Number(editor.options.tabSize)||4)),eol:document.eol===vscode.EndOfLine.CRLF?'\r\n':'\n'};
   const target=captureTarget(snapshot,vscode.workspace.getConfiguration('sharpvoice').get<string>('context','nearby'),mode);
   snapshot=target.snapshot;const context=target.context;range=new vscode.Range(document.positionAt(snapshot.start),document.positionAt(snapshot.end));
   await this.channel.connect();if(pending())return;this.eligible(vscode.window.activeTextEditor);
   if(vscode.window.activeTextEditor!==editor||document.version!==version)throw new Error('Editor changed while connecting. Try again.');
   if(this.disposed)return;
   const id=randomUUID();const timer=setTimeout(()=>this.cancel('Voice edit expired. Start a new request.'),300000);
   this.session={id,mode,snapshot,context,editor,range,state:instruction?'processing':'arming',rounds:0,timer,applying:false};
   if(!instruction)this.awaitReady(this.session);this.channel.send({type:'start',id,mode,context,instruction,workspace:this.workspaceKey()});this.display(instruction?'Working · Click to Cancel':'Starting Microphone · Click to Cancel','sharpvoice.cancel');
  }finally{this.starting=false;}
 }
 private awaitReady(s:Session):void{clearTimeout(s.readyTimer);s.readyTimer=setTimeout(()=>{if(this.session===s&&s.state==='arming')this.fail(new Error('SharpVoice did not confirm microphone readiness. Check the desktop app, then retry.'));},15000);}
 stop():void{const s=this.session;if(s?.state==='arming'){this.cancel();return;}if(s?.state!=='recording')return;s.state='processing';this.channel.send({type:'stop',id:s.id});this.display('Working · Click to Cancel');}
 cancel(message?:string):void{this.panelEpoch++;const s=this.session;if(s?.applying)return;if(!s)return;try{this.channel.send({type:'cancel',id:s.id});}catch{}this.clear();if(message)void vscode.window.showWarningMessage(message);}
 private current(s:Session,review=false):boolean{
  const document=s.editor.document;
  if(this.session!==s||document.isClosed||!this.focused()||!isCurrent(s.snapshot,{uri:document.uri.toString(),version:document.version})||document.getText(s.range)!==s.context.source_text)return false;
  const active=vscode.window.activeTextEditor;
  return active===s.editor||review&&!!active&&[s.proposedUri?.toString(),s.originalUri?.toString()].includes(active.document.uri.toString());
 }
 async receive(frame:Frame):Promise<void>{
  if(frame.type==='answer_copy'){
   const copy=this.pendingCopy;if(!copy||frame.id!==copy.id||frame.host_id!==copy.host||frame.client_id!==copy.client||frame.conversation_id!==copy.conversation||frame.turn_id!==copy.turn||frame.sequence!==copy.sequence+1||typeof frame.message!=='string'||frame.message.length>32000)return;
   copy.text+=frame.message;copy.sequence++;if(copy.text.length>200000){this.clearCopy();return;}if(frame.state==='complete'){this.clearCopy();await this.clipboard(copy.text);}return;
  }
  if(frame.type==='hello'){this.clearCopy();this.workspace={type:'workspace',host_id:frame.host_id,client_id:frame.client_id,state:'idle',message:'Ready'};this.panel?.update(this.workspace);return;}
  if(frame.type==='workspace'||frame.type==='draft'){if(frame.type==='workspace'){this.workspace=frame;if(!['arming','recording','processing'].includes(frame.state??''))this.askContext=undefined;if(frame.state==='idle'&&!this.session&&this.panel?.visible&&this.source&&frame.context&&this.source.document.uri.toString()!==frame.context.uri&&['code','editor_ask'].includes(frame.conversation?.scope.domain??''))void this.syncDocument().catch(e=>this.fail(e));}this.panel?.update(frame);return;}
  if(frame.type==='error'&&!this.session){this.workspace={...this.workspace,type:'workspace',sequence:undefined,state:'idle',message:frame.message??'Request failed'};this.panel?.update(this.workspace);return;}
  const s=this.session;if(!s||frame.id!==s.id)return;
  if(frame.type==='apply_ready'){s.applyReady?.(true);return;}
  if(frame.type==='error'){throw new Error(frame.message??'SharpVoice code request failed.');}
  if(frame.type==='cancelled'){this.clear();return;}
  if(frame.type==='validate'){
   if(this.panel&&this.focused()&&!s.editor.document.isClosed&&isCurrent(s.snapshot,{uri:s.editor.document.uri.toString(),version:s.editor.document.version}))s.editor=await vscode.window.showTextDocument(s.editor.document,{preserveFocus:false,preview:false,viewColumn:s.editor.viewColumn});
   if(!['recording','processing'].includes(s.state)||!this.current(s)){this.cancel('The original document or focus changed. Try again.');return;}
   this.eligible(vscode.window.activeTextEditor);
   const selection=s.editor.selection;
   // A deliberate highlight made while speaking takes priority before generation.
   // An empty moved cursor does not silently retarget the original insertion point.
   if(!selection.isEmpty){
    const target=captureTarget({...s.snapshot,start:s.editor.document.offsetAt(selection.start),end:s.editor.document.offsetAt(selection.end)},vscode.workspace.getConfiguration('sharpvoice').get<string>('context','nearby'),s.mode);
    s.snapshot=target.snapshot;s.context=target.context;s.range=new vscode.Range(s.editor.document.positionAt(s.snapshot.start),s.editor.document.positionAt(s.snapshot.end));
   }
   this.channel.send({type:'validated',id:s.id,message:frame.message,context:s.context});return;
  }
  if(frame.type==='status'){
   if(frame.message==='recording'&&s.state==='arming'){clearTimeout(s.readyTimer);s.state='recording';this.display('Recording · Click to Stop','sharpvoice.stop');}
   else if(frame.message==='processing'&&['arming','recording','processing'].includes(s.state)){clearTimeout(s.readyTimer);s.state='processing';this.display('Working · Click to Cancel');}
   return;
  }
  if(frame.type!=='result')return;
  if(s.state!=='processing'&&s.state!=='recording')return;
  clearTimeout(s.readyTimer);
  if(this.panel&&this.focused()&&!s.editor.document.isClosed&&isCurrent(s.snapshot,{uri:s.editor.document.uri.toString(),version:s.editor.document.version}))s.editor=await vscode.window.showTextDocument(s.editor.document,{preserveFocus:false,preview:false,viewColumn:s.editor.viewColumn});
  if(!this.current(s)){this.cancel('The original document or focus changed. Try again.');return;}
  const result=validateResult(frame.result,s.mode,s.context.start,s.context.end);s.result=result;s.model=frame.model;
  if(result.kind==='unsupported'){this.clear();void vscode.window.showWarningMessage(result.question);return;}
  if(result.kind==='clarify'){
   s.state='clarify';this.display('Needs Clarification','sharpvoice.clarify');
   if(this.panel){await this.panel.show();return;}
   const action=await vscode.window.showInformationMessage(result.question,'Answer by Voice','Type Answer','Cancel');
   if(this.session!==s)return;
   if(action==='Answer by Voice')await this.clarify();else if(action==='Type Answer'){
    const answer=await vscode.window.showInputBox({title:'Clarify Voice Edit',prompt:result.question,ignoreFocusOut:true});
    if(this.session===s&&answer?.trim())await this.clarify(answer);else if(this.session===s&&answer===undefined)this.cancel();
   }else if(action==='Cancel')this.cancel();return;
  }
  s.state='opening';
  s.originalUri=vscode.Uri.parse(`sharpvoice-preview:/${s.id}/Original.${s.snapshot.name.split('.').pop()??'txt'}`);
  s.proposedUri=vscode.Uri.parse(`sharpvoice-preview:/${s.id}/Proposed.${s.snapshot.name.split('.').pop()??'txt'}`);
  this.documents.set(s.originalUri.toString(),s.snapshot.text);
  this.documents.set(s.proposedUri.toString(),s.snapshot.text.slice(0,s.context.start)+result.text+s.snapshot.text.slice(s.context.end));
  await vscode.commands.executeCommand('vscode.diff',s.originalUri,s.proposedUri,`SharpVoice · ${s.snapshot.name} · ${s.model??'Selected Model'}`,{preview:true});
  if(this.session!==s)return;s.state='review';this.display('Review · Click to Apply','sharpvoice.apply');
  if(this.panel)return;
  const action=await vscode.window.showInformationMessage('Review the voice edit. The source document is unchanged.','Apply','Cancel');
  if(this.session!==s)return;if(action==='Apply')await this.apply();else if(action==='Cancel')this.cancel();
 }
 async apply():Promise<void>{
  const s=this.session;if(!s||s.state!=='review'||!s.result)return;s.state='preparing_apply';
  if(this.panel&&this.focused()&&!s.editor.document.isClosed&&isCurrent(s.snapshot,{uri:s.editor.document.uri.toString(),version:s.editor.document.version}))s.editor=await vscode.window.showTextDocument(s.editor.document,{preserveFocus:false,preview:false,viewColumn:s.editor.viewColumn});
  if(this.session!==s||s.state!=='preparing_apply')return;
  if(!this.current(s,true)){this.cancel('Document changed or the original editor is no longer active. Retry the voice edit.');return;}
  s.state='applying';s.applying=true;this.display('Applying Approved Edit…');
  try{
   const ready=new Promise<boolean>(resolve=>{const timer=setTimeout(()=>resolve(false),5000);s.applyReady=value=>{clearTimeout(timer);resolve(value);};});
   this.channel.send({type:'applying',id:s.id});
   if(!await ready||this.session!==s)throw new Error('Apply confirmation was lost. Check the document before retrying.');
   const source=await vscode.window.showTextDocument(s.editor.document,{preview:false,preserveFocus:false,viewColumn:s.editor.viewColumn});
   s.editor=source;
   if(!this.current(s))throw new Error('The source changed before Apply. Retry the voice edit.');
   const success=await this.commit(source,s.range,s.result!.text);
   if(!success)throw new Error('VS Code rejected the edit. Nothing was applied.');
   if(this.session!==s)return;
   this.channel.send({type:'applied',id:s.id});this.clear();void vscode.window.showInformationMessage('SharpVoice edit applied. Undo restores the previous text.');
  }catch(e){if(this.session===s){s.applying=false;try{this.channel.send({type:'apply_failed',id:s.id});}catch{}this.fail(e);}}
 }
 async clarify(answer?:string):Promise<void>{
  const s=this.session;if(!s||s.state!=='clarify')return;
  if(!this.focused()||s.editor.document.isClosed||!isCurrent(s.snapshot,{uri:s.editor.document.uri.toString(),version:s.editor.document.version})){this.cancel('Clarification expired or document changed. Start again.');return;}
  if(answer&&answer.length>4000)throw new Error('Clarification is too long.');
  const selection=s.editor.selection;
  const target=captureTarget({...s.snapshot,start:s.editor.document.offsetAt(selection.start),end:s.editor.document.offsetAt(selection.end)},vscode.workspace.getConfiguration('sharpvoice').get<string>('context','nearby'),s.mode);
  const range=new vscode.Range(s.editor.document.positionAt(target.snapshot.start),s.editor.document.positionAt(target.snapshot.end));
  s.state='answering';this.display('Returning to Source · Click to Cancel','sharpvoice.cancel');
  // Notification actions can temporarily take editor focus. The explicit answer restores its unchanged source.
  const source=await vscode.window.showTextDocument(s.editor.document,{preview:false,preserveFocus:false,viewColumn:s.editor.viewColumn});
  if(this.session!==s)return;s.editor=source;
  if(!this.current(s)||++s.rounds>3){this.cancel('Clarification expired or document changed. Start again.');return;}
  s.snapshot=target.snapshot;s.context=target.context;s.range=range;
  s.state=answer?'processing':'arming';
  if(!answer)this.awaitReady(s);
  this.channel.send({type:'clarify',id:s.id,instruction:answer,context:s.context});this.display(answer?'Working · Click to Cancel':'Starting Microphone · Click to Cancel','sharpvoice.cancel');
 }
 private clear():void{const s=this.session;this.session=undefined;if(s){s.applyReady?.(false);clearTimeout(s.timer);clearTimeout(s.readyTimer);this.documents.delete(s.originalUri?.toString()??'');this.documents.delete(s.proposedUri?.toString()??'');const tabs=vscode.window.tabGroups.all.flatMap(g=>g.tabs).filter(t=>t.input instanceof vscode.TabInputTextDiff&&t.input.modified.toString()===s.proposedUri?.toString());if(tabs.length)void vscode.window.tabGroups.close(tabs,true);}this.contexts();this.displayIdle();}
 fail(error:unknown):void{this.cancel();this.channel.dispose();void vscode.window.showErrorMessage(error instanceof Error?error.message:'SharpVoice request failed.');}
 dispose():void{this.disposed=true;this.panelEpoch++;this.clearCopy();this.cancel();this.channel.dispose();for(const d of this.disposables)d.dispose();}
}
export function activate(context:vscode.ExtensionContext):void{
 const controller=new VoiceEditorController();const panel=new VoicePanel(context.extensionUri,intent=>controller.panelIntent(intent),()=>controller.hidePanel(),()=>controller.sync());controller.attachPanel(panel);context.subscriptions.push(controller,panel,vscode.window.registerWebviewViewProvider('sharpvoice.workspace',panel));
 const command=(name:string,action:()=>unknown)=>context.subscriptions.push(vscode.commands.registerCommand('sharpvoice.'+name,()=>Promise.resolve().then(action).catch(e=>controller.fail(e))));
 command('open',()=>controller.open());command('movePanel',()=>controller.panelIntent({action:'move'}));
 command('dictateCode',()=>controller.start('dictate'));command('editCode',()=>controller.start('edit'));command('stop',()=>controller.stop());command('cancel',()=>controller.cancel());command('apply',()=>controller.apply());command('clarify',()=>controller.clarify());
}

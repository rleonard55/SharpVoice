import * as vscode from 'vscode';
import {randomUUID} from 'node:crypto';
import * as path from 'node:path';
import * as fs from 'node:fs/promises';
import {Bridge} from './bridge';
import {IdeTools} from './ideTools';
import {ProjectContext,RelatedSource} from './projectContext';
import {candidateRejection,shortcutRejection,uncommentCandidate} from './languagePolicy';
import {EditorReview} from './editorReview';
import {VoicePanel} from './panel';
import {PanelIntent} from './panelModel';
import {captureTarget,editorShortcut,shortcutTarget,EditorShortcut,Context,Frame,isCurrent,Result,Snapshot,validateResult} from './model';

export interface Channel {connect():Promise<void>;send(frame:Frame):void;dispose():void}
interface RetainedContext {related_sources:RelatedSource[];project_guidance:string;guidance_files:string[];versions?:Map<string,number>}
interface Session {retainedContext?:RetainedContext;id:string;mode:string;snapshot:Snapshot;initial:Snapshot;shortcut?:EditorShortcut;action?:string;context:Context;editor:vscode.TextEditor;range:vscode.Range;state:string;result?:Result;model?:string;rounds:number;timer:NodeJS.Timeout;applying:boolean;readyTimer?:NodeJS.Timeout;beforeProblems?:string;relatedVersions?:Map<string,number>;applyReady?:(ready:boolean)=>void}
export class VoiceEditorController implements vscode.Disposable,vscode.TextDocumentContentProvider {
 private readonly reviews:EditorReview;
 private panel?:VoicePanel;private workspace:Frame={type:'workspace',state:'idle'};private source?:vscode.TextEditor;private askContext?:Context;
 private panelEpoch=0;private pendingCopy?:{id:string;host?:string;client?:string;conversation:string;turn:string;text:string;sequence:number;timer:NodeJS.Timeout};
 private hotkeyRequest?:string;private hotkeyMode?:string;private askStarted=false;private askReadyTimer?:NodeJS.Timeout;private hotkeyListening=false;private hotkeyConnected=false;private hotkeyConnecting=false;private hotkeyTimer?:NodeJS.Timeout;
 private session?:Session; private starting=false; private disposed=false;
 private readonly channel:Channel; private readonly disposables:vscode.Disposable[]=[];
 private readonly documents=new Map<string,string>();
 private readonly status=vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right,90);
 constructor(factory?:(receive:(frame:Frame)=>void,disconnected:()=>void)=>Channel, private readonly focused:()=>boolean=()=>vscode.window.state.focused,
  private readonly clipboard:(text:string)=>PromiseLike<void>=text=>vscode.env.clipboard.writeText(text),
  private readonly commit:(editor:vscode.TextEditor,range:vscode.Range,text:string)=>PromiseLike<boolean>=(editor,range,text)=>editor.edit(edit=>edit.replace(range,text),{undoStopBefore:true,undoStopAfter:true})) {
  this.reviews=new EditorReview(()=>this.refreshReview());
  this.disposables.push(this.reviews);
  this.channel=(factory??((receive,disconnected)=>new Bridge(receive,disconnected)))(frame=>{void this.receive(frame).catch(e=>this.fail(e));},()=>{this.hotkeyConnected=false;this.hotkeyRequest=undefined;this.hotkeyMode=undefined;clearTimeout(this.askReadyTimer);this.panelEpoch++;this.clearCopy();if(this.queue){this.queue.cancelled=true;if(!this.session?.applying)this.queue.reject?.(new Error('Disconnected'));}const committing=this.session?.applying;this.workspace={type:'workspace',state:'idle',message:committing?'Edit Outcome Unknown · Check the document before retrying':'Disconnected · Open SharpVoice to reconnect'};this.publish(this.workspace);if(this.session){this.clear();void vscode.window.showWarningMessage(committing?'SharpVoice disconnected during insertion. Check the working file before retrying.':'SharpVoice disconnected. Earlier pending changes remain available for review.');}});
  this.disposables.push(vscode.workspace.registerTextDocumentContentProvider('sharpvoice-preview',this),this.status,
   vscode.workspace.onDidChangeTextDocument(e=>{if(this.askContext&&e.document.uri.toString()===this.askContext.uri&&e.document.version!==this.askContext.version){try{this.channel.send({type:'workspace_cancel'});}catch{}this.askContext=undefined;}const s=this.session;if(s&&!s.applying&&(e.document.uri.toString()===s.snapshot.uri&&e.document.version!==s.snapshot.version||s.relatedVersions?.has(e.document.uri.toString())))this.cancel('Document changed; retry the voice edit.');}),
   vscode.workspace.onDidCloseTextDocument(d=>{if(this.askContext?.uri===d.uri.toString()){if(this.hotkeyMode==='ask')this.cancel();else{try{this.channel.send({type:'workspace_cancel'});}catch{}}this.askContext=undefined;}const s=this.session;if(s&&(d.uri.toString()===s.snapshot.uri||s.relatedVersions?.has(d.uri.toString())))this.cancel();}),
   vscode.window.onDidChangeWindowState(state=>{
    if(!this.focused()){this.hotkeyRequest=undefined;this.hidePanel();if(this.hotkeyListening&&!this.session?.applying){this.hotkeyConnected=false;this.channel.dispose();}}
    else if(this.hotkeyListening)void this.connectHotkeys();
   }),
   vscode.window.onDidChangeActiveTextEditor(editor=>{if(editor&&['file','untitled'].includes(editor.document.uri.scheme))this.source=editor;const s=this.session;if(!s){if(this.queue?.waiting)this.cancel('Editor changed during coordinated edits.');this.refreshReview();if(editor&&this.panel?.visible&&['code','editor_ask'].includes(this.workspace.conversation?.scope.domain??'')){if(this.workspace.state==='idle')void this.syncDocument().catch(e=>this.fail(e));else this.hidePanel();}return;}if(s.applying)return;if(editor&&editor.document.uri.toString()!==s.snapshot.uri)this.cancel('Editor changed; voice edit cancelled.');}),

  );
  this.source=vscode.window.activeTextEditor;this.displayIdle();
 }
 listenForHotkeys():void{
  if(this.hotkeyListening)return;this.hotkeyListening=true;
  this.hotkeyTimer=setInterval(()=>{void this.connectHotkeys();},1500);this.hotkeyTimer.unref();void this.connectHotkeys();
 }
 private async connectHotkeys():Promise<void>{
  if(this.disposed||this.hotkeyConnected||this.hotkeyConnecting||!this.focused()||process.platform!=='win32'||vscode.env.remoteName||!vscode.workspace.isTrusted)return;
  this.hotkeyConnecting=true;
  try{await this.sync();if(this.disposed||!this.focused()){this.channel.dispose();return;}this.hotkeyConnected=true;}
  catch{/* The desktop may not be running yet; retry while this editor window is focused. */}
  finally{this.hotkeyConnecting=false;}
 }
 attachPanel(panel:VoicePanel):void{this.panel=panel;}
 private workspaceKey():string{return (vscode.workspace.workspaceFolders?.map(f=>f.uri.toString()).sort().join('|')??'local-window').slice(0,8192);}
 async sync():Promise<void>{await this.channel.connect();this.channel.send({type:'workspace_sync',workspace:this.workspaceKey()});}
 async open():Promise<void>{await this.panel?.show();await this.sync();}
 hidePanel():void{this.panelEpoch++;this.clearCopy();if(this.hotkeyMode==='ask'){this.hotkeyRequest=undefined;this.hotkeyMode=undefined;clearTimeout(this.askReadyTimer);}if(this.session?.applying)return;if(this.session)this.cancel();else {try{this.channel.send({type:'workspace_cancel'});}catch{}}}
 private async fileContext(scope?:string):Promise<Context>{
  const editor=this.reviewEditor();this.eligible(editor);const doc=editor.document;
  const snapshot:Snapshot={uri:doc.uri.toString(),version:doc.version,language:doc.languageId,name:path.basename(doc.fileName),text:doc.getText(),start:doc.offsetAt(editor.selection.start),end:doc.offsetAt(editor.selection.end),diagnostics:'',indent:editor.options.insertSpaces===false?'\t':' '.repeat(Math.min(16,Number(editor.options.tabSize)||4)),eol:doc.eol===vscode.EndOfLine.CRLF?'\r\n':'\n'};
  return captureTarget(snapshot,scope??vscode.workspace.getConfiguration('sharpvoice').get<string>('context','nearby'),'dictate').context;
 }
 private async askFileContext(includeFile:boolean):Promise<Context|undefined>{
  if(includeFile)return this.fileContext();
  const editor=this.reviewEditor();if(!editor||editor.document.isClosed)return undefined;
  return this.fileContext('selection');
 }
 private async syncDocument():Promise<void>{
  if(this.session||!this.focused()||this.workspace.state!=='idle')return;const context=this.workspace.mode&&this.workspace.mode!=='ask'?await this.fileContext():await this.askFileContext(this.workspace.include_file===true);
  await this.channel.connect();if(this.session)return;
  this.channel.send({type:'workspace_mode',workspace:this.workspaceKey(),mode:this.workspace.mode??'ask',include_file:this.workspace.include_file,context});
 }
 private clearCopy():void {if(this.pendingCopy)clearTimeout(this.pendingCopy.timer);this.pendingCopy=undefined;}
 async copyAnswer():Promise<void>{
  const turn=this.workspace.conversation?.turns.filter(t=>t.response).at(-1);const conversation=this.workspace.conversation?.id;if(!turn||!conversation)return;
  this.clearCopy();const id=randomUUID();const copy={id,host:this.workspace.host_id,client:this.workspace.client_id,conversation,turn:turn.id,text:'',sequence:0,timer:setTimeout(()=>this.clearCopy(),10000)};this.pendingCopy=copy;
  try{this.channel.send({type:'workspace_copy',id,workspace:this.workspaceKey(),conversation_id:conversation,turn_id:turn.id});}catch(e){this.clearCopy();throw e;}
 }
 private readonly project=new ProjectContext(()=>this.refreshReview());
 private readonly tools=new IdeTools({edit:instruction=>this.start('edit',instruction),ask:instruction=>this.askReadOnly(instruction),createTests:instruction=>this.createTests(instruction),status:message=>this.workflowStatus(message)});
 private projectFiles:string[]=[];private verification='';private extraContext?:RetainedContext;private readonly contextGuards=new WeakMap<Context,Map<string,number>>();
 private queue?:{cancelled:boolean;done:number;inserted:number;total:number;receiptTimer?:NodeJS.Timeout;waiting?:string;resolve?:()=>void;reject?:(error:Error)=>void};
 initialize(context:vscode.ExtensionContext):void{
  this.reviews.enableRecovery(context.workspaceState);
  this.disposables.push(vscode.languages.registerCodeLensProvider([{scheme:'file'},{scheme:'untitled'}],this.reviews));
  if(this.reviews.snapshot(this.reviewEditor()?.document).recovery_available)void vscode.window.showInformationMessage('SharpVoice has local pending review to recover. Source files will not be overwritten.','Resume Review','Discard Recovery').then(action=>{if(action==='Resume Review')void this.reviews.resume();else if(action==='Discard Recovery')this.reviews.discardRecovery();});
 }
 private workflowStatus(message:string):void{this.verification=message;this.workspace={...this.workspace,sequence:undefined,message};this.refreshReview();}
 private problemCounts(document:vscode.TextDocument):string{const list=vscode.languages.getDiagnostics(document.uri);return `${list.filter(d=>d.severity===0).length} errors, ${list.filter(d=>d.severity===1).length} warnings`;}
 private async augment(context:Context,document:vscode.TextDocument,retained?:RetainedContext):Promise<Context>{
  const collected=await this.project.collect(document) as RetainedContext;
  const related=retained?[...retained.related_sources,...collected.related_sources.filter(s=>!retained.related_sources.some(r=>r.name===s.name))].slice(0,4):collected.related_sources;
  const guidance=[collected.project_guidance,retained?.project_guidance].filter(Boolean).filter((s,i,a)=>a.indexOf(s)===i).join('\n\n').slice(0,8000);
  const files=[...new Set([...collected.guidance_files,...retained?.guidance_files??[]])].slice(0,40);
  this.projectFiles=[...related.map(s=>s.name),...files];
  const result={...context,related_sources:related,project_guidance:guidance,guidance_files:files};
  this.contextGuards.set(result,new Map([...collected.versions??[],...retained?.versions??[]]));return result;
 }
 private guardsCurrent(guards:Map<string,number>|undefined):boolean{return !guards||[...guards].every(([uri,version])=>(vscode.workspace.textDocuments??[]).some(d=>d.uri.toString()===uri&&!d.isClosed&&d.version===version));}
 private reviewCommand(text:string):string|undefined{
  const p=text.trim().replace(/[.!?]+$/,'').trimEnd().toLowerCase();
  const commands=({'accept this':'acceptChange','accept change':'acceptChange','revert this':'revertChange','revert change':'revertChange','next change':'nextChange','previous change':'previousChange','accept all':'accept','revert all':'revert','accept all files':'acceptWorkspace','revert all files':'revertWorkspace','explain changes':'explainChanges','edit related files':'editRelated','related files':'related','resume review':'resume','discard recovery':'discardRecovery','configure shortcuts':'shortcuts'} as Record<string,string>);return Object.hasOwn(commands,p)?commands[p]:undefined;
 }
 private isLocalInstruction(text:string):boolean{return !!this.reviewCommand(text)||this.tools.canHandle(text);}
 private async runLocal(text:string):Promise<void>{const action=this.reviewCommand(text);if(action){const editor=this.reviewEditor();const id=editor&&this.reviews.currentToken(editor);if(['acceptChange','revertChange'].includes(action)&&!id){this.workflowStatus('Place the cursor inside a pending change first.');return;}await this.panelIntent({action,id});}else await this.tools.handle(text);}
 async workflow(action:string,id?:string):Promise<void>{await this.panelIntent({action,id});}
 private async askReadOnly(instruction:string):Promise<void>{
  if(this.session)throw new Error('Finish or cancel the current request first.');
  const editor=this.reviewEditor();this.eligible(editor);const version=editor.document.version;
  let context=await this.augment(await this.fileContext(),editor.document);
  if(instruction.length>4000){context={...context,context_text:instruction.slice(0,32000)};instruction='Explain or answer the request and material supplied in context_text. This is read-only; do not change source or claim execution.';}
  await this.channel.connect();if(editor.document.version!==version||!this.focused()||this.session)throw new Error('The source changed before the explanation request.');
  this.askContext=context;this.channel.send({type:'workspace_ask',mode:'ask',instruction,context,include_file:true,workspace:this.workspaceKey()});await this.panel?.show();
 }
 private async createTests(instruction:string):Promise<void>{
  const source=this.reviewEditor();this.eligible(source);const version=source.document.version;
  if(instruction.length>32000)throw new Error('Select a smaller source for test generation (less than 32,000 characters including instructions).');
  const sourceProject=await this.project.collect(source.document);
  const uri=await vscode.window.showSaveDialog({title:'Choose a test file (existing or new)',saveLabel:'Generate Tests Here'});if(!uri)return;
  if(!this.focused()||source.document.version!==version)throw new Error('Source changed while choosing the test file.');
  if(uri.toString()===source.document.uri.toString())throw new Error('Choose a separate test file to preserve the source.');
  let document:vscode.TextDocument;
  try{document=await vscode.workspace.openTextDocument(uri);}catch{document=await vscode.workspace.openTextDocument(uri.with({scheme:'untitled'}));}
  if(document.getText().length>32000)throw new Error('Choose a test file smaller than 32,000 characters.');
  const editor=await vscode.window.showTextDocument(document,{preview:false});editor.selection=new vscode.Selection(document.positionAt(0),document.positionAt(document.getText().length));
  const parts:RelatedSource[]=[];for(let offset=0;offset<instruction.length;offset+=8000)parts.push({name:path.basename(source.document.fileName)+` (source under test, part ${parts.length+1})`,language:source.document.languageId,text:instruction.slice(offset,offset+8000)});
  this.extraContext={related_sources:[...parts,...sourceProject.related_sources].slice(0,4),project_guidance:sourceProject.project_guidance,guidance_files:sourceProject.guidance_files,versions:new Map([...sourceProject.versions??[],[source.document.uri.toString(),version]])};
  try{await this.start(document.getText()?'edit':'dictate','Generate focused tests in this target test file for the source under test in related_sources. Use the project test framework and conventions; preserve existing tests and add meaningful behavior/edge cases. Do not change the source file or claim tests ran.');}finally{this.extraContext=undefined;}
 }
 private async editRelated():Promise<void>{
  if(this.queue||this.session)throw new Error('Finish the current request first.');
  const uris=await this.project.chooseRelated();if(!uris?.length)return;
  const instruction=await vscode.window.showInputBox({title:'Edit selected related files',prompt:'Describe the coordinated change. Each file will remain pending for review.',validateInput:value=>!value.trim()||value.length>3000?'Enter 1–3,000 characters.':undefined});if(!instruction)return;
  const documents=await Promise.all(uris.map(uri=>vscode.workspace.openTextDocument(uri)));
  if(documents.some(d=>d.getText().length>32000))throw new Error('Each coordinated target must be at most 32,000 characters.');
  const versions=new Map(documents.map(d=>[d.uri.toString(),d.version]));const queue=this.queue={cancelled:false,done:0,inserted:0,total:documents.length} as NonNullable<VoiceEditorController['queue']>;
  try{
   for(const document of documents){
    if(queue.cancelled||!this.focused()||documents.some(d=>d.isClosed||d.version!==versions.get(d.uri.toString())))throw new Error('A coordinated target changed or the operation was canceled.');
    const editor=await vscode.window.showTextDocument(document,{preview:false});editor.selection=new vscode.Selection(document.positionAt(0),document.positionAt(document.getText().length));
    await this.start(document.getText()?'edit':'dictate',instruction+'\nCoordinated edit: change only this target file; related_sources contain the other selected files. Preserve unrelated behavior.');
    const id=this.pendingId;if(queue.cancelled){this.cancel();throw new Error('Canceled while starting the next file.');}if(!id)throw new Error('The coordinated request did not start.');queue.waiting=id;
    await new Promise<void>((resolve,reject)=>{const timer=setTimeout(()=>reject(new Error('Desktop receipt timed out; check the current file.')),305000);queue.resolve=()=>{clearTimeout(timer);clearTimeout(queue.receiptTimer);resolve();};queue.reject=error=>{clearTimeout(timer);clearTimeout(queue.receiptTimer);reject(error);};});
    queue.done++;versions.set(document.uri.toString(),document.version);queue.waiting=undefined;
   }
   this.workflowStatus(`Coordinated edits finished: ${queue.done}/${queue.total} files. Review each change or accept/revert all files.`);
  }catch(error){this.workflowStatus(`Coordinated edits stopped: ${queue.inserted}/${queue.total} files changed, ${queue.done} acknowledged. Earlier changes remain pending. ${error instanceof Error?error.message:String(error)}`);}
  finally{if(this.queue===queue)this.queue=undefined;this.refreshReview();}
 }
 async flushRecovery():Promise<void>{await this.reviews.flush();}
 private async startAskHotkey(id:string):Promise<void>{
  const epoch=this.panelEpoch,includeFile=this.workspace.include_file===true;
  const valid=()=>!this.disposed&&this.focused()&&this.hotkeyRequest===id&&epoch===this.panelEpoch;
  if(process.platform!=='win32'||vscode.env.remoteName||!vscode.workspace.isTrusted)throw new Error('Ask AI requires a trusted local editor window.');
  const context=await this.askFileContext(includeFile);
  await this.channel.connect();if(!valid())return;
  if(context&&!(vscode.workspace.textDocuments??[]).some(d=>d.uri.toString()===context.uri&&!d.isClosed&&d.version===context.version))throw new Error('The attached source changed before Ask AI started.');
  this.askContext=context;this.workspace={...this.workspace,mode:'ask',state:'arming',message:'Starting Ask AI · Wait for the microphone tone',include_file:includeFile,context};this.publish(this.workspace);
  await this.panel?.show();if(!valid())return;
  if(context&&!(vscode.workspace.textDocuments??[]).some(d=>d.uri.toString()===context.uri&&!d.isClosed&&d.version===context.version))throw new Error('The attached source changed before Ask AI started.');
  this.channel.send({type:'workspace_voice',id,mode:'ask',state:'hotkey',context,include_file:includeFile,workspace:this.workspaceKey()});
  clearTimeout(this.askReadyTimer);this.askReadyTimer=setTimeout(()=>{if(this.hotkeyRequest===id&&(!this.askStarted||this.workspace.state==='arming')){this.cancel();this.workflowStatus('Ask AI did not confirm microphone readiness. Retry the shortcut.');}},15000);
 }
 private chosenQuestion?:string;
 private question():{id:string;question:string;choices:string[]}|undefined{
  const s=this.session;
  if(s)return s.state==='clarify'&&s.result?.kind==='clarify'?{id:`${this.workspace.host_id??''}:${s.id}:${s.rounds}`,question:s.result.question,choices:s.result.choices??[]}:undefined;
  if(this.workspace.state!=='idle'||this.workspace.conversation?.retired||!['general','editor_ask'].includes(this.workspace.conversation?.scope.domain??''))return undefined;
  const turn=this.workspace.conversation?.turns.at(-1);
  return turn?.status==='Needs Clarification'?{id:`${this.workspace.host_id??''}:${this.workspace.conversation!.id}:${turn.id}`,question:turn.response,choices:turn.choices??[]}:undefined;
 }
 async panelIntent(intent:PanelIntent):Promise<void>{
  const epoch=this.panelEpoch;
  if(['acceptChange','revertChange','jumpChange','acceptWorkspace','revertWorkspace','explainChanges','related','editRelated','resume','discardRecovery','fix','explainError','testThis','runTests','shortcuts','nextChange','previousChange'].includes(intent.action)){
   if(this.session||this.starting||this.reviews.busy)throw new Error('Finish or cancel the current request first.');
   switch(intent.action){
    case 'acceptChange':if(intent.id)this.reviews.acceptChange(intent.id);return;
    case 'revertChange':if(intent.id)await this.reviews.revertChange(intent.id);return;
    case 'jumpChange':if(intent.id)await this.reviews.jump(intent.id);return;
    case 'acceptWorkspace':this.reviews.acceptWorkspace();return;
    case 'revertWorkspace':await this.reviews.revertWorkspace();return;
    case 'nextChange':case 'previousChange':{const editor=this.requireReviewEditor();await this.reviews.next(editor,intent.action==='nextChange'?1:-1);return;}
    case 'explainChanges':await this.askReadOnly(this.reviews.explanation());return;
    case 'related':await this.project.chooseRelated();return;
    case 'editRelated':await this.editRelated();return;
    case 'resume':await this.reviews.resume();return;
    case 'discardRecovery':this.reviews.discardRecovery();return;
    case 'fix':await this.tools.fix();return;case 'explainError':await this.tools.explainError();return;
    case 'testThis':await this.tools.testThis();return;case 'runTests':await this.tools.runTests();return;
    case 'shortcuts':await this.project.configureShortcuts();return;
   }
  }
  if(intent.action==='choice'){
   const prompt=this.question();
   if(!prompt||prompt.id!==intent.id||this.chosenQuestion===prompt.id||!Number.isInteger(intent.index)||intent.index!<0||intent.index!>=prompt.choices.length)return;
   const text=prompt.choices[intent.index!];this.chosenQuestion=prompt.id;let sent=false;
   try{
    if(this.session){await this.clarify(text);sent=this.session?.state==='processing';return;}
    const conversation=this.workspace.conversation!.id,turn=this.workspace.conversation!.turns.at(-1)!.id;
    const includeFile=this.workspace.include_file===true;
    await this.channel.connect();if(epoch!==this.panelEpoch||this.disposed||this.question()?.id!==prompt.id)return;
    const context=await this.askFileContext(includeFile);
    if(epoch!==this.panelEpoch||this.disposed||this.question()?.id!==prompt.id)return;
    this.askContext=context;this.channel.send({type:'workspace_ask',workspace:this.workspaceKey(),instruction:text,mode:'ask',context,include_file:includeFile,conversation_id:conversation,turn_id:turn});sent=true;return;
   }finally{if(!sent&&this.chosenQuestion===prompt.id)this.chosenQuestion=undefined;}
  }
  if(intent.action==='reply'&&!this.session)intent={action:'start',mode:'ask',includeFile:this.workspace.include_file===true};
  if(intent.action==='move'){await this.panel?.move();return;}
  if(intent.action==='copy'){await this.copyAnswer();return;}
  if(intent.action==='review'){await this.reviewChanges();return;}
  if(intent.action==='apply'||intent.action==='accept'){this.acceptAll();return;}
  if(intent.action==='revert'){await this.revertAll();return;}
  if(intent.action==='cancel'){if(this.session)this.cancel();else this.channel.send({type:'workspace_cancel'});return;}
  if(intent.action==='stop'){if(this.session)this.stop();else this.channel.send({type:'workspace_stop'});return;}
  if(this.session?.state==='clarify'&&['reply','start','send'].includes(intent.action)){await this.clarify(intent.text);return;}
  if(['start','send'].includes(intent.action)&&intent.mode!=='ask') {await this.start(intent.mode??'dictate',intent.text,epoch);return;}
  await this.channel.connect();if(epoch!==this.panelEpoch||this.disposed)return;
  if(['mode','new','select','delete','clear'].includes(intent.action)){
   if(this.session)this.cancel();
   const context=intent.mode!==undefined&&intent.mode!=='ask'?await this.fileContext():await this.askFileContext(intent.includeFile===true);
   if(epoch!==this.panelEpoch||this.disposed)return;this.channel.send({type:'workspace_'+intent.action,workspace:this.workspaceKey(),mode:intent.mode,context,include_file:intent.includeFile,conversation_id:intent.id});return;
  }
  const context=await this.askFileContext(intent.includeFile===true);if(epoch!==this.panelEpoch||this.disposed)return;this.askContext=context;
  this.channel.send({type:intent.action==='send'?'workspace_ask':'workspace_voice',workspace:this.workspaceKey(),instruction:intent.text,context,include_file:intent.includeFile});
 }
 provideTextDocumentContent(uri:vscode.Uri):string{return this.documents.get(uri.toString())??'';}
 get controlLabel():string{return this.status.text;}
 get controlCommand():string|vscode.Command|undefined{return this.status.command;}
 private displayIdle():void{
  if(this.session)return;
  const editor=vscode.window.activeTextEditor;
  if(this.disposed||vscode.env.remoteName||!vscode.workspace.isTrusted){this.status.hide();return;}
  const count=this.reviews.snapshot(this.reviewEditor()?.document).pending_changes??0;
  this.status.text=count?`$(mic) SharpVoice · ${count} pending`:'$(mic) SharpVoice';this.status.command='sharpvoice.open';this.status.tooltip='Open the SharpVoice conversation panel.';this.status.show();
 }
 get pendingId():string|undefined{return this.session?.id;}
 get pendingState():string|undefined{return this.session?.state;}
 private contexts():void{const s=this.session;for(const [key,value]of Object.entries({sessionActive:!!s||this.hotkeyMode==='ask'&&['arming','recording','processing'].includes(this.workspace.state??''),recording:s?.state==='arming'||s?.state==='recording'||this.workspace.state==='arming'||this.workspace.state==='recording',canApply:!s&&!this.starting&&!this.reviews.busy&&(this.reviews.snapshot(this.reviewEditor()?.document).pending_changes??0)>0,needsClarification:s?.state==='clarify'}))void vscode.commands.executeCommand('setContext','sharpvoice.'+key,value);}
 private display(text:string,command='sharpvoice.cancel'):void{this.contexts();this.status.text='$(mic) '+text;this.status.command=command;this.status.tooltip='SharpVoice · '+text;this.status.show();this.publish({...this.workspace,type:'workspace',sequence:undefined,state:this.session?.state??'idle',message:text,result:this.session?.result});}
 private eligible(editor:vscode.TextEditor|undefined):asserts editor is vscode.TextEditor {
  if(process.platform!=='win32'||vscode.env.remoteName||!vscode.workspace.isTrusted||!this.focused()||!editor||editor.document.isClosed||editor.selections.length!==1||!['file','untitled'].includes(editor.document.uri.scheme)||vscode.workspace.fs.isWritableFileSystem(editor.document.uri.scheme)===false)throw new Error('Use one cursor in a local editable file in trusted Windows VS Code.');
 }
 async start(mode:string,instruction?:string,panelEpoch?:number,hotkeyId?:string):Promise<void>{
  const pending=()=>this.disposed||panelEpoch!==undefined&&panelEpoch!==this.panelEpoch||hotkeyId!==undefined&&this.hotkeyRequest!==hotkeyId;
  if(this.starting)return;if(this.reviews.busy)throw new Error('Finish the current document change first.');
  if(this.session){if(['arming','recording'].includes(this.session.state)&&this.session.mode===mode){this.stop();return;}throw new Error('Finish or cancel the current voice request first.');}
  if(instruction&&this.isLocalInstruction(instruction)){await this.runLocal(instruction);return;}
  let customExpanded=false;const initialEditor=vscode.window.activeTextEditor??this.source;
  if(instruction&&initialEditor){const expanded=this.project.expandShortcut(instruction,initialEditor.document);if(expanded){instruction=expanded;customExpanded=true;this.workflowStatus('Custom shortcut: '+expanded.slice(0,160));}}
  if(instruction&&instruction.length>4000)throw new Error('Code instruction exceeds 4,000 characters. Shorten the custom shortcut.');
  this.starting=true;
  try{
   let editor=hotkeyId?vscode.window.activeTextEditor:vscode.window.activeTextEditor??this.source;this.eligible(editor);
   this.reviews.assertAvailable(editor.document);const initialSelection=editor.selection,version=editor.document.version;
   editor=await vscode.window.showTextDocument(editor.document,{preview:false,preserveFocus:false,viewColumn:editor.viewColumn,selection:initialSelection});if(pending())return;this.eligible(editor);const document=editor.document;
   if(document.version!==version)throw new Error('Document changed while starting the request. Try again.');
   if(document.uri.scheme==='file'){const file=await fs.open(document.uri.fsPath,'r+');await file.close();if(pending())return;}
   let range: vscode.Range=initialSelection;
   this.eligible(vscode.window.activeTextEditor);if(vscode.window.activeTextEditor!==editor||document.version!==version)throw new Error('Document changed while choosing the target. Try again.');
   const text=document.getText();if(text.length>2_000_000)throw new Error('This first version supports documents up to 2 million characters.');
   let snapshot:Snapshot={uri:document.uri.toString(),version,language:document.languageId,name:path.basename(document.fileName),text,start:document.offsetAt(range.start),end:document.offsetAt(range.end),diagnostics:vscode.languages.getDiagnostics(document.uri).filter(d=>!!d.range.intersection(range)).map(d=>`${d.severity}: ${d.message}`).join('\n'),indent:editor.options.insertSpaces===false?'\t':' '.repeat(Math.min(16,Number(editor.options.tabSize)||4)),eol:document.eol===vscode.EndOfLine.CRLF?'\r\n':'\n'};
   const initial=snapshot,shortcut=customExpanded?undefined:editorShortcut(instruction??''),scope=vscode.workspace.getConfiguration('sharpvoice').get<string>('context','nearby');
   const rejection=shortcut&&shortcutRejection(shortcut,initial.language,initial.name,initial.start!==initial.end);
   if(rejection){this.rejectEdit(rejection);return;}
   const target=shortcut?shortcutTarget(initial,shortcut,scope)??captureTarget({...initial,end:initial.start},'selection','dictate'):captureTarget(snapshot,scope,!instruction?'dictate':mode);
   snapshot=target.snapshot;const retained=this.extraContext;const context=await this.augment(target.context,document,retained);range=new vscode.Range(document.positionAt(snapshot.start),document.positionAt(snapshot.end));
   await this.channel.connect();if(pending())return;this.eligible(vscode.window.activeTextEditor);
   if(vscode.window.activeTextEditor!==editor||document.version!==version||!this.guardsCurrent(this.contextGuards.get(context)))throw new Error('Editor or related context changed while connecting. Try again.');
   if(this.disposed)return;
   const id=hotkeyId??randomUUID();const timer=setTimeout(()=>this.cancel('Voice edit expired. Start a new request.'),300000);
   this.session={id,mode,snapshot,initial,shortcut,context,editor,range,state:instruction?'processing':'arming',rounds:0,timer,applying:false,beforeProblems:this.problemCounts(document),retainedContext:retained,relatedVersions:this.contextGuards.get(context)};
   if(!instruction)this.awaitReady(this.session);this.channel.send({type:'start',id,mode,context,instruction,workspace:this.workspaceKey(),state:hotkeyId?'hotkey':customExpanded?'expanded':undefined});this.display(instruction?'Working · Click to Cancel':'Starting Microphone · Click to Cancel','sharpvoice.cancel');
  }finally{this.starting=false;this.contexts();}
 }
 private awaitReady(s:Session):void{clearTimeout(s.readyTimer);s.readyTimer=setTimeout(()=>{if(this.session===s&&s.state==='arming')this.fail(new Error('SharpVoice did not confirm microphone readiness. Check the desktop app, then retry.'));},15000);}
 stop():void{const s=this.session;if(!s&&['arming','recording'].includes(this.workspace.state??'')){this.channel.send({type:this.workspace.state==='arming'?'workspace_cancel':'workspace_stop'});return;}if(s?.state==='arming'){this.cancel();return;}if(s?.state!=='recording')return;s.state='processing';this.channel.send({type:'stop',id:s.id});this.display('Working · Click to Cancel');}
 cancel(message?:string):void{if(this.queue){this.queue.cancelled=true;if(!this.session?.applying)this.queue.reject?.(new Error(message??'Canceled'));}this.panelEpoch++;const s=this.session;if(s?.applying)return;if(!s){if(this.hotkeyMode==='ask'){this.hotkeyRequest=undefined;this.hotkeyMode=undefined;clearTimeout(this.askReadyTimer);try{this.channel.send({type:'workspace_cancel'});}catch{}}return;}try{this.channel.send({type:'cancel',id:s.id});}catch{}this.clear();if(message)void vscode.window.showWarningMessage(message);}
 private current(s:Session):boolean{
  const document=s.editor.document;
  if(!this.guardsCurrent(s.relatedVersions))return false;
  if(this.session!==s||document.isClosed||document.languageId!==s.snapshot.language||!this.focused()||!isCurrent(s.snapshot,{uri:document.uri.toString(),version:document.version})||document.getText(s.range)!==s.context.source_text)return false;
  const active=vscode.window.activeTextEditor;
  return active===s.editor;
 }
 async receive(frame:Frame):Promise<void>{
  if(frame.type==='committed'){if(this.queue&&this.queue.waiting&&this.queue.waiting===frame.id)this.queue.resolve?.();return;}
  if(frame.type==='local_command'){const s=this.session;if(!s||frame.id!==s.id||!this.current(s)||typeof frame.instruction!=='string'||!this.isLocalInstruction(frame.instruction))return;this.clear();await this.runLocal(frame.instruction);return;}
  if(frame.type==='answer_copy'){
   const copy=this.pendingCopy;if(!copy||frame.id!==copy.id||frame.host_id!==copy.host||frame.client_id!==copy.client||frame.conversation_id!==copy.conversation||frame.turn_id!==copy.turn||frame.sequence!==copy.sequence+1||typeof frame.message!=='string'||frame.message.length>32000)return;
   copy.text+=frame.message;copy.sequence++;if(copy.text.length>200000){this.clearCopy();return;}if(frame.state==='complete'){this.clearCopy();await this.clipboard(copy.text);}return;
  }
  if(frame.type==='hotkey_cancel'){
   if(frame.id===this.hotkeyRequest){this.hotkeyRequest=undefined;this.hotkeyMode=undefined;clearTimeout(this.askReadyTimer);if(this.session?.id===frame.id)this.cancel();else if(this.workspace.state==='arming'){this.workspace={...this.workspace,state:'idle',message:'Ask handoff canceled'};this.publish(this.workspace);}}return;
  }
  if(frame.type==='hotkey_start'){
   const id=frame.id;
   if(id===this.hotkeyRequest)return;
   try{
    if(typeof id!=='string'||id.length<1||id.length>128||!['dictate','ask'].includes(frame.mode??''))throw new Error('Invalid desktop voice shortcut request.');
    if(!this.focused()||this.session||this.starting||this.queue)throw new Error('Focus the editor and finish the current voice operation first.');
    this.hotkeyRequest=id;this.hotkeyMode=frame.mode;
    if(frame.mode==='ask'){this.askStarted=false;await this.startAskHotkey(id);return;}
    await this.start('dictate',undefined,undefined,id);
    if(this.pendingId===id&&this.hotkeyRequest===id)await this.panel?.show();
   }catch(e){if(this.hotkeyRequest===id){this.hotkeyRequest=undefined;this.hotkeyMode=undefined;clearTimeout(this.askReadyTimer);}if(this.pendingId===id)this.cancel();this.channel.send({type:'hotkey_error',id,message:e instanceof Error?e.message:'Editor context unavailable.'});}
   return;
  }
  if(frame.type==='hello'){this.clearCopy();this.workspace={type:'workspace',host_id:frame.host_id,client_id:frame.client_id,state:'idle',message:'Ready'};this.publish(this.workspace);return;}
  if(frame.type==='workspace'||frame.type==='draft'){if(frame.type==='workspace'){this.workspace=frame;if(this.hotkeyMode==='ask'){if(frame.turn_id===this.hotkeyRequest&&['arming','recording','processing'].includes(frame.state??''))this.askStarted=true;if(['recording','processing'].includes(frame.state??''))clearTimeout(this.askReadyTimer);if(this.askStarted&&['idle','busy'].includes(frame.state??'')&&frame.turn_id!==this.hotkeyRequest){clearTimeout(this.askReadyTimer);this.hotkeyRequest=undefined;this.hotkeyMode=undefined;this.askStarted=false;}}this.contexts();if(!['arming','recording','processing'].includes(frame.state??'')&&!(this.hotkeyMode==='ask'&&this.hotkeyRequest))this.askContext=undefined;if(frame.state==='idle'&&!this.session&&this.panel?.visible&&this.source&&frame.context&&this.source.document.uri.toString()!==frame.context.uri&&['code','editor_ask'].includes(frame.conversation?.scope.domain??''))void this.syncDocument().catch(e=>this.fail(e));}this.publish(frame);return;}
  if(frame.type==='error'&&!this.session){this.hotkeyRequest=undefined;this.hotkeyMode=undefined;clearTimeout(this.askReadyTimer);this.chosenQuestion=undefined;this.workspace={...this.workspace,type:'workspace',sequence:undefined,state:'idle',message:frame.message??'Request failed'};this.publish(this.workspace);return;}
  const s=this.session;if(!s||frame.id!==s.id)return;
  if(frame.type==='apply_ready'){s.applyReady?.(true);return;}
  if(frame.type==='error'){throw new Error(frame.message??'SharpVoice code request failed.');}
  if(frame.type==='cancelled'){if(this.queue){this.queue.cancelled=true;this.queue.reject?.(new Error(frame.message??'Desktop canceled the request'));}this.clear();return;}
  if(frame.type==='validate'){
   if(this.panel&&this.focused()&&!s.editor.document.isClosed&&isCurrent(s.snapshot,{uri:s.editor.document.uri.toString(),version:s.editor.document.version}))s.editor=await vscode.window.showTextDocument(s.editor.document,{preserveFocus:false,preview:false,viewColumn:s.editor.viewColumn});
   if(!['recording','processing'].includes(s.state)||!this.current(s)){this.cancel('The original document or focus changed. Try again.');return;}
   this.eligible(vscode.window.activeTextEditor);
   if(frame.state==='initial'&&frame.instruction&&this.isLocalInstruction(frame.instruction)){this.channel.send({type:'validated',id:s.id,message:frame.message,context:s.context,state:'local'});return;}
   const expanded=frame.state==='initial'&&frame.instruction?this.project.expandShortcut(frame.instruction,s.editor.document):undefined;
   const shortcut=frame.state==='expanded'?undefined:frame.instruction===undefined?s.shortcut:editorShortcut(frame.instruction);
   if(frame.state!=='initial'&&frame.state!=='expanded'&&frame.instruction!==undefined&&(!shortcut||s.rounds!==0||s.shortcut&&s.shortcut!==shortcut))throw new Error('Invalid editor shortcut validation.');
   if(shortcut){
    const rejection=shortcutRejection(shortcut,s.initial.language,s.initial.name,s.initial.start!==s.initial.end);
    if(rejection){this.rejectEdit(rejection);return;}
    const target=shortcutTarget(s.initial,shortcut,vscode.workspace.getConfiguration('sharpvoice').get<string>('context','nearby'));
    const scoped=target??captureTarget({...s.initial,end:s.initial.start},'selection','dictate');
    s.shortcut=shortcut;s.snapshot=scoped.snapshot;s.context=await this.augment(scoped.context,s.editor.document,s.retainedContext);
    s.range=new vscode.Range(s.editor.document.positionAt(s.snapshot.start),s.editor.document.positionAt(s.snapshot.end));
    this.channel.send({type:'validated',id:s.id,message:frame.message,context:s.context,state:target?'shortcut':'noop'});return;
   }
   const selection=s.editor.selection;
   // A deliberate highlight made while speaking takes priority before generation.
   // An empty moved cursor does not silently retarget the original insertion point.
   if(!selection.isEmpty){
    const target=captureTarget({...s.snapshot,start:s.editor.document.offsetAt(selection.start),end:s.editor.document.offsetAt(selection.end)},vscode.workspace.getConfiguration('sharpvoice').get<string>('context','nearby'),s.mode);
    s.snapshot=target.snapshot;s.context=target.context;s.range=new vscode.Range(s.editor.document.positionAt(s.snapshot.start),s.editor.document.positionAt(s.snapshot.end));
   }
   s.context=await this.augment(s.context,s.editor.document,s.retainedContext);if(!this.current(s)){this.cancel('Context changed before generation.');return;}
   this.channel.send({type:'validated',id:s.id,message:frame.message,context:s.context,instruction:expanded});return;
  }
  if(frame.type==='status'){
   if(frame.message==='recording'&&s.state==='arming'){clearTimeout(s.readyTimer);s.state='recording';this.display('Recording · Click to Stop','sharpvoice.stop');}
   else if(frame.message==='processing'&&['arming','recording','processing'].includes(s.state)){clearTimeout(s.readyTimer);s.state='processing';this.display('Working · Click to Cancel');}
   return;
  }
  if(frame.type==='answered'){
   if(s.shortcut!=='explain'||!['processing','recording'].includes(s.state))throw new Error('Unexpected explanation completion.');
   this.clear();await this.panel?.show();return;
  }
  if(frame.type==='action'){
   if(!['processing','recording'].includes(s.state))return;
   const action=frame.mode;
   if(!s.shortcut||!['comment','uncomment','format'].includes(s.shortcut)||action!==s.shortcut&&action!=='noop'||
      action==='noop'&&(s.initial.start!==s.initial.end||s.shortcut==='format')||action!=='noop'&&s.shortcut!=='format'&&s.initial.start===s.initial.end)
    throw new Error('Invalid native editor action.');
   s.action=action;s.state='preparing_apply';clearTimeout(s.readyTimer);
   await this.insertProposal(s);return;
  }
  if(frame.type!=='result')return;
  if(s.state!=='processing'&&s.state!=='recording')return;
  s.state='receiving'; // Claim the result before restoring editor focus asynchronously.
  clearTimeout(s.readyTimer);
  if(this.panel&&this.focused()&&!s.editor.document.isClosed&&isCurrent(s.snapshot,{uri:s.editor.document.uri.toString(),version:s.editor.document.version}))s.editor=await vscode.window.showTextDocument(s.editor.document,{preserveFocus:false,preview:false,viewColumn:s.editor.viewColumn});
  if(!this.current(s)){this.cancel('The original document or focus changed. Try again.');return;}
  const result=validateResult(frame.result,s.mode,s.context.start,s.context.end);s.result=result;s.model=frame.model;
  if(result.kind==='unsupported'){this.cancel();void vscode.window.showWarningMessage(result.question);return;}
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
  s.state='preparing_apply';
  await this.insertProposal(s);
 }
 /** Compatibility command: acceptance now keeps changes already in the working file. */
 async apply():Promise<void>{this.acceptAll();}
 private async insertProposal(s:Session):Promise<void>{
  if(this.session!==s||!s.result&&!s.action)return;
  if(!this.current(s)){this.cancel('Document changed or the original editor is no longer active. Retry the voice edit.');return;}
  const uncomment=s.action==='uncomment'?uncommentCandidate(s.editor.document.languageId,s.editor.document.fileName,s.snapshot.text,s.initial.start,s.initial.end):undefined;
  const rejection=s.shortcut&&shortcutRejection(s.shortcut,s.editor.document.languageId,s.editor.document.fileName,s.initial.start!==s.initial.end)||
   (uncomment!==undefined?candidateRejection(s.editor.document.languageId,s.editor.document.fileName,uncomment):!s.action&&candidateRejection(s.editor.document.languageId,s.editor.document.fileName,s.snapshot.text.slice(0,s.snapshot.start)+s.result!.text+s.snapshot.text.slice(s.snapshot.end)));
  if(rejection){this.rejectEdit(rejection);return;}
  s.state='applying';s.applying=true;this.display('Updating Working File…');
  let inserted=false;
  try{
   const ready=new Promise<boolean>(resolve=>{const timer=setTimeout(()=>resolve(false),5000);s.applyReady=value=>{clearTimeout(timer);resolve(value);};});
   this.channel.send({type:'applying',id:s.id});
   if(!await ready||this.session!==s)throw new Error('Insertion confirmation was lost. Check the document before retrying.');
   const source=await vscode.window.showTextDocument(s.editor.document,{preview:false,preserveFocus:false,viewColumn:s.editor.viewColumn});
   s.editor=source;
   if(!this.current(s))throw new Error('The source changed before insertion. Retry the voice edit.');
   if(s.action){
    if(s.action==='noop')inserted=true;
    else if(uncomment!==undefined){
     const before=source.document.getText();
     inserted=uncomment===before||await this.reviews.insert(source,new vscode.Range(source.document.positionAt(0),source.document.positionAt(before.length)),uncomment,this.commit);
    }else {
     source.selection=new vscode.Selection(s.initial.start===s.initial.end?s.range.start:source.document.positionAt(s.initial.start),source.document.positionAt(s.initial.end));
     const command=s.action==='format'?(s.initial.start===s.initial.end?'editor.action.formatDocument':'editor.action.formatSelection'):s.action==='comment'?'editor.action.addCommentLine':'editor.action.removeCommentLine';
     if(s.action==='format')await vscode.commands.executeCommand(command);
     else await this.reviews.transform(source,()=>vscode.commands.executeCommand(command));
     inserted=true;
    }
   }else inserted=await this.reviews.insert(source,s.range,s.result!.text,this.commit);
   if(!inserted)throw new Error('VS Code rejected the edit. Nothing was inserted.');
   if(this.queue&&this.queue.waiting===s.id){const queue=this.queue;queue.inserted++;queue.receiptTimer=setTimeout(()=>queue.reject?.(new Error('Desktop receipt timed out after insertion. Check the file before retrying.')),10000);}
   // Local review was recorded before sending the receipt, even if the bridge disconnected.
   if(this.session!==s)return;
   this.channel.send({type:'applied',id:s.id});
   this.workspace={...this.workspace,state:'idle',message:s.action==='noop'?'No initial selection · Nothing changed':s.action==='format'?'Formatting finished · Native Undo is available':'Changes in working file · Continue editing, Accept All or Revert All'};
   this.verification=`Diagnostics before: ${s.beforeProblems}. Now: ${this.problemCounts(s.editor.document)} (language services may still be updating).`;
   this.clear();this.refreshReview();
  }catch(e){
   if(this.session===s){
    s.applying=false;
    if(inserted){if(this.queue){this.queue.cancelled=true;this.queue.reject?.(new Error('Desktop receipt lost after insertion'));}this.clear();this.workspace={...this.workspace,state:'idle',message:'Changes in working file · Desktop receipt lost'};this.refreshReview();void vscode.window.showWarningMessage('Changes were inserted, but the desktop receipt was lost. Review the working file before retrying.');}
    else {try{this.channel.send({type:'apply_failed',id:s.id});}catch{}this.fail(e);}
   }
  }
 }
 private rejectEdit(message:string):void{
  this.cancel();this.workspace={...this.workspace,type:'workspace',sequence:undefined,state:'idle',message};
  this.refreshReview();void vscode.window.showWarningMessage(message);
 }
 private reviewEditor():vscode.TextEditor|undefined{
  const active=vscode.window.activeTextEditor;return active&&['file','untitled'].includes(active.document.uri.scheme)?active:this.source;
 }
 private publish(frame:Frame):void{
  if(frame.type!=='workspace'){this.panel?.update(frame);return;}
  const state=this.session?.state??(this.queue?'processing':['review','applying'].includes(frame.state??'')?'idle':frame.state);
  this.panel?.update({...frame,project_files:this.projectFiles,verification:this.verification,...this.reviews.snapshot(this.reviewEditor()?.document),state,result:this.session?.result??frame.result,follow_up:this.question()});
 }
 private refreshReview():void{
  if(this.disposed)return;this.contexts();this.displayIdle();
  this.publish({...this.workspace,type:'workspace',sequence:undefined,state:this.session?.state??this.workspace.state});
 }
 private requireReviewEditor():vscode.TextEditor{
  if(this.session||this.starting||this.reviews.busy)throw new Error('Finish or cancel the current request before accepting or reverting changes.');
  const editor=this.reviewEditor();this.eligible(editor);return editor;
 }
 acceptAll():void{
  const editor=this.requireReviewEditor();this.reviews.accept(editor.document);
 }
 async revertAll():Promise<void>{
  const editor=this.requireReviewEditor();await this.reviews.revert(editor);
 }
 async reviewChanges():Promise<void>{
  const editor=this.requireReviewEditor(),document=editor.document;
  const original=vscode.Uri.parse(`sharpvoice-preview:/${randomUUID()}/Before.${document.fileName.split('.').pop()??'txt'}`);
  this.documents.set(original.toString(),this.reviews.original(document));
  await vscode.commands.executeCommand('vscode.diff',original,document.uri,'SharpVoice · Pending Changes',{preview:true});
 }
 async clarify(answer?:string):Promise<void>{
  const s=this.session;if(!s||s.state!=='clarify')return;
  if(!this.focused()||s.editor.document.isClosed||!isCurrent(s.snapshot,{uri:s.editor.document.uri.toString(),version:s.editor.document.version})){this.cancel('Clarification expired or document changed. Start again.');return;}
  if(answer&&answer.length>4000)throw new Error('Clarification is too long.');
  const selection=s.editor.selection;
  const scope=vscode.workspace.getConfiguration('sharpvoice').get<string>('context','nearby');
  const target=s.shortcut?shortcutTarget(s.initial,s.shortcut,scope)!:captureTarget({...s.snapshot,start:s.editor.document.offsetAt(selection.start),end:s.editor.document.offsetAt(selection.end)},scope,s.mode);
  const range=new vscode.Range(s.editor.document.positionAt(target.snapshot.start),s.editor.document.positionAt(target.snapshot.end));
  s.state='answering';this.display('Returning to Source · Click to Cancel','sharpvoice.cancel');
  // Notification actions can temporarily take editor focus. The explicit answer restores its unchanged source.
  const source=await vscode.window.showTextDocument(s.editor.document,{preview:false,preserveFocus:false,viewColumn:s.editor.viewColumn});
  if(this.session!==s)return;s.editor=source;
  if(!this.current(s)||++s.rounds>3){this.cancel('Clarification expired or document changed. Start again.');return;}
  s.snapshot=target.snapshot;s.context=await this.augment(target.context,s.editor.document,s.retainedContext);s.range=range;
  s.state=answer?'processing':'arming';
  if(!answer)this.awaitReady(s);
  this.channel.send({type:'clarify',id:s.id,instruction:answer,context:s.context});this.display(answer?'Working · Click to Cancel':'Starting Microphone · Click to Cancel','sharpvoice.cancel');
 }
 private clear():void{const s=this.session;if(s?.id===this.hotkeyRequest){this.hotkeyRequest=undefined;this.hotkeyMode=undefined;}this.session=undefined;if(s){s.applyReady?.(false);clearTimeout(s.timer);clearTimeout(s.readyTimer);}this.refreshReview();if(this.hotkeyListening&&!this.focused()){this.hotkeyConnected=false;this.channel.dispose();}}
 fail(error:unknown):void{this.hotkeyConnected=false;this.cancel();this.channel.dispose();void vscode.window.showErrorMessage(error instanceof Error?error.message:'SharpVoice request failed.');}
 dispose():void{clearTimeout(this.askReadyTimer);this.disposed=true;this.hotkeyRequest=undefined;clearInterval(this.hotkeyTimer);this.panelEpoch++;this.clearCopy();this.cancel();this.channel.dispose();for(const d of this.disposables)d.dispose();}
}
let activeController:VoiceEditorController|undefined;
export function activate(context:vscode.ExtensionContext):void{
 const controller=activeController=new VoiceEditorController();const panel=new VoicePanel(context.extensionUri,intent=>controller.panelIntent(intent),()=>controller.hidePanel(),()=>controller.sync());controller.attachPanel(panel);controller.initialize(context);context.subscriptions.push(controller,panel,vscode.window.registerWebviewViewProvider('sharpvoice.workspace',panel));
 const command=(name:string,action:(...args:unknown[])=>unknown)=>context.subscriptions.push(vscode.commands.registerCommand('sharpvoice.'+name,(...args:unknown[])=>Promise.resolve().then(()=>action(...args)).catch(e=>controller.fail(e))));
 for(const action of ['fix','explainError','testThis','runTests','related','editRelated','resume','discardRecovery','shortcuts','explainChanges','acceptWorkspace','revertWorkspace','nextChange','previousChange'])command(action,()=>controller.workflow(action));
 for(const action of ['acceptChange','revertChange','jumpChange'])command(action,id=>typeof id==='string'?controller.workflow(action,id):action==='jumpChange'?controller.workflow('nextChange'):controller.start('edit',action==='acceptChange'?'Accept This':'Revert This'));
 controller.listenForHotkeys();
 command('open',()=>controller.open());command('movePanel',()=>controller.panelIntent({action:'move'}));
 command('explain',()=>controller.start('edit','Explain'));command('format',()=>controller.start('edit','Format'));
 command('document',()=>controller.start('edit','Document'));command('comment',()=>controller.start('edit','Comment'));command('uncomment',()=>controller.start('edit','Uncomment'));
 command('dictateCode',()=>controller.start('dictate'));command('editCode',()=>controller.start('edit'));command('stop',()=>controller.stop());command('cancel',()=>controller.cancel());command('apply',()=>controller.apply());command('acceptAll',()=>controller.acceptAll());command('revertAll',()=>controller.revertAll());command('reviewChanges',()=>controller.reviewChanges());command('clarify',()=>controller.clarify());
}

export async function deactivate():Promise<void>{await activeController?.flushRecovery();activeController=undefined;}

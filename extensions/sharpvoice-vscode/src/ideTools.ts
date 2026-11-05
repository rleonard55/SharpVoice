import * as vscode from 'vscode';

type Host = {edit:(instruction:string)=>Promise<void>;ask:(instruction:string)=>Promise<void>;createTests:(instruction:string)=>Promise<void>;status:(message:string)=>void};
type Target = {editor:vscode.TextEditor;version:number;selection:vscode.Selection};
type SymbolTarget = {name:string;kind:vscode.SymbolKind;uri:vscode.Uri;range:vscode.Range;selectionRange:vscode.Range};

/** Native IDE actions. Only explicit user commands reach these methods. */
export class IdeTools {
 constructor(private readonly host:Host) {}
 canHandle(text:string):boolean {return this.action(text)!==undefined;}
 async handle(text:string):Promise<boolean> {const action=this.action(text);if(!action)return false;await action();return true;}
 private action(text:string):(()=>Promise<void>)|undefined {
  const phrase=text.trim().replace(/[.!?]+$/,'').trim();
  const actions:Record<string,()=>Promise<void>>={'fix':()=>this.fix(),'fix error':()=>this.fix(),'explain error':()=>this.explainError(),'test this':()=>this.testThis(),'run tests':()=>this.runTests(),'next error':()=>this.nextError(),'select this function':()=>this.navigate('select')};
  const key=phrase.toLowerCase(),action=Object.hasOwn(actions,key)?actions[key]:undefined;if(action)return action;
  const match=/^(go to|select)\s+(?:"([^"\r\n]+)"|'([^'\r\n]+)'|([\p{L}\p{N}_$][\p{L}\p{N}_$.:#<>-]*))$/iu.exec(phrase);
  if(!match)return undefined;
  return ()=>this.navigate(match[1].toLowerCase()==='select'?'select':'go',match[2]??match[3]??match[4]);
 }
 private capture():Target {
  const editor=vscode.window.activeTextEditor;
  if(!vscode.workspace.isTrusted||vscode.env.remoteName||!vscode.window.state.focused||!editor||editor.document.isClosed||editor.selections.length!==1||!['file','untitled'].includes(editor.document.uri.scheme)||vscode.workspace.fs.isWritableFileSystem(editor.document.uri.scheme)===false||vscode.workspace.workspaceFolders?.some(f=>f.uri.scheme!=='file'))throw new Error('Use one cursor in a local editable document in a trusted workspace.');
  return {editor,version:editor.document.version,selection:editor.selection};
 }
 private fresh(target:Target):void {
  const current=this.capture();
  if(current.editor!==target.editor||current.version!==target.version||!this.sameRange(current.selection,target.selection))throw new Error('The document, selection or editor changed while choosing the target. Try again.');
 }
 private sameRange(a:vscode.Range,b:vscode.Range):boolean {return a.start.line===b.start.line&&a.start.character===b.start.character&&a.end.line===b.end.line&&a.end.character===b.end.character;}
 private select(target:Target,range:vscode.Range):void {this.fresh(target);target.editor.selection=new vscode.Selection(range.start,range.end);target.selection=target.editor.selection;target.editor.revealRange(range,vscode.TextEditorRevealType.InCenterIfOutsideViewport);}
 private async guarded(action:()=>Promise<void>):Promise<void> {try{await action();}catch(error){this.host.status(error instanceof Error?error.message:String(error));}}
 private location(uri:vscode.Uri,range:vscode.Range):string {return `${uri.fsPath||uri.toString()}:${range.start.line+1}:${range.start.character+1}`;}
 private counts(uri:vscode.Uri):string {const list=vscode.languages.getDiagnostics(uri);return `${list.filter(d=>d.severity===vscode.DiagnosticSeverity.Error).length} errors, ${list.filter(d=>d.severity===vscode.DiagnosticSeverity.Warning).length} warnings`;}
 private async diagnostic(target:Target):Promise<vscode.Diagnostic|undefined> {
  const all=vscode.languages.getDiagnostics(target.editor.document.uri).filter(d=>d.severity===vscode.DiagnosticSeverity.Error||d.severity===vscode.DiagnosticSeverity.Warning);
  const near=all.filter(d=>d.range.contains(target.selection.active));
  const candidates=near.length?near:all;
  if(!candidates.length){this.host.status('No current errors or warnings in this document.');return;}
  let diagnostic:vscode.Diagnostic|undefined;
  if(candidates.length===1)diagnostic=candidates[0];else diagnostic=(await vscode.window.showQuickPick(candidates.map(d=>({label:d.message,description:this.location(target.editor.document.uri,d.range),detail:`${d.source??''} ${typeof d.code==='object'?d.code.value:d.code??''}`,diagnostic:d})),{placeHolder:'Choose the diagnostic to target'}))?.diagnostic;
  this.fresh(target);
  if(diagnostic&&!vscode.languages.getDiagnostics(target.editor.document.uri).includes(diagnostic))throw new Error('Diagnostics changed while choosing the target. Try again.');
  return diagnostic;
 }
 private async diagnosticAction(edit:boolean):Promise<void> {await this.guarded(async()=>{
  const target=this.capture(),diagnostic=await this.diagnostic(target);if(!diagnostic)return;
  this.fresh(target);const document=target.editor.document;this.select(target,diagnostic.range);this.host.status(`Before: ${this.counts(document.uri)}.`);
  const code=typeof diagnostic.code==='object'?diagnostic.code.value:diagnostic.code;
  const prefix=`${edit?'Fix this actual diagnostic in the selected code scope. Preserve unrelated code.':'Explain this actual diagnostic and suggest a fix. Read-only: do not modify source.'}\nDiagnostic: ${JSON.stringify({message:diagnostic.message,source:diagnostic.source,code,range:{start:{line:diagnostic.range.start.line,character:diagnostic.range.start.character},end:{line:diagnostic.range.end.line,character:diagnostic.range.end.character}}})}\nSelected scope (${document.languageId}, ${document.fileName}):\n`;
  if(prefix.length>4000)throw new Error('The complete diagnostic exceeds the 4,000-character instruction limit. Choose a smaller diagnostic or request a focused explanation manually.');
  const instruction=prefix+document.getText(diagnostic.range).slice(0,Math.min(2000,4000-prefix.length));
  this.fresh(target);await (edit?this.host.edit(instruction):this.host.ask(instruction));
  this.host.status(`Current diagnostics after ${edit?'fix request':'explanation'}: ${this.counts(document.uri)}. Language services may still be updating.`);
 });}
 async fix():Promise<void>{await this.diagnosticAction(true);}
 async explainError():Promise<void>{await this.diagnosticAction(false);}
 private flatten(symbols:(vscode.DocumentSymbol|vscode.SymbolInformation)[],uri:vscode.Uri):SymbolTarget[] {
  const result:SymbolTarget[]=[];
  for(const symbol of symbols){if('range' in symbol&&'selectionRange' in symbol){result.push({name:symbol.name,kind:symbol.kind,uri,range:symbol.range,selectionRange:symbol.selectionRange});result.push(...this.flatten(symbol.children??[],uri));}else if('location' in symbol)result.push({name:symbol.name,kind:symbol.kind,uri:symbol.location.uri,range:symbol.location.range,selectionRange:symbol.location.range});}
  return result;
 }
 private async documentSymbols(target:Target):Promise<SymbolTarget[]> {const result=await vscode.commands.executeCommand<(vscode.DocumentSymbol|vscode.SymbolInformation)[]>('vscode.executeDocumentSymbolProvider',target.editor.document.uri);this.fresh(target);return this.flatten(result??[],target.editor.document.uri);}
 private async functionTarget(target:Target):Promise<SymbolTarget|undefined> {
  const symbols=await this.documentSymbols(target),callables=[vscode.SymbolKind.Function,vscode.SymbolKind.Method,vscode.SymbolKind.Constructor];
  const candidates=symbols.filter(s=>s.uri.toString()===target.editor.document.uri.toString()&&callables.includes(s.kind)&&s.range.contains(target.selection.active));
  candidates.sort((a,b)=>a.range.end.line-a.range.start.line-(b.range.end.line-b.range.start.line)||a.range.end.character-a.range.start.character-(b.range.end.character-b.range.start.character));
  if(!candidates.length)this.host.status('No containing function was returned by this document’s symbol provider.');
  return candidates[0];
 }
 async testThis():Promise<void>{await this.guarded(async()=>{
  const target=this.capture();let scope:vscode.Range=target.selection;
  if(target.selection.isEmpty){const symbol=await this.functionTarget(target);if(!symbol)return;scope=symbol.range;}
  this.fresh(target);const source=target.editor.document.getText(scope);if(source.length>32000)throw new Error('Choose a smaller function or selection (at most 32,000 characters).');
  this.select(target,scope);this.fresh(target);
  await this.host.createTests(`Create focused tests for this ${target.editor.document.languageId} source from ${target.editor.document.fileName}. Use available project/framework context and cover meaningful behavior and edge cases. Generate into the user-chosen test document; preserve the source document.\nSource:\n${source}`);
 });}
 async navigate(kind:'go'|'select',name?:string):Promise<void>{await this.guarded(async()=>{
  const target=this.capture();let symbol:SymbolTarget|undefined;
  if(!name)symbol=await this.functionTarget(target);else{
   let symbols=(await this.documentSymbols(target)).filter(s=>s.name.toLowerCase()===name.toLowerCase());
   if(!symbols.length){const workspace=await vscode.commands.executeCommand<vscode.SymbolInformation[]>('vscode.executeWorkspaceSymbolProvider',name);this.fresh(target);symbols=this.flatten(workspace??[],target.editor.document.uri).filter(s=>s.name.toLowerCase()===name.toLowerCase()&&s.uri.scheme==='file'&&!!vscode.workspace.getWorkspaceFolder(s.uri));}
   if(!symbols.length){this.host.status(`No native symbol named "${name}" was found. A language symbol provider may be unavailable.`);return;}
   symbol=symbols.length===1?symbols[0]:(await vscode.window.showQuickPick(symbols.map(s=>({label:s.name,description:this.location(s.uri,s.range),symbol:s})),{placeHolder:'Choose a symbol'}))?.symbol;
   this.fresh(target);
  }
  if(!symbol)return;this.fresh(target);
  const range=kind==='select'?symbol.range:new vscode.Range(symbol.selectionRange.start,symbol.selectionRange.start);
  if(symbol.uri.toString()===target.editor.document.uri.toString()){this.select(target,range);return;}
  if(symbol.uri.scheme!=='file'||!vscode.workspace.getWorkspaceFolder(symbol.uri))throw new Error('The symbol is outside the local workspace.');
  const document=await vscode.workspace.openTextDocument(symbol.uri);this.fresh(target);const version=document.version;
  const editor=await vscode.window.showTextDocument(document,{preview:false,preserveFocus:false});
  // Revealing a document intentionally changes active editor; recheck both documents.
  const current=this.capture();if(current.editor!==editor||document.version!==version||target.editor.document.version!==target.version)throw new Error('The symbol document changed while opening it. Try again.');
  this.select(current,range);
 });}
 async nextError():Promise<void>{await this.guarded(async()=>{
  const target=this.capture(),diagnostics=vscode.languages.getDiagnostics(target.editor.document.uri).filter(d=>d.severity===vscode.DiagnosticSeverity.Error).sort((a,b)=>a.range.start.line-b.range.start.line||a.range.start.character-b.range.start.character);
  const next=diagnostics.find(d=>target.selection.active.isBefore(d.range.start))??diagnostics[0];
  if(!next){this.host.status('No current errors in this document.');return;}this.select(target,next.range);this.host.status(next.message);
 });}
 async runTests():Promise<void>{await this.guarded(async()=>{
  const target=this.capture(),tasks=await vscode.tasks.fetchTasks();this.fresh(target);
  const tests=tasks.filter(t=>t.group?.id===vscode.TaskGroup.Test.id||/\btest(s|ing)?\b/i.test(t.name));
  const choices=tests.length?tests:tasks;if(!choices.length){this.host.status('No existing workspace tasks are available. Configure a native test task first.');return;}
  const selected=await vscode.window.showQuickPick(choices.map(task=>({label:task.name,description:task.source,task})),{placeHolder:tests.length?'Select a native test task to run':'No test-labeled tasks. Select an existing task to run'});this.fresh(target);if(!selected)return;
  let execution:vscode.TaskExecution|undefined,finished=false;let timer:ReturnType<typeof setTimeout>|undefined;
  const earlyProcesses:vscode.TaskProcessEndEvent[]=[],earlyEnds:vscode.TaskEndEvent[]=[];
  const finish=(message:string)=>{if(finished)return;finished=true;if(timer)clearTimeout(timer);processListener.dispose();endListener.dispose();this.host.status(message);};
  const processEnd=(event:vscode.TaskProcessEndEvent)=>{if(!execution){earlyProcesses.push(event);return;}if(event.execution!==execution)return;finish(`Task "${selected.task.name}" ${event.exitCode===undefined?'ended with unknown exit status (possibly canceled)':`exited with code ${event.exitCode}`}.`);};
  const taskEnd=(event:vscode.TaskEndEvent)=>{if(!execution){earlyEnds.push(event);return;}if(event.execution!==execution)return;timer=setTimeout(()=>finish(`Task "${selected.task.name}" ended; exit status unknown or canceled.`),0);};
  const processListener=vscode.tasks.onDidEndTaskProcess(processEnd),endListener=vscode.tasks.onDidEndTask(taskEnd);
  try{this.fresh(target);execution=await vscode.tasks.executeTask(selected.task);if(!finished)this.host.status(`Started native task "${selected.task.name}". Waiting for its actual exit status.`);for(const event of earlyProcesses)processEnd(event);for(const event of earlyEnds)taskEnd(event);}catch(error){finish(`Could not execute native task: ${error instanceof Error?error.message:String(error)}`);}
 });}
}

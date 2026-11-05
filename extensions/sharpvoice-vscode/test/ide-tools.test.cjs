const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
class Position{constructor(line,character){this.line=line;this.character=character;}isBefore(p){return this.line<p.line||this.line===p.line&&this.character<p.character;}}
class Range{constructor(start,end){this.start=start;this.end=end;}contains(p){return !p.isBefore(this.start)&&!this.end.isBefore(p);}}
class Selection extends Range{constructor(start,end){super(start,end);this.anchor=start;this.active=end;this.isEmpty=start.line===end.line&&start.character===end.character;}}
const p=(l,c=0)=>new Position(l,c),range=(a,b)=>new Range(p(a),p(b));
function fixture(){
 const events=[],callbacks=[],uri={scheme:'file',fsPath:'C:/project/main.ts',toString(){return 'file:///C:/project/main.ts';}};
 const document={uri,version:1,isClosed:false,languageId:'typescript',fileName:uri.fsPath,getText:r=>r?'function target() {}':'function target() {}\n\n',lineCount:3};
 const editor={document,selection:new Selection(p(0),p(0)),selections:[],revealRange:r=>events.push(['reveal',r])};editor.selections=[editor.selection];
 const state={diagnostics:[],symbols:[],workspaceSymbols:[],tasks:[],picker:undefined,listeners:[],ends:[],execution:{task:{}},disposed:0};
 const vscode={Position,Range,Selection,SymbolKind:{Function:11,Method:5,Constructor:8},DiagnosticSeverity:{Error:0,Warning:1},TaskGroup:{Test:{id:'test'}},TextEditorRevealType:{InCenterIfOutsideViewport:1},env:{},window:{activeTextEditor:editor,state:{focused:true},showQuickPick:async items=>{events.push(['picker',items]);return state.picker?state.picker(items):items[0];},showInputBox:async()=>undefined,showTextDocument:async d=>{const e={...editor,document:d};vscode.window.activeTextEditor=e;return e;}},workspace:{isTrusted:true,workspaceFolders:[{uri}],getWorkspaceFolder:()=>({uri}),fs:{isWritableFileSystem:()=>true},openTextDocument:async()=>document},languages:{getDiagnostics:()=>state.diagnostics},commands:{executeCommand:async command=>command==='vscode.executeDocumentSymbolProvider'?state.symbols:state.workspaceSymbols},tasks:{fetchTasks:async()=>state.tasks,onDidEndTaskProcess:cb=>{state.listeners.push(cb);return {dispose(){state.disposed++;state.listeners=state.listeners.filter(x=>x!==cb);}};},onDidEndTask:cb=>{state.ends.push(cb);return {dispose(){state.disposed++;state.ends=state.ends.filter(x=>x!==cb);}};},executeTask:async task=>{state.execution.task=task;return state.execution;}}};
 const file=path.join(__dirname,'../out/ideTools.js');assert.ok(fs.existsSync(file),'native IDE tools module has not been implemented');
 const module={exports:{}};vm.runInNewContext(fs.readFileSync(file,'utf8'),{module,exports:module.exports,require:n=>{assert.equal(n,'vscode');return vscode;},setTimeout,clearTimeout,process});
 const host={status:m=>events.push(['status',m]),edit:async s=>callbacks.push(['edit',s,editor.selection]),ask:async s=>callbacks.push(['ask',s,editor.selection]),createTests:async s=>callbacks.push(['tests',s,editor.selection])};
 return {tools:new module.exports.IdeTools(host),vscode,state,editor,document,events,callbacks};
}
test('native phrase routing preserves ordinary longer AI instructions',async()=>{
 const f=fixture();for(const text of ['fix this carefully','explain error handling','run tests after editing','Select a useful implementation?'])assert.equal(await f.tools.handle(text),false,text);
 for(const [text,method,args] of [['Fix!','fix',[]],['Explain Error.','explainError',[]],['Test This','testThis',[]],['Run Tests','runTests',[]],['Next Error','nextError',[]],['Select This Function','navigate',['select']],['Go to target','navigate',['go','target']],['Select target','navigate',['select','target']]]){
 let actual;f.tools[method]=async(...a)=>{actual=a;};assert.equal(await f.tools.handle(text),true);assert.deepEqual(actual,args);
 }
});
test('diagnostic fix targets actual range and explanation remains read-only',async()=>{
 const f=fixture();f.state.diagnostics=[{message:'Cannot find name missing',range:range(0,1),severity:0,source:'ts',code:2304}];await f.tools.fix();assert.equal(f.callbacks[0][0],'edit');assert.match(f.callbacks[0][1],/Cannot find name missing/);assert.match(f.callbacks[0][1],/2304/);assert.equal(f.callbacks[0][2].end.line,1);assert.ok(f.events.some(e=>e[0]==='status'&&/1 error/.test(e[1])));
 await f.tools.explainError();assert.equal(f.callbacks[1][0],'ask');assert.match(f.callbacks[1][1],/Cannot find name missing/);
});
test('diagnostic picker cancellation and changed document never generate',async()=>{
 for(const change of ['cancel','version','focus']){const f=fixture();f.state.diagnostics=[{message:'a',range:range(1,2),severity:0},{message:'b',range:range(2,3),severity:1}];f.state.picker=items=>{if(change==='cancel')return undefined;if(change==='version')f.document.version++;if(change==='focus')f.vscode.window.state.focused=false;return items[0];};await f.tools.fix();assert.equal(f.callbacks.length,0,change);}
});
test('symbol ambiguity uses native candidates and selection range',async()=>{
 const f=fixture();f.state.symbols=[{name:'target',kind:11,range:range(0,2),selectionRange:range(0,0),children:[]},{name:'target',kind:11,range:range(3,6),selectionRange:range(3,3),children:[]}];f.state.picker=items=>items[1];await f.tools.navigate('select','target');assert.equal(f.editor.selection.start.line,3);assert.equal(f.editor.selection.end.line,6);assert.equal(f.callbacks.length,0);
});
test('Select This Function selects smallest containing callable and rejects stale symbols',async()=>{
 const f=fixture();f.state.symbols=[{name:'outer',kind:11,range:range(0,8),selectionRange:range(0,0),children:[{name:'inner',kind:5,range:range(0,2),selectionRange:range(0,0),children:[]}]}];await f.tools.navigate('select');assert.equal(f.editor.selection.end.line,2);
 f.vscode.commands.executeCommand=async()=>{f.document.version++;return f.state.symbols;};f.editor.selection=new Selection(p(0),p(0));await f.tools.navigate('select');assert.equal(f.editor.selection.end.line,0);
});
test('Test This uses source and language in generation callback without source edits',async()=>{
 const f=fixture();f.state.symbols=[{name:'target',kind:11,range:range(0,1),selectionRange:range(0,0),children:[]}];await f.tools.testThis();assert.equal(f.callbacks.length,1);assert.equal(f.callbacks[0][0],'tests');assert.match(f.callbacks[0][1],/typescript/);assert.match(f.callbacks[0][1],/function target/);
});
test('Run Tests executes only selected existing task and reports actual exit',async()=>{
 const f=fixture();const task={name:'unit tests',group:{id:'test'}};f.state.tasks=[task,{name:'build'}];await f.tools.runTests();assert.equal(f.state.execution.task,task);assert.equal(f.events.find(e=>e[0]==='picker')[1].length,1);for(const cb of [...f.state.listeners])cb({execution:f.state.execution,exitCode:7});assert.ok(f.events.some(e=>e[0]==='status'&&/7/.test(e[1])));assert.equal(f.state.listeners.length,0);
});
test('task cancellation, unknown exit and native task failure do not claim success',async()=>{
 for(const mode of ['cancel','unknown','failure','end']){const f=fixture();f.state.tasks=[{name:'build'}];if(mode==='cancel')f.state.picker=()=>undefined;if(mode==='failure')f.vscode.tasks.executeTask=async()=>{throw Error('native failure');};await f.tools.runTests();if(mode==='cancel')assert.equal(f.state.execution.task.name,undefined);if(mode==='unknown')for(const cb of [...f.state.listeners])cb({execution:f.state.execution,exitCode:undefined});if(mode==='end'){for(const cb of [...f.state.ends])cb({execution:f.state.execution});await new Promise(r=>setTimeout(r,20));}if(mode!=='cancel')assert.ok(f.events.some(e=>e[0]==='status'&&/unknown|cancel|native failure/i.test(e[1])));assert.equal(f.state.listeners.length,0);}
});
test('Next Error cycles actual document errors and untrusted workspaces cannot invoke tools',async()=>{
 const f=fixture();f.state.diagnostics=[{message:'warning',range:range(1,2),severity:1},{message:'error',range:range(2,3),severity:0}];await f.tools.nextError();assert.equal(f.editor.selection.start.line,2);f.vscode.workspace.isTrusted=false;await f.tools.fix();await f.tools.runTests();assert.equal(f.callbacks.length,0);assert.equal(f.state.execution.task.name,undefined);
});
test('native classifier does not interpret object prototype names as commands',async()=>{
 const f=fixture();for(const name of ['constructor','toString','valueOf','hasOwnProperty'])assert.equal(await f.tools.handle(name),false,name);
});
test('selection changes during native symbol lookup prevent navigation',async()=>{
 const f=fixture();f.state.symbols=[{name:'target',kind:11,range:range(0,2),selectionRange:range(0,0),children:[]}];f.vscode.commands.executeCommand=async()=>{f.editor.selection=new Selection(p(2),p(2));return f.state.symbols;};await f.tools.navigate('select','target');assert.equal(f.editor.selection.end.line,2);assert.equal(f.events.filter(e=>e[0]==='reveal').length,0);assert.ok(f.events.some(e=>e[0]==='status'&&/changed/.test(e[1])));
});
test('unsupported symbol providers and absent diagnostics report without AI generation',async()=>{
 const f=fixture();await f.tools.testThis();await f.tools.navigate('go','absent');await f.tools.fix();await f.tools.nextError();assert.equal(f.callbacks.length,0);assert.equal(f.events.filter(e=>e[0]==='reveal').length,0);assert.ok(f.events.some(e=>e[0]==='status'&&/provider/.test(e[1])));
});
test('workspace symbol navigation opens a local candidate and selects its precise native range',async()=>{
 const f=fixture(),otherUri={scheme:'file',fsPath:'C:/project/other.ts',toString:()=> 'file:///C:/project/other.ts'};const other={...f.document,uri:otherUri,fileName:otherUri.fsPath};f.state.workspaceSymbols=[{name:'remoteTarget',kind:11,location:{uri:otherUri,range:range(3,6)}}];f.vscode.workspace.openTextDocument=async()=>other;await f.tools.navigate('select','remoteTarget');assert.equal(f.vscode.window.activeTextEditor.document,other);assert.equal(f.vscode.window.activeTextEditor.selection.start.line,3);assert.equal(f.vscode.window.activeTextEditor.selection.end.line,6);
});
test('Run Tests refuses a stale picker and ignores unrelated task completion',async()=>{
 const f=fixture();f.state.tasks=[{name:'tests',source:'workspace'}];f.state.picker=items=>{f.document.version++;return items[0];};await f.tools.runTests();assert.equal(f.state.execution.task.name,undefined);
 const g=fixture();g.state.tasks=[{name:'tests',source:'workspace'}];await g.tools.runTests();for(const cb of [...g.state.listeners])cb({execution:{task:{name:'other'}},exitCode:0});assert.equal(g.state.listeners.length,1);for(const cb of [...g.state.listeners])cb({execution:g.state.execution,exitCode:0});assert.equal(g.state.listeners.length,0);assert.equal(g.state.ends.length,0);
});
test('canHandle classifies spoken commands without reading editor state or executing callbacks',()=>{
 const f=fixture();assert.equal(typeof f.tools.canHandle,'function','spoken native command classifier is missing');Object.defineProperty(f.vscode.window,'activeTextEditor',{get(){throw Error('classifier must be pure');}});
 for(const text of ['Fix','Fix Error','Explain Error.','Go to target','Select "named target"','Run Tests'])assert.equal(f.tools.canHandle(text),true,text);
 for(const text of ['Fix this function','Run tests after editing','constructor','Select a useful implementation?'])assert.equal(f.tools.canHandle(text),false,text);assert.equal(f.callbacks.length,0);assert.equal(f.events.length,0);
});
test('document symbols with compatibility locations preserve declaration selectionRange and children',async()=>{
 const f=fixture();f.state.symbols=[{name:'target',kind:11,range:range(0,2),selectionRange:new Range(p(0,9),p(0,15)),location:{uri:f.document.uri,range:range(0,2)},children:[{name:'child',kind:5,range:range(0,1),selectionRange:new Range(p(0,18),p(0,23)),children:[]}]}];await f.tools.navigate('go','target');assert.equal(f.editor.selection.start.character,9);assert.equal(f.editor.selection.end.character,9);await f.tools.navigate('go','child');assert.equal(f.editor.selection.start.character,18);
});
test('diagnostic instruction fits bridge limit without truncating diagnostic semantics',async()=>{
 const f=fixture();f.document.getText=()=> 'x'.repeat(32000);f.state.diagnostics=[{message:'Cannot find name missing',range:range(0,1),severity:0,source:'ts',code:2304}];await f.tools.fix();assert.equal(f.callbacks.length,1);assert.ok(f.callbacks[0][1].length<=4000);assert.match(f.callbacks[0][1],/Cannot find name missing/);assert.match(f.callbacks[0][1],/2304/);
 const g=fixture();g.state.diagnostics=[{message:'m'.repeat(4100),range:range(0,1),severity:0}];await g.tools.explainError();assert.equal(g.callbacks.length,0);assert.ok(g.events.some(e=>e[0]==='status'&&/diagnostic.*4,000|4,000.*diagnostic/i.test(e[1])));
});

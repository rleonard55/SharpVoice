const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),os=require('node:os'),path=require('node:path'),vm=require('node:vm');
function fixture(){
 const root=fs.mkdtempSync(path.join(os.tmpdir(),'sharpvoice-context-')),docs=[],warnings=[],config={shortcuts:{'Ship it':'Review and document','format':'overridden','go to foo':'overridden','Fix Error':'overridden'},codingInstructions:'Personal guidance'};
 const uri=p=>({scheme:'file',authority:'',fsPath:p,toString:()=>`file:${p}`});
 const workspace={isTrusted:true,textDocuments:docs,getWorkspaceFolder:u=>u.fsPath===root||u.fsPath.startsWith(root+path.sep)?{uri:uri(root)}:undefined,getConfiguration:()=>({get:(key,fallback)=>config[key]??fallback}),openTextDocument:async u=>{let doc=docs.find(d=>d.uri.fsPath===u.fsPath);if(!doc){doc={uri:u,version:1,languageId:'typescript',getText:()=>fs.readFileSync(u.fsPath,'utf8')};docs.push(doc);}return doc;},asRelativePath:u=>path.relative(root,u.fsPath),findFiles:async()=>[]};
 const vscode={workspace,Uri:{file:uri},window:{showWarningMessage:async m=>warnings.push(m),showQuickPick:async()=>undefined},commands:{executeCommand:async()=>{}}};
 const module={exports:{}},file=path.join(__dirname,'../out/projectContext.js');vm.runInNewContext(fs.readFileSync(file,'utf8'),{module,exports:module.exports,require:n=>n==='vscode'?vscode:require(n),Buffer,console},{filename:file});
 const write=(name,text)=>{const p=path.join(root,name);fs.mkdirSync(path.dirname(p),{recursive:true});fs.writeFileSync(p,text);return uri(p);};
 const document={uri:write('src/main.ts','active'),languageId:'typescript',getText:()=> 'active'};
 return {root,docs,warnings,config,vscode,uri,write,document,context:new module.exports.ProjectContext(),cleanup:()=>fs.rmSync(root,{recursive:true,force:true})};
}
test('custom shortcuts match exact normalized phrases and cannot override built-ins',()=>{const f=fixture();try{
 assert.equal(f.context.expandShortcut(' SHIP IT!? ',f.document),'Review and document');
 for(const text of ['please ship it','ship it again','format.','go to foo','Fix Error'])assert.equal(f.context.expandShortcut(text,f.document),undefined);
}finally{f.cleanup();}});
test('selected context uses unsaved text, caps sources and marks clipping within bounds',async()=>{const f=fixture();try{
 const files=Array.from({length:6},(_,i)=>f.write(`src/related${i}.ts`,'old'));
 f.docs.push({uri:files[0],version:1,languageId:'typescript',getText:()=> 'x'.repeat(9000)});
 f.context.setRelated([f.document.uri,...files]);const result=await f.context.collect(f.document);
 assert.ok(result.related_sources.length<=4);assert.equal(result.related_sources[0].name,'src/related0.ts');assert.match(result.related_sources[0].text,/truncated/i);assert.ok(result.related_sources.every(x=>x.text.length<=8000));assert.ok(result.related_sources.reduce((n,x)=>n+x.text.length,0)<=32000);
}finally{f.cleanup();}});
test('context rejects outside, symlink escapes, secret files and untrusted workspaces',async()=>{const f=fixture(),outside=fs.mkdtempSync(path.join(os.tmpdir(),'sharpvoice-outside-'));try{
 const out=path.join(outside,'other.ts');fs.writeFileSync(out,'PRIVATE');
 const secret=f.write('.env','TOKEN'); const local=f.write('safe.ts','safe');
 try{fs.symlinkSync(out,path.join(f.root,'linked.ts'));}catch{}
 f.context.setRelated([f.uri(out),secret,f.uri(path.join(f.root,'linked.ts')),local]);
 assert.equal((await f.context.collect(f.document)).related_sources.length,1);
 f.vscode.workspace.isTrusted=false;assert.equal((await f.context.collect(f.document)).related_sources.length,0);
}finally{f.cleanup();fs.rmSync(outside,{recursive:true,force:true});}});
test('guidance walks root to leaf, uses dirty documents and applies personal instructions last',async()=>{const f=fixture();try{
 f.write('AGENTS.md','ROOT guidance');const child=f.write('src/AGENTS.md','old');f.write('.editorconfig','root=true');
 f.docs.push({uri:child,version:2,languageId:'markdown',getText:()=> 'DIRTY local guidance'});
 const result=await f.context.collect(f.document);
 assert.deepEqual(Array.from(result.guidance_files),['AGENTS.md','src/AGENTS.md','.editorconfig']);
 assert.ok(result.project_guidance.indexOf('ROOT guidance')<result.project_guidance.indexOf('DIRTY local guidance'));
 assert.ok(result.project_guidance.indexOf('DIRTY local guidance')<result.project_guidance.indexOf('Personal guidance'));
 f.config.codingInstructions='z'.repeat(12000);assert.ok((await f.context.collect(f.document)).project_guidance.length<=8000);
}finally{f.cleanup();}});
test('picker cancellation and excess selection preserve existing context',async()=>{const f=fixture();try{
 const files=Array.from({length:5},(_,i)=>f.write(`pick${i}.ts`,'source'));f.context.setRelated([files[0]]);
 f.vscode.workspace.findFiles=async()=>files;
 assert.equal(await f.context.chooseRelated(),undefined);assert.equal(f.context.related[0].fsPath,files[0].fsPath);
 f.vscode.window.showQuickPick=async()=>files.map(uri=>({uri}));assert.equal(await f.context.chooseRelated(),undefined);
 assert.equal(f.context.related.length,1);assert.match(f.warnings[0],/at most 4/);
 f.vscode.window.showQuickPick=async()=>[];assert.equal((await f.context.chooseRelated()).length,0);assert.equal(f.context.related.length,0);
}finally{f.cleanup();}});
test('huge unopened sources are skipped with an explicit explanation',async()=>{const f=fixture();try{
 f.context.setRelated([f.write('huge.ts','x'.repeat(2*1024*1024+1))]);
 assert.equal((await f.context.collect(f.document)).related_sources.length,0);assert.match(f.warnings[0],/exceeds 2 MiB/);
}finally{f.cleanup();}});
test('symlinked guidance cannot pull instructions from outside the workspace',async()=>{const f=fixture(),outside=fs.mkdtempSync(path.join(os.tmpdir(),'sharpvoice-guidance-'));try{
 fs.writeFileSync(path.join(outside,'AGENTS.md'),'EXTERNAL instructions');
 // Directory junctions do not require Windows symlink developer privileges.
 fs.symlinkSync(outside,path.join(f.root,'external'),'junction');
 const uri=f.uri(path.join(f.root,'external','AGENTS.md'));f.context.setRelated([uri]);
 assert.equal((await f.context.collect(f.document)).related_sources.length,0);
 f.write('src/local.ts','local');
 assert.equal((await f.context.collect(f.document)).project_guidance.includes('EXTERNAL'),false);
}finally{f.cleanup();fs.rmSync(outside,{recursive:true,force:true});}});

test('controller command phrases remain reserved over configured custom shortcuts',()=>{const f=fixture();try{
 const phrases=['Fix','Accept This','Revert This','Edit Related Files','Related Files','Resume Review','Discard Recovery','Configure Shortcuts'];
 for(const phrase of phrases){f.config.shortcuts[phrase]='custom override';assert.equal(f.context.expandShortcut(` ${phrase.toUpperCase()}!? `,f.document),undefined,phrase);}
}finally{f.cleanup();}});
test('deep guidance trees never exceed the protocol 40-file cap or 8000-character cap',async()=>{const f=fixture();try{
 let directory='';for(let i=0;i<45;i++){f.write(`${directory}AGENTS.md`,'instruction');directory+='d/';}
 const uri=f.write(`${directory}main.ts`,'active');const result=await f.context.collect({uri,languageId:'typescript',getText:()=> 'active'});
 assert.equal(result.guidance_files.length,40);assert.ok(result.project_guidance.length<=8000);
}finally{f.cleanup();}});

test('delayed later source opening rejects an earlier source edited after its text was read',async()=>{const f=fixture();try{
 const first=f.write('first.ts','before'),second=f.write('second.ts','later');
 const firstDoc={uri:first,version:1,languageId:'typescript',getText:()=> 'before'};f.docs.push(firstDoc);
 let unblock;const originalOpen=f.vscode.workspace.openTextDocument;
 f.vscode.workspace.openTextDocument=async uri=>{await new Promise(resolve=>{unblock=resolve;});return originalOpen(uri);};
 f.context.setRelated([first,second]);const collecting=f.context.collect(f.document);assert.equal(typeof unblock,'function');
 firstDoc.version=2;unblock();await assert.rejects(collecting,/context.*changed.*retry/i);
}finally{f.cleanup();}});
test('collected versions describe the exact related and already-open guidance snapshots',async()=>{const f=fixture();try{
 const source=f.write('related.ts','source'),guide=f.write('AGENTS.md','disk');
 f.docs.push({uri:source,version:7,languageId:'typescript',getText:()=> 'current related'},{uri:guide,version:9,languageId:'markdown',getText:()=> 'current guidance'});
 f.context.setRelated([source]);const result=await f.context.collect(f.document);
 assert.equal(result.versions.get(source.toString()),7);assert.equal(result.versions.get(guide.toString()),9);
}finally{f.cleanup();}});
test('trusted untitled targets retain personal instructions and explicitly selected context',async()=>{const f=fixture();try{
 const source=f.write('related.ts','existing source');f.context.setRelated([source]);f.write('AGENTS.md','workspace instructions');
 const untitled={uri:{scheme:'untitled',authority:'',fsPath:'Untitled-1',toString:()=> 'untitled:Untitled-1'},languageId:'typescript',getText:()=> ''};
 const result=await f.context.collect(untitled);
 assert.equal(result.related_sources[0].text,'existing source');assert.match(result.project_guidance,/Personal guidance/);
 assert.equal(result.guidance_files.length,0);assert.equal(result.versions.get(source.toString()),1);
}finally{f.cleanup();}});
test('untitled intended workspace paths receive applicable local ancestor guidance only',async()=>{const f=fixture();try{
 f.write('AGENTS.md','root guidance');f.write('src/AGENTS.md','folder guidance');
 const intended=f.uri(path.join(f.root,'src','new.ts'));const uri={...intended,scheme:'untitled',toString:()=> `untitled:${intended.fsPath}`};
 const result=await f.context.collect({uri,languageId:'typescript',getText:()=> ''});
 assert.deepEqual(Array.from(result.guidance_files),['AGENTS.md','src/AGENTS.md']);assert.match(result.project_guidance,/folder guidance/);
 f.vscode.workspace.isTrusted=false;const blocked=await f.context.collect({uri,languageId:'typescript',getText:()=> ''});assert.equal(blocked.project_guidance,'');
}finally{f.cleanup();}});

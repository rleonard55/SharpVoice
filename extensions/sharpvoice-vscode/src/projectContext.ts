import * as vscode from 'vscode';
import * as fs from 'node:fs';
import * as path from 'node:path';

export interface RelatedSource {name:string;language:string;text:string}
const SOURCE_LIMIT=8000,GUIDANCE_LIMIT=8000,MAX_DISK_BYTES=2*1024*1024;
const normalize=(text:string)=>text.trim().replace(/[.!?]+$/,'').trimEnd().toLowerCase();
const reserved=new Set(['fix','accept this','revert this','edit related files','related files','resume review','discard recovery','configure shortcuts','document','comment','uncomment','explain','format','fix error','explain error','test this','run tests','select this function','next error','explain changes','accept change','revert change','accept all','revert all','next change','previous change','accept all changes','revert all changes','accept file','revert file','accept all files','revert all files']);
const formatterNames=['.editorconfig','.prettierrc','.prettierrc.json','prettier.config.js','prettier.config.cjs','.clang-format','rustfmt.toml','.rustfmt.toml','pyproject.toml'];
const clip=(text:string,limit:number)=>{const marker='\n[context truncated]\n';return text.length<=limit?text:text.slice(0,Math.max(0,limit-marker.length))+marker.slice(0,limit);};
const relative=(root:string,file:string)=>path.relative(root,file).split(path.sep).join('/');
const inside=(root:string,file:string)=>{const rel=path.relative(root,file);return rel===''||(!rel.startsWith(`..${path.sep}`)&&rel!=='..'&&!path.isAbsolute(rel));};

/** Collect literal, bounded context; never execute repository configuration or instructions. */
export class ProjectContext {
 private selected:vscode.Uri[]=[];
 constructor(private readonly changed:()=>void=()=>{}){}
 get related():readonly vscode.Uri[]{return [...this.selected];}
 private local(uri:vscode.Uri):{root:string;file:string}|undefined{
  if(!vscode.workspace.isTrusted||uri.scheme!=='file'||uri.authority)return;
  const folder=vscode.workspace.getWorkspaceFolder(uri);if(!folder||folder.uri.scheme!=='file'||folder.uri.authority)return;
  const root=folder.uri.fsPath,file=uri.fsPath;
  if(!inside(root,file))return;
  const rel=relative(root,file);
  if(rel.split('/').some(part=>['.git','.aws','.ssh','.gnupg'].includes(part))||/(^|\/)(\.env(?:\..*)?|credentials(?:\.json)?|secrets?(?:\..*)?|id_rsa|id_ed25519)$/i.test(rel)||/\.(pem|p12|pfx|key)$/i.test(file))return;
  try{if(!inside(fs.realpathSync(root),fs.realpathSync(file))||!fs.statSync(file).isFile())return;}catch{return;}
  return {root,file};
 }
 private intendedLocation(uri:vscode.Uri):{root:string;file:string}|undefined{
  if(uri.scheme!=='untitled'||uri.authority||!path.isAbsolute(uri.fsPath))return;
  const folder=vscode.workspace.getWorkspaceFolder(vscode.Uri.file(uri.fsPath));
  if(!folder||folder.uri.scheme!=='file'||folder.uri.authority||!inside(folder.uri.fsPath,uri.fsPath))return;
  // A proposed filename need not exist, but its parent must be a verified local directory.
  try{
   const root=folder.uri.fsPath,parent=path.dirname(uri.fsPath);
   if(!fs.statSync(parent).isDirectory()||!inside(fs.realpathSync(root),fs.realpathSync(parent)))return;
   return {root,file:uri.fsPath};
  }catch{return;}
 }
 async chooseRelated():Promise<vscode.Uri[]|undefined>{
  if(!vscode.workspace.isTrusted){void vscode.window.showWarningMessage('SharpVoice related context requires a trusted local workspace.');return;}
  const files=(await vscode.workspace.findFiles('**/*','**/{.git,node_modules,bin,obj,out,dist}/**',2000)).filter(uri=>this.local(uri));
  const choices=files.map(uri=>({label:vscode.workspace.asRelativePath(uri),description:uri.fsPath,picked:this.selected.some(item=>item.toString()===uri.toString()),uri}));
  const picked=await vscode.window.showQuickPick(choices,{canPickMany:true,title:'SharpVoice: choose up to 4 related files',placeHolder:'File contents will be sent as context with your request'});
  if(picked===undefined)return;
  if(picked.length>4){void vscode.window.showWarningMessage('Choose at most 4 related files. The previous selection was kept.');return;}
  this.setRelated(picked.map(item=>item.uri));return [...this.selected];
 }
 setRelated(uris:readonly vscode.Uri[]):void{
  const seen=new Set<string>();this.selected=[];
  for(const uri of uris){if(this.selected.length===4)break;if(!this.local(uri)||seen.has(uri.toString()))continue;seen.add(uri.toString());this.selected.push(uri);}
  this.changed();
 }
 expandShortcut(text:string,document:vscode.TextDocument):string|undefined{
  const phrase=normalize(text);if(reserved.has(phrase)||/^(go to|select)\s+\S/.test(phrase))return;
  const shortcuts=vscode.workspace.getConfiguration('sharpvoice',document.uri).get<Record<string,string>>('shortcuts',{});
  if(!shortcuts||typeof shortcuts!=='object'||Array.isArray(shortcuts))return;
  for(const [key,value] of Object.entries(shortcuts))if(normalize(key)===phrase&&phrase&&typeof value==='string'&&value.trim())return value;
  return;
 }
 async configureShortcuts():Promise<void>{await vscode.commands.executeCommand('workbench.action.openSettings','sharpvoice.shortcuts');}
 private readGuidance(uri:vscode.Uri,versions:Map<string,number>):string|undefined{
  if(!this.local(uri))return;
  const opened=vscode.workspace.textDocuments.find(doc=>doc.uri.toString()===uri.toString());if(opened){const version=opened.version,text=clip(opened.getText(),GUIDANCE_LIMIT);versions.set(opened.uri.toString(),version);return text;}
  let fd:number|undefined;
  try{fd=fs.openSync(uri.fsPath,'r');const buffer=Buffer.alloc(32000),size=fs.readSync(fd,buffer,0,buffer.length,0);return clip(buffer.subarray(0,size).toString('utf8'),GUIDANCE_LIMIT);}catch{return;}finally{if(fd!==undefined)fs.closeSync(fd);}
 }
 async collect(document:vscode.TextDocument):Promise<{related_sources:RelatedSource[];project_guidance:string;guidance_files:string[];versions?:Map<string,number>}>{
  const versions=new Map<string,number>();
  const result:{related_sources:RelatedSource[];project_guidance:string;guidance_files:string[];versions:Map<string,number>}={related_sources:[],project_guidance:'',guidance_files:[],versions};
  const active=this.local(document.uri);
  const untitled=vscode.workspace.isTrusted&&document.uri.scheme==='untitled'&&!document.uri.authority;
  if(!active&&!untitled)return result;
  const guidanceLocation=active??this.intendedLocation(document.uri);
  for(const uri of this.selected){
   const source=this.local(uri);if(!source||uri.toString()===document.uri.toString())continue;
   try{
    // An alias of the active document must not become a second source.
    if(active&&fs.realpathSync(source.file)===fs.realpathSync(active.file))continue;
    const opened=vscode.workspace.textDocuments.find(doc=>doc.uri.toString()===uri.toString());
    if(!opened&&fs.statSync(source.file).size>MAX_DISK_BYTES){void vscode.window.showWarningMessage(`SharpVoice skipped ${relative(source.root,source.file)}: file exceeds 2 MiB. Open it to explicitly use its bounded text.`);continue;}
    const doc=opened??await vscode.workspace.openTextDocument(uri);
    if(!this.local(uri))continue;
    const version=doc.version,text=clip(doc.getText(),SOURCE_LIMIT);
    versions.set(doc.uri.toString(),version);
    result.related_sources.push({name:relative(source.root,source.file),language:doc.languageId,text});
   }catch{void vscode.window.showWarningMessage(`SharpVoice could not read related file ${relative(source.root,source.file)}.`);}
  }
  const config=vscode.workspace.getConfiguration('sharpvoice',document.uri).get<string>('codingInstructions','');
  const personal=typeof config==='string'&&config.trim()?clip(`Personal/workspace coding instructions:\n${config}`,GUIDANCE_LIMIT):'';
  let available=GUIDANCE_LIMIT-personal.length-(personal?2:0);
  const directories:string[]=[];let directory=guidanceLocation?path.dirname(guidanceLocation.file):'';
  while(guidanceLocation&&inside(guidanceLocation.root,directory)){directories.unshift(directory);if(directory===guidanceLocation.root)break;const parent=path.dirname(directory);if(parent===directory)break;directory=parent;}
  const candidates=[...directories.map(dir=>path.join(dir,'AGENTS.md')),...directories.flatMap(dir=>formatterNames.map(name=>path.join(dir,name)))];
  const sections:string[]=[];
  for(const file of candidates){
   if(available<60||result.guidance_files.length>=40)break;
   const text=this.readGuidance(vscode.Uri.file(file),versions);if(text===undefined||!text.trim())continue;
   const name=relative(guidanceLocation!.root,file),section=clip(`File: ${name}\n${text}`,available);
   sections.push(section);result.guidance_files.push(name);available-=section.length+2;
  }
  if(personal)sections.push(personal);
  result.project_guidance=sections.join('\n\n').slice(0,GUIDANCE_LIMIT);
  for(const [uri,version] of versions){
   const current=vscode.workspace.textDocuments.find(doc=>doc.uri.toString()===uri);
   if(!current||current.isClosed||current.version!==version)throw new Error('Project context changed while collecting files. Please retry your request.');
  }
  return result;
 }
}

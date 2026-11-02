const {test}=require('node:test'); const assert=require('node:assert/strict');
const {captureContext, validateResult, isCurrent, FrameReader}=require('../out/model');
const snapshot={uri:'file:///C:/test.ts',version:3,language:'typescript',name:'test.ts',text:'const x = 1;\n',start:0,end:0,diagnostics:'',indent:'  ',eol:'\n'};
test('context retains unsaved source and rejects oversized target/remote',()=>{
 assert.equal(captureContext(snapshot,'nearby').context_text,snapshot.text);
 assert.throws(()=>captureContext({...snapshot,uri:'vscode-remote://host/file'},'nearby'));
 assert.throws(()=>captureContext({...snapshot,text:'x'.repeat(32001),end:32001},'selection'));
});
test('result contract enforces scope and preserves code whitespace',()=>{
 const result=validateResult({kind:'insert',text:'  let x;\n',question:''},'dictate',0,0);
 assert.equal(result.text,'  let x;\n');
 assert.throws(()=>validateResult(result,'edit',0,0));
 assert.throws(()=>validateResult({...result,path:'other.ts'},'dictate',0,0));
 assert.equal(isCurrent(snapshot,{...snapshot,version:4}),false);
 assert.equal(isCurrent(snapshot,{...snapshot}),true);
});
test('wire handles split utf8/coalesced lines and caps unterminated input',()=>{
 const reader=new FrameReader(); const bytes=Buffer.from('{"type":"status","message":"é"}\n{"type":"ack"}\n');
 const parts=[...reader.push(bytes.subarray(0,29)),...reader.push(bytes.subarray(29))]; assert.equal(parts.length,2); assert.equal(parts[0].message,'é');
 assert.throws(()=>new FrameReader().push(Buffer.alloc(1048577,120)));
});
test('nearby context stays bounded around a target deep inside a large buffer',()=>{
 const text='x\n'.repeat(25000)+'TARGET\n'+'y\n'.repeat(25000);
 const c=captureContext({...snapshot,text,start:50000,end:50006},'nearby');
 assert.ok(c.context_text.length<=32000);assert.ok(c.context_text.includes('TARGET'));assert.equal(c.source_text,'TARGET');
});
test('code entry and edits use the available document without requiring a highlight',()=>{
 const {captureTarget}=require('../out/model');
 const text='function main() { return 1; }\nfunction helper() { return 2; }\n';
 const c=captureTarget({...snapshot,text,start:18,end:18},'nearby','dictate');
 assert.equal(c.context.source_text,text);assert.equal(c.context.target_kind,'document');assert.equal(c.context.cursor_offset,18);
 assert.equal(c.snapshot.start,0);assert.equal(c.snapshot.end,text.length);
 const selected=captureTarget({...snapshot,text,start:18,end:26},'nearby','edit');
 assert.equal(selected.context.source_text,text.slice(18,26));assert.equal(selected.context.target_kind,'selection');
 const restricted=captureTarget({...snapshot,text,start:18,end:18},'selection','dictate');
 assert.equal(restricted.context.source_text,'');assert.equal(restricted.context.context_text,'');
 assert.throws(()=>captureTarget({...snapshot,text,start:18,end:18},'selection','edit'),/Selection Only/);
 assert.throws(()=>captureTarget(snapshot,'invalid','dictate'),/valid/);
});
test('automatic large-file targets stay bounded and include cursor without expanding selection-only sharing',()=>{
 const {captureTarget}=require('../out/model');const text='a\n'.repeat(25000)+'target();\n'+'b\n'.repeat(25000);
 const target=captureTarget({...snapshot,text,start:50000,end:50000},'nearby','edit');
 assert.equal(target.context.target_kind,'nearby');assert.ok(target.context.source_text.length<=32000);
 assert.equal(target.context.source_text,text.slice(target.snapshot.start,target.snapshot.end));
 assert.equal(target.context.cursor_offset,50000-target.snapshot.start);assert.ok(target.context.source_text.includes('target();'));
 assert.throws(()=>captureTarget({...snapshot,text,start:0,end:0},'document','edit'),/32,000/);
});
test('editor protocol 3 accepts updated frames and rejects older desktop frames',()=>{
 assert.equal(new FrameReader().push(Buffer.from('{"type":"hello","version":3}\n'))[0].version,3);
 assert.throws(()=>new FrameReader().push(Buffer.from('{"type":"hello","version":1}\n')),/Incompatible/);
});
test('automatic nearby targets retain code when cursor is inside a very long line',()=>{
 const {captureTarget}=require('../out/model');const text='x'.repeat(100000);
 const target=captureTarget({...snapshot,text,start:50000,end:50000},'nearby','edit');
 assert.equal(target.context.source_text.length,32000);assert.equal(target.context.cursor_offset,16000);
 assert.equal(target.context.source_text,text.slice(target.snapshot.start,target.snapshot.end));
});

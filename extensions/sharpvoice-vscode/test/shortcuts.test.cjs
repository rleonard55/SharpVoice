const {test}=require('node:test'),assert=require('node:assert/strict');
const model=require('../out/model');
const snapshot={uri:'untitled:test.cs',version:1,language:'csharp',name:'test.cs',text:'first\nsecond\n',start:0,end:5,diagnostics:'',indent:'    ',eol:'\n'};
test('only exact shortcut names intercept ordinary instructions',()=>{
 assert.equal(typeof model.editorShortcut,'function','shortcut classifier is missing');
 for(const [text,want] of [['Document','document'],[' COMMENT! ','comment'],['Uncomment.','uncomment'],['document this function',undefined],['Do not comment',undefined],['// Comment',undefined]])assert.equal(model.editorShortcut(text),want);
});
test('Document uses initial selection or whole file rather than nearby scope',()=>{
 assert.equal(typeof model.shortcutTarget,'function','shortcut targeting is missing');
 assert.equal(model.shortcutTarget(snapshot,'document','selection').context.source_text,'first');
 const whole=model.shortcutTarget({...snapshot,start:8,end:8},'document','nearby');assert.equal(whole.context.source_text,'first\nsecond\n');assert.equal(whole.context.start,0);assert.equal(whole.context.target_kind,'document');
 assert.throws(()=>model.shortcutTarget({...snapshot,text:'x'.repeat(32001),start:0,end:0},'document','nearby'),/32,000/);
});
test('Comment and Uncomment never expand an absent initial selection',()=>{
 assert.equal(typeof model.shortcutTarget,'function','shortcut targeting is missing');
 for(const action of ['comment','uncomment']){
  assert.equal(model.shortcutTarget({...snapshot,start:0,end:0},action,'nearby'),undefined);
  assert.equal(model.shortcutTarget(snapshot,action,'nearby').context.source_text,'first');
 }
});
test('Explain targets initial selection or entire file and Format works on large files locally',()=>{
 assert.equal(model.editorShortcut('Explain?'),'explain');assert.equal(model.editorShortcut('Format.'),'format');
 assert.equal(model.shortcutTarget(snapshot,'explain','nearby').context.source_text,'first');
 assert.equal(model.shortcutTarget({...snapshot,start:0,end:0},'explain','nearby').context.source_text,'first\nsecond\n');
 assert.ok(model.shortcutTarget({...snapshot,text:'x'.repeat(40000),start:0,end:0},'format','document').context.source_text.length<=32000);
 assert.equal(model.shortcutTarget({...snapshot,text:'x'.repeat(40000)},'comment','document').context.source_text,'xxxxx');
});

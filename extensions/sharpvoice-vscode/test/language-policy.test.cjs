const {test}=require('node:test'),assert=require('node:assert/strict');
let policy;try{policy=require('../out/languagePolicy');}catch{}
function p(){assert.ok(policy,'language policy is missing');return policy;}
test('strict JSON rejects Comment and Document while preserving repair/explain/format',()=>{
 for(const command of ['comment','document'])assert.match(p().shortcutRejection(command,'json','test.json',true),/JSON/);
 for(const command of ['uncomment','explain','format'])assert.equal(p().shortcutRejection(command,'json','test.json',true),undefined);
 assert.equal(p().shortcutRejection('comment','json','test.json',false),undefined);
});
test('JSONC and code languages keep legitimate native comments',()=>{
 for(const [language,name] of [['jsonc','settings.json'],['jsonc','test.jsonc'],['csharp','test.cs'],['python','test.py'],['html','test.html']])
  assert.equal(p().shortcutRejection('comment',language,name,true),undefined);
});
test('known data formats cannot acquire inline documentation or comment markers',()=>{
 for(const [language,name] of [['jsonl','test.jsonl'],['plaintext','test.ndjson'],['csv','test.csv'],['tsv','test.tsv']])
  for(const command of ['comment','document'])assert.ok(p().shortcutRejection(command,language,name,true));
});
test('strict JSON validation checks actual syntax without confusing strings for comments',()=>{
 for(const text of ['{ "url": "https://example.com", "text": "// not a comment" }','[1,true,null,"😀"]','\ufeff{"a":1}'])assert.equal(p().candidateRejection('json','test.json',text),undefined);
 for(const text of ['{ // comment\n "a":1 }','{"a":1,}','{"a":}','/*comment*/ {}'])assert.match(p().candidateRejection('json','test.json',text),/invalid JSON/);
 assert.equal(p().candidateRejection('jsonc','settings.json','{ // comment\n "a":1, }'),undefined);
});
test('JSON Lines validates every nonblank record',()=>{
 assert.equal(p().candidateRejection('plaintext','test.ndjson','{"a":1}\n{"b":2}\n'),undefined);
 assert.ok(p().candidateRejection('jsonl','test.jsonl','{"a":1}\n// invalid\n'));
});
test('JSON uncomment unwraps selected code without touching comment-like strings',()=>{
 const text='{\n  // "a": 1,\n  "url": "https://example.com/*literal*/"\n}';
 assert.equal(p().uncommentCandidate('json','test.json',text,2,15),'{\n  "a": 1,\n  "url": "https://example.com/*literal*/"\n}');
 const valid='{"text":"/* literal */ // value"}';assert.equal(p().uncommentCandidate('json','test.json',valid,0,valid.length),valid);
});

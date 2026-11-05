import {PendingChange,PendingReview} from './review';
export interface Checkpoint {uri:string;text:string;changes:PendingChange[]}
export const checkpointKey='sharpvoice.review.v1',checkpointLimit=2*1024*1024;
export function encodeCheckpoints(entries:readonly {uri:string;review:PendingReview}[]):string{
 if(entries.length>100||entries.some(e=>e.review.changes.length>2000))throw new Error('Review checkpoint count limit reached. Resolve earlier review before reloading.');
 const encoded=JSON.stringify({version:1,entries:entries.map(e=>({uri:e.uri,text:e.review.text,changes:e.review.changes}))});
 if(Buffer.byteLength(encoded,'utf8')>checkpointLimit)throw new Error('Review checkpoint limit reached (2 MiB). Accept or discard earlier review to enable recovery.');return encoded;
}
export function decodeCheckpoints(value:unknown):Checkpoint[]{
 try{
  if(typeof value!=='string'||Buffer.byteLength(value,'utf8')>checkpointLimit)return [];
  const v=JSON.parse(value);if(v.version!==1||!Array.isArray(v.entries)||v.entries.length>100)return [];
  const uris=new Set<string>();
  for(const e of v.entries){
   if(typeof e.uri!=='string'||e.uri.length>4096||! /^(file:\/\/|untitled:)/.test(e.uri)||uris.has(e.uri)||typeof e.text!=='string'||!Array.isArray(e.changes)||e.changes.length>2000)return [];uris.add(e.uri);
   const ids=new Set<string>();
   for(const c of e.changes){if(typeof c.id!=='string'||!c.id||c.id.length>128||ids.has(c.id)||!Number.isInteger(c.start)||!Number.isInteger(c.end)||c.start<0||c.end<c.start||c.end>e.text.length||typeof c.before!=='string'||typeof c.after!=='string'||typeof c.conflicted!=='boolean'||!c.conflicted&&e.text.slice(c.start,c.end)!==c.after)return [];ids.add(c.id);}
  }return v.entries;
 }catch{return [];}
}
export function restoreReview(checkpoint:Checkpoint,current:string):PendingReview{
 const r=new PendingReview(current);const changed=current!==checkpoint.text;
 r.changes=checkpoint.changes.map(c=>({...c,start:Math.min(c.start,current.length),end:Math.min(c.end,current.length),conflicted:c.conflicted||changed}));return r;
}

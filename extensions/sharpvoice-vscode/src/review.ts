import {randomUUID} from 'node:crypto';
/** Offsets are UTF-16 offsets, matching VS Code's document changes. */
export interface ReviewEdit {start:number;end:number;text:string}
export interface PendingChange {id:string;start:number;end:number;before:string;after:string;conflicted:boolean}
export interface DocumentChange {rangeOffset:number;rangeLength:number;text:string}
export interface RevertPlan {edits:ReviewEdit[];resolved:PendingChange[];source:string}
const overlaps=(start:number,end:number,c:PendingChange)=>start<=c.end&&end>=c.start;
// Editor ranges must never start/end inside a surrogate pair or CRLF.
function splitPair(text:string,offset:number):boolean{
 const left=text.charCodeAt(offset-1),right=text.charCodeAt(offset);
 return left>=0xD800&&left<=0xDBFF&&right>=0xDC00&&right<=0xDFFF||left===13&&right===10;
}
function trim(start:number,before:string,after:string):{start:number;before:string;after:string}{
 let left=0;while(left<before.length&&left<after.length&&before[left]===after[left])left++;
 if(splitPair(before,left)||splitPair(after,left))left--;
 let right=0;while(right<before.length-left&&right<after.length-left&&before[before.length-1-right]===after[after.length-1-right])right++;
 if(splitPair(before,before.length-right)||splitPair(after,after.length-right))right--;
 return {start:start+left,before:before.slice(left,before.length-right),after:after.slice(left,after.length-right)};
}
/** Line LCS separates distant edits; bounded work for very large generated replacements. */
export function changedRanges(before:string,after:string):ReviewEdit[]{
 if(before===after)return [];
 const a=before.match(/[^\n]*\n|[^\n]+$/g)??[],b=after.match(/[^\n]*\n|[^\n]+$/g)??[];
 if((a.length+1)*(b.length+1)>1_000_000){const c=trim(0,before,after);return [{start:c.start,end:c.start+c.before.length,text:c.after}];}
 const width=b.length+1,dp=new Uint32Array((a.length+1)*width);
 for(let i=a.length-1;i>=0;i--)for(let j=b.length-1;j>=0;j--)dp[i*width+j]=a[i]===b[j]?dp[(i+1)*width+j+1]+1:Math.max(dp[(i+1)*width+j],dp[i*width+j+1]);
 const edits:ReviewEdit[]=[];let i=0,j=0,offset=0;
 while(i<a.length||j<b.length){
  if(i<a.length&&j<b.length&&a[i]===b[j]){offset+=a[i++].length;j++;continue;}
  const start=offset;let old='',next='';
  while(i<a.length||j<b.length){if(i<a.length&&j<b.length&&a[i]===b[j])break;
   if(j<b.length&&(i===a.length||dp[i*width+j+1]>=dp[(i+1)*width+j]))next+=b[j++];else {old+=a[i];offset+=a[i++].length;}}
  const c=trim(start,old,next);edits.push({start:c.start,end:c.start+c.before.length,text:c.after});
 }
 return edits.reverse();
}
export class PendingReview {
 changes:PendingChange[]=[];
 constructor(public text:string){}
 accept(id?:string):void{this.changes=id?this.changes.filter(c=>c.id!==id):[];}
 record(before:string,after:string):void{
  if(this.text!==before)throw new Error('Review history no longer matches the document.');
  for(const edit of changedRanges(before,after))this.applyAI(edit);
 }
 private applyAI(edit:ReviewEdit):void{
  const touched=this.changes.filter(c=>overlaps(edit.start,edit.end,c));
  const start=Math.min(edit.start,...touched.map(c=>c.start)),end=Math.max(edit.end,...touched.map(c=>c.end));
  let original=this.text.slice(start,end);
  for(const c of [...touched].sort((a,b)=>b.start-a.start))original=original.slice(0,c.start-start)+c.before+original.slice(c.end-start);
  const replacement=this.text.slice(start,edit.start)+edit.text+this.text.slice(edit.end,end);
  const delta=edit.text.length-(edit.end-edit.start);
  this.changes=this.changes.filter(c=>!touched.includes(c));
  for(const c of this.changes)if(c.start>edit.end){c.start+=delta;c.end+=delta;}
  const conflicted=touched.some(c=>c.conflicted),c=trim(start,original,replacement);
  if(c.before!==c.after||conflicted)this.changes.push({id:randomUUID(),start:c.start,end:c.start+c.after.length,before:c.before,after:c.after,conflicted});
  this.text=this.text.slice(0,edit.start)+edit.text+this.text.slice(edit.end);
 }
 update(changes:readonly DocumentChange[],text:string,ambiguous=false):void{
  for(const e of [...changes].sort((a,b)=>b.rangeOffset-a.rangeOffset)){
   const start=e.rangeOffset,end=start+e.rangeLength,delta=e.text.length-e.rangeLength;
   for(const c of this.changes){
    if(overlaps(start,end,c)){
     c.conflicted=true;
     c.start=c.start<start?c.start:c.start>end?c.start+delta:start;
     c.end=c.end>end?c.end+delta:Math.max(c.start,start+e.text.length);
    }else if(c.start>end){c.start+=delta;c.end+=delta;}
   }
  }
  this.text=text;
  for(const c of this.changes){if(ambiguous||c.start<0||c.end>text.length||text.slice(c.start,c.end)!==c.after)c.conflicted=true;}
 }
 planRevert(ids?:readonly string[]):RevertPlan{
  const resolved=this.changes.filter(c=>(!ids||ids.includes(c.id))&&!c.conflicted&&this.text.slice(c.start,c.end)===c.after);
  return {source:this.text,resolved,edits:resolved.map(c=>({start:c.start,end:c.end,text:c.before})).sort((a,b)=>b.start-a.start)};
 }
 completeRevert(plan:RevertPlan,text:string):void{
  if(this.text!==plan.source)throw new Error('The document changed during reversal.');
  this.changes=this.changes.filter(c=>!plan.resolved.includes(c));
  this.update(plan.edits.map(e=>({rangeOffset:e.start,rangeLength:e.end-e.start,text:e.text})),text);
 }
 originalText():string{
  let original=this.text;for(const e of this.planRevert().edits)original=original.slice(0,e.start)+e.text+original.slice(e.end);return original;
 }
}

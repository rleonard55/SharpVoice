import type {EditorShortcut} from './model';

/** JSONC deliberately remains distinct: settings.json can legitimately contain comments. */
function dataFormat(language:string,name:string):string|undefined{
 const id=language.toLowerCase(),file=name.toLowerCase();
 if(['jsonl','jsonlines','ndjson'].includes(id)||/\.(jsonl|ndjson)$/.test(file))return 'JSON Lines';
 if(id==='json'||id!=='jsonc'&&file.endsWith('.json'))return 'Strict JSON';
 if(id==='csv'||file.endsWith('.csv'))return 'CSV';
 if(id==='tsv'||file.endsWith('.tsv'))return 'TSV';
 return undefined;
}
export function shortcutRejection(shortcut:EditorShortcut,language:string,name:string,hasSelection:boolean):string|undefined{
 if(shortcut==='comment'&&!hasSelection)return undefined;
 if(shortcut!=='comment'&&shortcut!=='document')return undefined;
 const format=dataFormat(language,name);if(!format)return undefined;
 return shortcut==='comment'?`${format} does not permit comments. Nothing changed. Uncomment can remove existing invalid comments.`:
  `${format} has no inline documentation syntax. Use Explain instead; the file was left unchanged.`;
}
/** Check the complete candidate, including text outside the selected replacement. */
export function candidateRejection(language:string,name:string,text:string):string|undefined{
 const format=dataFormat(language,name);if(format!=='Strict JSON'&&format!=='JSON Lines')return undefined;
 try{
  const source=text.replace(/^\uFEFF/,'');
  if(format==='JSON Lines'){
   const records=source.split(/\r?\n/).filter(line=>line.trim());
   for(const record of records)JSON.parse(record);
  }else JSON.parse(source);
 }catch{return `The proposed edit would leave this file as invalid ${format==='JSON Lines'?'JSON Lines':'JSON'}. Nothing changed.`;}
 return undefined;
}

/** Plan JSON repair before writing. Comment-like characters inside strings are literal data. */
export function uncommentCandidate(language:string,name:string,text:string,start:number,end:number):string|undefined{
 const format=dataFormat(language,name);if(format!=='Strict JSON'&&format!=='JSON Lines')return undefined;
 const from=start===0?0:text.lastIndexOf('\n',start-1)+1;
 const last=end>start&&text[end-1]==='\n'?end-1:end;
 const next=text.indexOf('\n',last),to=next<0?text.length:next;
 let output='',i=0;
 while(i<text.length){
  const begin=i;
  if(text[i]==='"'){
   i++;while(i<text.length){if(text[i]==='\\'){i+=2;continue;}if(text[i++]==='"')break;}
   output+=text.slice(begin,i);continue;
  }
  if(text.startsWith('//',i)){
   const newline=text.indexOf('\n',i),stop=newline<0?text.length:newline;
   const selected=i>=from&&i<to;
   const body=i+2+(text[i+2]===' '?1:0);
   output+=text.slice(selected?body:i,stop);i=stop;continue;
  }
  if(text.startsWith('/*',i)){
   const close=text.indexOf('*/',i+2);if(close<0){output+=text.slice(i);break;}
   output+=i>=from&&close+2<=to?text.slice(i+2,close):text.slice(i,close+2);i=close+2;continue;
  }
  output+=text[i++];
 }
 return output;
}

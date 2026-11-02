import * as fs from 'node:fs/promises';
import * as net from 'node:net';
import * as path from 'node:path';
import {Frame,FrameReader} from './model';
export class Bridge {
 private hostId?:string;private clientId?:string;
 private socket?:net.Socket; private ready=false; private connecting?:Promise<void>;
 constructor(private readonly receive:(frame:Frame)=>void,private readonly disconnected:()=>void){}
 async connect():Promise<void>{if(this.ready)return;if(this.connecting)return this.connecting;this.connecting=this.open();try{await this.connecting;}finally{this.connecting=undefined;}}
 private async open():Promise<void>{
  if(process.platform!=='win32')throw new Error('SharpVoice requires Windows native VS Code with local files.');
  const file=path.join(process.env.LOCALAPPDATA??'', 'SharpVoice','editor','connection.json');
  const stat=await fs.stat(file).catch(()=>{throw new Error('Start the updated SharpVoice desktop app first.');});if(stat.size>4096)throw new Error('Invalid SharpVoice discovery file.');
  const d=JSON.parse(await fs.readFile(file,'utf8'));
  if(d.version!==3||!/^SharpVoice\.Editor\.[a-f0-9]{32}$/.test(d.pipe)||!/^[A-F0-9]{64}$/.test(d.token)||!Number.isInteger(d.pid))throw new Error('Update SharpVoice and its VS Code extension together.');
  await new Promise<void>((resolve,reject)=>{
   const socket=this.socket=net.connect('\\\\.\\pipe\\'+d.pipe);const reader=new FrameReader();let settled=false;
   const fail=(error:Error)=>{if(!settled){settled=true;clearTimeout(timer);reject(error);}socket.destroy();};
   const timer=setTimeout(()=>fail(new Error('SharpVoice connection timed out. Another editor may be connected.')),5000);
   socket.on('connect',()=>socket.write(JSON.stringify({type:'hello',token:d.token,version:3})+'\n'));
   socket.on('data',bytes=>{if(this.socket!==socket)return;try{for(const f of reader.push(bytes)){
    if(!this.ready){if(f.type!=='hello')throw new Error(f.message??'SharpVoice authentication failed.');if(typeof f.host_id!=='string'||typeof f.client_id!=='string')throw new Error('Missing SharpVoice session identity.');this.hostId=f.host_id;this.clientId=f.client_id;this.receive(f);this.ready=true;settled=true;clearTimeout(timer);resolve();}else if(this.socket===socket&&f.host_id===this.hostId&&f.client_id===this.clientId)this.receive(f);
   }}catch(e){fail(e instanceof Error?e:new Error('Invalid editor message.'));}});
   socket.on('error',fail);socket.on('close',()=>{clearTimeout(timer);if(!settled){settled=true;reject(new Error('SharpVoice disconnected.'));}if(this.socket===socket){this.ready=false;this.socket=undefined;this.disconnected();}});
  });
 }
 send(frame:Frame):void{if(!this.ready||!this.socket)throw new Error('SharpVoice disconnected.');const text=JSON.stringify({...frame,version:3,host_id:this.hostId,client_id:this.clientId})+'\n';if(Buffer.byteLength(text)>1048577)throw new Error('Editor request too large.');this.socket.write(text);}
 dispose():void{this.ready=false;const socket=this.socket;this.socket=undefined;if(socket){socket.end();setTimeout(()=>socket.destroy(),5000).unref();}}
}

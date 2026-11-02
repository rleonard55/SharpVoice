const {test}=require('node:test');const assert=require('node:assert/strict');const {EventEmitter}=require('node:events');const Module=require('node:module');
test('closing an old editor socket cannot disable a newly authenticated connection',async()=>{
 const sockets=[];class Socket extends EventEmitter{writes=[];write(text){this.writes.push(JSON.parse(text));}end(){}destroy(){if(!this.destroyed){this.destroyed=true;this.emit('close');}}}
 const load=Module._load;Module._load=function(name,...rest){if(name==='node:net')return {connect(){const socket=new Socket();sockets.push(socket);queueMicrotask(()=>{socket.emit('connect');socket.emit('data',Buffer.from(JSON.stringify({type:'hello',version:3,host_id:'host',client_id:'client'+sockets.length})+'\n'));});return socket;}};if(name==='node:fs/promises')return {stat:async()=>({size:100}),readFile:async()=>JSON.stringify({version:3,pipe:'SharpVoice.Editor.'+'a'.repeat(32),token:'A'.repeat(64),pid:123})};return load.call(this,name,...rest);};
 let Bridge;try{delete require.cache[require.resolve('../out/bridge')];Bridge=require('../out/bridge').Bridge;}finally{Module._load=load;}
 const received=[];const bridge=new Bridge(frame=>received.push(frame),()=>{});
 await bridge.connect();bridge.dispose();await bridge.connect();sockets[0].emit('close');
 bridge.send({type:'workspace_sync'});assert.equal(sockets[1].writes.at(-1).client_id,'client2');
 const count=received.length;sockets[0].emit('data',Buffer.from('{"type":"workspace","host_id":"host","client_id":"client1","version":3}\n'));assert.equal(received.length,count);
 bridge.dispose();
});

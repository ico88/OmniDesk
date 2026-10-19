import {readFileSync,writeFileSync,existsSync} from 'node:fs';
import {spawnSync,spawn} from 'node:child_process';
const domain=process.env.MESH_DOMAIN;
if(!domain || !/^[a-zA-Z0-9.-]+$/.test(domain)) throw new Error('MESH_DOMAIN required');
const origin=new URL(process.env.MESH_PUBLIC_ORIGIN||`https://${domain}/`);
if(origin.protocol!=='https:'||origin.hostname!==domain||origin.username||origin.password||origin.pathname!=='/'||origin.search||origin.hash)throw new Error('Invalid public origin');
const directory='/opt/meshcentral/meshcentral-data';
const config={settings:{cert:domain,port:8081,aliasPort:Number(origin.port||443),redirPort:0,mpsPort:0,tlsOffload:true,wanOnly:true,exactPorts:true,lockAgentDownload:true},domains:{'':{title:'OmniDesk Remote',newAccounts:false,certUrl:origin.href,guestDeviceSharing:{maxSessionTime:5},userNameIsEmail:false,consentMessages:{title:'OmniDesk — Assistenza remota',desktop:'{0} richiede controllo del desktop. Acconsenti?',terminal:'{0} richiede accesso al terminale. Acconsenti?',files:'{0} richiede accesso ai file. Acconsenti?',consentTimeout:30,autoAcceptOnTimeout:false,autoAcceptIfNoUser:false,autoAcceptIfLocked:false}}}};
writeFileSync(`${directory}/config.json`,JSON.stringify(config,null,2),{mode:0o600});
const main='/opt/meshcentral/node_modules/meshcentral';
function run(args){const result=spawnSync('node',[main,...args],{stdio:['ignore','pipe','pipe']});if(result.status!==0)throw new Error('MeshCentral provisioning failed');}
if(!existsSync(`${directory}/omnidesk-service-v2`)){
 if(existsSync(`${directory}/omnidesk-provisioned`))throw new Error('Legacy privileged service account requires explicit migration');
 const password=readFileSync('/run/secrets/mesh_password','utf8').trim();
 if(password.length<32)throw new Error('Service password too short');
 run(['--createaccount','omnidesk-service','--pass',password]);
 // This account owns only groups it creates; it is never promoted to site administrator.
 writeFileSync(`${directory}/omnidesk-service-v2`,'1',{mode:0o600});
}
const child=spawn('node',[main],{stdio:'inherit'});
for(const signal of ['SIGTERM','SIGINT']) process.on(signal,()=>child.kill(signal));
child.on('exit',code=>process.exit(code??1));

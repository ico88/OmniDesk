import {readFileSync,writeFileSync,existsSync} from 'node:fs';
import {spawnSync,spawn} from 'node:child_process';
const domain=process.env.MESH_DOMAIN;
if(!domain || !/^[a-zA-Z0-9.-]+$/.test(domain)) throw new Error('MESH_DOMAIN required');
const directory='/opt/meshcentral/meshcentral-data';
const config={settings:{cert:domain,port:8081,aliasPort:443,redirPort:0,tlsOffload:true,wanOnly:true,exactPorts:true},domains:{'':{title:'OmniDesk Remote',newAccounts:false,certUrl:`https://${domain}/`,guestDeviceSharing:{maxSessionTime:5},userNameIsEmail:false}}};
writeFileSync(`${directory}/config.json`,JSON.stringify(config,null,2),{mode:0o600});
const main='/opt/meshcentral/node_modules/meshcentral';
function run(args){const result=spawnSync('node',[main,...args],{stdio:['ignore','pipe','pipe']});if(result.status!==0)throw new Error('MeshCentral provisioning failed');}
if(!existsSync(`${directory}/omnidesk-provisioned`)){
 const password=readFileSync('/run/secrets/mesh_password','utf8').trim();
 run(['--createaccount','omnidesk-service','--pass',password]);
 run(['--adminaccount','omnidesk-service']);
 writeFileSync(`${directory}/omnidesk-provisioned`,'1',{mode:0o600});
}
const child=spawn('node',[main],{stdio:'inherit'});
for(const signal of ['SIGTERM','SIGINT']) process.on(signal,()=>child.kill(signal));
child.on('exit',code=>process.exit(code??1));

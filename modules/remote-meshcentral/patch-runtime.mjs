import {readFileSync,writeFileSync} from 'node:fs';
// MeshCentral's startup checker otherwise tries to downgrade our locked security override.
const path='/opt/meshcentral/node_modules/meshcentral/meshcentral.js';
const source=readFileSync(path,'utf8');
const expected="'compression@1.8.1'";
if(source.split(expected).length!==2)throw new Error('Unexpected MeshCentral startup checker');
const actual=JSON.parse(readFileSync('/opt/meshcentral/node_modules/compression/package.json','utf8')).version;
if(actual!=='1.8.2')throw new Error('Security override not installed');
writeFileSync(path,source.replace(expected,"'compression@1.8.2'"));

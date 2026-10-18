// Probe real runtime images over local HTTPS. The local CA is deliberately not public.
import https from 'node:https';
import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';
const cookies = new Map();
async function request(path, method = 'GET', body, token) {
  return new Promise((resolve, reject) => {
    const req = https.request(`https://omnidesk.test${path}`, {
      rejectUnauthorized: false, method,
      headers: { 'Content-Type':'application/json', Cookie:[...cookies].map(([k,v])=>`${k}=${v}`).join('; '), ...(token ? {'X-CSRF-TOKEN':token} : {}) },
    }, res => {
      for (const cookie of res.headers['set-cookie'] ?? []) {
        const [pair] = cookie.split(';'); const split = pair.indexOf('=');
        cookies.set(pair.slice(0,split),pair.slice(split+1));
      }
      let text = ''; res.on('data', d => text += d); res.on('end', () => resolve({status:res.statusCode,headers:res.headers,text}));
    });
    req.on('error', reject); if(body) req.write(JSON.stringify(body)); req.end();
  });
}
let ready = false;
for(let attempt=0;attempt<40;attempt++) {
  try { if((await request('/api/health/ready')).status === 200) { ready=true; break; } } catch {}
  await new Promise(resolve=>setTimeout(resolve,500));
}
assert.ok(ready,'Runtime API not ready');
assert.equal((await request('/')).status,200);
assert.match((await request('/')).text, /id="root"/);
assert.equal((await request('/api/devices')).status,401);
const token=JSON.parse((await request('/api/auth/csrf')).text).token;
const login=await request('/api/auth/login','POST',{email:'admin@example.test',password:readFileSync('/run/secrets/bootstrap_password','utf8').trim()},token);
assert.equal(login.status,204);
const session=login.headers['set-cookie'].find(c=>c.startsWith('__Host-OmniDesk.Session='));
assert.match(session,/secure/i);assert.match(session,/httponly/i);assert.match(session,/samesite=strict/i);
assert.equal((await request('/api/auth/me')).status,200);
assert.deepEqual(JSON.parse((await request('/api/devices')).text),[]);
const authedToken=JSON.parse((await request('/api/auth/csrf')).text).token;
assert.equal((await request('/api/branding','PUT',{name:'Smoke brand',logoUrl:'',primaryColor:'#123456',theme:'light'},authedToken)).status,200);
assert.equal(JSON.parse((await request('/api/branding')).text).name,'Smoke brand');
assert.ok(JSON.parse((await request('/api/admin/audit')).text).some(x=>x.action==='user.bootstrap'));
console.log('PASS: real containers, migrations/bootstrap, HTTPS proxy, UI, secure login cookies, empty fleet, branding and audit');

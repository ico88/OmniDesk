import fs from 'node:fs';
import https from 'node:https';

const ca = fs.readFileSync(process.env.RESTORE_CA_PATH);
const host = process.env.RESTORE_PROBE_HOST || 'caddy';

function request(path) {
  return new Promise((resolve, reject) => {
    const req = https.get({
      hostname: host,
      port: 8443,
      path,
      servername: 'omnidesk.restore.test',
      ca,
      headers: { host: 'omnidesk.restore.test:8443' },
      timeout: 5000,
    }, response => {
      response.resume();
      response.on('end', () => resolve({ status: response.statusCode, headers: response.headers }));
    });
    req.on('timeout', () => req.destroy(new Error('Restore probe timed out')));
    req.on('error', reject);
  });
}

let ready;
let lastError;
for (let attempt = 0; attempt < 60; attempt++) {
  try {
    ready = await request('/api/health/ready');
    if (ready.status === 200) break;
    lastError = new Error(`Readiness returned HTTP ${ready.status}`);
  } catch (error) {
    lastError = error;
  }
  await new Promise(resolve => setTimeout(resolve, 1000));
}
if (ready?.status !== 200) throw lastError || new Error('Restored API did not become ready');

const anonymous = await request('/api/devices');
if (anonymous.status !== 401) throw new Error(`Anonymous API request returned HTTP ${anonymous.status}`);

const csrf = await request('/api/auth/csrf');
if (csrf.status !== 200) throw new Error(`Restored Identity/antiforgery returned HTTP ${csrf.status}`);
const cookies = csrf.headers['set-cookie'] || [];
if (!cookies.some(cookie => cookie.startsWith('__Host-OmniDesk.Csrf=') && /; secure(?:;|$)/i.test(cookie)))
  throw new Error('HTTPS antiforgery cookie was not issued with the restored service');

process.stdout.write('PASS: restored API readiness, Identity/antiforgery, anonymous denial, HTTPS and persisted Caddy CA\n');

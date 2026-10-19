import net from 'node:net';
import assert from 'node:assert/strict';

const host = process.env.CLAMAV_HOST ?? 'clamav';
const port = Number(process.env.CLAMAV_PORT ?? 3310);
const eicar = Buffer.from(String.raw`X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*`);

async function scan(bytes) {
  const socket = net.createConnection({ host, port });
  socket.setTimeout(15_000);
  await new Promise((resolve, reject) => {
    socket.once('connect', resolve);
    socket.once('error', reject);
  });
  const reply = new Promise((resolve, reject) => {
    const chunks = [];
    socket.on('data', chunk => {
      chunks.push(chunk);
      const response = Buffer.concat(chunks);
      if (response.includes(0)) resolve(response.toString('utf8').split('\0', 1)[0]);
    });
    socket.once('error', reject);
    socket.once('timeout', () => reject(new Error('ClamAV probe timed out')));
    socket.once('close', () => reject(new Error('ClamAV closed without a response')));
  });
  const length = Buffer.alloc(4);
  length.writeUInt32BE(bytes.length);
  socket.write(Buffer.concat([Buffer.from('zINSTREAM\0'), length, bytes, Buffer.alloc(4)]));
  const response = await reply;
  socket.end();
  return response;
}

let clean = '', infected = '';
for (let attempt = 0; attempt < 150; attempt++) {
  try {
    clean = await scan(Buffer.from('OmniDesk ClamAV runtime clean fixture'));
    infected = await scan(eicar);
    if (clean.endsWith(': OK') && infected.endsWith('FOUND')) break;
  } catch {}
  await new Promise(resolve => setTimeout(resolve, 2_000));
}
assert.ok(clean.endsWith(': OK'), `Clean fixture was not accepted: ${clean}`);
assert.ok(infected.endsWith('FOUND'), `EICAR fixture was not blocked: ${infected}`);
console.log('PASS: isolated ClamAV runtime scanned clean and EICAR fixtures over the private service network');

// Independent, loopback-only RTMP/AMF fixture. No production server dependency.
// node tests/rtmp-auth-fixture.mjs <dotnet> <EngineTests.dll> <artifact-directory>
import net from 'node:net';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';

const [dotnet, assembly, artifacts] = process.argv.slice(2);
if (!artifacts) throw Error('Expected dotnet, EngineTests.dll and artifact directory');
fs.mkdirSync(artifacts, { recursive: true });
const username = 'fixture-user', password = 'p@ss:word%+', streamKey = 'folder/key?token=a+b%20c&x=2';
const salt = 'sf-salt', challenge = 'sf-challenge';
const md5 = s => crypto.createHash('md5').update(s).digest('base64');
let received, sockets = new Set();

function amf(value) {
  if (value === null) return Buffer.from([5]);
  if (typeof value === 'number') { const b = Buffer.alloc(9); b[0] = 0; b.writeDoubleBE(value, 1); return b; }
  if (typeof value === 'boolean') return Buffer.from([1, +value]);
  if (typeof value === 'string') { const b = Buffer.from(value); const h = Buffer.alloc(3); h[0] = 2; h.writeUInt16BE(b.length, 1); return Buffer.concat([h, b]); }
  return Buffer.concat([Buffer.from([3]), ...Object.entries(value).flatMap(([key, val]) => {
    const name = Buffer.from(key), size = Buffer.alloc(2); size.writeUInt16BE(name.length); return [size, name, amf(val)];
  }), Buffer.from([0, 0, 9])]);
}
function readAmf(buffer) {
  let p = 0;
  const str = () => { const length = buffer.readUInt16BE(p); p += 2; const s = buffer.toString('utf8', p, p + length); p += length; return s; };
  function value() {
    switch (buffer[p++]) {
      case 0: { const n = buffer.readDoubleBE(p); p += 8; return n; }
      case 1: return !!buffer[p++];
      case 2: return str();
      case 5: case 6: return null;
      case 8: p += 4; // fall through to object
      case 3: { const o = {}; while (!(buffer[p] === 0 && buffer[p + 1] === 0 && buffer[p + 2] === 9)) { const name = str(); o[name] = value(); } p += 3; return o; }
      default: throw Error('Unsupported fixture AMF value');
    }
  }
  const result = []; while (p < buffer.length) result.push(value()); return result;
}
function connection(socket) {
  sockets.add(socket); socket.on('close', () => sockets.delete(socket)); socket.on('error', () => {});
  let buffer = Buffer.alloc(0), phase = 0, chunkSize = 128, authenticated = false;
  const chunks = new Map();
  function send(type, payload, stream = 0) {
    const pieces = [], csid = type < 8 ? 2 : 3;
    for (let p = 0; p < payload.length; p += 128) {
      if (!p) { const h = Buffer.alloc(12); h[0] = csid; h.writeUIntBE(payload.length, 4, 3); h[7] = type; h.writeUInt32LE(stream, 8); pieces.push(h); }
      else pieces.push(Buffer.from([0xc0 | csid]));
      pieces.push(payload.subarray(p, p + 128));
    }
    socket.write(Buffer.concat(pieces));
  }
  const invoke = (stream, ...args) => send(20, Buffer.concat(args.map(amf)), stream);
  function message(type, body) {
    if (type === 1) { chunkSize = body.readUInt32BE(0) & 0x7fffffff; if (chunkSize > 65536) throw Error('Oversized fixture chunk'); return; }
    if (type === 8 || type === 9) {
      if (!authenticated) throw Error('Media before authentication');
      if (type === 8) { received.audio++; if (body[1] === 0) received.aac = (body[0] >> 4) === 10; }
      else { received.video++; if (body[1] === 0) received.h264 = (body[0] & 15) === 7; }
      return;
    }
    if (type !== 20) return;
    const [command, tx, object, argument] = readAmf(body);
    if (command === 'connect') {
      // Keep + in base64 intact; URLSearchParams would reinterpret it as a space.
      const query = Object.fromEntries((object.app.split('?')[1] || '').split('&').filter(Boolean).map(s => { const i = s.indexOf('='); return [s.slice(0, i), s.slice(i + 1)]; }));
      if (!query.response) {
        received.challenges++;
        invoke(0, '_error', tx, null, { level: 'error', code: 'NetConnection.Connect.Rejected', description: `authmod=adobe ?reason=needauth&user=${username}&salt=${salt}&challenge=${challenge}` });
      } else if (query.user === username && query.response === md5(md5(username + salt + password) + challenge + query.challenge)) {
        authenticated = true; received.authenticated++;
        if (object.app.split('?')[0] !== 'live/application') throw Error('Wrong application path');
        const window = Buffer.alloc(4); window.writeUInt32BE(2500000); send(5, window);
        invoke(0, '_result', tx, { fmsVer: 'FMS/3,5,7,7009', capabilities: 31 }, { level: 'status', code: 'NetConnection.Connect.Success', description: 'Accepted', objectEncoding: 0 });
      } else {
        received.rejected++;
        invoke(0, '_error', tx, null, { level: 'error', code: 'NetConnection.Connect.Rejected', description: 'authmod=adobe ?reason=authfailed' });
      }
    } else if (command === 'createStream') invoke(0, '_result', tx, null, 1);
    else if (command === 'releaseStream' || command === 'FCPublish') invoke(0, '_result', tx, null, null);
    else if (command === 'publish') {
      if (!authenticated || argument !== streamKey) throw Error('Wrong publish key or authentication state');
      received.published++;
      const begin = Buffer.alloc(6); begin.writeUInt32BE(1, 2); send(4, begin);
      invoke(1, 'onStatus', 0, null, { level: 'status', code: 'NetStream.Publish.Start', description: 'Publishing' });
    }
  }
  socket.on('data', data => {
    try {
      buffer = Buffer.concat([buffer, data]);
      if (phase === 0) {
        if (buffer.length < 1537) return;
        const s1 = Buffer.alloc(1536); crypto.randomFillSync(s1, 8);
        socket.write(Buffer.concat([Buffer.from([3]), s1, buffer.subarray(1, 1537)])); buffer = buffer.subarray(1537); phase = 1;
      }
      if (phase === 1) { if (buffer.length < 1536) return; buffer = buffer.subarray(1536); phase = 2; }
      while (buffer.length) {
        let p = 1, csid = buffer[0] & 63; const fmt = buffer[0] >> 6;
        if (csid === 0) { if (buffer.length < 2) return; csid = 64 + buffer[p++]; }
        else if (csid === 1) { if (buffer.length < 3) return; csid = 64 + buffer[p] + 256 * buffer[p + 1]; p += 2; }
        const headerLength = [11, 7, 3, 0][fmt]; if (buffer.length < p + headerLength) return;
        const old = chunks.get(csid);
        let length = old?.length, type = old?.type, stream = old?.stream, extended = old?.extended;
        if (fmt < 3) extended = buffer.readUIntBE(p, 3) === 0xffffff;
        if (fmt < 2) { length = buffer.readUIntBE(p + 3, 3); type = buffer[p + 6]; }
        if (fmt === 0) stream = buffer.readUInt32LE(p + 7);
        p += headerLength; if (extended) p += 4;
        if (length === undefined || length > 16 * 1024 * 1024) throw Error('Invalid RTMP fixture message header');
        const continuing = old && old.read < old.length;
        const read = continuing ? old.read : 0, count = Math.min(chunkSize, length - read);
        if (buffer.length < p + count) return;
        const state = continuing ? old : { length, type, stream, extended, read: 0, pieces: [] };
        state.pieces.push(buffer.subarray(p, p + count)); state.read += count; chunks.set(csid, state); buffer = buffer.subarray(p + count);
        if (state.read === state.length) message(state.type, Buffer.concat(state.pieces));
      }
    } catch (error) { received.error = error.message; socket.destroy(); }
  });
}
const server = net.createServer(connection);
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
try {
  for (const valid of [true, false]) {
    received = { challenges: 0, authenticated: 0, rejected: 0, published: 0, video: 0, audio: 0, h264: false, aac: false };
    const file = path.resolve(artifacts, valid ? 'auth-ok.stageflow' : 'auth-rejected.stageflow');
    fs.writeFileSync(file, JSON.stringify({ schemaVersion: 2, stream: { hlsEnabled: false, videoBitrateKbps: 2000, keyframeSeconds: 2, bFrames: 2, profile: 'High' }, networkOutputs: [{
      enabled: true, protocol: 'Rtmp', url: `rtmp://127.0.0.1:${server.address().port}/live/application`, streamKey, authenticationEnabled: true, username, password: valid ? password : 'incorrect-fixture-password'
    }] }));
    const result = await new Promise((resolve, reject) => {
      const child = spawn(dotnet, [assembly, '--network-project', file], { windowsHide: true }); let log = '';
      const timeout = setTimeout(() => { child.kill(); reject(Error('RTMP fixture timed out')); }, 25000);
      child.stdout.on('data', s => { log += s; }); child.stderr.on('data', s => { log += s; });
      child.on('error', reject); child.on('exit', code => { clearTimeout(timeout); resolve({ code, log }); });
    });
    fs.writeFileSync(path.resolve(artifacts, valid ? 'auth-ok.log' : 'auth-rejected.log'), result.log);
    if (received.error || !received.challenges || (valid ? result.code !== 0 || !received.authenticated || !received.published || received.video < 150 || received.audio < 200 || !received.h264 || !received.aac
      : result.code === 0 || !received.rejected || received.published || received.video || received.audio)) throw Error(JSON.stringify({ valid, result, received }));
    console.log('PASS RTMP ' + (valid ? 'challenge authentication, exact stream key, H.264 and AAC' : 'incorrect credentials rejected before publishing') + ': ' + JSON.stringify(received));
    for (const socket of sockets) socket.destroy();
  }
} finally { for (const socket of sockets) socket.destroy(); server.close(); }

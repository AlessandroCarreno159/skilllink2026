require('dotenv').config();

const crypto = require('crypto');
const http = require('http');
const express = require('express');
const cors = require('cors');
const { Server } = require('socket.io');
const { createAdapter } = require('@socket.io/redis-adapter');
const Redis = require('ioredis');

const PORT = Number(process.env.PORT || 3001);
const REDIS_URL = process.env.REDIS_URL || 'redis://localhost:6379';
const REALTIME_SECRET = process.env.REALTIME_SECRET || '';
const EVENTS_CHANNEL = process.env.REALTIME_CHANNEL || 'skilllink:events';
const PRESENCE_TTL = Number(process.env.PRESENCE_TTL || 120);
const CHAT_HISTORY_LIMIT = Number(process.env.CHAT_HISTORY_LIMIT || 200);
const CORS_ORIGINS = (process.env.CORS_ORIGIN ||
  'http://localhost:5000,https://localhost:5001,http://localhost:5276,https://localhost:7189,http://localhost:8080,https://localhost:8080')
  .split(',')
  .map((origin) => origin.trim())
  .filter(Boolean);

if (!REALTIME_SECRET) {
  console.error('[realtime] Falta REALTIME_SECRET. Debe coincidir con Realtime:Secret de ASP.NET Core.');
  process.exit(1);
}

const app = express();
app.use(cors({ origin: CORS_ORIGINS, credentials: true }));
app.use(express.json());

const server = http.createServer(app);
const io = new Server(server, {
  cors: {
    origin: CORS_ORIGINS,
    methods: ['GET', 'POST'],
    credentials: true
  },
  transports: ['websocket', 'polling']
});

const redisOptions = {
  maxRetriesPerRequest: null,
  enableReadyCheck: true
};

const pubClient = new Redis(REDIS_URL, redisOptions);
const subClient = pubClient.duplicate();
const eventsSub = pubClient.duplicate();

for (const [name, client] of [
  ['pub', pubClient],
  ['sub', subClient],
  ['events', eventsSub]
]) {
  client.on('error', (err) => {
    console.error(`[realtime] Redis ${name} error:`, err.message);
  });
}

const userRoom = (userId) => `user:${userId}`;
const presenceKey = (userId) => `skilllink:presence:${userId}`;
const chatKey = (room) => `skilllink:chat:${room}`;
const chatRoomFor = (a, b) => `chat:${[String(a), String(b)].sort().join(':')}`;

function isValidId(value) {
  return typeof value === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(value);
}

function parseJson(value) {
  try {
    return JSON.parse(value);
  } catch {
    return null;
  }
}

function verifyToken(token) {
  if (!token || !REALTIME_SECRET) return null;
  const parts = String(token).split('.');
  if (parts.length !== 2) return null;

  const [body, signature] = parts;
  const expected = crypto
    .createHmac('sha256', REALTIME_SECRET)
    .update(body)
    .digest('base64url');

  const receivedBuffer = Buffer.from(signature);
  const expectedBuffer = Buffer.from(expected);
  if (receivedBuffer.length !== expectedBuffer.length ||
      !crypto.timingSafeEqual(receivedBuffer, expectedBuffer)) {
    return null;
  }

  const payload = parseJson(Buffer.from(body, 'base64url').toString('utf8'));
  if (!payload || !isValidId(String(payload.sub || '')) || !payload.exp) return null;
  if (Number(payload.exp) < Math.floor(Date.now() / 1000)) return null;

  return payload;
}

async function presenceAdd(userId, socketId) {
  const key = presenceKey(userId);
  await pubClient.sadd(key, socketId);
  await pubClient.expire(key, PRESENCE_TTL);
}

async function presenceRefresh(userId, socketId) {
  const key = presenceKey(userId);
  await pubClient.sadd(key, socketId);
  await pubClient.expire(key, PRESENCE_TTL);
}

async function presenceRemove(userId, socketId) {
  const key = presenceKey(userId);
  await pubClient.srem(key, socketId);
  const remaining = await pubClient.scard(key);
  if (remaining === 0) await pubClient.del(key);
}

io.use((socket, next) => {
  const payload = verifyToken(socket.handshake.auth?.token);
  if (!payload) return next(new Error('unauthorized'));
  socket.data.user = {
    id: String(payload.sub),
    name: String(payload.name || payload.sub),
    role: String(payload.role || '')
  };
  next();
});

io.on('connection', async (socket) => {
  const user = socket.data.user;

  socket.join(userRoom(user.id));
  try {
    await presenceAdd(user.id, socket.id);
  } catch (err) {
    console.error('[realtime] No se pudo registrar presencia:', err.message);
  }
  socket.emit('realtime:ready', { userId: user.id, socketId: socket.id });

  socket.on('presence:ping', async () => {
    try {
      await presenceRefresh(user.id, socket.id);
    } catch {
      /* Redis caído: no romper la conexión del cliente. */
    }
  });

  socket.on('presence:get', async (payload, ack) => {
    const ids = Array.isArray(payload?.userIds) ? payload.userIds.slice(0, 100) : [];
    const presence = {};
    for (const id of ids) {
      if (!isValidId(id)) continue;
      try {
        presence[id] = (await pubClient.exists(presenceKey(id))) === 1;
      } catch {
        presence[id] = false;
      }
    }
    if (typeof ack === 'function') ack({ ok: true, presence });
  });

  socket.on('chat:open', async (payload, ack) => {
    const peerId = String(payload?.peerId || '').trim();
    if (!isValidId(peerId) || peerId === user.id) {
      if (typeof ack === 'function') ack({ ok: false, error: 'peerId inválido' });
      return;
    }

    const room = chatRoomFor(user.id, peerId);
    socket.join(room);

    let messages = [];
    try {
      const raw = await pubClient.lrange(chatKey(room), -50, -1);
      messages = raw.map(parseJson).filter(Boolean);
    } catch (err) {
      console.error('[realtime] No se pudo leer historial:', err.message);
    }

    const data = { room, peerId, messages };
    socket.emit('chat:history', data);
    if (typeof ack === 'function') ack({ ok: true, ...data });
  });

  socket.on('chat:history', async (payload, ack) => {
    const peerId = String(payload?.peerId || '').trim();
    if (!isValidId(peerId) || peerId === user.id) {
      if (typeof ack === 'function') ack({ ok: false, error: 'peerId inválido' });
      return;
    }

    const room = chatRoomFor(user.id, peerId);
    socket.join(room);

    let messages = [];
    try {
      const raw = await pubClient.lrange(chatKey(room), -50, -1);
      messages = raw.map(parseJson).filter(Boolean);
    } catch (err) {
      console.error('[realtime] No se pudo leer historial:', err.message);
    }

    const data = { room, peerId, messages };
    socket.emit('chat:history', data);
    if (typeof ack === 'function') ack({ ok: true, ...data });
  });

  socket.on('chat:send', async (payload, ack) => {
    const peerId = String(payload?.peerId || '').trim();
    const text = String(payload?.text || '').trim();

    if (!isValidId(peerId) || peerId === user.id) {
      if (typeof ack === 'function') ack({ ok: false, error: 'peerId inválido' });
      return;
    }
    if (!text || text.length > 2000) {
      if (typeof ack === 'function') ack({ ok: false, error: 'Mensaje vacío o demasiado largo' });
      return;
    }

    const room = chatRoomFor(user.id, peerId);
    socket.join(room);

    const message = {
      id: crypto.randomUUID(),
      room,
      from: user.id,
      fromName: user.name,
      to: peerId,
      text,
      at: new Date().toISOString()
    };

    try {
      await pubClient
        .multi()
        .rpush(chatKey(room), JSON.stringify(message))
        .ltrim(chatKey(room), -CHAT_HISTORY_LIMIT, -1)
        .expire(chatKey(room), 60 * 60 * 24 * 7)
        .exec();
    } catch (err) {
      console.error('[realtime] No se pudo guardar el mensaje:', err.message);
    }

    io.to(room).emit('chat:message', message);

    try {
      const peerSockets = await io.in(userRoom(peerId)).fetchSockets();
      const peerInRoom = peerSockets.some((s) => s.rooms.has(room));
      if (!peerInRoom) io.to(userRoom(peerId)).emit('chat:message', message);
    } catch {
      io.to(userRoom(peerId)).emit('chat:message', message);
    }

    if (typeof ack === 'function') ack({ ok: true, message });
  });

  socket.on('chat:typing', (payload) => {
    const peerId = String(payload?.peerId || '').trim();
    if (!isValidId(peerId) || peerId === user.id) return;
    io.to(userRoom(peerId)).emit('chat:typing', {
      from: user.id,
      name: user.name,
      typing: Boolean(payload?.typing)
    });
  });

  socket.on('disconnect', async () => {
    try {
      await presenceRemove(user.id, socket.id);
    } catch {
      /* Redis caído: nada que limpiar. */
    }
  });
});

eventsSub.on('message', (channel, raw) => {
  if (channel !== EVENTS_CHANNEL) return;

  const event = parseJson(raw);
  if (!event || !event.type) return;

  if (event.room) {
    io.to(String(event.room)).emit(event.type, event.payload ?? {});
    return;
  }

  if (event.userId) {
    io.to(userRoom(String(event.userId))).emit(event.type, event.payload ?? {});
  }
});

app.get('/health', async (_req, res) => {
  let redis = 'down';
  try {
    redis = (await pubClient.ping()) === 'PONG' ? 'up' : 'down';
  } catch {
    redis = 'down';
  }
  res.status(redis === 'up' ? 200 : 503).json({
    ok: redis === 'up',
    redis,
    uptime: process.uptime(),
    connections: io.engine.clientsCount
  });
});

app.get('/', (_req, res) => {
  res.json({
    name: 'SkillLink realtime gateway',
    socketio: '/socket.io/socket.io.js',
    health: '/health'
  });
});

async function start() {
  await pubClient.ping();
  io.adapter(createAdapter(pubClient, subClient));
  await eventsSub.subscribe(EVENTS_CHANNEL);

  server.listen(PORT, () => {
    console.log(`[realtime] Gateway escuchando en http://localhost:${PORT}`);
    console.log(`[realtime] Redis: ${REDIS_URL}`);
    console.log(`[realtime] CORS: ${CORS_ORIGINS.join(', ')}`);
  });
}

async function shutdown(signal) {
  console.log(`[realtime] Cerrando (${signal})...`);
  io.close();
  server.close();
  await Promise.allSettled([
    pubClient.quit(),
    subClient.quit(),
    eventsSub.quit()
  ]);
  process.exit(0);
}

process.on('SIGINT', () => shutdown('SIGINT'));
process.on('SIGTERM', () => shutdown('SIGTERM'));

start().catch((err) => {
  console.error('[realtime] No se pudo iniciar el gateway:', err);
  process.exit(1);
});

const fs = require('fs');
const { io } = require('socket.io-client');

const [url, token, outputPath] = process.argv.slice(2);
if (!url || !token || !outputPath) {
  console.error('Uso: node Test-ChatbotRealtime.js <url> <token> <archivo-salida>');
  process.exit(2);
}

const events = [];
let finished = false;
const readyPath = `${outputPath}.ready`;

function finish(code) {
  if (finished) return;
  finished = true;
  try {
    fs.writeFileSync(outputPath, JSON.stringify(events));
  } catch (error) {
    console.error('No se pudo escribir el resultado:', error.message);
  }
  socket.close();
  process.exit(code);
}

const socket = io(url, {
  auth: { token },
  transports: ['websocket', 'polling'],
  reconnection: false,
});

socket.on('connect', () => events.push({ type: 'connect' }));
socket.on('realtime:ready', (payload) => {
  events.push({ type: 'realtime:ready', payload });
  fs.writeFileSync(readyPath, 'ready');
});
socket.on('chatbot:token', (payload) => events.push({ type: 'chatbot:token', payload }));
socket.on('chatbot:tool', (payload) => events.push({ type: 'chatbot:tool', payload }));
socket.on('chatbot:done', (payload) => {
  events.push({ type: 'chatbot:done', payload });
  finish(0);
});
socket.on('chatbot:error', (payload) => {
  events.push({ type: 'chatbot:error', payload });
  finish(1);
});
socket.on('connect_error', (error) => {
  events.push({ type: 'connect_error', payload: { message: error.message } });
  finish(1);
});

setTimeout(() => {
  events.push({ type: 'timeout' });
  finish(3);
}, 60000);

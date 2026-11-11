// Tiempo real de SkillLink: Socket.io + Redis.
// - Se autentica con un token HMAC emitido por /Realtime/Token.
// - Escucha notificaciones/postulaciones y gestiona el chat privado.
(function () {
  'use strict';

  var cfg = window.__skRealtime;
  if (!cfg || !cfg.url || !window.io) return;

  var state = {
    socket: null,
    me: null,
    peer: null,
    peerName: '',
    room: null,
    seen: Object.create(null),
    typingTimer: null,
    typingHideTimer: null,
    heartbeat: null,
    unreadChats: 0,
    panelOpen: false,
    pendingChat: null,
    connectWaitTimer: null
  };

  var el = {};
  var panel = null;

  function $(id) { return document.getElementById(id); }

  function initEls() {
    el.panel = $('skChatPanel');
    el.title = $('sk-chat-title');
    el.empty = $('sk-chat-empty');
    el.messages = $('sk-chat-messages');
    el.typing = $('sk-chat-typing');
    el.form = $('sk-chat-form');
    el.input = $('sk-chat-input');
  }

  function chatRoomFor(a, b) {
    return 'chat:' + [String(a), String(b)].sort().join(':');
  }

  function setNotifBadge(total) {
    var badge = document.getElementById('notif-badge');
    if (!badge) return;
    if (total > 0) {
      badge.textContent = total > 99 ? '99+' : String(total);
      badge.classList.remove('d-none');
    } else {
      badge.classList.add('d-none');
    }
  }

  function refreshNotifBadge() {
    fetch('/Notifications/NoLeidas', { credentials: 'same-origin' })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) { if (data) setNotifBadge(data.total || 0); })
      .catch(function () { /* polling sigue activo */ });
  }

  function updateChatBadge() {
    if (!el.badge) return;
    if (state.unreadChats > 0) {
      el.badge.textContent = state.unreadChats > 99 ? '99+' : String(state.unreadChats);
      el.badge.classList.remove('d-none');
    } else {
      el.badge.classList.add('d-none');
    }
  }

  function toast(titulo, mensaje, enlace, alHacerClic) {
    var main = document.querySelector('main.container');
    if (!main) return;

    var div = document.createElement('div');
    div.className = 'alert alert-info alert-dismissible fade show sk-toast sk-toast-info';
    div.setAttribute('role', 'alert');

    var icon = document.createElement('i');
    icon.className = 'bi bi-bell-fill';

    var strong = document.createElement('strong');
    strong.textContent = titulo;

    var span = document.createElement('span');
    span.textContent = mensaje ? ' ' + mensaje : '';

    div.appendChild(icon);
    div.appendChild(strong);
    div.appendChild(span);

    if (enlace) {
      var link = document.createElement('a');
      link.href = enlace;
      link.className = 'ms-2 small fw-semibold';
      link.textContent = 'Ver';
      div.appendChild(link);
    } else if (typeof alHacerClic === 'function') {
      var button = document.createElement('button');
      button.type = 'button';
      button.className = 'btn btn-link btn-sm ms-1 p-0 small';
      button.textContent = 'Abrir';
      button.addEventListener('click', alHacerClic);
      div.appendChild(button);
    }

    var close = document.createElement('button');
    close.type = 'button';
    close.className = 'btn-close';
    close.setAttribute('data-bs-dismiss', 'alert');
    div.appendChild(close);

    main.prepend(div);
    setTimeout(function () { div.remove(); }, 10000);
  }

  function scrollChat() {
    if (el.messages) el.messages.scrollTop = el.messages.scrollHeight;
  }

  function renderMessage(msg) {
    if (!el.messages) return;
    var mine = state.me && msg.from === state.me.userId;

    var wrap = document.createElement('div');
    wrap.className = 'sk-chat-msg ' + (mine ? 'sk-chat-msg-me' : 'sk-chat-msg-peer');

    var meta = document.createElement('div');
    meta.className = 'sk-chat-meta';
    var hora = '';
    try { hora = new Date(msg.at).toLocaleTimeString('es-PE', { hour: '2-digit', minute: '2-digit' }); } catch (e) { hora = ''; }
    meta.textContent = (mine ? 'Tú' : (msg.fromName || 'Usuario')) + (hora ? ' · ' + hora : '');

    var bubble = document.createElement('div');
    bubble.className = 'sk-chat-bubble';
    bubble.textContent = msg.text;

    wrap.appendChild(meta);
    wrap.appendChild(bubble);
    el.messages.appendChild(wrap);
  }

  function appendMessage(msg) {
    if (!msg || !msg.id || state.seen[msg.id]) return false;
    state.seen[msg.id] = true;
    renderMessage(msg);
    scrollChat();
    return true;
  }

  function showPanel() {
    if (!el.panel) return;
    if (!panel && window.bootstrap && window.bootstrap.Offcanvas) {
      panel = new window.bootstrap.Offcanvas(el.panel);
    }
    if (panel) panel.show();
  }

  function connectionReady() {
    return !!(state.socket && state.socket.connected);
  }

  function setChatStatus(text, mode) {
    var status = document.getElementById('sk-chat-status');
    if (!status) return;
    status.textContent = text;
    status.classList.remove('is-online', 'is-connecting', 'is-offline');
    status.classList.add('is-' + (mode || 'online'));
  }

  function renderConnecting() {
    if (!el.messages) return;
    el.messages.innerHTML = '';

    var box = document.createElement('div');
    box.className = 'sk-chat-connecting';

    var spinner = document.createElement('span');
    spinner.className = 'sk-chat-connecting-spinner';

    var label = document.createElement('span');
    label.textContent = 'Conectando con ' + state.peerName + '…';

    box.appendChild(spinner);
    box.appendChild(label);
    el.messages.appendChild(box);
  }

  // Muestra el panel de inmediato con el nombre del contacto, sin exigir que
  // el socket ya esté listo (evita el aviso "chat no disponible" prematuro).
  function showChatShell(peerId, name) {
    state.peer = String(peerId);
    state.peerName = name || 'Usuario';
    state.room = state.me ? chatRoomFor(state.me.userId, state.peer) : null;
    state.seen = Object.create(null);
    state.unreadChats = 0;
    updateChatBadge();

    if (el.title) el.title.textContent = state.peerName;
    if (el.empty) el.empty.classList.add('d-none');
    if (el.form) el.form.classList.add('d-none');
    if (el.typing) el.typing.textContent = '';
    renderConnecting();
    showPanel();
  }

  // Ejecuta la apertura real una vez que el socket está conectado.
  function commitChat() {
    if (!state.pendingChat || !connectionReady()) return;
    var pending = state.pendingChat;
    state.pendingChat = null;
    if (state.connectWaitTimer) {
      clearTimeout(state.connectWaitTimer);
      state.connectWaitTimer = null;
    }

    state.peer = pending.peerId;
    state.peerName = pending.name;
    state.room = chatRoomFor(state.me.userId, state.peer);
    state.seen = Object.create(null);

    if (el.title) el.title.textContent = state.peerName;
    if (el.empty) el.empty.classList.add('d-none');
    if (el.messages) el.messages.innerHTML = '';
    if (el.typing) el.typing.textContent = '';
    if (el.form) el.form.classList.remove('d-none');

    setChatStatus('En línea', 'online');
    state.socket.emit('chat:open', { peerId: state.peer });
    if (el.input) setTimeout(function () { el.input.focus(); }, 350);
  }

  function openChat(peerId, name) {
    if (!peerId || (state.me && peerId === state.me.userId)) return;

    state.pendingChat = { peerId: String(peerId), name: name || 'Usuario' };
    showChatShell(state.pendingChat.peerId, state.pendingChat.name);

    if (connectionReady()) {
      commitChat();
      return;
    }

    setChatStatus('Conectando…', 'connecting');
    if (!state.connectWaitTimer) {
      state.connectWaitTimer = setTimeout(function () {
        state.connectWaitTimer = null;
        if (connectionReady() || !state.pendingChat) return;

        setChatStatus('Sin conexión', 'offline');
        var retryPeer = state.pendingChat.peerId;
        var retryName = state.pendingChat.name;
        toast('Chat no disponible', 'El servicio de tiempo real no responde. Intenta de nuevo en unos segundos.', null, function () {
          openChat(retryPeer, retryName);
        });
      }, 12000);
    }
  }

  function onChatMessage(msg) {
    if (!msg || !msg.id) return;
    var isCurrentPeer = state.peer && (msg.from === state.peer || msg.to === state.peer);

    if (isCurrentPeer) {
      if (appendMessage(msg) && msg.from === state.peer && !state.panelOpen) {
        state.unreadChats++;
        updateChatBadge();
        toast('Nuevo mensaje', (msg.fromName || 'Usuario') + ': ' + msg.text);
      }
      return;
    }

    if (state.me && msg.from !== state.me.userId) {
      state.unreadChats++;
      updateChatBadge();
      toast('Nuevo mensaje', (msg.fromName || 'Usuario') + ': ' + msg.text, null, function () {
        openChat(msg.from, msg.fromName || 'Usuario');
      });
    }
  }

  function onHistory(data) {
    if (!data || data.peerId !== state.peer) return;
    state.room = data.room || state.room;
    state.seen = Object.create(null);
    if (el.messages) el.messages.innerHTML = '';
    (data.messages || []).forEach(appendMessage);
    scrollChat();
    if (el.form) el.form.classList.remove('d-none');
    setChatStatus('En línea', 'online');
  }

  function onTyping(data) {
    if (!data || data.from !== state.peer || !el.typing) return;
    clearTimeout(state.typingHideTimer);
    if (data.typing) {
      el.typing.textContent = (data.name || 'Usuario') + ' está escribiendo...';
      state.typingHideTimer = setTimeout(function () { el.typing.textContent = ''; }, 2500);
    } else {
      el.typing.textContent = '';
    }
  }

  function startHeartbeat() {
    stopHeartbeat();
    state.heartbeat = setInterval(function () {
      if (state.socket && state.socket.connected) state.socket.emit('presence:ping');
    }, 45000);
  }

  function stopHeartbeat() {
    if (state.heartbeat) clearInterval(state.heartbeat);
    state.heartbeat = null;
  }

  function bindUi() {
    if (el.panel) {
      el.panel.addEventListener('show.bs.offcanvas', function () {
        state.panelOpen = true;
        state.unreadChats = 0;
        updateChatBadge();
        scrollChat();
      });
      el.panel.addEventListener('hidden.bs.offcanvas', function () {
        state.panelOpen = false;
      });
    }

    if (el.form) {
      el.form.addEventListener('submit', function (event) {
        event.preventDefault();
        if (!state.peer || !state.socket || !state.socket.connected) return;

        var text = el.input ? el.input.value.trim() : '';
        if (!text) return;
        el.input.value = '';
        el.input.focus();

        state.socket.emit('chat:send', { peerId: state.peer, text: text }, function (res) {
          if (!res || !res.ok) {
            toast('No se pudo enviar', (res && res.error) || 'Intenta de nuevo.');
            return;
          }
          if (res.message) appendMessage(res.message);
        });
      });
    }

    if (el.input) {
      el.input.addEventListener('input', function () {
        if (!state.peer || !state.socket || !state.socket.connected) return;
        state.socket.emit('chat:typing', { peerId: state.peer, typing: true });
        clearTimeout(state.typingTimer);
        state.typingTimer = setTimeout(function () {
          if (state.socket && state.socket.connected) {
            state.socket.emit('chat:typing', { peerId: state.peer, typing: false });
          }
        }, 1200);
      });
    }

    // Delegación: cualquier botón con data-chat-user abre el chat privado.
    document.addEventListener('click', function (event) {
      var target = event.target.closest('[data-chat-user]');
      if (!target) return;
      event.preventDefault();
      openChat(target.getAttribute('data-chat-user'), target.getAttribute('data-chat-name'));
    });
  }

  function connect() {
    fetch('/Realtime/Token', {
      credentials: 'same-origin',
      headers: { 'Accept': 'application/json' }
    })
      .then(function (response) { return response.ok ? response.json() : null; })
      .then(function (data) {
        if (!data || !data.enabled || !data.token) {
          console.warn('[realtime] Token no disponible. Revisa Realtime:Secret en appsettings.');
          return;
        }

        state.me = data;
        var socket = window.io(String(cfg.url).replace(/\/$/, ''), {
          auth: { token: data.token },
          transports: ['websocket', 'polling'],
          reconnection: true,
          reconnectionAttempts: Infinity,
          reconnectionDelay: 1000,
          reconnectionDelayMax: 15000
        });
        state.socket = socket;

        socket.on('connect', function () {
          startHeartbeat();
          if (state.pendingChat) {
            commitChat();
          } else if (state.peer) {
            setChatStatus('En línea', 'online');
            socket.emit('chat:open', { peerId: state.peer });
          }
        });
        socket.on('disconnect', function () {
          stopHeartbeat();
          if (state.peer) setChatStatus('Reconectando…', 'connecting');
        });
        socket.on('connect_error', function (error) {
          console.warn('[realtime] No se pudo conectar:', error && error.message);
          if (state.peer) setChatStatus('Sin conexión', 'offline');
        });

        socket.on('notification:new', function (payload) {
          refreshNotifBadge();
          toast(payload && payload.titulo ? payload.titulo : 'Notificación',
            (payload && payload.mensaje) || '', payload && payload.enlace);
        });

        socket.on('application:new', function (payload) {
          refreshNotifBadge();
          toast('Nueva postulación',
            ((payload && payload.trabajadorNombre) || 'Un postulante') +
            ' postuló a ' + ((payload && payload.publicacionTitulo) || 'tu oferta') + '.',
            '/Solicitudes/Recibidas');
        });

        socket.on('application:status', function (payload) {
          refreshNotifBadge();
          toast('Postulación actualizada',
            'Tu postulación quedó ' + ((payload && payload.estado) || 'actualizada') + '.',
            '/Solicitudes/MisPostulaciones');
        });

        socket.on('chat:message', onChatMessage);
        socket.on('chat:history', onHistory);
        socket.on('chat:typing', onTyping);

        window.addEventListener('beforeunload', function () { socket.disconnect(); });
        window.SkillLinkChat = { open: openChat };
        window.SkillLinkRealtime = { socket: socket, me: data };
        window.dispatchEvent(new CustomEvent('sk:realtime:ready', {
          detail: { socket: socket, me: data }
        }));
      })
      .catch(function (error) {
        console.warn('[realtime] Error obteniendo el token:', error);
      });
  }

  function boot() {
    initEls();
    bindUi();
    connect();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();

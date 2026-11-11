// Asistente virtual SkillLink: HTTP + streaming por Socket.io/Redis.
(function () {
  'use strict';

  var root = document.getElementById('sk-bot-widget');
  if (!root) return;

  var el = {
    toggle: document.getElementById('sk-bot-toggle'),
    panel: document.getElementById('sk-bot-panel'),
    minimize: document.getElementById('sk-bot-minimize'),
    pin: document.getElementById('sk-bot-pin'),
    close: document.getElementById('sk-bot-close'),
    clear: document.getElementById('sk-bot-clear'),
    messages: document.getElementById('sk-bot-messages'),
    suggestions: document.getElementById('sk-bot-suggestions'),
    status: document.getElementById('sk-bot-status'),
    form: document.getElementById('sk-bot-form'),
    input: document.getElementById('sk-bot-input'),
    send: document.getElementById('sk-bot-send')
  };

  var state = {
    conversationId: '',
    pending: null,
    busy: false,
    enabled: true,
    pinned: false
  };

  var MAX_CHARS = 1000;
  var PIN_KEY = 'skilllink.chatbot.pinned';

  function csrfToken() {
    var input = document.querySelector('#sk-bot-antiforgery input[name="__RequestVerificationToken"]');
    return input ? input.value : '';
  }

  function postJson(url, body) {
    return fetch(url, {
      method: 'POST',
      credentials: 'same-origin',
      headers: {
        'Accept': 'application/json',
        'Content-Type': 'application/json',
        'X-CSRF-TOKEN': csrfToken()
      },
      body: JSON.stringify(body || {})
    });
  }

  function setStatus(text, isError) {
    if (!el.status) return;
    el.status.textContent = text || '';
    el.status.classList.toggle('d-none', !text);
    el.status.classList.toggle('sk-bot-status-error', !!isError);
  }

  function hideStatus() {
    setStatus('', false);
  }

  function scrollMessages() {
    if (el.messages) el.messages.scrollTop = el.messages.scrollHeight;
  }

  function openPanel() {
    if (!el.panel) return;
    el.panel.classList.remove('d-none');
    if (el.toggle) el.toggle.setAttribute('aria-expanded', 'true');
    scrollMessages();
    if (el.input) setTimeout(function () { el.input.focus(); }, 120);
  }

  function closePanel() {
    if (!el.panel) return;
    el.panel.classList.add('d-none');
    if (el.toggle) el.toggle.setAttribute('aria-expanded', 'false');
  }

  function loadPinned() {
    try {
      return window.localStorage.getItem(PIN_KEY) === '1';
    } catch (error) {
      return false;
    }
  }

  function savePinned() {
    try {
      window.localStorage.setItem(PIN_KEY, state.pinned ? '1' : '0');
    } catch (error) {
      /* Sin persistencia si el navegador bloquea localStorage. */
    }
  }

  function updatePinButton() {
    if (!el.pin) return;
    var label = state.pinned ? 'Desfijar ventana' : 'Fijar ventana';
    el.pin.classList.toggle('activo', state.pinned);
    el.pin.setAttribute('aria-pressed', state.pinned ? 'true' : 'false');
    el.pin.setAttribute('title', label);
    el.pin.setAttribute('aria-label', label);
    var icon = el.pin.querySelector('i');
    if (icon) icon.className = 'bi ' + (state.pinned ? 'bi-pin-angle-fill' : 'bi-pin-angle');
  }

  function togglePinned() {
    state.pinned = !state.pinned;
    savePinned();
    updatePinButton();
  }

  function makeMessage(role, text) {
    var wrap = document.createElement('div');
    wrap.className = 'sk-bot-msg ' + (role === 'user' ? 'sk-bot-msg-user' : 'sk-bot-msg-bot');

    var meta = document.createElement('div');
    meta.className = 'sk-bot-meta';
    meta.textContent = role === 'user' ? 'Tú · ahora' : 'SkillLink Assist · ahora';

    var bubble = document.createElement('div');
    bubble.className = 'sk-bot-bubble';
    bubble.textContent = text || '';

    wrap.appendChild(meta);
    wrap.appendChild(bubble);
    if (el.messages) el.messages.appendChild(wrap);
    scrollMessages();
    return { wrap: wrap, bubble: bubble, text: text || '' };
  }

  function startBotMessage() {
    var msg = makeMessage('bot', '');
    msg.bubble.innerHTML = '<span class="sk-bot-typing" aria-label="Escribiendo"><span></span><span></span><span></span></span>';
    return {
      wrap: msg.wrap,
      bubble: msg.bubble,
      text: '',
      typing: true,
      messageId: null,
      finalized: false
    };
  }

  function appendToken(turn, text) {
    if (!turn || turn.finalized || !text) return;
    if (turn.typing) {
      turn.bubble.textContent = '';
      turn.typing = false;
    }
    turn.text += text;
    turn.bubble.textContent = turn.text;
    hideStatus();
    scrollMessages();
  }

  function finalizeTurn(turn, data) {
    if (!turn || turn.finalized) return;
    turn.finalized = true;

    if (data && data.conversationId) state.conversationId = data.conversationId;
    if (data && data.messageId) {
      turn.messageId = data.messageId;
      turn.wrap.setAttribute('data-message-id', data.messageId);
    }

    var reply = (data && data.reply) || turn.text || 'No pude generar una respuesta en este momento.';
    turn.bubble.textContent = reply;
    turn.wrap.classList.remove('sk-bot-msg-error');

    if (data && Array.isArray(data.cards)) renderCards(turn.wrap, data.cards);
    if (turn.messageId) renderFeedback(turn.wrap, turn.messageId);
    renderSuggestions((data && data.suggestions) || []);
    hideStatus();

    if (state.pending === turn) state.pending = null;
    setBusy(false);
    scrollMessages();
  }

  function failTurn(turn, mensaje) {
    if (!turn || turn.finalized) return;
    turn.finalized = true;
    turn.wrap.classList.add('sk-bot-msg-error');
    turn.bubble.textContent = mensaje || 'No pude procesar tu consulta. Intenta nuevamente.';
    turn.typing = false;
    setStatus(mensaje || 'No pude procesar tu consulta.', true);

    if (state.pending === turn) state.pending = null;
    setBusy(false);
    renderSuggestions([]);
    scrollMessages();
  }

  function setBusy(busy) {
    state.busy = busy;
    if (el.send) el.send.disabled = busy || !state.enabled;
    if (el.input) el.input.disabled = !state.enabled;
  }

  function renderCards(wrap, cards) {
    if (!Array.isArray(cards) || cards.length === 0) return;
    var box = document.createElement('div');
    box.className = 'sk-bot-cards';

    cards.slice(0, 6).forEach(function (card) {
      if (!card || !card.title) return;

      var href = typeof card.url === 'string' && card.url.length > 0 ? card.url : null;
      var item = document.createElement(href ? 'a' : 'div');
      item.className = 'sk-bot-card';
      if (href) item.href = href;

      var img = document.createElement('img');
      img.className = 'sk-bot-card-img';
      img.alt = '';
      img.loading = 'lazy';
      img.src = card.imageUrl || '/img/placeholder.svg';
      img.addEventListener('error', function () {
        img.src = '/img/placeholder.svg';
      });

      var body = document.createElement('div');
      body.className = 'sk-bot-card-body';

      var title = document.createElement('div');
      title.className = 'sk-bot-card-title';
      title.textContent = card.title;

      var sub = document.createElement('div');
      sub.className = 'sk-bot-card-sub';
      sub.textContent = card.subtitle || card.reason || '';

      var meta = document.createElement('div');
      meta.className = 'sk-bot-card-meta';
      if (card.city) meta.appendChild(metaItem('bi-geo-alt', card.city));
      if (card.price) meta.appendChild(metaItem('bi-cash', card.price));
      if (card.rating) meta.appendChild(metaItem('bi-star-fill', Number(card.rating).toFixed(1) + (card.ratingTotal ? ' (' + card.ratingTotal + ')' : '')));
      if (card.availability) meta.appendChild(metaItem('bi-calendar-check', card.availability));

      body.appendChild(title);
      if (sub.textContent) body.appendChild(sub);
      if (meta.children.length) body.appendChild(meta);

      if (href) {
        var cta = document.createElement('div');
        cta.className = 'sk-bot-card-cta';
        cta.textContent = card.type === 'tecnico' ? 'Ver perfil' : 'Ver publicación';
        body.appendChild(cta);
      }

      item.appendChild(img);
      item.appendChild(body);
      box.appendChild(item);
    });

    if (box.children.length) wrap.appendChild(box);
  }

  function metaItem(icono, texto) {
    var span = document.createElement('span');
    var i = document.createElement('i');
    i.className = 'bi ' + icono;
    span.appendChild(i);
    span.appendChild(document.createTextNode(String(texto)));
    return span;
  }

  function renderFeedback(wrap, messageId) {
    if (!messageId) return;
    var box = document.createElement('div');
    box.className = 'sk-bot-feedback';

    [
      { rating: 5, icon: 'bi-hand-thumbs-up', label: 'Respuesta útil' },
      { rating: 1, icon: 'bi-hand-thumbs-down', label: 'Respuesta poco útil' }
    ].forEach(function (opcion) {
      var btn = document.createElement('button');
      btn.type = 'button';
      btn.title = opcion.label;
      btn.setAttribute('aria-label', opcion.label);
      var icon = document.createElement('i');
      icon.className = 'bi ' + opcion.icon;
      btn.appendChild(icon);
      btn.addEventListener('click', function () {
        enviarFeedback(messageId, opcion.rating, box, btn);
      });
      box.appendChild(btn);
    });

    wrap.appendChild(box);
  }

  function enviarFeedback(messageId, rating, box, boton) {
    postJson('/Chatbot/Feedback', {
      conversationId: state.conversationId,
      messageId: messageId,
      rating: rating
    })
      .then(function (res) { return res.json().catch(function () { return {}; }); })
      .then(function (data) {
        if (!data || !data.ok) throw new Error((data && data.error) || 'No se pudo guardar.');
        boton.classList.add('activo');
        Array.prototype.forEach.call(box.querySelectorAll('button'), function (b) { b.disabled = true; });
        setStatus('Gracias por tu opinión.', false);
        setTimeout(hideStatus, 2500);
      })
      .catch(function () {
        setStatus('No se pudo guardar tu opinión.', true);
      });
  }

  function renderSuggestions(list) {
    if (!el.suggestions) return;
    el.suggestions.innerHTML = '';
    (list || []).slice(0, 4).forEach(function (texto) {
      if (!texto) return;
      var btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'sk-bot-chip';
      btn.textContent = texto;
      btn.addEventListener('click', function () {
        if (state.busy || !state.enabled) return;
        if (el.input) el.input.value = texto;
        enviar(texto);
      });
      el.suggestions.appendChild(btn);
    });
  }

  function saludo() {
    makeMessage('bot',
      'Hola, soy SkillLink Assist. Puedo ayudarte a buscar técnicos u ofertas, revisar el estado de tus solicitudes y responder dudas sobre la plataforma. ¿Qué necesitas?');
    renderSuggestions([
      'Buscar un técnico',
      'Ver ofertas de trabajo',
      'Estado de mis solicitudes'
    ]);
  }

  function enviar(texto) {
    texto = (texto || '').trim();
    if (!texto || state.busy || !state.enabled) return;
    if (texto.length > MAX_CHARS) {
      setStatus('Tu mensaje es demasiado largo. Usa como máximo ' + MAX_CHARS + ' caracteres.', true);
      return;
    }

    setBusy(true);
    hideStatus();
    renderSuggestions([]);
    makeMessage('user', texto);
    var turn = startBotMessage();
    state.pending = turn;
    setStatus('Pensando...', false);

    if (el.input) {
      el.input.value = '';
      resizeInput();
    }

    postJson('/Chatbot/Message', {
      message: texto,
      conversationId: state.conversationId || null
    })
      .then(function (res) {
        return res.json().catch(function () { return {}; }).then(function (data) {
          return { ok: res.ok, status: res.status, data: data || {} };
        });
      })
      .then(function (resultado) {
        if (!resultado.ok || !resultado.data.ok) {
          var mensaje = resultado.data.error || 'No pude procesar tu consulta.';
          if (resultado.status === 429) {
            var espera = resultado.data.retryAfter || 0;
            if (espera > 0) mensaje += ' Podrás intentar en ' + espera + ' segundos.';
          }
          failTurn(turn, mensaje);
          return;
        }
        finalizeTurn(turn, resultado.data);
      })
      .catch(function () {
        failTurn(turn, 'No se pudo conectar con el asistente. Revisa tu conexión e intenta nuevamente.');
      })
      .finally(function () {
        if (state.pending === turn) state.pending = null;
        setBusy(false);
      });
  }

  function resizeInput() {
    if (!el.input) return;
    el.input.style.height = 'auto';
    el.input.style.height = Math.min(el.input.scrollHeight, 120) + 'px';
  }

  function cargarHistorial() {
    fetch('/Chatbot/History', {
      credentials: 'same-origin',
      headers: { 'Accept': 'application/json' }
    })
      .then(function (res) { return res.ok ? res.json() : null; })
      .then(function (data) {
        if (!data || !data.ok) { saludo(); return; }
        state.conversationId = data.conversationId || '';
        var mensajes = Array.isArray(data.messages) ? data.messages : [];
        if (mensajes.length === 0) {
          saludo();
          return;
        }
        mensajes.slice(-20).forEach(function (m) {
          makeMessage(m.role === 'model' ? 'bot' : 'user', m.text || '');
        });
        renderSuggestions([
          'Buscar un técnico',
          'Ver ofertas de trabajo',
          'Estado de mis solicitudes'
        ]);
      })
      .catch(function () { saludo(); });
  }

  function cargarEstado() {
    fetch('/Chatbot/Status', {
      credentials: 'same-origin',
      headers: { 'Accept': 'application/json' }
    })
      .then(function (res) { return res.ok ? res.json() : null; })
      .then(function (data) {
        state.enabled = !!(data && data.enabled);
        if (!state.enabled) {
          setStatus('El asistente no está disponible en este momento.', true);
          if (el.send) el.send.disabled = true;
          if (el.input) el.input.disabled = true;
        } else if (!state.busy) {
          setBusy(false);
        }
      })
      .catch(function () {
        state.enabled = false;
        setBusy(false);
        setStatus('No se pudo comprobar el estado del asistente.', true);
      });
  }

  function limpiar() {
    if (!window.confirm('¿Empezar una conversación nueva?')) return;
    postJson('/Chatbot/Clear', {})
      .then(function (res) { return res.json().catch(function () { return {}; }); })
      .then(function (data) {
        if (!data || !data.ok) throw new Error('No se pudo limpiar.');
        state.conversationId = '';
        state.pending = null;
        if (el.messages) el.messages.innerHTML = '';
        renderSuggestions([]);
        hideStatus();
        saludo();
      })
      .catch(function () {
        setStatus('No se pudo iniciar una conversación nueva.', true);
      });
  }

  function attachSocket(socket) {
    if (!socket || socket.__skBotAttached) return;
    socket.__skBotAttached = true;

    socket.on('chatbot:token', function (payload) {
      if (!payload || !payload.messageId) return;
      var turn = state.pending;
      if (!turn || turn.finalized) return;
      if (!turn.messageId) {
        turn.messageId = payload.messageId;
        turn.wrap.setAttribute('data-message-id', payload.messageId);
        if (payload.conversationId) state.conversationId = payload.conversationId;
      }
      if (turn.messageId !== payload.messageId) return;
      appendToken(turn, payload.text || '');
    });

    socket.on('chatbot:tool', function (payload) {
      if (!state.pending || state.pending.finalized) return;
      if (payload && payload.status === 'running') setStatus('Consultando información de SkillLink...', false);
    });

    socket.on('chatbot:done', function (payload) {
      if (!payload || !payload.messageId) return;
      var turn = state.pending;
      if (!turn || turn.finalized) return;
      if (turn.messageId && turn.messageId !== payload.messageId) return;
      finalizeTurn(turn, payload);
    });

    socket.on('chatbot:error', function (payload) {
      if (!payload) return;
      var turn = state.pending;
      if (!turn || turn.finalized) return;
      if (turn.messageId && payload.messageId && turn.messageId !== payload.messageId) return;
      failTurn(turn, payload.message || 'No pude procesar tu consulta.');
    });
  }

  function bind() {
    if (el.toggle) el.toggle.addEventListener('click', function () {
      if (!el.panel) return;
      if (el.panel.classList.contains('d-none')) openPanel(); else closePanel();
    });
    if (el.minimize) el.minimize.addEventListener('click', closePanel);
    if (el.pin) el.pin.addEventListener('click', togglePinned);
    if (el.close) el.close.addEventListener('click', closePanel);
    if (el.clear) el.clear.addEventListener('click', limpiar);

    if (el.form) {
      el.form.addEventListener('submit', function (event) {
        event.preventDefault();
        enviar(el.input ? el.input.value : '');
      });
    }

    if (el.input) {
      el.input.addEventListener('input', resizeInput);
      el.input.addEventListener('keydown', function (event) {
        if (event.key === 'Enter' && !event.shiftKey) {
          event.preventDefault();
          enviar(el.input.value);
        }
      });
    }

    document.addEventListener('click', function (event) {
      if (!el.panel || el.panel.classList.contains('d-none')) return;
      if (state.pinned || root.contains(event.target)) return;
      closePanel();
    });

    document.addEventListener('keydown', function (event) {
      if (event.key === 'Escape' && el.panel && !el.panel.classList.contains('d-none')) closePanel();
    });

    window.addEventListener('sk:realtime:ready', function (event) {
      attachSocket(event && event.detail ? event.detail.socket : null);
    });

    if (window.SkillLinkRealtime && window.SkillLinkRealtime.socket) {
      attachSocket(window.SkillLinkRealtime.socket);
    }
  }

  function init() {
    state.pinned = loadPinned();
    updatePinButton();
    bind();
    cargarEstado();
    cargarHistorial();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();

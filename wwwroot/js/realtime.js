// Tiempo real vía PieSocket: actualiza la campana y muestra aviso sin recargar.
// window.__skPie lo inyecta el layout solo si hay ApiKey+Channel configurados.
// El backend publica en UN canal; aquí se filtra por el usuario actual.
(function () {
  var cfg = window.__skPie;
  var myId = window.__skUserId;
  if (!cfg || !cfg.key || !cfg.channel || !myId) return;

  var delay = 2000;
  function bumpBadge() {
    fetch('/Notifications/NoLeidas').then(function (r) {
      if (!r.ok) return null;
      return r.json();
    }).then(function (data) {
      if (!data) return;
      var badge = document.getElementById('notif-badge');
      if (badge && data.total > 0) {
        badge.textContent = data.total;
        badge.classList.remove('d-none');
      }
    }).catch(function () { });
  }
  function toast(titulo, mensaje) {
    var main = document.querySelector('main.container');
    if (!main) return;
    var div = document.createElement('div');
    div.className = 'alert alert-info alert-dismissible fade show sk-toast sk-toast-info';
    div.setAttribute('role', 'alert');
    div.innerHTML = '<i class="bi bi-bell-fill"></i> <strong></strong> <span></span>'
      + '<button type="button" class="btn-close" data-bs-dismiss="alert"></button>';
    div.querySelector('strong').textContent = titulo;
    div.querySelector('span').textContent = ' ' + mensaje;
    main.prepend(div);
    setTimeout(function () { div.remove(); }, 8000);
  }
  function handle(raw) {
    var msg = raw;
    try {
      var outer = JSON.parse(raw);
      msg = (typeof outer === 'string') ? JSON.parse(outer) : outer;
    } catch (e) { return; }
    if (!msg || msg.usuarioId !== myId) return;
    bumpBadge();
    toast(msg.titulo || 'Notificación', msg.mensaje || '');
  }
  function connect() {
    var url = cfg.endpoint.replace(/\/$/, '')
      + '/v3/' + encodeURIComponent(cfg.channel)
      + '?api_key=' + encodeURIComponent(cfg.key) + '&notify_self=1';
    var ws;
    try {
      ws = new WebSocket(url);
    } catch (e) {
      setTimeout(connect, delay);
      return;
    }
    ws.onmessage = function (ev) { handle(ev.data); };
    ws.onclose = function () {
      delay = Math.min(delay * 2, 60000);
      setTimeout(connect, delay);
    };
    ws.onerror = function () { try { ws.close(); } catch (e) { } };
  }
  connect();
})();

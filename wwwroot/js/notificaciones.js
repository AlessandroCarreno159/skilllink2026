// Badge de notificaciones no leídas (equivale a main.js/nav-search.js del Flask).
(async function () {
  try {
    const r = await fetch('/Notifications/NoLeidas');
    if (!r.ok) return;
    const data = await r.json();
    const badge = document.getElementById('notif-badge');
    if (badge && data.total > 0) {
      badge.textContent = data.total;
      badge.classList.remove('d-none');
    }
  } catch { /* sin notificaciones */ }
})();

// Copia el enlace de la publicación al portapapeles (con fallback para HTTP).
(function () {
  document.querySelectorAll('[data-copy-share]').forEach(function (btn) {
    btn.addEventListener('click', async function () {
      var input = document.getElementById('shareUrl');
      if (!input) return;
      try {
        if (navigator.clipboard && window.isSecureContext) {
          await navigator.clipboard.writeText(input.value);
        } else {
          input.removeAttribute('readonly');
          input.select();
          document.execCommand('copy');
          input.setAttribute('readonly', 'readonly');
        }
        var original = btn.innerHTML;
        btn.innerHTML = '<i class="bi bi-check2"></i> Copiado';
        btn.disabled = true;
        setTimeout(function () {
          btn.innerHTML = original;
          btn.disabled = false;
        }, 1500);
      } catch (e) {
        input.removeAttribute('readonly');
        input.select();
        input.setAttribute('readonly', 'readonly');
      }
    });
  });
})();

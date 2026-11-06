// Write your JavaScript code. SK: helpers de UI (precio por rango, file pickers en español).

// --- Precio: compone S/min - S/max desde dos campos numéricos ---
(function () {
  var min = document.getElementById('precioMin');
  var max = document.getElementById('precioMax');
  var out = document.getElementById('precioTexto');
  if (!min || !max || !out) return;
  function soloNumeros(el) {
    // Solo dígitos y una coma decimal
    var v = el.value.replace(/[^0-9,]/g, '');
    var i = v.indexOf(',');
    if (i !== -1) v = v.slice(0, i + 1) + v.slice(i + 1).replace(/,/g, '');
    if (v !== el.value) el.value = v;
  }
  function componer() {
    var a = min.value.trim().replace(/^,+|,+$/g, '');
    var b = max.value.trim().replace(/^,+|,+$/g, '');
    out.value = !a ? '' : (!b ? 'S/' + a : 'S/' + a + ' - S/' + b);
  }
  min.addEventListener('input', function () { soloNumeros(min); componer(); });
  max.addEventListener('input', function () { soloNumeros(max); componer(); });
  min.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') { e.preventDefault(); max.focus(); max.select(); }
  });
  max.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') { e.preventDefault(); componer(); min.focus(); }
  });
  var form = out.closest('form');
  if (form) form.addEventListener('submit', componer);
})();

// --- File pickers en español: botón "Elegir archivo" + nombre visible ---
(function () {
  document.querySelectorAll('input[type="file"].sk-file-es').forEach(function (input) {
    var wrap = document.createElement('span');
    wrap.className = 'sk-file-es-wrap d-flex align-items-center gap-2';
    input.parentNode.insertBefore(wrap, input);
    wrap.appendChild(input);
    input.classList.add('d-none');
    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'btn sk-btn-lila btn-sm';
    btn.innerHTML = '<i class="bi bi-paperclip"></i> Elegir archivo';
    var name = document.createElement('small');
    name.className = 'text-muted sk-file-es-name';
    name.textContent = 'Ningún archivo seleccionado';
    btn.addEventListener('click', function () { input.click(); });
    input.addEventListener('change', function () {
      name.textContent = input.files.length ? input.files[0].name : 'Ningún archivo seleccionado';
    });
    wrap.appendChild(btn);
    wrap.appendChild(name);
  });
})();

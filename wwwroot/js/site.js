// Write your JavaScript code. SK: helpers de UI (precio por rango, file pickers en español).

// --- Precio: compone S/min - S/max o US$/min - US$/max desde dos campos numéricos ---
(function () {
  var min = document.getElementById('precioMin');
  var max = document.getElementById('precioMax');
  var out = document.getElementById('precioTexto');
  if (!min || !max || !out) return;
  var moneda = document.getElementById('Moneda');

  function simbolo() {
    return moneda && moneda.value === 'USD' ? 'US$' : 'S/';
  }
  function syncSimbolos() {
    var s = simbolo();
    document.querySelectorAll('[data-moneda-simbolo]').forEach(function (el) { el.textContent = s; });
  }
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
    var s = simbolo();
    out.value = !a ? '' : (!b ? s + a : s + a + ' - ' + s + b);
  }
  min.addEventListener('input', function () { soloNumeros(min); componer(); });
  max.addEventListener('input', function () { soloNumeros(max); componer(); });
  min.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') { e.preventDefault(); max.focus(); max.select(); }
  });
  max.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') { e.preventDefault(); componer(); min.focus(); }
  });
  if (moneda) {
    moneda.addEventListener('change', function () { syncSimbolos(); componer(); });
  }
  // Restaura min/max si el servidor devolvió un PrecioTexto ya compuesto.
  if (out.value && !min.value && !max.value) {
    var nums = out.value.match(/\d+(?:[.,]\d+)?/g);
    if (nums && nums[0]) min.value = nums[0];
    if (nums && nums[1]) max.value = nums[1];
  }
  syncSimbolos();
  componer();
  var form = out.closest('form');
  if (form) form.addEventListener('submit', componer);
})();

// --- Formulario de publicación: muestra u oculta campos según el tipo elegido ---
(function () {
  var tipo = document.getElementById('Tipo');
  if (!tipo) return;
  var descripcion = document.getElementById('tipoDescripcion');
  var grupoPrecio = document.getElementById('grupoPrecio');
  var grupoVacantes = document.getElementById('grupoVacantes');
  var grupoHabilidades = document.getElementById('grupoHabilidades');
  var etiquetaHabilidades = document.getElementById('etiquetaHabilidades');
  var precioMin = document.getElementById('precioMin');
  var precioMax = document.getElementById('precioMax');
  var precioTexto = document.getElementById('precioTexto');
  var moneda = document.getElementById('Moneda');
  var vacantes = document.getElementById('Vacantes');
  var habilidades = document.getElementById('HabilidadesReq');
  var precioDual = document.getElementById('PrecioDual');

  function mostrar(el, visible) {
    if (el) el.classList.toggle('d-none', !visible);
  }
  function limpiar(el) {
    if (el) el.value = '';
  }
  function aplicar(limpiarOcultos) {
    var opcion = tipo.options[tipo.selectedIndex];
    if (!opcion) return;
    var permitePrecio = opcion.dataset.precio === '1';
    var permiteVacantes = opcion.dataset.vacantes === '1';
    var permiteHabilidades = opcion.dataset.habilidades === '1';
    var precioOpcional = opcion.dataset.precioOpcional === '1';

    if (descripcion) descripcion.textContent = opcion.dataset.descripcion || '';
    mostrar(grupoPrecio, permitePrecio);
    mostrar(grupoVacantes, permiteVacantes);
    mostrar(grupoHabilidades, permiteHabilidades);
    document.querySelectorAll('.sk-precio-opcional').forEach(function (el) {
      el.classList.toggle('d-none', !precioOpcional);
    });
    if (etiquetaHabilidades && opcion.dataset.etiquetaHabilidades) {
      etiquetaHabilidades.textContent = opcion.dataset.etiquetaHabilidades;
    }

    if (!limpiarOcultos) return;
    if (!permitePrecio) {
      limpiar(precioMin);
      limpiar(precioMax);
      limpiar(precioTexto);
      if (precioDual) precioDual.checked = false;
      if (moneda) {
        moneda.value = 'PEN';
        moneda.dispatchEvent(new Event('change', { bubbles: true }));
      }
    }
    if (!permiteVacantes) limpiar(vacantes);
    if (!permiteHabilidades) limpiar(habilidades);
  }

  tipo.addEventListener('change', function () { aplicar(true); });
  aplicar(false);
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

// --- Altura del header: mantiene los paneles flotantes debajo de la barra fija ---
(function () {
  var header = document.querySelector('.navbar-skilllink');
  if (!header) return;
  function sync() {
    document.documentElement.style.setProperty('--sk-header-h', header.offsetHeight + 'px');
  }
  sync();
  window.addEventListener('resize', sync);
  window.addEventListener('load', sync);
  if (window.ResizeObserver) {
    new ResizeObserver(sync).observe(header);
  }
})();

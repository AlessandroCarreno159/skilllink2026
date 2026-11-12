// Ubicación aproximada del visitante basada en OpenStreetMap/Nominatim.
// Solo se guarda el departamento en la cookie sk_ciudad; nunca las coordenadas.
(function () {
  'use strict';

  var COOKIE_CIUDAD = 'sk_ciudad';
  var COOKIE_TODAS = 'sk_ciudad_all';
  var MAX_AGE = 60 * 60 * 24;
  var recargando = false;

  function leerCookie(nombre) {
    var partes = document.cookie ? document.cookie.split('; ') : [];
    for (var i = 0; i < partes.length; i++) {
      var index = partes[i].indexOf('=');
      if (index < 0) continue;
      if (partes[i].slice(0, index) !== nombre) continue;
      var valor = partes[i].slice(index + 1);
      try { return decodeURIComponent(valor); } catch (err) { return valor; }
    }
    return null;
  }

  function escribirCookie(nombre, valor, maxAge) {
    document.cookie = nombre + '=' + encodeURIComponent(valor) +
      '; Max-Age=' + maxAge + '; Path=/; SameSite=Lax';
  }

  function borrarCookie(nombre) {
    document.cookie = nombre + '=; Max-Age=0; Path=/; SameSite=Lax';
  }

  function ciudadGuardada() {
    var valor = leerCookie(COOKIE_CIUDAD);
    return valor && valor.trim() ? valor.trim() : null;
  }

  function todasSeleccionadas() {
    return leerCookie(COOKIE_TODAS) === '1';
  }

  function guardarCiudad(ciudad) {
    if (!ciudad) return;
    escribirCookie(COOKIE_CIUDAD, ciudad, MAX_AGE);
    borrarCookie(COOKIE_TODAS);
  }

  function marcarTodas() {
    escribirCookie(COOKIE_TODAS, '1', MAX_AGE);
    borrarCookie(COOKIE_CIUDAD);
  }

  function normalizar(valor) {
    var texto = String(valor == null ? '' : valor).trim();
    if (texto.normalize) {
      texto = texto.normalize('NFD').replace(/[\u0300-\u036f]/g, '');
    }
    return texto.toLowerCase();
  }

  function opcionDeCiudad(select, ciudad) {
    if (!select) return null;
    var buscado = normalizar(ciudad);
    for (var i = 0; i < select.options.length; i++) {
      var opcion = select.options[i];
      if (opcion.value && normalizar(opcion.value) === buscado) return opcion;
    }
    return null;
  }

  function seleccionarCiudad(select, ciudad) {
    var opcion = opcionDeCiudad(select, ciudad);
    if (!opcion) return false;
    select.value = opcion.value;
    if (typeof Event === 'function') {
      select.dispatchEvent(new Event('change', { bubbles: true }));
    }
    return true;
  }

  function selectCiudadFeed() {
    return document.querySelector('.sk-search-form select[name="ciudad"], select[name="ciudad"]');
  }

  function ciudadEnLista(ciudad) {
    var select = selectCiudadFeed();
    if (!select) return true;
    return !!opcionDeCiudad(select, ciudad);
  }

  function seleccionarTodasLasCiudades() {
    var select = selectCiudadFeed();
    if (select && select.value) {
      select.value = '';
      if (typeof Event === 'function') {
        select.dispatchEvent(new Event('change', { bubbles: true }));
      }
    }
    marcarTodas();
  }

  function soportaGeolocalizacion() {
    return window.isSecureContext === true && !!navigator.geolocation;
  }

  function mensajeUbicacion(error) {
    if (error && error.code === 1) return 'Permiso de ubicación denegado.';
    if (error && error.code === 2) return 'No se pudo obtener tu ubicación.';
    if (error && error.code === 3) return 'La ubicación tardó demasiado.';
    return error && error.message ? error.message : 'No se pudo obtener tu ubicación.';
  }

  function obtenerCoordenadas() {
    return new Promise(function (resolve, reject) {
      if (!soportaGeolocalizacion()) {
        reject(new Error('La geolocalización solo está disponible en conexiones seguras.'));
        return;
      }
      navigator.geolocation.getCurrentPosition(
        function (posicion) {
          resolve({ lat: posicion.coords.latitude, lon: posicion.coords.longitude });
        },
        function (error) { reject(error); },
        { enableHighAccuracy: false, timeout: 10000, maximumAge: 600000 }
      );
    });
  }

  function consultarCiudad(lat, lon) {
    var url = '/geo/ciudad?lat=' + encodeURIComponent(lat) + '&lon=' + encodeURIComponent(lon);
    return fetch(url, {
      method: 'GET',
      headers: { 'Accept': 'application/json' },
      credentials: 'same-origin'
    }).then(function (respuesta) {
      if (!respuesta.ok) throw new Error('No se pudo resolver la ubicación.');
      return respuesta.json();
    }).then(function (datos) {
      return datos && datos.ciudad ? String(datos.ciudad).trim() : null;
    });
  }

  function usarMiUbicacion() {
    return obtenerCoordenadas()
      .then(function (coords) { return consultarCiudad(coords.lat, coords.lon); })
      .then(function (ciudad) {
        if (ciudad) guardarCiudad(ciudad);
        return ciudad;
      });
  }

  function estadoDe(boton, texto) {
    var contenedor = boton.parentElement;
    var estado = contenedor ? contenedor.querySelector('[data-geo-status]') : null;
    if (estado) estado.textContent = texto || '';
  }

  function precargarSelects() {
    if (todasSeleccionadas()) return;
    var ciudad = ciudadGuardada();
    if (!ciudad) return;

    var selects = document.querySelectorAll(
      'select[data-geo-ciudad], select[name="Ciudad"], select[name="ciudad"]');
    Array.prototype.forEach.call(selects, function (select) {
      if (!select.value) seleccionarCiudad(select, ciudad);
    });
  }

  function initDeteccionFormulario() {
    if (todasSeleccionadas() || ciudadGuardada()) return;
    if (!soportaGeolocalizacion()) return;

    var selects = document.querySelectorAll('select[data-geo-ciudad]');
    var hayVacios = Array.prototype.some.call(selects, function (select) {
      return !select.value;
    });
    if (!hayVacios) return;

    usarMiUbicacion().then(function (ciudad) {
      if (!ciudad) return;
      Array.prototype.forEach.call(selects, function (select) {
        if (!select.value) seleccionarCiudad(select, ciudad);
      });
    }).catch(function () {
      // El usuario puede escribir o elegir el departamento manualmente.
    });
  }

  function initBotonesUbicacion() {
    var botones = document.querySelectorAll('[data-geo-usar-ubicacion]');
    Array.prototype.forEach.call(botones, function (boton) {
      boton.addEventListener('click', function () {
        if (boton.disabled) return;
        boton.disabled = true;
        estadoDe(boton, 'Obteniendo ubicación...');

        usarMiUbicacion().then(function (ciudad) {
          if (!ciudad) {
            estadoDe(boton, 'No se pudo determinar tu departamento.');
            return;
          }

          var targetId = boton.getAttribute('data-geo-target');
          var objetivo = targetId ? document.getElementById(targetId) : null;
          if (!objetivo) {
            var form = boton.closest('form');
            objetivo = form ? form.querySelector('select[name="Ciudad"], select[name="ciudad"]') : null;
          }

          if (objetivo) seleccionarCiudad(objetivo, ciudad);

          var autor = document.querySelector('[data-geo-ciudad-autor]');
          if (autor) {
            autor.textContent = ciudad;
            autor.setAttribute('data-ciudad', ciudad);
          }

          estadoDe(boton, 'Ubicación detectada: ' + ciudad);
        }).catch(function (error) {
          estadoDe(boton, mensajeUbicacion(error));
        }).then(function () {
          boton.disabled = false;
        });
      });
    });
  }

  function initFiltrosCiudad() {
    var controles = document.querySelectorAll(
      'select[name="ciudad"], input[type="radio"][name="ciudad"]');
    Array.prototype.forEach.call(controles, function (control) {
      control.addEventListener('change', function () {
        if (control.type === 'radio' && !control.checked) return;
        var valor = String(control.value || '').trim();
        if (valor) guardarCiudad(valor);
        else marcarTodas();
      });
    });

    Array.prototype.forEach.call(document.forms, function (form) {
      if (!form.querySelector('[name="ciudad"]')) return;
      form.addEventListener('submit', function () {
        var select = form.querySelector('select[name="ciudad"]');
        var marcado = form.querySelector('input[type="radio"][name="ciudad"]:checked');
        var valor = select ? select.value : (marcado ? marcado.value : '');
        if (String(valor || '').trim()) guardarCiudad(valor);
        else marcarTodas();
      });
    });

    var limpiar = document.querySelectorAll('[data-geo-todas]');
    Array.prototype.forEach.call(limpiar, function (elemento) {
      elemento.addEventListener('click', marcarTodas);
    });
  }

  function initUbicacionAutor() {
    var autor = document.querySelector('[data-geo-ciudad-autor]');
    if (!autor) return;

    var valor = (autor.getAttribute('data-ciudad') || '').trim();
    if (valor) {
      autor.textContent = valor;
      return;
    }

    var ciudad = todasSeleccionadas() ? null : ciudadGuardada();
    if (ciudad) {
      autor.textContent = ciudad;
      autor.setAttribute('data-ciudad', ciudad);
      return;
    }

    if (!soportaGeolocalizacion()) {
      autor.textContent = 'Sin ubicación';
      return;
    }

    usarMiUbicacion().then(function (detectada) {
      autor.textContent = detectada || 'Sin ubicación';
      if (detectada) autor.setAttribute('data-ciudad', detectada);
    }).catch(function () {
      autor.textContent = 'Sin ubicación';
    });
  }

  function recargarConCiudad(ciudad) {
    if (recargando || !ciudad) return;
    recargando = true;

    var params = new URLSearchParams(window.location.search);
    params.set('ciudad', ciudad);
    params.delete('pagina');
    var query = params.toString();
    window.location.replace(window.location.pathname + (query ? '?' + query : '') + window.location.hash);
  }

  function initFeed() {
    if (!document.body || document.body.getAttribute('data-geo-auto-feed') !== '1') return;

    var params = new URLSearchParams(window.location.search);
    var ciudadExplicita = (params.get('ciudad') || '').trim();
    if (ciudadExplicita) {
      // Si la ciudad de la URL no existe en el catálogo mostrado, se limpia el
      // filtro y se muestran todas las ciudades.
      if (!ciudadEnLista(ciudadExplicita)) {
        seleccionarTodasLasCiudades();
        params.delete('ciudad');
        var query = params.toString();
        window.location.replace(window.location.pathname + (query ? '?' + query : '') + window.location.hash);
      }
      return;
    }
    if (todasSeleccionadas()) return;

    var ciudad = ciudadGuardada();
    if (ciudad) {
      if (ciudadEnLista(ciudad)) recargarConCiudad(ciudad);
      else seleccionarTodasLasCiudades();
      return;
    }

    if (!soportaGeolocalizacion()) return;

    usarMiUbicacion().then(function (detectada) {
      if (!detectada) return;
      if (ciudadEnLista(detectada)) recargarConCiudad(detectada);
      else seleccionarTodasLasCiudades();
    }).catch(function () {
      // Sin permiso o sin red: se mantiene el feed sin filtro de ubicación.
    });
  }

  function init() {
    precargarSelects();
    initDeteccionFormulario();
    initBotonesUbicacion();
    initFiltrosCiudad();
    initUbicacionAutor();
    initFeed();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();

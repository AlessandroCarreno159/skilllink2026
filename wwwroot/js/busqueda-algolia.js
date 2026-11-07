(function () {
  'use strict';

  var LOG = '[SkillLink búsqueda]';

  function log() {
    try {
      console.log.apply(console, [LOG].concat(Array.prototype.slice.call(arguments)));
    } catch (err) { /* console no disponible */ }
  }

  function warn() {
    try {
      console.warn.apply(console, [LOG].concat(Array.prototype.slice.call(arguments)));
    } catch (err) { /* console no disponible */ }
  }

  function init() {
    log('Script cargado. Inicializando buscador...');

    var root = document.getElementById('sk-search');
    if (!root) {
      log('No existe #sk-search en esta página; no se inicializa el buscador.');
      return;
    }

    var input = root.querySelector('#inputBusqueda') || root.querySelector('#sk-search-input');
    var clearBtn = root.querySelector('[data-sk-search-clear]') || root.querySelector('.sk-search-clear');
    var form = root.querySelector('form');

    function resolvePanel() {
      var el = document.getElementById('search-results-container');
      if (!el) el = root.querySelector('[data-sk-search-results]');
      if (!el) el = document.getElementById('sk-search-results');
      return el;
    }

    var panel = resolvePanel();

    // Compatibilidad con vistas Razor antiguas ya compiladas: si el HTML servido
    // todavía no trae el contenedor nuevo, se crea aquí para que el dropdown
    // funcione sin esperar a reiniciar la aplicación.
    if (input && !panel) {
      panel = document.createElement('div');
      panel.className = 'sk-search-results';
      panel.setAttribute('data-sk-search-results', '');
      panel.setAttribute('role', 'listbox');
      panel.setAttribute('aria-label', 'Resultados de búsqueda');
      panel.setAttribute('aria-live', 'polite');
      panel.hidden = true;
      var anchor = root.querySelector('.sk-search-box') || input.parentNode;
      if (anchor && anchor.parentNode) anchor.parentNode.insertBefore(panel, anchor.nextSibling);
      else root.appendChild(panel);
      log('Contenedor de resultados creado dinámicamente (vista antigua detectada).');
    }

    if (panel) {
      panel.id = 'search-results-container';
      if (input) input.setAttribute('aria-controls', panel.id);
    }

    log('Selectores DOM:', {
      root: !!root,
      input: !!input,
      panel: !!panel,
      clearButton: !!clearBtn,
      form: !!form
    });

    if (!input || !panel) {
      warn('Faltan el input de búsqueda o el contenedor de resultados. El buscador no puede inicializarse.');
      return;
    }

    if (clearBtn) clearBtn.hidden = input.value.length === 0;

    var appId = (root.getAttribute('data-app-id') || '').trim();
    var searchKey = (root.getAttribute('data-search-key') || '').trim();
    var indexName = (root.getAttribute('data-index-name') || 'skilllink_index').trim();
    var detailsBase = root.getAttribute('data-details-base') || '/Feed/Details';
    var feedUrl = root.getAttribute('data-feed-url') || '/Feed';
    var algoliaLib = window.algoliasearch;
    var libOk = typeof algoliaLib === 'function';

    log('Configuración Algolia:', {
      applicationId: appId || '(vacío)',
      searchApiKeyPresente: !!searchKey,
      indexName: indexName,
      cdnAlgoliaCargado: libOk,
      cdnGlobal: typeof algoliaLib
    });

    var categorias = {
      construccion: 'Construcción',
      tecnologia: 'Tecnología',
      diseno: 'Diseño',
      hogar: 'Hogar',
      otros: 'Otros'
    };
    var modalidades = {
      tiempo_completo: 'Tiempo completo',
      medio_tiempo: 'Medio tiempo',
      por_proyecto: 'Por proyecto'
    };
    var tipos = {
      oferta: 'Oferta',
      convocatoria: 'Convocatoria',
      experiencia: 'Experiencia',
      busqueda: 'Búsqueda',
      proyecto: 'Proyecto',
      empresa_info: 'Anuncio'
    };

    var missing = [];
    if (!appId) missing.push('Falta Algolia:ApplicationId.');
    if (!searchKey) missing.push('Falta Algolia:SearchApiKey (Search-Only API Key).');
    if (!libOk) missing.push('No se cargó algoliasearch desde el CDN. Revisa la conexión o un bloqueador de scripts.');

    var index = null;
    var configured = false;
    var configError = missing.join(' ');

    if (!missing.length) {
      try {
        var client = algoliaLib(appId, searchKey);
        log('Cliente Algolia creado.', {
          appId: appId,
          tieneInitIndex: !!client && typeof client.initIndex === 'function'
        });
        index = client.initIndex(indexName);
        configured = !!index && typeof index.search === 'function';
        log('Índice Algolia inicializado:', {
          indexName: indexName,
          tieneSearch: !!(index && typeof index.search === 'function'),
          configured: configured
        });
        if (!configured) configError = 'El cliente de Algolia no expone el método search().';
      } catch (err) {
        configError = 'No se pudo crear el cliente de Algolia: ' + (err && err.message ? err.message : err);
        warn(configError, err);
      }
    } else {
      warn('Buscador no configurado:', configError);
    }

    var timer = null;
    var requestSeq = 0;
    var activeIndex = -1;
    var MIN_CHARS = 3;
    var DEBOUNCE_MS = 300;

    function truncate(value, max) {
      var text = String(value == null ? '' : value).replace(/\s+/g, ' ').trim();
      if (text.length <= max) return text;
      return text.slice(0, max).replace(/\s+\S*$/, '') + '…';
    }

    function label(map, value, fallback) {
      if (value && map[value]) return map[value];
      return value || fallback || '';
    }

    function showPanel() {
      panel.hidden = false;
      panel.style.display = 'block';
      panel.classList.add('is-open');
      input.setAttribute('aria-expanded', 'true');
      log('Panel de resultados visible.', { display: panel.style.display, hidden: panel.hidden });
    }

    function hidePanel() {
      panel.hidden = true;
      panel.style.display = 'none';
      panel.classList.remove('is-open');
      input.setAttribute('aria-expanded', 'false');
      activeIndex = -1;
      panel.querySelectorAll('.sk-search-result.is-active').forEach(function (el) {
        el.classList.remove('is-active');
        el.setAttribute('aria-selected', 'false');
      });
    }

    function clearPanel() {
      while (panel.firstChild) panel.removeChild(panel.firstChild);
    }

    function appendMessage(containerClass, iconClass, title, message) {
      clearPanel();
      var wrap = document.createElement('div');
      wrap.className = containerClass;
      if (iconClass) {
        var icon = document.createElement('i');
        icon.className = iconClass;
        icon.setAttribute('aria-hidden', 'true');
        wrap.appendChild(icon);
      }
      if (title) {
        var strong = document.createElement('strong');
        strong.textContent = title;
        wrap.appendChild(strong);
      }
      if (message) {
        var span = document.createElement('span');
        span.textContent = message;
        wrap.appendChild(span);
      }
      panel.appendChild(wrap);
      showPanel();
      activeIndex = -1;
    }

    function showLoading() {
      clearPanel();
      var wrap = document.createElement('div');
      wrap.className = 'sk-search-loading';
      var spinner = document.createElement('span');
      spinner.className = 'spinner-border spinner-border-sm';
      spinner.setAttribute('aria-hidden', 'true');
      var text = document.createElement('span');
      text.textContent = 'Buscando…';
      wrap.appendChild(spinner);
      wrap.appendChild(text);
      panel.appendChild(wrap);
      showPanel();
      activeIndex = -1;
    }

    function createChip(text, iconClass) {
      var chip = document.createElement('span');
      chip.className = 'sk-search-chip';
      if (iconClass) {
        var icon = document.createElement('i');
        icon.className = iconClass;
        icon.setAttribute('aria-hidden', 'true');
        chip.appendChild(icon);
        chip.appendChild(document.createTextNode(' '));
      }
      chip.appendChild(document.createTextNode(text));
      return chip;
    }

    function createResult(hit) {
      var objectId = hit.objectID == null ? '' : String(hit.objectID);
      if (!objectId) return null;

      var link = document.createElement('a');
      link.className = 'sk-search-result';
      link.setAttribute('role', 'option');
      link.setAttribute('aria-selected', 'false');
      link.tabIndex = -1;
      link.href = detailsBase + '/' + encodeURIComponent(objectId);

      var img = document.createElement('img');
      img.className = 'sk-search-thumb';
      img.src = hit.imagenUrl || '/img/placeholder.svg';
      img.alt = '';
      img.loading = 'lazy';
      link.appendChild(img);

      var body = document.createElement('span');
      body.className = 'sk-search-result-body';

      var title = document.createElement('span');
      title.className = 'sk-search-result-title';
      title.textContent = hit.titulo || '(Publicación sin título)';
      body.appendChild(title);

      var snippetText = truncate(hit.contenido, 130);
      if (snippetText) {
        var snippet = document.createElement('span');
        snippet.className = 'sk-search-result-snippet';
        snippet.textContent = snippetText;
        body.appendChild(snippet);
      }

      var chips = [];
      if (hit.categoria || hit.categoriaLabel) {
        chips.push(createChip(hit.categoriaLabel || label(categorias, hit.categoria, 'General')));
      }
      if (hit.modalidad || hit.modalidadLabel) {
        chips.push(createChip(hit.modalidadLabel || label(modalidades, hit.modalidad, hit.modalidad)));
      }
      if (hit.tipo || hit.tipoLabel) {
        chips.push(createChip(hit.tipoLabel || label(tipos, hit.tipo, hit.tipo)));
      }
      if (hit.ciudad) {
        chips.push(createChip(hit.ciudad, 'bi bi-geo-alt-fill'));
      }

      if (chips.length) {
        var meta = document.createElement('span');
        meta.className = 'sk-search-result-meta';
        chips.forEach(function (chip) { meta.appendChild(chip); });
        body.appendChild(meta);
      }

      link.appendChild(body);
      return link;
    }

    function renderResults(query, hits, total) {
      var visible = (hits || []).filter(function (hit) { return hit && hit.objectID != null; });
      log('Pintando resultados:', { query: query, recibidos: (hits || []).length, visibles: visible.length, total: total });

      if (!visible.length) {
        appendMessage('sk-search-empty', 'bi bi-search', 'No se encontraron resultados',
          'No hay publicaciones que coincidan con “' + query + '”.');
        return;
      }

      clearPanel();
      var fragment = document.createDocumentFragment();
      visible.forEach(function (hit) {
        var node = createResult(hit);
        if (node) fragment.appendChild(node);
      });

      var allUrl = feedUrl + '?q=' + encodeURIComponent(query);
      var count = typeof total === 'number' && total > 0 ? total : visible.length;
      var all = document.createElement('a');
      all.className = 'sk-search-all';
      all.href = allUrl;
      all.appendChild(document.createTextNode('Ver ' + count + ' resultado' + (count === 1 ? '' : 's') + ' '));
      var arrow = document.createElement('i');
      arrow.className = 'bi bi-arrow-right';
      arrow.setAttribute('aria-hidden', 'true');
      all.appendChild(arrow);
      fragment.appendChild(all);

      panel.appendChild(fragment);
      showPanel();
      activeIndex = -1;
    }

    function runSearch(query) {
      if (!configured) {
        warn('runSearch() ignorado: el buscador no está configurado.', configError);
        return;
      }

      var current = ++requestSeq;
      log('Consultando Algolia...', { query: query, request: current, indexName: indexName });

      showLoading();

      index.search(query, {
        hitsPerPage: 6,
        filters: 'activa:true',
        attributesToRetrieve: [
          'objectID', 'titulo', 'contenido', 'habilidades',
          'categoria', 'categoriaLabel', 'modalidad', 'modalidadLabel',
          'tipo', 'tipoLabel', 'precio', 'imagenUrl', 'ciudad', 'autor', 'fecha', 'activa'
        ]
      }).then(function (response) {
        log('Respuesta de Algolia recibida:', {
          request: current,
          hits: response && response.hits ? response.hits.length : 0,
          nbHits: response ? response.nbHits : undefined
        });
        if (current !== requestSeq) {
          log('Respuesta descartada: hay una búsqueda más reciente.', { request: current, actual: requestSeq });
          return;
        }
        if (input.value.trim() !== query) {
          log('Respuesta descartada: el texto del input cambió.', { query: query, input: input.value.trim() });
          return;
        }
        renderResults(query, response.hits || [], response.nbHits);
      }).catch(function (err) {
        if (current !== requestSeq) return;
        console.error(LOG, 'Error al buscar en Algolia.', err);
        var detalle = err && err.message ? err.message : 'Error desconocido';
        appendMessage('sk-search-empty', 'bi bi-exclamation-triangle-fill', 'Búsqueda no disponible',
          'No se pudo completar la consulta: ' + detalle);
      });
    }

    function showConfigError() {
      appendMessage('sk-search-empty', 'bi bi-gear-fill', 'Buscador no disponible',
        configError || 'Revisa la configuración de Algolia.');
    }

    function handleInput() {
      var query = input.value.trim();
      log('Evento input disparado.', { value: input.value, query: query, length: query.length });
      if (clearBtn) clearBtn.hidden = input.value.length === 0;

      if (!configured) {
        requestSeq++;
        clearTimeout(timer);
        if (query.length >= MIN_CHARS) showConfigError();
        else hidePanel();
        return;
      }

      if (query.length < MIN_CHARS) {
        requestSeq++;
        clearTimeout(timer);
        hidePanel();
        return;
      }

      clearTimeout(timer);
      timer = setTimeout(function () { runSearch(query); }, DEBOUNCE_MS);
    }

    function getResults() {
      return Array.prototype.slice.call(panel.querySelectorAll('.sk-search-result'));
    }

    function setActive(items, next) {
      if (!items.length) return;
      activeIndex = (next + items.length) % items.length;
      items.forEach(function (item, i) {
        var on = i === activeIndex;
        item.classList.toggle('is-active', on);
        item.setAttribute('aria-selected', on ? 'true' : 'false');
      });
      items[activeIndex].scrollIntoView({ block: 'nearest' });
    }

    input.addEventListener('input', handleInput);

    input.addEventListener('keydown', function (event) {
      if (panel.hidden) return;
      var items = getResults();

      if (event.key === 'ArrowDown') {
        if (!items.length) return;
        event.preventDefault();
        setActive(items, activeIndex + 1);
      } else if (event.key === 'ArrowUp') {
        if (!items.length) return;
        event.preventDefault();
        setActive(items, activeIndex - 1);
      } else if (event.key === 'Escape') {
        hidePanel();
      } else if (event.key === 'Enter' && activeIndex >= 0 && items[activeIndex]) {
        event.preventDefault();
        window.location.href = items[activeIndex].getAttribute('href');
      }
    });

    if (clearBtn) {
      clearBtn.addEventListener('click', function () {
        input.value = '';
        clearBtn.hidden = true;
        requestSeq++;
        clearTimeout(timer);
        hidePanel();
        input.focus();
      });
    }

    if (form) {
      form.addEventListener('submit', function () {
        hidePanel();
      });
    }

    panel.addEventListener('click', function (event) {
      var link = event.target.closest('a');
      if (link) hidePanel();
    });

    document.addEventListener('mousedown', function (event) {
      if (!root.contains(event.target)) hidePanel();
    });

    document.addEventListener('keydown', function (event) {
      if (event.key === 'Escape' && document.activeElement === input) hidePanel();
    });

    log('Buscador listo.', { configured: configured });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();

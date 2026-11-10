// Malbo — journal : versions (releases GitHub) et activité de développement (commits), chargés en direct.
(function () {
  var C = window.MALBO || {}, API = 'https://api.github.com/repos/' + C.repo;
  var esc = function (s) { return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); };
  var fdate = function (d) { return new Date(d).toLocaleDateString('fr-FR', { day: 'numeric', month: 'long', year: 'numeric' }); };

  function inline(s) {
    return esc(s)
      .replace(/`([^`]+)`/g, '<code>$1</code>')
      .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
      .replace(/(^|[\s(])\*([^*\s][^*]*)\*/g, '$1<em>$2</em>')
      .replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g, '<a href="$2" rel="noopener">$1</a>');
  }
  // Mini rendu Markdown : titres, listes, citations, paragraphes.
  function md(src) {
    var out = [], list = false;
    var close = function () { if (list) { out.push('</ul>'); list = false; } };
    src.replace(/<!--[\s\S]*?-->/g, '').replace(/\r/g, '').split('\n').forEach(function (l) {
      var m;
      if (/^\s*$/.test(l) || /^---+$/.test(l)) { close(); return; }
      if ((m = l.match(/^(#{1,4})\s+(.*)$/))) { close(); out.push('<h4 class="md-h md-h' + m[1].length + '">' + inline(m[2]) + '</h4>'); return; }
      if ((m = l.match(/^\s*[-*]\s+(.*)$/))) { if (!list) { out.push('<ul>'); list = true; } out.push('<li>' + inline(m[1]) + '</li>'); return; }
      if ((m = l.match(/^>\s?(.*)$/))) { close(); out.push('<blockquote>' + inline(m[1]) + '</blockquote>'); return; }
      close(); out.push('<p>' + inline(l) + '</p>');
    });
    close();
    return out.join('');
  }

  function load(path, fallback) {
    return fetch(API + path, { headers: { Accept: 'application/vnd.github+json' } })
      .then(function (r) { if (!r.ok) throw 0; return r.json(); })
      .catch(function () { return fetch(fallback).then(function (r) { return r.json(); }); });
  }

  var kindOf = function (r) {
    if (!r.prerelease) return 'Stable';
    return /alpha/i.test(r.tag_name) ? 'Alpha' : /beta/i.test(r.tag_name) ? 'Bêta' : 'Pré-version';
  };

  var relBox = document.getElementById('releases');
  if (relBox) load('/releases?per_page=30', 'assets/data/releases.json').then(function (list) {
    list = list.filter(function (r) { return !r.draft; });
    var count = document.getElementById('rel-count'); if (count) count.textContent = list.length;
    relBox.innerHTML = list.map(function (r, n) {
      var exe = (r.assets || []).filter(function (a) { return /\.exe$/i.test(a.name); })[0];
      var size = exe ? ' · ' + Math.round(exe.size / 1048576) + ' Mo' : '';
      return '<details class="card rel"' + (n === 0 ? ' open' : '') + ' data-kind="' + (r.prerelease ? 'pre' : 'stable') + '">' +
        '<summary><span class="rel-tag">' + esc(r.tag_name) + '</span>' +
        '<span class="chip' + (r.prerelease ? '' : ' chip-solid') + '">' + kindOf(r) + '</span>' +
        '<span class="rel-date">' + fdate(r.published_at) + size + '</span><span class="rel-chev" aria-hidden="true"></span></summary>' +
        '<div class="md">' + (md(r.body || '') || '<p>Pas de notes pour cette version.</p>') + '</div>' +
        '<div class="rel-actions">' + (exe ? '<a class="btn btn-primary btn-sm" href="' + esc(exe.browser_download_url) + '">Télécharger Malbo.exe</a>' : '') +
        '<a class="btn btn-ghost btn-sm" href="' + esc(r.html_url) + '" rel="noopener">Voir sur GitHub</a></div></details>';
    }).join('') || '<div class="card"><p>Aucune version publiée pour le moment.</p></div>';
  }).catch(function () { relBox.innerHTML = '<div class="card"><p>Impossible de charger les versions. Elles sont disponibles sur GitHub.</p></div>'; });

  var logBox = document.getElementById('devlog');
  if (logBox) load('/commits?per_page=40', 'assets/data/commits.json').then(function (list) {
    var type = function (msg) {
      if (/^(fix|corr|bug|hotfix)|corrig|\bfix\b/i.test(msg)) return 'Correctif';
      if (/^(feat|add|ajout|nouveau)|ajoute/i.test(msg)) return 'Nouveauté';
      if (/release|version|publi|\bv?\d+\.\d+/i.test(msg)) return 'Version';
      return 'Dev';
    };
    logBox.innerHTML = list.map(function (c) {
      var lines = c.commit.message.split('\n'), title = lines[0], rest = lines.slice(1).join('\n').trim();
      return '<li><span class="log-dot" aria-hidden="true"></span><div><div class="log-top"><span class="chip">' + type(title) + '</span>' +
        '<time>' + fdate(c.commit.author.date) + '</time><a href="' + esc(c.html_url) + '" rel="noopener"><code>' + esc(c.sha.slice(0, 7)) + '</code></a></div>' +
        '<p class="log-title">' + inline(title) + '</p>' + (rest ? '<div class="md">' + md(rest) + '</div>' : '') + '</div></li>';
    }).join('') || '<li><p>Aucune activité.</p></li>';
  }).catch(function () { logBox.innerHTML = '<li><p>Impossible de charger l\'activité.</p></li>'; });

  // Filtres des versions
  document.querySelectorAll('[data-filter]').forEach(function (b) {
    b.addEventListener('click', function () {
      document.querySelectorAll('[data-filter]').forEach(function (x) { x.setAttribute('aria-pressed', x === b ? 'true' : 'false'); });
      var f = b.dataset.filter;
      document.querySelectorAll('.rel').forEach(function (el) { el.hidden = f !== 'all' && el.dataset.kind !== f; });
    });
  });
})();

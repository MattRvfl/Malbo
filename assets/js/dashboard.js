// Malbo — tableau de bord des visites. Lit les agrégats via la fonction Supabase stats(), protégée par mot de passe.
(function () {
  var C = window.MALBO || {};
  var $ = function (id) { return document.getElementById(id); };
  var esc = function (s) { return String(s == null ? '' : s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); };
  var nf = new Intl.NumberFormat('fr-FR');
  var DEMO = /[?&]demo\b/.test(location.search);
  var state = { days: 30, metric: 'visits', data: null, pass: null };
  try { state.pass = sessionStorage.getItem('malbo-dash'); } catch (e) {}

  var NAMES = { direct: 'Accès direct', google: 'Google', bing: 'Bing', duckduckgo: 'DuckDuckGo', discord: 'Discord', youtube: 'YouTube', tiktok: 'TikTok',
    x: 'X (Twitter)', reddit: 'Reddit', instagram: 'Instagram', facebook: 'Facebook', twitch: 'Twitch', github: 'GitHub', linkedin: 'LinkedIn',
    telegram: 'Telegram', whatsapp: 'WhatsApp', snapchat: 'Snapchat', threads: 'Threads', bluesky: 'Bluesky', producthunt: 'Product Hunt' };
  var CHANNELS = { social: 'Réseaux sociaux', search: 'Moteurs de recherche', direct: 'Accès direct', referral: 'Autres sites', campaign: 'Liens de campagne' };
  var LANGS = { fr: 'Français', en: 'Anglais', es: 'Espagnol', de: 'Allemand', it: 'Italien', pt: 'Portugais', nl: 'Néerlandais', ru: 'Russe', ar: 'Arabe', ja: 'Japonais', zh: 'Chinois', pl: 'Polonais', tr: 'Turc' };
  var METRICS = { visits: 'Visites', pageviews: 'Pages vues', downloads: 'Téléchargements', discord: 'Clics Discord' };
  var name = function (k) { return NAMES[k] || (k ? k.charAt(0).toUpperCase() + k.slice(1) : '?'); };

  // ---------- Données ----------
  function fetchStats() {
    if (DEMO) return Promise.resolve(demo(state.days));
    return fetch(C.supabaseUrl + '/rest/v1/rpc/stats', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', apikey: C.supabaseKey, Authorization: 'Bearer ' + C.supabaseKey },
      body: JSON.stringify({ pass: state.pass, days: state.days })
    }).then(function (r) {
      if (r.status === 401 || r.status === 403 || r.status === 400) throw new Error('auth');
      if (!r.ok) throw new Error('net');
      return r.json();
    });
  }

  function demo(days) {
    var seed = 7, rnd = function () { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
    var daily = [], T = { visits: 0, pageviews: 0, downloads: 0, discord: 0, github: 0 };
    for (var i = days - 1; i >= 0; i--) {
      var d = new Date(Date.now() - i * 864e5), wk = [0, 6].indexOf(d.getDay()) >= 0 ? 1.35 : 1;
      var v = Math.round((40 + (days - i) * 1.4 + rnd() * 30) * wk * (i === 9 ? 3.2 : 1));
      var row = { d: d.toISOString().slice(0, 10), visits: v, pageviews: Math.round(v * (1.9 + rnd() * .5)), downloads: Math.round(v * (.16 + rnd() * .06)), discord: Math.round(v * (.05 + rnd() * .03)) };
      daily.push(row); T.visits += row.visits; T.pageviews += row.pageviews; T.downloads += row.downloads; T.discord += row.discord;
    }
    T.github = Math.round(T.visits * .07);
    var split = function (keys, shares, extra) {
      return keys.map(function (k, n) { var o = { k: k, visits: Math.round(T.visits * shares[n]) }; if (extra) extra(o, n); return o; });
    };
    var conv = [.24, .12, .2, .09, .22, .15, .3, .11, .1];
    return {
      days: days, demo: true, totals: T,
      previous: { visits: Math.round(T.visits * .74), pageviews: Math.round(T.pageviews * .78), downloads: Math.round(T.downloads * .69), discord: Math.round(T.discord * .9), github: 0 },
      daily: daily,
      hours: Array.from({ length: 24 }, function (_, h) { return { h: h, v: Math.round(T.pageviews / 40 * (.15 + Math.exp(-Math.pow(h - 19, 2) / 18) + .5 * Math.exp(-Math.pow(h - 13, 2) / 10))) }; }),
      sources: split(['tiktok', 'direct', 'youtube', 'google', 'discord', 'reddit', 'github', 'x', 'instagram'], [.27, .19, .16, .12, .09, .07, .05, .03, .02], function (o, n) {
        o.medium = ['social', 'direct', 'social', 'search', 'social', 'social', 'referral', 'social', 'social'][n];
        o.pageviews = Math.round(o.visits * 2.1); o.downloads = Math.round(o.visits * conv[n]); o.discord = Math.round(o.visits * .06);
      }),
      channels: split(['social', 'direct', 'search', 'referral', 'campaign'], [.6, .19, .12, .06, .03], function (o) { o.downloads = Math.round(o.visits * .18); }),
      referrers: split(['youtube.com', 'google.com', 'reddit.com', 'github.com', 'korben.info'], [.14, .12, .07, .05, .02]),
      campaigns: split(['lancement-1.3', 'video-demo', 'bio-tiktok'], [.11, .08, .05], function (o, n) { o.source = ['discord', 'youtube', 'tiktok'][n]; o.downloads = Math.round(o.visits * .2); }),
      pages: split(['index.html', 'download.html', 'features.html', 'changelog.html', 'docs.html', 'discord.html', 'about.html'], [.95, .52, .31, .14, .11, .08, .05]),
      devices: split(['ordinateur', 'mobile', 'tablette'], [.58, .39, .03]),
      browsers: split(['Chrome', 'Edge', 'Firefox', 'Safari', 'Opera'], [.52, .2, .13, .1, .05]),
      os: split(['Windows', 'Android', 'iOS', 'macOS', 'Linux'], [.55, .24, .15, .04, .02]),
      langs: split(['fr', 'en', 'es', 'de'], [.71, .2, .05, .04]),
      zones: split(['Europe/Paris', 'Europe/Brussels', 'America/Toronto', 'Europe/Zurich'], [.68, .12, .11, .09]),
      recent: Array.from({ length: 8 }, function (_, n) {
        return { ts: new Date(Date.now() - n * 1000 * 60 * (3 + n * 4)).toISOString(), kind: n % 3 === 1 ? 'click' : 'pageview', name: n % 3 === 1 ? (n % 2 ? 'download' : 'discord') : null,
          page: ['index.html', 'download.html', 'features.html'][n % 3], source: ['tiktok', 'youtube', 'direct', 'google'][n % 4], device: n % 2 ? 'mobile' : 'ordinateur' };
      })
    };
  }

  // ---------- Rendu ----------
  function kpis(d) {
    var t = d.totals, p = d.previous || {};
    var conv = t.visits ? t.downloads / t.visits : 0, pconv = p.visits ? p.downloads / p.visits : 0;
    var tiles = [
      ['Visites', t.visits, p.visits], ['Pages vues', t.pageviews, p.pageviews],
      ['Téléchargements', t.downloads, p.downloads], ['Clics Discord', t.discord, p.discord],
      ['Taux de téléchargement', conv, pconv, true]
    ];
    $('kpis').innerHTML = tiles.map(function (x) {
      var cur = x[1] || 0, prev = x[2] || 0, delta = '';
      if (prev > 0) {
        var ch = (cur - prev) / prev, up = ch >= 0;
        delta = '<span class="delta ' + (up ? 'up' : 'down') + '">' + (up ? '▲' : '▼') + ' ' + nf.format(Math.abs(Math.round(ch * 100))) + ' %</span> <span class="vs">vs période précédente</span>';
      } else delta = '<span class="vs">pas de période précédente</span>';
      var val = x[3] ? (cur * 100).toFixed(1).replace('.', ',') + ' %' : nf.format(cur);
      return '<div class="card kpi"><div class="kpi-label">' + x[0] + '</div><div class="kpi-val">' + val + '</div><div class="kpi-delta">' + delta + '</div></div>';
    }).join('');
  }

  function bars(id, rows, fmt, empty) {
    var el = $(id); if (!el) return;
    rows = (rows || []).filter(function (r) { return r.visits > 0; });
    if (!rows.length) { el.innerHTML = '<p class="empty">' + (empty || 'Pas encore de données sur cette période.') + '</p>'; return; }
    var max = Math.max.apply(null, rows.map(function (r) { return r.visits; }));
    var total = rows.reduce(function (s, r) { return s + r.visits; }, 0);
    el.innerHTML = rows.map(function (r) {
      var label = fmt ? fmt(r.k) : r.k, pct = Math.round(r.visits / total * 100);
      return '<div class="bar-row" title="' + esc(label) + ' : ' + nf.format(r.visits) + ' (' + pct + ' %)">' +
        '<span class="bar-label">' + esc(label) + '</span><span class="bar-track"><i style="width:' + Math.max(1.5, r.visits / max * 100) + '%"></i></span>' +
        '<span class="bar-val">' + nf.format(r.visits) + '</span><span class="bar-pct">' + pct + ' %</span></div>';
    }).join('');
  }

  function sources(d) {
    var rows = (d.sources || []).filter(function (r) { return r.visits > 0 || r.downloads > 0; });
    var el = $('sources');
    if (!rows.length) { el.innerHTML = '<p class="empty">Aucune visite sur cette période. Partagez un lien suivi (voir plus bas) pour commencer.</p>'; return; }
    var max = Math.max.apply(null, rows.map(function (r) { return r.visits; })), total = d.totals.visits || 1;
    el.innerHTML = '<table class="table src"><thead><tr><th>Provenance</th><th class="hide-s">Canal</th><th></th><th class="num">Visites</th><th class="num hide-s">Part</th><th class="num">Téléch.</th><th class="num hide-s">Discord</th><th class="num">Conv.</th></tr></thead><tbody>' +
      rows.map(function (r) {
        var conv = r.visits ? Math.round(r.downloads / r.visits * 100) + ' %' : '–';
        return '<tr><td><strong>' + esc(name(r.k)) + '</strong></td><td class="hide-s muted">' + esc(CHANNELS[r.medium] || r.medium || '') + '</td>' +
          '<td class="bar-cell"><span class="bar-track"><i style="width:' + Math.max(1.5, r.visits / max * 100) + '%"></i></span></td>' +
          '<td class="num">' + nf.format(r.visits) + '</td><td class="num hide-s muted">' + Math.round(r.visits / total * 100) + ' %</td>' +
          '<td class="num">' + nf.format(r.downloads) + '</td><td class="num hide-s">' + nf.format(r.discord) + '</td><td class="num">' + conv + '</td></tr>';
      }).join('') + '</tbody></table>';
  }

  function niceMax(v) {
    if (v <= 4) return 4;
    var p = Math.pow(10, Math.floor(Math.log10(v))), n = v / p;
    return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 4 ? 4 : n <= 5 ? 5 : n <= 8 ? 8 : 10) * p;
  }
  var dshort = function (s) { return new Date(s + 'T12:00:00').toLocaleDateString('fr-FR', { day: 'numeric', month: 'short' }); };

  function lineChart() {
    var box = $('chart'), d = state.data; if (!box || !d) return;
    var rows = d.daily || [], m = state.metric, W = Math.max(300, box.clientWidth), H = 280;
    var L = 44, R = 14, T = 14, B = 30, w = W - L - R, h = H - T - B;
    var max = niceMax(Math.max.apply(null, rows.map(function (r) { return r[m] || 0; }).concat([1])));
    var x = function (i) { return L + (rows.length < 2 ? w / 2 : i / (rows.length - 1) * w); };
    var y = function (v) { return T + h - v / max * h; };
    var s = '<svg viewBox="0 0 ' + W + ' ' + H + '" width="' + W + '" height="' + H + '" role="img" aria-label="' + METRICS[m] + ' par jour">' +
      '<defs><linearGradient id="ar" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".22"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient></defs>';
    for (var g = 0; g <= 4; g++) {
      var gv = max / 4 * g, gy = y(gv);
      s += '<line x1="' + L + '" x2="' + (W - R) + '" y1="' + gy + '" y2="' + gy + '" stroke="' + (g ? '#232323' : '#3a3a3a') + '"/>' +
        '<text x="' + (L - 10) + '" y="' + (gy + 4) + '" text-anchor="end" class="tick">' + nf.format(Math.round(gv)) + '</text>';
    }
    var step = Math.max(1, Math.ceil(rows.length / Math.max(2, Math.floor(w / 80))));
    rows.forEach(function (r, i) { if ((rows.length - 1 - i) % step === 0) s += '<text x="' + x(i) + '" y="' + (H - 8) + '" text-anchor="' + (i === rows.length - 1 ? 'end' : i === 0 ? 'start' : 'middle') + '" class="tick">' + dshort(r.d) + '</text>'; });
    var pts = rows.map(function (r, i) { return x(i).toFixed(1) + ',' + y(r[m] || 0).toFixed(1); });
    if (rows.length > 1) s += '<path d="M' + pts.join('L') + 'L' + x(rows.length - 1) + ',' + (T + h) + 'L' + x(0) + ',' + (T + h) + 'Z" fill="url(#ar)"/>' +
      '<path d="M' + pts.join('L') + '" fill="none" stroke="#fff" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"/>';
    s += '<g id="hov" style="display:none"><line id="hl" y1="' + T + '" y2="' + (T + h) + '" stroke="#6b6b6b" stroke-dasharray="3 3"/><circle id="hc" r="5" fill="#fff" stroke="#131313" stroke-width="2"/></g></svg><div class="tip" id="tip" hidden></div>';
    box.innerHTML = s;
    var svg = box.querySelector('svg'), hov = svg.querySelector('#hov'), tip = $('tip');
    var move = function (e) {
      if (!rows.length) return;
      var rect = svg.getBoundingClientRect(), px = (e.touches ? e.touches[0].clientX : e.clientX) - rect.left;
      var i = Math.max(0, Math.min(rows.length - 1, Math.round((px - L) / w * (rows.length - 1)))), r = rows[i];
      hov.style.display = ''; svg.querySelector('#hl').setAttribute('x1', x(i)); svg.querySelector('#hl').setAttribute('x2', x(i));
      svg.querySelector('#hc').setAttribute('cx', x(i)); svg.querySelector('#hc').setAttribute('cy', y(r[m] || 0));
      tip.hidden = false;
      tip.innerHTML = '<b>' + new Date(r.d + 'T12:00:00').toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long' }) + '</b>' +
        Object.keys(METRICS).map(function (k) { return '<span' + (k === m ? ' class="on"' : '') + '>' + METRICS[k] + '<em>' + nf.format(r[k] || 0) + '</em></span>'; }).join('');
      var tx = x(i) + 14; if (tx + tip.offsetWidth > W) tx = x(i) - tip.offsetWidth - 14;
      tip.style.left = Math.max(0, tx) + 'px'; tip.style.top = Math.max(0, y(r[m] || 0) - 20) + 'px';
    };
    var out = function () { hov.style.display = 'none'; tip.hidden = true; };
    svg.addEventListener('mousemove', move); svg.addEventListener('touchstart', move, { passive: true }); svg.addEventListener('touchmove', move, { passive: true });
    svg.addEventListener('mouseleave', out);
  }

  function hours(d) {
    var el = $('hours'), by = {}; (d.hours || []).forEach(function (r) { by[r.h] = r.v; });
    var max = Math.max.apply(null, Object.keys(by).map(function (k) { return by[k]; }).concat([1]));
    var s = '';
    for (var hh = 0; hh < 24; hh++) {
      var v = by[hh] || 0;
      s += '<div class="hcol" title="' + hh + ' h : ' + nf.format(v) + ' pages vues"><i style="height:' + (v ? Math.max(3, v / max * 100) : 0) + '%"></i><span>' + (hh % 3 === 0 ? hh + 'h' : '') + '</span></div>';
    }
    el.innerHTML = s;
  }

  function recent(d) {
    var rows = d.recent || [];
    $('recent').innerHTML = rows.length ? rows.map(function (r) {
      var what = r.kind === 'click' ? (r.name === 'download' ? 'Clic sur Télécharger' : r.name === 'discord' ? 'Clic vers Discord' : 'Clic vers GitHub') : 'Page vue';
      return '<li><span class="chip' + (r.kind === 'click' ? ' chip-solid' : '') + '">' + what + '</span><span class="grow"><code>' + esc(r.page || '') + '</code> depuis <strong>' + esc(name(r.source)) + '</strong>' +
        (r.device ? ' · ' + esc(r.device) : '') + '</span><time>' + new Date(r.ts).toLocaleString('fr-FR', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }) + '</time></li>';
    }).join('') : '<li><p class="empty">Rien pour l\'instant.</p></li>';
  }

  function render() {
    var d = state.data; if (!d) return;
    $('demo-note').hidden = !d.demo;
    kpis(d); lineChart(); sources(d); hours(d); recent(d);
    bars('channels', d.channels, function (k) { return CHANNELS[k] || name(k); });
    bars('referrers', d.referrers, null, 'Aucun site référent : les visiteurs arrivent en direct ou via des liens suivis.');
    bars('pages', d.pages);
    bars('devices', d.devices, name); bars('browsers', d.browsers); bars('os', d.os);
    bars('langs', d.langs, function (k) { return LANGS[k] || k.toUpperCase(); });
    bars('zones', d.zones, function (k) { return k.replace(/_/g, ' ').replace('/', ' › '); });
    var camp = $('campaigns'), rows = d.campaigns || [];
    camp.innerHTML = rows.length ? '<table class="table"><thead><tr><th>Campagne</th><th>Provenance</th><th class="num">Visites</th><th class="num">Téléch.</th></tr></thead><tbody>' +
      rows.map(function (r) { return '<tr><td><code>' + esc(r.k) + '</code></td><td>' + esc(name(r.source)) + '</td><td class="num">' + nf.format(r.visits) + '</td><td class="num">' + nf.format(r.downloads) + '</td></tr>'; }).join('') + '</tbody></table>'
      : '<p class="empty">Aucune campagne. Ajoutez un nom de campagne dans le générateur de liens pour comparer vos publications.</p>';
    $('updated').textContent = 'Mis à jour à ' + new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }

  function refresh() {
    document.body.classList.add('loading');
    return fetchStats().then(function (d) {
      state.data = d; $('login').hidden = true; $('dash').hidden = false;
      try { if (state.pass) sessionStorage.setItem('malbo-dash', state.pass); } catch (e) {}
      render();
    }).catch(function (e) {
      $('dash').hidden = true; $('login').hidden = false;
      $('login-err').textContent = e.message === 'auth' ? (state.tried ? 'Mot de passe incorrect.' : '') : 'Connexion à la base impossible. Réessayez dans un instant.';
      try { sessionStorage.removeItem('malbo-dash'); } catch (x) {}
      state.pass = null;
    }).then(function () { document.body.classList.remove('loading'); });
  }

  // ---------- Événements ----------
  $('login-form').addEventListener('submit', function (e) {
    e.preventDefault(); state.pass = $('pass').value; state.tried = true; $('login-err').textContent = ''; refresh();
  });
  document.querySelectorAll('[data-days]').forEach(function (b) {
    b.addEventListener('click', function () {
      state.days = +b.dataset.days;
      document.querySelectorAll('[data-days]').forEach(function (x) { x.setAttribute('aria-pressed', x === b ? 'true' : 'false'); });
      refresh();
    });
  });
  document.querySelectorAll('[data-metric]').forEach(function (b) {
    b.addEventListener('click', function () {
      state.metric = b.dataset.metric;
      document.querySelectorAll('[data-metric]').forEach(function (x) { x.setAttribute('aria-pressed', x === b ? 'true' : 'false'); });
      $('chart-title').textContent = METRICS[state.metric] + ' par jour';
      lineChart();
    });
  });
  $('refresh').addEventListener('click', refresh);
  $('logout').addEventListener('click', function () {
    try { sessionStorage.removeItem('malbo-dash'); } catch (e) {}
    location.href = location.pathname;
  });
  var rt; window.addEventListener('resize', function () { clearTimeout(rt); rt = setTimeout(lineChart, 120); });

  // Générateur de liens suivis
  var base = location.href.replace(/[^/]*([?#].*)?$/, '');
  function links() {
    var src = ($('lb-src').value || 'tiktok').toLowerCase().replace(/[^a-z0-9._-]/g, ''), camp = $('lb-camp').value.trim().replace(/\s+/g, '-');
    var q = '?ref=' + encodeURIComponent(src) + (camp ? '&utm_campaign=' + encodeURIComponent(camp) : '');
    $('lb-site').value = base + q; $('lb-dl').value = base + 'download.html' + q; $('lb-discord').value = base + 'discord.html' + q;
  }
  ['lb-src', 'lb-camp'].forEach(function (id) { $(id).addEventListener('input', links); });
  links();
  document.querySelectorAll('[data-copy]').forEach(function (b) {
    b.addEventListener('click', function () {
      var i = $(b.dataset.copy); i.select();
      (navigator.clipboard ? navigator.clipboard.writeText(i.value) : Promise.reject()).catch(function () { document.execCommand('copy'); });
      var t = b.textContent; b.textContent = 'Copié'; setTimeout(function () { b.textContent = t; }, 1300);
    });
  });
  // Ne pas compter ses propres visites
  var own = $('own');
  if (own) { own.checked = localStorage.getItem('malbo-notrack') === '1'; own.addEventListener('change', function () { window.malboOptOut(own.checked); }); }

  if (DEMO || state.pass) refresh();
})();

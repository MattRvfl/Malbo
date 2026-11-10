// Malbo — mesure d'audience anonyme, sans cookie.
// Enregistre : page vue, provenance (site ou lien ?ref=), type d'appareil. Jamais d'adresse IP ni d'identifiant durable.
(function () {
  var C = window.MALBO || {};
  var KEY_OFF = 'malbo-notrack';
  var store = function (s, k, v) { try { if (v === undefined) return s.getItem(k); s.setItem(k, v); } catch (e) { return null; } };

  window.malboOptOut = function (off) { store(localStorage, KEY_OFF, off ? '1' : '0'); };
  window.malboIsOptedOut = function () {
    return store(localStorage, KEY_OFF) === '1' || navigator.doNotTrack === '1' || navigator.globalPrivacyControl === true;
  };

  // Provenances connues : domaine -> [source, canal]
  var KNOWN = [
    [/(^|\.)google\./, 'google', 'search'], [/(^|\.)bing\.com$/, 'bing', 'search'], [/duckduckgo\.com$/, 'duckduckgo', 'search'],
    [/(^|\.)yahoo\./, 'yahoo', 'search'], [/ecosia\.org$/, 'ecosia', 'search'], [/qwant\.com$/, 'qwant', 'search'],
    [/search\.brave\.com$/, 'brave', 'search'], [/yandex\./, 'yandex', 'search'],
    [/discord(app)?\.(com|gg)$/, 'discord', 'social'], [/(youtube\.com|youtu\.be)$/, 'youtube', 'social'],
    [/tiktok\.com$/, 'tiktok', 'social'], [/(twitter\.com|x\.com|t\.co)$/, 'x', 'social'],
    [/reddit\.com$/, 'reddit', 'social'], [/instagram\.com$/, 'instagram', 'social'],
    [/(facebook\.com|fb\.com|fb\.me)$/, 'facebook', 'social'], [/twitch\.tv$/, 'twitch', 'social'],
    [/linkedin\.com$|lnkd\.in$/, 'linkedin', 'social'], [/(telegram\.org|t\.me)$/, 'telegram', 'social'],
    [/whatsapp\.com$|wa\.me$/, 'whatsapp', 'social'], [/snapchat\.com$/, 'snapchat', 'social'],
    [/threads\.(net|com)$/, 'threads', 'social'], [/bsky\.app$/, 'bluesky', 'social'], [/mastodon\./, 'mastodon', 'social'],
    [/pinterest\./, 'pinterest', 'social'], [/github\.com$/, 'github', 'referral'], [/producthunt\.com$/, 'producthunt', 'referral']
  ];
  var ALIAS = { twitter: 'x', yt: 'youtube', ig: 'instagram', insta: 'instagram', fb: 'facebook', tt: 'tiktok' };
  var channelOf = function (src) {
    for (var i = 0; i < KNOWN.length; i++) if (KNOWN[i][1] === src) return KNOWN[i][2];
    return null;
  };

  function attribution() {
    var saved = store(sessionStorage, 'malbo-src');
    if (saved) { try { return JSON.parse(saved); } catch (e) {} }
    var q = new URLSearchParams(location.search);
    var a = { source: 'direct', medium: 'direct', campaign: q.get('utm_campaign') || '', ref_host: '' };
    var host = '';
    try { if (document.referrer) host = new URL(document.referrer).hostname.replace(/^www\./, '').toLowerCase(); } catch (e) {}
    if (host && host !== location.hostname.replace(/^www\./, '')) a.ref_host = host;
    var tag = (q.get('ref') || q.get('utm_source') || q.get('source') || '').trim().toLowerCase().replace(/[^a-z0-9._-]/g, '').slice(0, 40);
    if (tag) {
      a.source = ALIAS[tag] || tag;
      a.medium = (q.get('utm_medium') || '').toLowerCase() || channelOf(a.source) || 'campaign';
    } else if (a.ref_host) {
      a.source = a.ref_host; a.medium = 'referral';
      for (var i = 0; i < KNOWN.length; i++) if (KNOWN[i][0].test(a.ref_host)) { a.source = KNOWN[i][1]; a.medium = KNOWN[i][2]; break; }
    }
    store(sessionStorage, 'malbo-src', JSON.stringify(a));
    return a;
  }

  function sessionId() {
    var id = store(sessionStorage, 'malbo-sid');
    if (!id) { id = Math.random().toString(36).slice(2) + Date.now().toString(36); store(sessionStorage, 'malbo-sid', id); }
    return id;
  }

  function client() {
    var ua = navigator.userAgent, w = Math.min(screen.width, screen.height);
    var device = /Mobi|Android.+Mobile|iPhone/.test(ua) ? 'mobile' : (/iPad|Tablet|Android/.test(ua) || (w >= 600 && w < 1000 && 'ontouchstart' in window)) ? 'tablette' : 'ordinateur';
    var browser = /Edg\//.test(ua) ? 'Edge' : /OPR\/|Opera/.test(ua) ? 'Opera' : /Firefox\//.test(ua) ? 'Firefox' : /SamsungBrowser/.test(ua) ? 'Samsung Internet' : /Chrome\//.test(ua) ? 'Chrome' : /Safari\//.test(ua) ? 'Safari' : 'Autre';
    var os = /Windows/.test(ua) ? 'Windows' : /Android/.test(ua) ? 'Android' : /iPhone|iPad|iPod/.test(ua) ? 'iOS' : /Mac OS X/.test(ua) ? 'macOS' : /CrOS/.test(ua) ? 'ChromeOS' : /Linux/.test(ua) ? 'Linux' : 'Autre';
    var tz = ''; try { tz = Intl.DateTimeFormat().resolvedOptions().timeZone || ''; } catch (e) {}
    return { device: device, browser: browser, os: os, lang: navigator.language || '', tz: tz };
  }

  function send(kind, name) {
    if (!C.supabaseUrl || !C.supabaseKey || window.malboIsOptedOut()) return;
    if (/^(localhost|127\.|\[::1\])/.test(location.hostname) || location.protocol === 'file:') return;
    var a = attribution(), c = client();
    var page = location.pathname.split('/').pop() || 'index.html';
    var p = { kind: kind, name: name || null, page: page, source: a.source, medium: a.medium, campaign: a.campaign, ref_host: a.ref_host,
      device: c.device, browser: c.browser, os: c.os, lang: c.lang, tz: c.tz, sid: sessionId() };
    try {
      fetch(C.supabaseUrl + '/rest/v1/rpc/track', {
        method: 'POST', keepalive: true,
        headers: { 'Content-Type': 'application/json', apikey: C.supabaseKey, Authorization: 'Bearer ' + C.supabaseKey },
        body: JSON.stringify({ p: p })
      }).catch(function () {});
    } catch (e) {}
  }
  window.malboTrack = send;

  if (!document.documentElement.hasAttribute('data-notrack')) send('pageview');

  // Clics suivis : téléchargement (liens vers les releases) et GitHub
  document.addEventListener('click', function (e) {
    var a = e.target.closest && e.target.closest('a[href]');
    if (!a) return;
    var h = a.getAttribute('href');
    if (/\/releases(\/|$)/.test(h) || /\.exe$/.test(h)) send('click', 'download');
    else if (/^https:\/\/github\.com\//.test(h)) send('click', 'github');
  }, true);
})();

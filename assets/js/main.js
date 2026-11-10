// Malbo — interactions du site (aucune dépendance).
(function () {
  var reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // Menu mobile
  var burger = document.querySelector('.burger');
  var nav = document.getElementById('nav');
  if (burger && nav) {
    burger.addEventListener('click', function () {
      var open = nav.classList.toggle('open');
      burger.setAttribute('aria-expanded', open ? 'true' : 'false');
    });
  }

  // Apparition des cartes au défilement
  var items = document.querySelectorAll('.reveal');
  if ('IntersectionObserver' in window && !reduce) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) {
        if (!e.isIntersecting) return;
        e.target.classList.add('in');
        e.target.querySelectorAll('.meter i').forEach(function (m) { m.style.width = m.dataset.w; });
        io.unobserve(e.target);
      });
    }, { threshold: 0.12 });
    items.forEach(function (el, n) { el.style.transitionDelay = (n % 4) * 60 + 'ms'; io.observe(el); });
  } else {
    items.forEach(function (el) { el.classList.add('in'); });
    document.querySelectorAll('.meter i').forEach(function (m) { m.style.width = m.dataset.w; });
  }

  // Démo de l'île : alterne les états, s'ouvre au survol ou au clic
  var island = document.querySelector('.island');
  if (island) {
    var states = ['music', 'notif', 'timer', 'open'];
    var dots = document.querySelectorAll('.dots i');
    var idx = 0, held = false, timer = null;
    var set = function (s) {
      island.dataset.state = s;
      dots.forEach(function (d, n) { d.classList.toggle('on', states[n] === s); });
    };
    var next = function () { if (held) return; idx = (idx + 1) % states.length; set(states[idx]); };
    var start = function () { if (!reduce && !timer) timer = setInterval(next, 2800); };
    var hold = function () { held = true; set('open'); };
    var release = function () { held = false; idx = 0; set('music'); };
    island.addEventListener('mouseenter', hold);
    island.addEventListener('mouseleave', release);
    island.addEventListener('focus', hold);
    island.addEventListener('blur', release);
    island.addEventListener('click', function () { if (island.dataset.state !== 'open') hold(); });
    set('music');
    start();
  }

  // Paroles synchronisées (carte de démonstration)
  var lines = document.querySelectorAll('.lyrics span');
  if (lines.length && !reduce) {
    var li = 0;
    setInterval(function () {
      lines[li].classList.remove('on');
      li = (li + 1) % lines.length;
      lines[li].classList.add('on');
    }, 2200);
  }

  // Confidentialité : case « ne pas compter mes visites »
  var opt = document.getElementById('optout');
  if (opt && window.malboOptOut) {
    try { opt.checked = localStorage.getItem('malbo-notrack') === '1'; } catch (e) {}
    opt.addEventListener('change', function () { window.malboOptOut(opt.checked); });
  }

  // Boutons « copier »
  document.querySelectorAll('.copy').forEach(function (b) {
    b.addEventListener('click', function () {
      var pre = b.parentElement.querySelector('pre');
      if (!pre || !navigator.clipboard) return;
      navigator.clipboard.writeText(pre.innerText).then(function () {
        b.classList.add('done');
        b.setAttribute('aria-label', 'Copié');
        var old = b.innerHTML;
        b.innerHTML = '<svg class="i" viewBox="0 0 24 24"><path d="M20 6 9 17l-5-5"/></svg>';
        setTimeout(function () { b.innerHTML = old; b.setAttribute('aria-label', 'Copier'); }, 1400);
      });
    });
  });
})();

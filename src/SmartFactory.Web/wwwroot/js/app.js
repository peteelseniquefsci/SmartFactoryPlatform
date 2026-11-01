// Theme: Midnight is the default; Light is the user's choice, remembered per browser.
(function () {
  var root = document.documentElement;
  try { var saved = localStorage.getItem('sf-theme'); if (saved === 'light') root.setAttribute('data-theme', 'light'); } catch (e) {}
  document.addEventListener('click', function (ev) {
    var t = ev.target.closest('[data-theme-toggle]');
    if (!t) return;
    var light = root.getAttribute('data-theme') === 'light';
    if (light) root.removeAttribute('data-theme'); else root.setAttribute('data-theme', 'light');
    try { localStorage.setItem('sf-theme', light ? 'midnight' : 'light'); } catch (e) {}
  });
  // Weight check on My KRA: live total of active slots (a slot is active when its measure is non-empty).
  function weightCheck() {
    var out = document.querySelector('[data-weight-total]');
    if (!out || !document.querySelector('[data-slot-row]')) return;
    var total = 0;
    document.querySelectorAll('[data-slot-row]').forEach(function (row) {
      var measure = row.querySelector('[data-measure]');
      var weight = row.querySelector('[data-weight]');
      var retired = row.querySelector('[data-retired]');
      if (!measure || !weight) return;
      if (measure.value.trim() === '' || (retired && retired.checked)) return;
      total += parseFloat(weight.value || '0') || 0;
    });
    var rounded = Math.round(total * 1000) / 1000;
    out.textContent = rounded + '%';
    var note = document.querySelector('[data-weight-note]');
    var ok = Math.abs(rounded - 100) < 0.0005;
    out.classList.toggle('text-ember', !ok);
    out.classList.toggle('text-moss', ok);
    if (note) note.textContent = ok ? 'Weights total 100%.' : (rounded > 100 ? ('Excess of ' + (Math.round((rounded - 100) * 1000) / 1000) + '% to remove.') : ('Shortfall of ' + (Math.round((100 - rounded) * 1000) / 1000) + '% to add.'));
    document.querySelectorAll('[data-needs-100]').forEach(function (b) { b.disabled = !ok; b.setAttribute('aria-disabled', String(!ok)); });
  }
  document.addEventListener('input', function (ev) { if (ev.target.closest('[data-slot-row]')) weightCheck(); });
  document.addEventListener('change', function (ev) { if (ev.target.closest('[data-slot-row]')) weightCheck(); });
  document.addEventListener('DOMContentLoaded', weightCheck);
  // Collapsible slot details on My KRA.
  document.addEventListener('click', function (ev) {
    var b = ev.target.closest('[data-toggle]');
    if (!b) return;
    var target = document.getElementById(b.getAttribute('data-toggle'));
    if (!target) return;
    var hidden = target.classList.toggle('hidden');
    b.setAttribute('aria-expanded', String(!hidden));
  });
})();

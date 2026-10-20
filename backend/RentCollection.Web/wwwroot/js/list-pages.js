/* List pages — shared behaviours (2026-10 redesign).
   - [data-lp-search]      instant client-side row filter (matches tr[data-search])
   - [data-lp-filter=KEY]  chip filters: button[data-value] inside, rows carry data-KEY
   - th[data-sort=KEY]     column sort on tr[data-sort-KEY] (numeric when parseable)
   - .kebab-btn            row action menus
   - [data-lp-count]       "Showing N of M" label
   - details.lp-group      groups auto-open when a search matches inside them
*/
(function () {
    'use strict';

    function rowsOf(scope) {
        return Array.from((scope || document).querySelectorAll('tr[data-search], .lp-card[data-search]'));
    }

    function applyFilters(scope) {
        var q = (scope.querySelector('[data-lp-search]') || {}).value || '';
        q = q.trim().toLowerCase();
        var active = {};
        scope.querySelectorAll('[data-lp-filter]').forEach(function (g) {
            var on = g.querySelector('.lp-chip-btn.is-on');
            if (on && on.dataset.value !== '') active[g.dataset.lpFilter] = on.dataset.value;
        });
        var shown = 0, total = 0;
        rowsOf(scope).forEach(function (r) {
            total++;
            var ok = !q || (r.dataset.search || '').indexOf(q) !== -1;
            if (ok) for (var k in active) {
                var v = r.getAttribute('data-' + k) || '';
                if (active[k] === '__any__' ? v === '' : v.split(' ').indexOf(active[k]) === -1) { ok = false; break; }
            }
            r.hidden = !ok;
            if (ok) shown++;
        });
        var c = scope.querySelector('[data-lp-count]');
        if (c) c.textContent = shown === total ? (c.dataset.all || '').replace('{0}', total) : (c.dataset.some || '').replace('{0}', shown).replace('{1}', total);
        // Groups: hide those with no visible rows; open those that match a search.
        scope.querySelectorAll('details.lp-group').forEach(function (g) {
            var vis = rowsOf(g).filter(function (r) { return !r.hidden; }).length;
            g.hidden = vis === 0;
            var cnt = g.querySelector('[data-lp-group-count]');
            if (cnt) cnt.textContent = vis;
            if (q && vis > 0) g.open = true;
        });
        var empty = scope.querySelector('[data-lp-empty]');
        if (empty) empty.hidden = shown !== 0;
    }

    function sortTable(th) {
        var table = th.closest('table');
        var key = th.dataset.sort;
        var dir = th.classList.contains('asc') ? 'desc' : 'asc';
        table.querySelectorAll('th[data-sort]').forEach(function (h) { h.classList.remove('asc', 'desc'); });
        th.classList.add(dir);
        var body = table.tBodies[0];
        var rows = Array.from(body.rows);
        rows.sort(function (a, b) {
            var av = a.getAttribute('data-sort-' + key) || '', bv = b.getAttribute('data-sort-' + key) || '';
            var an = parseFloat(av), bn = parseFloat(bv);
            var cmp = (!isNaN(an) && !isNaN(bn)) ? an - bn : av.localeCompare(bv, undefined, { numeric: true, sensitivity: 'base' });
            return dir === 'asc' ? cmp : -cmp;
        });
        rows.forEach(function (r) { body.appendChild(r); });
    }

    function initKebabs() {
        document.addEventListener('click', function (e) {
            var btn = e.target.closest('.kebab-btn');
            if (btn) {
                e.preventDefault(); e.stopPropagation();
                var menu = btn.parentElement.querySelector('.kebab-menu');
                var wasOpen = menu && !menu.hasAttribute('hidden');
                document.querySelectorAll('.kebab-menu').forEach(function (m) { m.setAttribute('hidden', ''); });
                document.querySelectorAll('.kebab-btn').forEach(function (b) { b.setAttribute('aria-expanded', 'false'); });
                if (menu && !wasOpen) { menu.removeAttribute('hidden'); btn.setAttribute('aria-expanded', 'true'); }
                return;
            }
            if (!e.target.closest('.kebab-menu')) {
                document.querySelectorAll('.kebab-menu').forEach(function (m) { m.setAttribute('hidden', ''); });
            }
        });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') document.querySelectorAll('.kebab-menu').forEach(function (m) { m.setAttribute('hidden', ''); });
        });
    }

    function init() {
        document.querySelectorAll('[data-lp-scope]').forEach(function (scope) {
            var search = scope.querySelector('[data-lp-search]');
            if (search) {
                var t;
                search.addEventListener('input', function () { clearTimeout(t); t = setTimeout(function () { applyFilters(scope); }, 120); });
            }
            scope.querySelectorAll('[data-lp-filter]').forEach(function (g) {
                g.addEventListener('click', function (e) {
                    var b = e.target.closest('.lp-chip-btn');
                    if (!b || b.tagName === 'A' || b.getAttribute('type') === 'submit') return;
                    e.preventDefault();
                    g.querySelectorAll('.lp-chip-btn').forEach(function (x) { x.classList.remove('is-on'); });
                    b.classList.add('is-on');
                    applyFilters(scope);
                });
            });
            scope.querySelectorAll('th[data-sort]').forEach(function (th) {
                th.addEventListener('click', function () { sortTable(th); });
            });
            applyFilters(scope);
        });
        initKebabs();
    }

    // Money / date helpers for the KPI-batch scripts on each page.
    window.LP = {
        kwd: function (v) { return (Number(v) || 0).toLocaleString('en-US', { minimumFractionDigits: 3, maximumFractionDigits: 3 }) + ' KWD'; },
        shortDate: function (iso) { if (!iso) return ''; var d = new Date(iso); return isNaN(d) ? '' : d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short' }); },
        fullDate: function (iso) { if (!iso) return ''; var d = new Date(iso); return isNaN(d) ? '' : d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }); },
        refresh: function (scope) { applyFilters(scope || document.querySelector('[data-lp-scope]')); }
    };

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();

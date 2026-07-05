/*
 * form-helpers.js — Sprint 23 forms revamp.
 * Tiny, dependency-free helpers shared by Tenant / Lease / Property
 * create forms. Loaded after jQuery in _Layout but doesn't require it.
 *
 * What it does:
 *   • Inline validation on blur (red border + hint chip below the field)
 *   • "More details" toggle (collapses optional sections; remembers state
 *     per form in sessionStorage so the operator's choice sticks)
 *   • "Save & add another" submit handler (posts the form normally but
 *     marks the action so the controller redirects back to Create instead
 *     of Index)
 *   • sessionStorage draft autosave so a tab reload doesn't lose work
 *   • Lock the submit button + show spinner once clicked (no double-submit)
 */
(function (global) {
    'use strict';

    var FormHelpers = {
        /** Hook all `.form-revamp` forms on the page. Idempotent. */
        init: function () {
            document.querySelectorAll('form.form-revamp').forEach(function (form) {
                FormHelpers._wireToggles(form);
                FormHelpers._wireInlineValidation(form);
                FormHelpers._wireDraftAutosave(form);
                FormHelpers._wireSubmitLock(form);
                FormHelpers._wireSaveAndAddAnother(form);
            });
        },

        // ── "More details" collapsible toggles ────────────────────────────
        _wireToggles: function (form) {
            form.querySelectorAll('[data-fr-toggle]').forEach(function (btn) {
                var target = form.querySelector('[data-fr-collapsible="' + btn.dataset.frToggle + '"]');
                if (!target) return;

                var storageKey = 'fr-toggle:' + (form.id || form.action) + ':' + btn.dataset.frToggle;
                // Restore previous state
                try {
                    if (sessionStorage.getItem(storageKey) === 'open') {
                        target.classList.add('is-open');
                        btn.classList.add('is-open');
                    }
                } catch (e) { /* sessionStorage blocked — silent */ }

                btn.addEventListener('click', function () {
                    var nowOpen = !target.classList.contains('is-open');
                    target.classList.toggle('is-open', nowOpen);
                    btn.classList.toggle('is-open', nowOpen);
                    try { sessionStorage.setItem(storageKey, nowOpen ? 'open' : 'closed'); }
                    catch (e) { /* silent */ }
                });
            });
        },

        // ── Inline validation: required + email + minlength + custom ─────
        _wireInlineValidation: function (form) {
            form.querySelectorAll('.fr-field input, .fr-field select, .fr-field textarea')
                .forEach(function (el) {
                    el.addEventListener('blur', function () {
                        FormHelpers._validateField(el);
                    });
                    // Clear error chip as soon as user starts fixing
                    el.addEventListener('input', function () {
                        var hint = FormHelpers._hintEl(el);
                        if (hint && hint.classList.contains('is-error')) {
                            hint.classList.remove('is-error');
                            hint.textContent = hint.dataset.frDefaultText || '';
                            el.style.borderColor = '';
                        }
                    });
                });
        },

        _validateField: function (el) {
            var hint = FormHelpers._hintEl(el);
            if (!hint) return true;

            // Skip validation while collapsed (operator never opened it)
            var collapsible = el.closest('[data-fr-collapsible]');
            if (collapsible && !collapsible.classList.contains('is-open')) return true;

            var isRequired = el.hasAttribute('required') || el.hasAttribute('data-fr-required');
            var value = (el.value || '').trim();

            if (isRequired && !value) {
                FormHelpers._showHint(hint, 'Required', 'error');
                el.style.borderColor = '#dc2626';
                return false;
            }
            if (el.type === 'email' && value && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) {
                FormHelpers._showHint(hint, 'Not a valid email', 'error');
                el.style.borderColor = '#dc2626';
                return false;
            }
            if (el.type === 'number' && value && el.hasAttribute('min')) {
                var min = parseFloat(el.getAttribute('min'));
                if (parseFloat(value) < min) {
                    FormHelpers._showHint(hint, 'Must be ≥ ' + min, 'error');
                    el.style.borderColor = '#dc2626';
                    return false;
                }
            }

            // Clear error chip if previously errored
            if (hint.classList.contains('is-error')) {
                FormHelpers._showHint(hint, hint.dataset.frDefaultText || '', null);
                el.style.borderColor = '';
            }
            return true;
        },

        _hintEl: function (el) {
            // The hint chip is the next sibling within the same .fr-field block
            var field = el.closest('.fr-field');
            if (!field) return null;
            var hint = field.querySelector('.fr-hint');
            if (hint && !hint.dataset.frDefaultText) {
                hint.dataset.frDefaultText = hint.textContent;
            }
            return hint;
        },

        _showHint: function (hint, text, kind) {
            hint.classList.remove('is-error', 'is-warn', 'is-success');
            if (kind) hint.classList.add('is-' + kind);
            hint.textContent = text;
        },

        // ── Draft autosave to sessionStorage ──────────────────────────────
        _wireDraftAutosave: function (form) {
            var key = 'fr-draft:' + (form.id || form.action);

            // Opt-out: forms marked data-fr-no-draft never restore prior values
            // (e.g. Tenant Create — operators don't want the last tenant's name
            // pre-filled). Purge any draft saved before the opt-out was added.
            if (form.hasAttribute('data-fr-no-draft')) {
                try { sessionStorage.removeItem(key); } catch (e) { /* silent */ }
                return;
            }

            // Restore on load (skip files, hidden tokens, password fields)
            try {
                var saved = sessionStorage.getItem(key);
                if (saved) {
                    var data = JSON.parse(saved);
                    Object.keys(data).forEach(function (name) {
                        var el = form.querySelector('[name="' + CSS.escape(name) + '"]');
                        if (el && el.type !== 'file' && el.type !== 'password' && el.type !== 'hidden' && !el.value) {
                            el.value = data[name];
                        }
                    });
                    FormHelpers._showDraftBadge(form);
                }
            } catch (e) { /* silent */ }

            // Save on input (debounced)
            var saveTimer = null;
            form.addEventListener('input', function () {
                clearTimeout(saveTimer);
                saveTimer = setTimeout(function () { FormHelpers._saveDraft(form, key); }, 500);
            });

            // Clear on successful submit
            form.addEventListener('submit', function () {
                try { sessionStorage.removeItem(key); } catch (e) { /* silent */ }
            });
        },

        _saveDraft: function (form, key) {
            try {
                var data = {};
                form.querySelectorAll('input, select, textarea').forEach(function (el) {
                    if (!el.name || el.type === 'file' || el.type === 'password' || el.type === 'hidden') return;
                    if (el.type === 'checkbox') return;
                    if (el.value) data[el.name] = el.value;
                });
                sessionStorage.setItem(key, JSON.stringify(data));
            } catch (e) { /* silent */ }
        },

        _showDraftBadge: function (form) {
            var slot = form.querySelector('[data-fr-draft-slot]');
            if (!slot) return;
            slot.innerHTML = '<span class="fr-draft-badge"><i class="fas fa-rotate-left"></i> Draft restored</span>';
        },

        // ── Lock submit + show spinner (prevents double-submit) ──────────
        // Only lock when the form will ACTUALLY submit. jQuery unobtrusive
        // validation calls preventDefault() on invalid input, which cancels the
        // POST — if we locked the buttons unconditionally on the 'submit' event,
        // the spinner would spin forever and the disabled buttons would block any
        // retry (the "Create Tenant loads all the time" bug). So we gate the lock
        // on validity and defensively clear it if validation cancels the submit.
        _wireSubmitLock: function (form) {
            function setLock(on) {
                form.querySelectorAll('button[type="submit"]').forEach(function (btn) {
                    btn.classList.toggle('is-loading', on);
                    btn.disabled = on;
                });
            }
            form.addEventListener('submit', function () {
                var valid = true;
                try {
                    if (window.jQuery && window.jQuery.fn && window.jQuery.fn.validate
                        && window.jQuery(form).data('validator')) {
                        valid = window.jQuery(form).valid();      // authoritative for asp.net unobtrusive
                    } else if (typeof form.checkValidity === 'function') {
                        valid = form.checkValidity();
                    }
                } catch (e) { valid = true; }
                if (!valid) { setLock(false); return; }           // invalid → POST is cancelled, keep buttons usable
                setLock(true);
            });
            // Safety nets so a lock can never get stuck:
            form.addEventListener('invalid', function () { setLock(false); }, true);  // native constraint fail
            window.addEventListener('pageshow', function () { setLock(false); });     // bfcache Back restore
        },

        // ── "Save & add another" ─────────────────────────────────────────
        _wireSaveAndAddAnother: function (form) {
            var btn = form.querySelector('[data-fr-save-and-add]');
            if (!btn) return;
            btn.addEventListener('click', function () {
                // Tag the form so the controller can read it and redirect back
                // to Create instead of Index/Details after save.
                var marker = form.querySelector('input[name="__SaveAndAddAnother"]');
                if (!marker) {
                    marker = document.createElement('input');
                    marker.type = 'hidden';
                    marker.name = '__SaveAndAddAnother';
                    form.appendChild(marker);
                }
                marker.value = 'true';
                // Allow normal submit flow to continue
            });
        },
    };

    global.FormHelpers = FormHelpers;
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', FormHelpers.init);
    } else {
        FormHelpers.init();
    }
})(window);

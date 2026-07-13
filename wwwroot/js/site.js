// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

/* ── Theme toggle (dark / light mode) ── */
(function () {
    var toggle = document.getElementById('themeToggle');
    if (!toggle) return;

    var saved = localStorage.getItem('sito-theme');

    // checked = light mode (sun), unchecked = dark mode (moon)
    if (saved === 'dark') {
        document.body.classList.add('dark-mode');
        toggle.checked = false;
    } else {
        toggle.checked = true; // default: light
    }

    toggle.addEventListener('change', function () {
        if (this.checked) {
            document.body.classList.remove('dark-mode');
            localStorage.setItem('sito-theme', 'light');
        } else {
            document.body.classList.add('dark-mode');
            localStorage.setItem('sito-theme', 'dark');
        }
    });
})();

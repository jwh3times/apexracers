// Synchronous, same-origin bootstrap: choose the theme before the first render.
(function () {
  var theme = 'auto';
  try {
    var stored = localStorage.getItem('ar_theme');
    if (stored === 'light' || stored === 'dark') theme = stored;
  } catch {
    // Storage may be unavailable; the system theme still works.
  }
  document.documentElement.classList.add('theme-' + theme);
})();

// The shop's tokens turn dark with data-theme="dark" on <html> (design/theme.css);
// Keycloak's own theme turns PatternFly dark with a class. This sets the one the
// tokens read, from the same media query, before the first paint — a classic
// script in <head>, so it runs before the page is drawn.
(function () {
  var dark = window.matchMedia('(prefers-color-scheme: dark)');
  var apply = function (isDark) {
    document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
  };
  apply(dark.matches);
  dark.addEventListener('change', function (event) {
    apply(event.matches);
  });
})();

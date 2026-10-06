// Drives the _PeriodToggle partial. The selected period is remembered in the "statsPeriod" cookie
// ("all"; absent means the last 12 months), so it carries across the dashboard and vehicle pages.
// It's applied as data-stats-period on <html>, and site.css hides the .period-stats elements that don't match.
// Loaded synchronously straight after the buttons, so the right period is shown before the stats render;
// the partial reads the same cookie to render the selected button.
(() => {
    'use strict';
    const cookieName = 'statsPeriod';
    const buttons = document.querySelectorAll('[data-show-period]');

    const saved = () =>
        document.cookie.split('; ').includes(`${cookieName}=all`) ? 'all' : '12m';

    const save = period => {
        document.cookie = period === 'all'
            ? `${cookieName}=all; path=/; max-age=31536000; samesite=lax`
            : `${cookieName}=; path=/; max-age=0; samesite=lax`;
    };

    const apply = period => {
        document.documentElement.dataset.statsPeriod = period;
        buttons.forEach(b => {
            const active = b.dataset.showPeriod === period;
            b.classList.toggle('active', active);
            b.setAttribute('aria-selected', active);
        });
    };

    apply(saved());

    buttons.forEach(button => button.addEventListener('click', () => {
        save(button.dataset.showPeriod);
        apply(button.dataset.showPeriod);
    }));

    // Pages restored by the back/forward cache keep their old state – bring them up to date.
    window.addEventListener('pageshow', e => {
        if (e.persisted) apply(saved());
    });
})();

// Drives the _PeriodToggle partial: shows every .period-stats element whose data-period
// matches the selected button and hides the rest.
(() => {
    'use strict';
    const buttons = document.querySelectorAll('[data-show-period]');
    buttons.forEach(button => button.addEventListener('click', () => {
        const period = button.dataset.showPeriod;
        buttons.forEach(b => {
            b.classList.toggle('active', b === button);
            b.setAttribute('aria-selected', b === button);
        });
        document.querySelectorAll('.period-stats').forEach(el =>
            el.classList.toggle('d-none', el.dataset.period !== period));
    }));
})();

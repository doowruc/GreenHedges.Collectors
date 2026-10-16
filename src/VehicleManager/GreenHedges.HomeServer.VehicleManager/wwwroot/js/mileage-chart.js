// Draws a mileage-over-time chart in each <canvas data-mileage-chart='[{"date":"yyyy-MM-dd","miles":45000,"note":"Main Dealer","failed":false}, …]'>
// (oldest first), e.g. a vehicle's service history or MOT tests. Dates go on a linear axis, so the gaps between
// readings show to scale. A reading with "failed": true (a failed MOT) is drawn in red; "note" goes in its tooltip.
// Needs Chart.js first. Charts in a hidden tab are sized when the tab is shown (Chart.js watches the canvas's size).
(() => {
    'use strict';
    const formatDate = (ms, options) => new Date(ms).toLocaleDateString('en-GB', { timeZone: 'UTC', ...options });
    const msPerMonth = 365.25 / 12 * 86400000;
    const lineColour = '#0d6efd', failedColour = '#dc3545';

    const styleDefaults = () => {
        const { text, grid } = window.themeColors();
        Chart.defaults.color = text;
        Chart.defaults.borderColor = grid;
    };
    styleDefaults();

    const charts = [...document.querySelectorAll('canvas[data-mileage-chart]')].map(canvas => {
        const readings = JSON.parse(canvas.dataset.mileageChart);
        const points = readings.map(r => ({ x: Date.parse(r.date), y: r.miles }));
        const colours = readings.map(r => r.failed ? failedColour : lineColour);
        // A little room either side, so the first and last points aren't cut off at the edges.
        const first = points[0].x, last = points[points.length - 1].x, pad = Math.max((last - first) * 0.03, 7 * 86400000);
        return new Chart(canvas, {
            type: 'line',
            data: {
                datasets: [{
                    label: 'Miles', data: points, borderColor: lineColour, backgroundColor: colours,
                    pointBorderColor: colours, pointRadius: 3, pointHoverRadius: 5
                }]
            },
            options: {
                // Fill .chart-box's height rather than a fixed width:height ratio, which left it very short on phones.
                maintainAspectRatio: false,
                interaction: { mode: 'nearest', axis: 'x', intersect: false },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            title: items => items.length === 0 ? '' : formatDate(items[0].parsed.x, { day: '2-digit', month: '2-digit', year: 'numeric' }),
                            label: item => `${item.parsed.y.toLocaleString('en-GB')} miles`,
                            afterLabel: item => readings[item.dataIndex].note ?? ''
                        }
                    }
                },
                scales: {
                    x: {
                        type: 'linear',
                        min: first - pad,
                        max: last + pad,
                        ticks: {
                            // Days as well as months over a short span, so the labels don't repeat.
                            callback(v) {
                                const short = this.max - this.min < 4 * msPerMonth;
                                return formatDate(v, short ? { day: 'numeric', month: 'short' } : { month: 'short', year: 'numeric' });
                            },
                            maxRotation: 0,
                            autoSkipPadding: 16
                        }
                    },
                    y: {
                        title: { display: true, text: 'Miles' },
                        grace: '5%',
                        ticks: { callback: v => v.toLocaleString('en-GB') }
                    }
                }
            }
        });
    });

    document.addEventListener('themechange', () => {
        styleDefaults();
        // Scales copy the defaults when created, so update them directly too.
        const { text, grid } = window.themeColors();
        for (const chart of charts) {
            for (const scale of Object.values(chart.options.scales)) {
                scale.ticks.color = text;
                scale.title.color = text;
                scale.grid.color = grid;
                scale.border.color = grid;
            }
            chart.update('none');
        }
    });
})();

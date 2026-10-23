// Draws a price-per-litre chart in each <canvas data-price-chart='[{"date":"yyyy-MM-dd","price":1.459}, …]'>
// (oldest first), e.g. a station's page. Dates go on a linear axis, so gaps between fill-ups show as gaps.
// A dashed least-squares polynomial trend curve runs across them, with its latest rate of change in the legend.
// Needs Chart.js first.
(() => {
    'use strict';
    const formatDate = (ms, options) => new Date(ms).toLocaleDateString('en-GB', { timeZone: 'UTC', ...options });

    const styleDefaults = () => {
        const { text, grid } = window.themeColors();
        Chart.defaults.color = text;
        Chart.defaults.borderColor = grid;
    };
    styleDefaults();

    const msPerMonth = 365.25 / 12 * 86400000;

    // Degree of the trend curve: at most 4 (up to 3 bends), and one degree per 3 dates with fill-ups, so a
    // station visited only a few times gets a gentle curve or a straight line rather than one that swings
    // wildly between its points. Fill-ups on the same date count once, as they add no new point in time.
    const maxTrendDegree = 4;
    const datesPerDegree = 3;

    // Least-squares polynomial through the points, as points along the curve plus its change per month at
    // the latest date; null without at least two different dates.
    const trend = points => {
        const dates = new Set(points.map(p => p.x)).size;
        const degree = Math.min(maxTrendDegree, Math.max(1, Math.floor(dates / datesPerDegree)), dates - 1);
        if (degree < 1) return null;
        const x0 = points[0].x, span = points[points.length - 1].x - x0;
        const scaled = x => (x - x0) / span;   // 0 to 1, so the powers stay well scaled

        // Normal equations (AᵀA)c = Aᵀy as an augmented matrix, solved by Gauss–Jordan elimination.
        const size = degree + 1;
        const m = Array.from({ length: size }, () => new Array(size + 1).fill(0));
        for (const p of points) {
            const u = scaled(p.x);
            for (let i = 0; i < size; i++) {
                for (let j = 0; j < size; j++) m[i][j] += u ** (i + j);
                m[i][size] += u ** i * p.y;
            }
        }
        for (let col = 0; col < size; col++) {
            let pivot = col;
            for (let r = col + 1; r < size; r++) if (Math.abs(m[r][col]) > Math.abs(m[pivot][col])) pivot = r;
            [m[col], m[pivot]] = [m[pivot], m[col]];
            for (let r = 0; r < size; r++) {
                if (r === col) continue;
                const f = m[r][col] / m[col][col];
                for (let k = col; k <= size; k++) m[r][k] -= f * m[col][k];
            }
        }
        const c = m.map((row, i) => row[size] / row[i]);   // c[i] is the coefficient of u^i

        const at = u => c.reduce((sum, ci, i) => sum + ci * u ** i, 0);
        const slopeAtEnd = c.reduce((sum, ci, i) => sum + i * ci, 0) / span;   // d/dx at u = 1
        const steps = 60;
        // Rounded to 0.01p: otherwise rounding error in the fit (e.g. when every price is the same) shows as
        // wild swings, as the axis zooms in on differences of 0.0000000000001p.
        const round = y => Math.round(y * 10000) / 10000;
        const line = Array.from({ length: steps + 1 }, (_, k) => ({ x: x0 + span * k / steps, y: round(at(k / steps)) }));
        return { line, perMonth: slopeAtEnd * msPerMonth };
    };

    // Hover picks the fill-up(s) nearest the pointer along the date axis (all of them when several share
    // a date), never a point on the trend curve.
    Chart.Interaction.modes.nearestFillUp = (chart, e) => {
        const { x } = Chart.helpers.getRelativePosition(e, chart);
        const items = chart.getDatasetMeta(0).data.map((element, index) => ({ element, datasetIndex: 0, index }));
        const closest = Math.min(...items.map(i => Math.abs(i.element.x - x)));
        return items.filter(i => Math.abs(i.element.x - x) === closest);
    };

    const charts = [...document.querySelectorAll('canvas[data-price-chart]')].map(canvas => {
        const points = JSON.parse(canvas.dataset.priceChart).map(p => ({ x: Date.parse(p.date), y: p.price }));
        const fit = trend(points);
        const datasets = [{ label: '£ per litre', data: points, borderColor: '#fd7e14', backgroundColor: '#fd7e14', tension: .2, pointRadius: 2 }];
        if (fit) {
            const pence = Math.round(fit.perMonth * 1000) / 10;   // to 0.1p, so it never shows as "−0.0p"
            datasets.push({
                label: `Trend: now ${pence > 0 ? '+' : pence < 0 ? '−' : '±'}${Math.abs(pence).toFixed(1)}p a month`, data: fit.line,
                borderColor: '#adb5bd', backgroundColor: '#adb5bd', borderDash: [6, 4], borderWidth: 2, pointRadius: 0, pointHitRadius: 0
            });
        }
        return new Chart(canvas, {
            type: 'line',
            data: { datasets },
            options: {
                // Fill .chart-box's height rather than a fixed width:height ratio, which left it very short on phones.
                maintainAspectRatio: false,
                interaction: { mode: 'nearestFillUp', intersect: false },
                plugins: {
                    legend: { display: !!fit },
                    tooltip: {
                        callbacks: {
                            title: items => items.length === 0 ? '' : formatDate(items[0].parsed.x, { day: '2-digit', month: '2-digit', year: 'numeric' }),
                            label: item => `£${item.parsed.y.toFixed(3)} per litre`
                        }
                    }
                },
                scales: {
                    x: {
                        type: 'linear',
                        // With a single date, a month either side rather than Chart.js's default spread of years.
                        ...(fit ? {} : { min: points[0].x - msPerMonth, max: points[0].x + msPerMonth }),
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
                    y: { title: { display: true, text: '£ / litre' } }
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
            chart.options.plugins.legend.labels.color = text;
            chart.update('none');
        }
    });
})();

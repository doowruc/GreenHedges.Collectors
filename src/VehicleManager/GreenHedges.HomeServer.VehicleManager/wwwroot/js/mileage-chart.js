// Draws a mileage-over-time chart in each <canvas data-mileage-chart='[{"date":"yyyy-MM-dd","miles":45000,"note":"Main Dealer",
// "failed":false,"previousOwner":false}, …]'> (oldest first), e.g. a vehicle's service history or MOT tests. Dates go on a
// linear axis, so the gaps between readings show to scale. "note" goes in the tooltip. A failed MOT's point is red; a
// previous owner's reading is a hollow grey point, with the line dashed grey from it to the next. A legend explains the
// colours when there are any.
// A reading with a "ref" links to the page's element with that data-chart-ref: clicking near its point scrolls there
// and highlights it. If only a hidden copy is found inside a .period-stats block (e.g. an older MOT while "Last 12
// months" is shown), the period toggle is switched to "All time" first.
// Needs Chart.js first. Charts in a hidden tab are sized when the tab is shown (Chart.js watches the canvas's size).
(() => {
    'use strict';
    const formatDate = (ms, options) => new Date(ms).toLocaleDateString('en-GB', { timeZone: 'UTC', ...options });
    const msPerMonth = 365.25 / 12 * 86400000;
    const lineColour = '#0d6efd', failedColour = '#dc3545', previousColour = '#6c757d';
    const previousDash = [5, 4];

    const styleDefaults = () => {
        const { text, grid } = window.themeColors();
        Chart.defaults.color = text;
        Chart.defaults.borderColor = grid;
    };
    styleDefaults();

    // The visible element for a ref, switching the period toggle to "All time" if that's where it is.
    const target = ref => {
        const all = [...document.querySelectorAll('[data-chart-ref]')].filter(el => el.dataset.chartRef === ref);
        const visible = () => all.find(el => el.offsetParent !== null);
        if (!visible() && all.some(el => el.closest('.period-stats[data-period="all"]')))
            document.querySelector('[data-show-period="all"]')?.click();
        return visible();
    };

    const showRef = ref => {
        const el = target(ref);
        if (!el) return;
        el.scrollIntoView({ behavior: 'smooth', block: 'center' });
        el.classList.add('chart-ref-highlight');
        // Removed on the next frame, so the background fades out (site.css).
        setTimeout(() => requestAnimationFrame(() => el.classList.remove('chart-ref-highlight')), 600);
    };

    const charts = [...document.querySelectorAll('canvas[data-mileage-chart]')].map(canvas => {
        const readings = JSON.parse(canvas.dataset.mileageChart);
        const points = readings.map(r => ({ x: Date.parse(r.date), y: r.miles }));
        const colours = readings.map(r => r.failed ? failedColour : r.previousOwner ? previousColour : lineColour);
        // Hollow for a previous owner's (the canvas shows through), so it differs by more than colour.
        const fills = readings.map((r, i) => r.previousOwner && !r.failed ? 'transparent' : colours[i]);
        // The line out of a previous owner's reading, i.e. through their time with the vehicle.
        const previousSegment = (ctx, value) => readings[ctx.p0DataIndex].previousOwner ? value : undefined;

        const legend = [
            ...(readings.some(r => r.previousOwner) ? [
                { text: 'Previous owner', fillStyle: 'transparent', strokeStyle: previousColour, lineDash: previousDash, lineWidth: 2 },
                { text: 'Yours', fillStyle: lineColour, strokeStyle: lineColour, lineWidth: 2 }
            ] : []),
            ...(readings.some(r => r.failed) ? [{ text: 'Failed MOT', fillStyle: failedColour, strokeStyle: failedColour, lineWidth: 2 }] : [])
        ];
        // A little room either side, so the first and last points aren't cut off at the edges.
        const first = points[0].x, last = points[points.length - 1].x, pad = Math.max((last - first) * 0.03, 7 * 86400000);
        return new Chart(canvas, {
            type: 'line',
            data: {
                datasets: [{
                    label: 'Miles', data: points, borderColor: lineColour, backgroundColor: fills,
                    pointBorderColor: colours, pointBorderWidth: 1.5, pointRadius: 3, pointHoverRadius: 5,
                    segment: {
                        borderColor: ctx => previousSegment(ctx, previousColour),
                        borderDash: ctx => previousSegment(ctx, previousDash)
                    }
                }]
            },
            options: {
                // Fill .chart-box's height rather than a fixed width:height ratio, which left it very short on phones.
                maintainAspectRatio: false,
                interaction: { mode: 'nearest', axis: 'x', intersect: false },
                // Clicking picks the point the tooltip shows (the nearest along the date axis).
                onClick: (e, elements) => {
                    const ref = elements.length > 0 ? readings[elements[0].index].ref : null;
                    if (ref) showRef(ref);
                },
                onHover: (e, elements) => {
                    e.native.target.style.cursor = elements.length > 0 && readings[elements[0].index].ref ? 'pointer' : 'default';
                },
                plugins: {
                    // Only to explain the colours, so its items aren't buttons that hide the line.
                    legend: {
                        display: legend.length > 0, onClick: () => {},
                        labels: {
                            boxWidth: 24, boxHeight: 8,
                            // Custom items need the text colour themselves (it follows the theme, see below).
                            generateLabels: chart => legend.map(item => ({ ...item, fontColor: chart.options.plugins.legend.labels.color }))
                        }
                    },
                    tooltip: {
                        callbacks: {
                            title: items => items.length === 0 ? '' : formatDate(items[0].parsed.x, { day: '2-digit', month: '2-digit', year: 'numeric' }),
                            label: item => `${item.parsed.y.toLocaleString('en-GB')} miles`,
                            afterLabel: item => {
                                const r = readings[item.dataIndex];
                                return [r.note, r.previousOwner ? 'Previous owner' : null].filter(Boolean).join(' · ');
                            }
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
            chart.options.plugins.legend.labels.color = text;
            chart.update('none');
        }
    });
})();

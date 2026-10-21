// The dashboard's fuel spending charts.
// <canvas data-spend-chart='{"labels":["Nov 25",…],"vehicles":[{"name":"Ford Focus","colour":0,"amounts":[75.5,…]},…]}'>:
// bars of spend per period (month or year), stacked by vehicle. "colour" indexes the palette, so a vehicle has the same
// colour in every chart.
// <canvas data-spend-trend='[{"year":2026,"cumulative":[30.5,…,null,null]},…]'> (newest first): the running total through
// each year, January to December, one line per year; this year bold, last year beside it, earlier years faint.
// Needs Chart.js first. Charts in a hidden period block are sized when it's shown (Chart.js watches the canvas's size).
(() => {
    'use strict';
    const palette = ['#0d6efd', '#fd7e14', '#198754', '#6f42c1', '#20c997', '#d63384', '#ffc107', '#0dcaf0'];
    const olderColour = 'rgba(108, 117, 125, 0.45)';
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const pounds = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP', maximumFractionDigits: 0 });
    const pence = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' });

    const styleDefaults = () => {
        const { text, grid } = window.themeColors();
        Chart.defaults.color = text;
        Chart.defaults.borderColor = grid;
    };
    styleDefaults();

    const yAxis = { beginAtZero: true, ticks: { callback: v => pounds.format(v) } };

    const bars = [...document.querySelectorAll('canvas[data-spend-chart]')].map(canvas => {
        const spend = JSON.parse(canvas.dataset.spendChart);
        return new Chart(canvas, {
            type: 'bar',
            data: {
                labels: spend.labels,
                datasets: spend.vehicles.map(v => ({
                    label: v.name, data: v.amounts, backgroundColor: palette[v.colour % palette.length], borderRadius: 2
                }))
            },
            options: {
                // Fill .chart-box's height rather than a fixed width:height ratio, which left it very short on phones.
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { display: spend.vehicles.length > 1 },
                    tooltip: {
                        // Only the vehicles that had fill-ups, then the total.
                        filter: item => item.parsed.y > 0,
                        callbacks: {
                            label: item => `${item.dataset.label}: ${pence.format(item.parsed.y)}`,
                            footer: items => spend.vehicles.length > 1 ? `Total: ${pence.format(items.reduce((sum, i) => sum + i.parsed.y, 0))}` : ''
                        }
                    }
                },
                scales: { x: { stacked: true, grid: { display: false } }, y: { ...yAxis, stacked: true } }
            }
        });
    });

    const trends = [...document.querySelectorAll('canvas[data-spend-trend]')].map(canvas => {
        // Oldest first, so this year is drawn on top.
        const years = JSON.parse(canvas.dataset.spendTrend).reverse();
        const style = age => age === 0 ? { colour: palette[0], width: 3 } : age === 1 ? { colour: palette[1], width: 2 } : { colour: olderColour, width: 1.5 };
        return new Chart(canvas, {
            type: 'line',
            data: {
                labels: months,
                datasets: years.map((y, i) => {
                    const { colour, width } = style(years.length - 1 - i);
                    return {
                        label: String(y.year), data: y.cumulative, borderColor: colour, backgroundColor: colour,
                        borderWidth: width, pointRadius: 0, pointHoverRadius: 4, tension: .2
                    };
                })
            },
            options: {
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { labels: { boxWidth: 24, boxHeight: 2 } },
                    tooltip: {
                        // Newest year first; not this year's months still to come.
                        itemSort: (a, b) => b.datasetIndex - a.datasetIndex,
                        filter: item => item.raw !== null,
                        callbacks: {
                            title: items => items.length === 0 ? '' : `To the end of ${months[items[0].dataIndex]}`,
                            label: item => `${item.dataset.label}: ${pence.format(item.parsed.y)}`
                        }
                    }
                },
                scales: { x: { grid: { display: false } }, y: yAxis }
            }
        });
    });

    document.addEventListener('themechange', () => {
        styleDefaults();
        // Scales copy the defaults when created, so update them directly too.
        const { text, grid } = window.themeColors();
        for (const chart of [...bars, ...trends]) {
            for (const scale of Object.values(chart.options.scales)) {
                scale.ticks.color = text;
                scale.grid.color = grid;
                scale.border.color = grid;
            }
            chart.options.plugins.legend.labels.color = text;
            chart.update('none');
        }
    });
})();

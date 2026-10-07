// Makes each table wrapped in a .data-table (the fuel log's _FuelTable, the service history's _ServiceTable) a
// DataTable: sorting, search and paging in the browser. The wrapper's data-noun names its rows ("fill-ups").
// Needs the DataTables scripts first (Pages/Shared/_DataTableScripts.cshtml).
document.querySelectorAll('.data-table').forEach(wrapper => {
    const table = wrapper.querySelector('table');
    const noun = wrapper.dataset.noun || 'rows';
    // The footer's invisible rows keep the columns' widths steady (see _FuelTable). DataTables would rework
    // them as column footers, so it's set up without them and they go back afterwards.
    const sizer = table.querySelector('tfoot.column-sizer');
    sizer?.remove();
    try {
        // Newest first, then by vehicle (A–Z) where there's a Vehicle column (the Fuel Log, not a vehicle's own page).
        const vehicleColumn = [...table.tHead.rows[0].cells].findIndex(th => th.dataset.column === 'vehicle');
        new DataTable(table, {
            order: vehicleColumn < 0 ? [[0, 'desc']] : [[0, 'desc'], [vehicleColumn, 'asc']],
            // Leave column widths to the browser and the footer's sizing rows: DataTables would fix them from
            // this table's own rows, so they'd differ between tables (e.g. a station's fuel type tabs).
            autoWidth: false,
            // DataTables right-aligns columns it sorts as dates; keep the date on the left as elsewhere.
            columnDefs: [{ targets: 0, className: 'text-start' }],
            pageLength: 10,   // keep in step with the dt-pending rule in site.css
            lengthMenu: [10, 25, 50, 100, { label: 'All', value: -1 }],
            language: {
                search: '', searchPlaceholder: `Search ${noun}`, lengthMenu: '_MENU_ per page',
                info: `_START_–_END_ of _TOTAL_ ${noun}`, infoEmpty: `No ${noun}`, infoFiltered: '(of _MAX_)',
                zeroRecords: `No ${noun} match`
            }
        });
    } catch (e) {
        console.error(`Table of ${noun}:`, e);
    } finally {
        if (sizer) table.append(sizer);
        // dt-pending showed only the first page of rows; show them all if DataTables failed to load.
        wrapper.classList.remove('dt-pending');
    }
});

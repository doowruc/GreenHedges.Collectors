// Makes each fuel log table (Pages/Shared/_FuelTable.cshtml) a DataTable: sorting, search and
// paging in the browser. Needs the DataTables scripts first (Pages/Shared/_FuelTableScripts.cshtml).
document.querySelectorAll('.fuel-table').forEach(wrapper => {
    const table = wrapper.querySelector('table');
    // The footer's invisible rows keep the columns' widths steady (see _FuelTable). DataTables would rework
    // them as column footers, so it's set up without them and they go back afterwards.
    const sizer = table.querySelector('tfoot.column-sizer');
    sizer?.remove();
    try {
        // Newest first, then by vehicle (A–Z) where there's a Vehicle column (not on a vehicle's own page).
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
                search: '', searchPlaceholder: 'Search fill-ups', lengthMenu: '_MENU_ per page',
                info: '_START_–_END_ of _TOTAL_ fill-ups', infoEmpty: 'No fill-ups', infoFiltered: '(of _MAX_)',
                zeroRecords: 'No fill-ups match'
            }
        });
    } catch (e) {
        console.error('Fuel log table:', e);
    } finally {
        if (sizer) table.append(sizer);
        // dt-pending showed only the first page of rows; show them all if DataTables failed to load.
        wrapper.classList.remove('dt-pending');
    }
});

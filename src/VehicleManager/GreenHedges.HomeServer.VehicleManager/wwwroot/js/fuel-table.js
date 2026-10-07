// Makes each fuel log table (Pages/Shared/_FuelTable.cshtml) a DataTable: sorting, search and
// paging in the browser. Needs the DataTables scripts first (Pages/Shared/_FuelTableScripts.cshtml).
document.querySelectorAll('.fuel-table').forEach(wrapper => {
    try {
        const table = wrapper.querySelector('table');
        // Newest first, then by vehicle (A–Z) where there's a Vehicle column (not on a vehicle's own page).
        const vehicleColumn = [...table.tHead.rows[0].cells].findIndex(th => th.dataset.column === 'vehicle');
        new DataTable(table, {
            order: vehicleColumn < 0 ? [[0, 'desc']] : [[0, 'desc'], [vehicleColumn, 'asc']],
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
        // dt-pending showed only the first page of rows; show them all if DataTables failed to load.
        wrapper.classList.remove('dt-pending');
    }
});

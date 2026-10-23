// Suggestions for text inputs shown as a Bootstrap dropdown menu under the input, instead of a
// <datalist> (which each browser draws its own way). Free text is still allowed.
//
//   typeahead(root, selector, options)
//
// Every input under root matching selector, including ones added later, gets the suggestions
// options(input) returns (an array of strings). Like a drop-down list, going into the box shows them
// all, with the current value highlighted; typing narrows them to those containing what's typed
// (ignoring case), ones starting with it first. Choosing one sets the value and fires input and change.
// Keyboard: Up/Down move, Enter chooses, Escape closes.
window.typeahead = (root, selector, options) => {
    'use strict';
    const norm = s => s.trim().replace(/\s+/g, ' ').toLowerCase();
    const menu = document.createElement('ul');
    menu.className = 'dropdown-menu typeahead-menu';
    menu.id = `typeahead-${Math.random().toString(36).slice(2)}`;
    menu.setAttribute('role', 'listbox');
    let input = null, active = -1;

    const items = () => [...menu.children];

    function close() {
        menu.classList.remove('show');
        input?.setAttribute('aria-expanded', 'false');
        input?.removeAttribute('aria-activedescendant');
        active = -1;
    }

    function setActive(i) {
        const all = items();
        all.forEach((li, n) => li.firstChild.classList.toggle('active', n === i));
        active = i;
        if (i >= 0) {
            input.setAttribute('aria-activedescendant', all[i].id);
            all[i].scrollIntoView({ block: 'nearest' });
        } else {
            input.removeAttribute('aria-activedescendant');
        }
    }

    function open(target, filter = false) {
        input = target;
        const typed = norm(input.value);
        const all = options(input) ?? [];
        const matches = filter ? [
            ...all.filter(o => norm(o).startsWith(typed)),
            ...all.filter(o => !norm(o).startsWith(typed) && norm(o).includes(typed))
        ] : all;
        // Nothing to suggest, or (while typing) the only suggestion is what's already there.
        if (matches.length === 0 || (filter && matches.length === 1 && norm(matches[0]) === typed)) return close();

        menu.replaceChildren(...matches.map((text, i) => {
            const li = document.createElement('li');
            li.id = `${menu.id}-${i}`;
            li.setAttribute('role', 'option');
            const a = document.createElement('button');
            a.type = 'button';
            a.tabIndex = -1;
            a.className = 'dropdown-item';
            a.textContent = text;
            li.append(a);
            return li;
        }));
        // Sit just under the input, as wide as it. The menu lives in <body>, so copying a form row
        // (e.g. to add another) doesn't copy it.
        const box = input.getBoundingClientRect();
        if (!menu.isConnected) document.body.append(menu);
        menu.style.top = `${box.bottom + window.scrollY}px`;
        menu.style.left = `${box.left + window.scrollX}px`;
        menu.style.width = `${box.width}px`;
        menu.classList.add('show');
        input.setAttribute('aria-controls', menu.id);
        input.setAttribute('aria-expanded', 'true');
        setActive(matches.findIndex(o => norm(o) === typed));
    }

    function choose(i) {
        input.value = items()[i].textContent;
        close();
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    const isTarget = el => el instanceof HTMLInputElement && el.matches(selector);

    function prepare(el) {
        el.setAttribute('role', 'combobox');
        el.setAttribute('aria-autocomplete', 'list');
        el.setAttribute('aria-expanded', 'false');
        el.autocomplete = 'off';
    }
    root.querySelectorAll(selector).forEach(prepare);

    root.addEventListener('focusin', e => { if (isTarget(e.target)) { prepare(e.target); open(e.target); } });
    root.addEventListener('click', e => { if (isTarget(e.target) && !menu.classList.contains('show')) open(e.target); });
    root.addEventListener('input', e => { if (isTarget(e.target) && e.isTrusted) open(e.target, true); });
    root.addEventListener('focusout', e => { if (e.target === input) close(); });
    root.addEventListener('keydown', e => {
        if (!isTarget(e.target)) return;
        const shown = menu.classList.contains('show');
        const count = items().length;
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            if (!shown) return open(e.target);
            setActive(e.key === 'ArrowDown' ? (active + 1) % count : (active <= 0 ? count : active) - 1);
        } else if (e.key === 'Enter' && shown && active >= 0) {
            e.preventDefault();   // choose rather than submit the form
            choose(active);
        } else if (e.key === 'Escape' && shown) {
            e.preventDefault();
            close();
        }
    });
    // mousedown rather than click, so the input keeps focus (and doesn't close the menu first).
    // Also covers dragging its scroll bar.
    menu.addEventListener('mousedown', e => {
        e.preventDefault();
        const li = e.target.closest('li');
        if (li) choose(items().indexOf(li));
    });
    window.addEventListener('resize', close);
};

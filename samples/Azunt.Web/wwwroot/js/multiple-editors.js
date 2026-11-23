// MVC/Razor Pages demo; only this page's form and editor IDs are used.
export function showModal(dialog) {
    if (dialog.dataset.blazorModal && !dialog.dataset.cancelBound) {
        dialog.addEventListener("cancel", event => event.preventDefault());
        dialog.dataset.cancelBound = "true";
    }
    if (!dialog.open) dialog.showModal();
}
export function closeModal(dialog) {
    if (dialog.open) dialog.close();
}
const form = document.getElementById("multiple-editor-form");
if (form) {
    const template = document.getElementById("multi-template");
    const dynamic = document.getElementById("multi-dynamic");
    const modal = document.getElementById("multi-modal");
    const modalContent = document.getElementById("multi-modal-content");
    const saved = document.getElementById("multi-saved");
    let next = 0;
    let modalValues = { ModalNotes: "<p>Modal notes</p>", ModalComments: "<p>Modal comments</p>" };
    function add(container, id, name, html, removable = false) {
        const section = document.createElement("section");
        const heading = document.createElement("h2"); heading.textContent = name;
        const root = template.content.firstElementChild.cloneNode(true);
        root.id = id;
        const value = root.querySelector("[data-azunt-value]");
        value.name = name; value.value = html;
        section.append(heading, root);
        if (removable) {
            const remove = document.createElement("button"); remove.type = "button"; remove.textContent = "Remove " + name;
            remove.addEventListener("click", () => { AzuntEditor.unmount(id); section.remove(); });
            section.append(remove);
        }
        container.append(section); AzuntEditor.mount(root);
    }
    function captureModal() {
        for (const root of modalContent.querySelectorAll("[data-azunt-editor]"))
            modalValues[root.querySelector("[data-azunt-value]").name] = AzuntEditor.getHtml(root.id);
    }
    function dismiss() {
        captureModal();
        for (const root of modalContent.querySelectorAll("[data-azunt-editor]")) AzuntEditor.unmount(root.id);
        closeModal(modal); modalContent.replaceChildren();
    }
    document.getElementById("multi-add").addEventListener("click", () => {
        const n = ++next; add(dynamic, "multi-extra-" + n, "Extra" + n, "<p>Extra editor</p>", true);
    });
    document.getElementById("multi-open").addEventListener("click", () => {
        if (modal.open) return;
        for (const [name, html] of Object.entries(modalValues)) add(modalContent, "multi-" + name, name, html);
        showModal(modal);
    });
    document.getElementById("multi-close").addEventListener("click", dismiss);
    modal.addEventListener("cancel", event => { event.preventDefault(); dismiss(); });
    form.addEventListener("submit", event => {
        event.preventDefault(); captureModal();
        for (const root of form.querySelectorAll("[data-azunt-editor]")) AzuntEditor.getHtml(root.id);
        saved.textContent = JSON.stringify({ ...modalValues, ...Object.fromEntries(new FormData(form)) }, null, 2);
    });
}

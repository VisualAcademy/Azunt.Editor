// Host-owned example. The editor library is not responsible for modal UI.
export function pickContent(dialog) {
    if (!dialog?.isConnected || dialog.open) return Promise.resolve(null);
    return new Promise((resolve, reject) => {
        let choice = null;
        // Finish within the default Blazor interop timeout, even if the user leaves the dialog open.
        let timer;
        const cleanup = () => {
            clearTimeout(timer);
            dialog.removeEventListener("click", onClick);
            dialog.removeEventListener("close", onClose);
        };
        const onClick = event => {
            const button = event.target.closest("button[data-pick]");
            if (!button || !dialog.contains(button)) return;
            choice = button.dataset.pick === "cancel" ? null : button.dataset.pick;
            dialog.close();
        };
        const onClose = () => { cleanup(); resolve(choice); };
        dialog.addEventListener("click", onClick);
        dialog.addEventListener("close", onClose);
        try {
            dialog.showModal();
            timer = setTimeout(() => { if (dialog.open) dialog.close(); else { cleanup(); resolve(null); } }, 45000);
        }
        catch (error) { cleanup(); reject(error); }
    });
}

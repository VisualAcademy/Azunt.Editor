/* Azunt.Editor.Export - streamed browser download for Blazor Server. */
(() => {
  const connected = new WeakMap();
  // Opt-in form bridge; use the real submitter so validation, antiforgery,
  // editor form synchronization and existing server handlers remain intact.
  function connectForms(root = document) {
    const api = window.AzuntEditor;
    if (!api || typeof api.registerCommand !== "function") return;
    for (const group of root.querySelectorAll("[data-azunt-export-editor]")) {
      if (connected.has(group)) continue;
      const id = group.dataset.azuntExportEditor;
      const editor = document.getElementById(id);
      const form = group.closest("form");
      if (!form || !editor || editor.closest("form") !== form) continue;
      const buttons = [...group.querySelectorAll("button[data-azunt-export-command]")];
      for (const button of buttons) {
        api.registerCommand({
          id: button.dataset.azuntExportCommand, label: button.textContent.trim(), group: "Export",
          execute: () => {
            if (button.isConnected && button.form === form && !button.disabled)
              form.requestSubmit(button);
          }
        }, id);
      }
      connected.set(group, { id, buttons });
    }
  }
  function disconnectForm(group) {
    const registration = connected.get(group);
    if (!registration) return;
    for (const button of registration.buttons)
      window.AzuntEditor?.unregisterCommand(button.dataset.azuntExportCommand, registration.id);
    connected.delete(group);
  }
  window.AzuntEditorExport = Object.freeze({
  connectForms, disconnectForm,
  async download(fileName, dotNetStream) {
    if (!dotNetStream || typeof dotNetStream.arrayBuffer !== "function")
      throw new Error("Missing .NET stream for download.");
    const data = await dotNetStream.arrayBuffer();
    const mime = /\.pdf$/i.test(fileName) ? "application/pdf"
      : /\.docx$/i.test(fileName) ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
      : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    const blob = new Blob([data], { type: mime });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = String(fileName || "Document");
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  }
});

  if (document.readyState === "loading")
    document.addEventListener("DOMContentLoaded", () => connectForms(), { once: true });
  else connectForms();
})();

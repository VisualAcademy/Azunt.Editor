/* Azunt.Editor.Export - streamed browser download for Blazor Server. */
window.AzuntEditorExport = Object.freeze({
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

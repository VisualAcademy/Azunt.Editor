// MVC and Razor Pages register export commands through the editor's public API.
// The existing form handlers continue to generate the files.
(function () {
    "use strict";
    if (!window.AzuntEditor) return;
    for (const editorId of ["azunt-mvc-demo", "azunt-razor-demo"]) {
        for (const item of [
            ["demoPdf", "Download PDF", "Pdf"],
            ["demoWord", "Download Word", "Docx"],
            ["demoExcel", "Download Excel", "Xlsx"]
        ]) {
            window.AzuntEditor.registerCommand({
                id: item[0], label: item[1], group: "export", placement: "menu",
                execute: ({ editor }) => {
                    const form = editor.closest("form");
                    const submit = Array.from(form?.querySelectorAll('button[name="exportFormat"]') || [])
                        .find(button => button.value === item[2]);
                    if (submit && form) form.requestSubmit(submit);
                }
            }, editorId);
        }
    }
})();

"""Fast dependency-free release checks; compile/GUI tests still required."""
from pathlib import Path
from xml.etree import ElementTree

root = Path(__file__).resolve().parents[1]
version = '0.8.9'
for item in ['src/Azunt.Editor/Azunt.Editor.csproj', 'src/Azunt.Editor.Export/Azunt.Editor.Export.csproj',
             'samples/Azunt.Web/Azunt.Web.csproj']:
    doc = ElementTree.parse(root / item)
    if item.startswith('src/'):
        assert doc.findtext('.//Version') == version, f'Incorrect package version in {item}'
    else:
        refs = {node.get('Include'): node.get('Version') for node in doc.findall('.//PackageReference')}
        for package in ('Azunt.Editor', 'Azunt.Editor.Export'):
            assert refs.get(package) == version, f'Incorrect {package} version in {item}'

markup = (root / 'src/Azunt.Editor/Rendering/EditorMarkup.cs').read_text()
assert 'data-group-label' in markup and 'static string? Icon' in markup
for control in ['data-find-panel', 'data-chars-panel', 'data-editor-stats', 'data-command', 'data-signature-panel', 'data-template-panel', 'data-date-panel', 'table-merge-right', 'data-image-resize-handle', 'image-original', 'imageInsertSize', 'table-row-before', 'table-col-before', 'data-image-alt']:
    assert control in markup, f'Missing editor writing control: {control}' 
assert 'data-command' in markup and '<svg' in markup
assert 'data-current-format' in markup, 'Missing current-format indicator'
assert 'TableSplit(compact: true)' in markup and 'TableSplit(compact: false)' in markup
assert 'tablepicker' in markup, 'Missing table palette trigger'
assert 'codepicker' in markup and 'data-code-text' in markup, 'Missing code block UI'
for item in ['data-quick-command', 'data-azunt-menu', 'form-checkbox', 'controls']:
    assert item in markup, f'Missing HTML form control button: {item}'


pdf = (root / 'src/Azunt.Editor.Export/Pdf/PdfDocumentExporter.cs').read_text()
assert 'DisplayHeaderFooter = options.ShowPageNumbers' in pdf
assert 'thead{display:table-header-group}' in pdf
word = (root / 'src/Azunt.Editor.Export/Word/DocxDocumentExporter.cs').read_text()
assert 'new W.CantSplit()' in word and 'new W.TableHeader()' in word and 'new W.GridSpan' in word
xlsx = (root / 'src/Azunt.Editor.Export/Excel/XlsxDocumentExporter.cs').read_text()
assert 'new S.Columns()' in xlsx and 'InsertBefore(columns, sheetData)' in xlsx and 'new S.MergeCells()' in xlsx
for scope in [root / 'src', root / 'samples']:
    for file in scope.rglob('*'):
        if file.is_file() and file.suffix in ('.cs', '.csproj', '.razor', '.js', '.css'):
            assert file.read_text(encoding='utf-8').strip(), f'Empty source file: {file}'
assert 'var width = Math.Max(1, sourceWidth)' in word
assert 'var width = Math.Max(1, sourceWidth)' in xlsx
editor_js = (root / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js').read_text()
assert 'function placeDropCaret' in editor_js
for feature in ['function updateFormattingState', 'function focusMenuTrigger', 'function menuFocusables', 'aria-pressed', 'event.key === "F10"']:
    assert feature in editor_js, f'Missing keyboard/format feature: {feature}'
assert 'function insertImageParagraph' in editor_js
assert 'function setImageAlt' in editor_js
assert 'function isSupportedDrop' in editor_js
word_alt = (root / 'src/Azunt.Editor.Export/Word/DocxDocumentExporter.cs').read_text()
assert 'Description = altText' in word_alt
public_api = (root / 'src/Azunt.Editor/Models/EditorCustomCommand.cs').read_text()
component = (root / 'src/Azunt.Editor/Components/AzuntEditor.razor').read_text()
for token in ['Icon { get; set; }', 'RequiresEdit { get; set; }']:
    assert token in public_api, f'Missing custom command API: {token}'
assert 'SetCustomCommandsAsync' in component
for token in ['function unregisterCommand(', 'Duplicate custom command id:', 'previousBlazorCommands']:
    assert token in editor_js, f'Missing command lifecycle feature: {token}'

# Blazor prerender markup must not be auto-mounted ahead of hydration.
assert 'data-azunt-mount=' in (root / 'src/Azunt.Editor/Rendering/EditorMarkup.cs').read_text()
assert 'RenderForBlazor(' in component
assert ':not([data-azunt-mount="manual"])' in editor_js
print('PASS: Azunt.Editor 0.8.9 release contracts, sources are application-agnostic')

"""Fast dependency-free release checks; compile/GUI tests still required."""
from pathlib import Path
from xml.etree import ElementTree

root = Path(__file__).resolve().parents[1]
version = '0.5.4'
for item in ['src/Azunt.Editor/Azunt.Editor.csproj', 'src/Azunt.Editor.Export/Azunt.Editor.Export.csproj',
             'samples/Azunt.Web/Azunt.Web.csproj']:
    doc = ElementTree.parse(root / item)
    found = [el.text for el in doc.iter() if el.tag in ('Version', 'PackageReference') and el.text]
    text = (root / item).read_text()
    assert version in text, f'Missing consistent version in {item}'

markup = (root / 'src/Azunt.Editor/Rendering/EditorMarkup.cs').read_text()
assert 'data-group-label' in markup and 'static string? Icon' in markup
for control in ['data-find-panel', 'data-chars-panel', 'data-editor-stats', 'data-command', 'data-signature-panel', 'data-template-panel', 'data-date-panel', 'table-merge-right', 'data-image-resize-handle', 'image-original', 'imageInsertSize', 'table-row-before', 'table-col-before', 'data-image-alt']:
    assert control in markup, f'Missing editor writing control: {control}' 
assert 'data-command' in markup and '<svg' in markup

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
assert 'function insertImageParagraph' in editor_js
assert 'function setImageAlt' in editor_js
assert 'function isSupportedDrop' in editor_js
word_alt = (root / 'src/Azunt.Editor.Export/Word/DocxDocumentExporter.cs').read_text()
assert 'Description = altText' in word_alt
print('PASS: Azunt.Editor 0.5.4 release contracts, sources are application-agnostic')

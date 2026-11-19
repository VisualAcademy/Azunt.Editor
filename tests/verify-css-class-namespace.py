"""Checks Azunt.Editor-owned CSS classes are prefixed with az- and match markup."""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
styles = [root / 'src/Azunt.Editor/wwwroot/css/azunt-editor.css',
          root / 'src/Azunt.Editor.Export/wwwroot/css/azunt-editor-export.css']
classes = set()
for css in styles:
    selectors = set(re.findall(r'\.([A-Za-z][\w-]*)', re.sub(r'/\*.*?\*/', '', css.read_text(encoding='utf-8'), flags=re.S)))
    assert all(x.startswith('az-') for x in selectors), (css, sorted(selectors - {x for x in selectors if x.startswith('az-')}))
    classes.update(selectors)

files = [root / 'src/Azunt.Editor/Rendering/EditorMarkup.cs',
         root / 'src/Azunt.Editor/wwwroot/js/azunt-editor.js',
         root / 'src/Azunt.Editor.Export/TagHelpers/AzuntExportButtonsTagHelper.cs',
         root / 'src/Azunt.Editor.Export/Components/AzuntExportButtons.razor']
for f in files:
    data = f.read_text(encoding='utf-8')
    assert not re.search(r'(?<![\w-])(?:azunt-editor-(?:toolbar|canvas|button|export-actions)|azunt-print-page|is-resizing)(?![\w-])', data), str(f)

markup = files[0].read_text(encoding='utf-8')
for name in ('az-azunt-editor', 'az-azunt-editor-canvas', 'az-azunt-editor-toolbar', 'az-azunt-editor-menu-panel'):
    assert name in classes and name in markup, name
assert 'data-azunt-editor' in markup
assert 'az-azunt-print-page' in files[1].read_text(encoding='utf-8')
assert 'az-is-resizing' in classes and 'az-is-resizing' in files[1].read_text(encoding='utf-8')
assert 'az-azunt-editor-export-actions' in classes
print('PASS: editor, export and print CSS class namespace')

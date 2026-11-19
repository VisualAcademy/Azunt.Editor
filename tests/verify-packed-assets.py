"""Check that locally packed RCL NuGet files ship editor static assets."""
from pathlib import Path
from zipfile import ZipFile

packages = Path('artifacts/packages')
for package_name, expected in {
    'Azunt.Editor.0.6.5.nupkg': ('azunt-editor.js', 'azunt-editor.css'),
    'Azunt.Editor.Export.0.6.5.nupkg': ('azunt-editor-export.js', 'azunt-editor-export.css'),
}.items():
    file = packages / package_name
    assert file.is_file(), f'Missing NuGet package: {file}'
    with ZipFile(file) as z:
        names = [name.lower() for name in z.namelist()]
        for item in expected:
            assert any(name.endswith('/' + item) for name in names), f'{item} not packed in {file}'
        assert any(name.endswith('/readme.md') or name == 'readme.md' for name in names), f'Readme not packed in {file}'
    print(f'PASS: {package_name} has bundled static assets and README')

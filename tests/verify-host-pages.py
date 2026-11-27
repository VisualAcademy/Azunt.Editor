"""Optional integration check: run Azunt.Web first, then python tests/verify-host-pages.py.
Override AZUNT_BASE_URL if the sample host runs on another port.
"""
import os
from urllib.request import urlopen

base = os.environ.get('AZUNT_BASE_URL', 'http://localhost:5217').rstrip('/')
for path in ['/editor/mvc', '/editor/razor-pages', '/editor/blazor', '/editor/export']:
    with urlopen(base + path, timeout=10) as response:
        assert response.status == 200, f'{path}: HTTP {response.status}'
        page = response.read().decode('utf-8')
    for marker in ['Azunt.Editor', 'data-azunt-editor', 'data-toolbar-mode="compact"', 'data-azunt-menu="format"']:
        assert marker in page, f'{path}: expected marker {marker} is missing'
    if path == '/editor/export':
        for marker in ['Export Demo', 'Multi-page Report', 'PDF profile', 'Download PDF', 'Download Word', 'Download Excel']:
            assert marker in page, f'{path}: expected export marker {marker} is missing'
    print('PASS:', path)

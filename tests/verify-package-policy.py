"""Validate direct NuGet references follow the Azunt.Editor Microsoft / first-party Azunt-only policy.
Only Microsoft-maintained framework packages and Microsoft's official Open XML
SDK are permitted. This does not attest to package provenance or audit all
transitive dependencies (use dotnet list package --include-transitive for that).
"""
from pathlib import Path
from xml.etree import ElementTree

root = Path(__file__).resolve().parents[1]
allowed_prefixes = ('Microsoft.', 'System.')
allowed_exact = {'DocumentFormat.OpenXml', 'DocumentFormat.OpenXml.Framework', 'Azunt.Editor', 'Azunt.Editor.Export'}
issues = []
count = 0
for project in root.rglob('*.csproj'):
    if 'obj' in project.parts or 'bin' in project.parts:
        continue
    tree = ElementTree.parse(project)
    for node in tree.findall('.//PackageReference'):
        name = node.get('Include', node.get('Update', ''))
        count += 1
        if not name.startswith(allowed_prefixes) and name not in allowed_exact:
            issues.append(f'{project.relative_to(root)}: {name}')
if issues:
    raise SystemExit('Disallowed direct NuGet references:\n' + '\n'.join(issues))
print(f'PASS: Microsoft / Azunt first-party NuGet policy ({count} reference(s), across {len(list(root.rglob("*.csproj")))} projects)')

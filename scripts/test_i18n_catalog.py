"""Check translated static captions without translating protocol or user data."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).parents[1] / 'SW2URDF'
def catalog(name):
    entries = ET.parse(root / 'UI' / name).getroot().findall('data')
    values = {e.get('name'): e.findtext('value') for e in entries}
    assert len(entries) == len({key.lower() for key in values}), 'duplicate resource keys (ResGen is case insensitive)'
    assert all(values.values()), 'empty translation'
    return values

english = catalog('Strings.resx')
chinese = catalog('Strings.zh-TW.resx')
assert english.keys() == chinese.keys(), 'resource catalogs differ'
for key in english:
    assert sorted(re.findall(r'\{\d+\}', english[key])) == sorted(re.findall(r'\{\d+\}', chinese[key])), key
# These symbols and interchange-format terms intentionally stay identical.
invariant = {'*', '1', 'x', 'y', 'z', 'ixx', 'ixy', 'ixz', 'iyy', 'iyz', 'izz',
             'OBJ + MTL', 'ROS 1', 'ROS 2', 'pos = multiplier * pos_other + offset'}
literal = r'"((?:[^"\\]|\\.)*)"'
missing = set()
for name in ('AssemblyExportForm', 'PartExportForm'):
    source = (root / 'UI' / (name + '.Designer.cs')).read_text(encoding='utf-8-sig')
    for match in re.finditer(r'\bText\s*=\s*(' + literal + r'(?:\s*\+\s*' + literal + r')*)', source):
        caption = ''.join(re.findall(literal, match[1])).replace("\\'", "'")
        if caption and caption not in invariant and caption.lower() not in chinese:
            missing.add(caption)
assert not missing, 'Missing static captions: ' + repr(sorted(missing))
for source in [root/'UI/AssemblyExportFormExtension.cs', root/'URDFExport/ExportPropertyManager.cs']:
    for key in re.findall(r'ui.Get\("([^"\n]+)"\)', source.read_text(encoding='utf-8-sig')):
        assert key.lower() in chinese, (source.name, key)
print('PASS: matching catalogs, static form captions and dynamic label resources')

"""Compare localized form exports with baseline form exports (excluding comments)."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from verify_obj import verify
from verify_obj_live import equivalent

def check(baseline, actual):
    result = {}
    for package in ('part_obj', 'assembly_obj'):
        before, after = baseline / package, actual / package
        left, right = list((before / 'urdf').glob('*.urdf')), list((after / 'urdf').glob('*.urdf'))
        assert len(left) == len(right) == 1, package
        a, b = ET.parse(left[0]).getroot(), ET.parse(right[0]).getroot()
        equivalent(a, b)
        assets = {}
        for link in b.findall('link'):
            for kind in ('visual', 'collision'):
                uri = link.find(kind + '/geometry/mesh').get('filename')
                assert uri.startswith('package://' + package + '/') and uri.endswith('.obj')
                mesh = after / uri.split('/', 3)[3]
                assert mesh.exists()
            for suffix in ('.obj', '.mtl'):
                one = before / mesh.relative_to(after).with_suffix(suffix)
                two = mesh.with_suffix(suffix)
                assert hashlib.sha256(one.read_bytes()).digest() == hashlib.sha256(two.read_bytes()).digest(), two
            assets[link.get('name')] = verify(mesh.with_suffix('.3dxml'), mesh, reload=True)
        result[package] = dict(urdf_semantically_identical=True, obj_mtl_byte_identical=True, assets=assets)
    return result

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('baseline', type=Path)
    parser.add_argument('actual', type=Path)
    parser.add_argument('report', type=Path)
    args = parser.parse_args()
    result = check(args.baseline, args.actual)
    args.report.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print('PASS: both localized form packages preserve complete URDF semantics and OBJ/MTL bytes')

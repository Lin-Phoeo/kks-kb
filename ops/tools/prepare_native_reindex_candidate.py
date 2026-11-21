"""Reindex explicitly reviewed Word views without bundling any source documents.

The caller supplies exact document keys and a canonical JSON hash as review pins.
"""
import argparse
import copy
from hashlib import sha256
import json
from pathlib import Path
import re

from app.indexer import chunk_doc


def canonical_digest(value):
    return sha256(json.dumps(value, ensure_ascii=False, sort_keys=True,
                             separators=(',', ':')).encode()).hexdigest()


def project(seed, rows, *, reviewed_keys):
    result = copy.deepcopy(seed)
    views = result['documents']
    expected = set(reviewed_keys)
    if not expected or len(expected) != len(reviewed_keys) or set(views) != expected:
        raise ValueError('Expected exactly the reviewed document keys')
    identities = {}
    for key in expected:
        col, separator, path = key.partition(':')
        if not separator or not col or not path or path in identities:
            raise ValueError('Ambiguous reviewed document identity')
        identities[path] = (col, key)
    seen = set()
    for row in rows:
        path = row['doc_path']
        if path not in identities:
            continue
        col, key = identities[path]
        # The reader retrieves by path alone. A collision in another collection is ambiguous.
        if row['col'] != col or path in seen:
            raise ValueError(f'Ambiguous inventory identity: {path}')
        seen.add(path)
        view = views[key]
        text = row['text']
        if view.get('kind') != 'word_semantic_flow' or not text or sha256(text.encode()).hexdigest() != view.get('indexed_body_sha256'):
            raise ValueError(f'Unexpected view type or inventory text digest: {path}')
        chunks = [body for _, body in chunk_doc(text)]
        if not chunks or '\n\n'.join(chunks) != text:
            raise ValueError(f'Chunk order or content differs from the inventory: {path}')
        view['indexed_body_sha256'] = sha256('\n'.join(chunks).encode()).hexdigest()
    if seen != set(identities):
        raise ValueError('Missing reviewed inventory document')
    return result


def unique_object(pairs):
    obj = dict(pairs)
    if len(obj) != len(pairs):
        raise ValueError('Duplicate JSON member')
    return obj


def prepare(seed_path, inventory_path, output, *, expected_seed_sha256, reviewed_keys):
    if output.exists() or output.is_symlink():
        raise FileExistsError(output)
    if not re.fullmatch(r'[0-9a-f]{64}', expected_seed_sha256):
        raise ValueError('A canonical SHA-256 review pin is required')
    seed = json.loads(seed_path.read_bytes(), object_pairs_hook=unique_object)
    if canonical_digest(seed) != expected_seed_sha256:
        raise ValueError('Input differs from the reviewed seed')
    inventory = json.loads(inventory_path.read_bytes(), object_pairs_hook=unique_object)
    result = project(seed, inventory['documents'], reviewed_keys=reviewed_keys)
    payload = (json.dumps(result, ensure_ascii=False, indent=2) + '\n').encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open('xb') as target:
        target.write(payload)
    return sha256(payload).hexdigest()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('seed', 'inventory', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--expected-seed-sha256', required=True,
                        help='SHA-256 of canonical JSON (sorted keys, compact separators, UTF-8)')
    parser.add_argument('--reviewed-key', action='append', required=True,
                        help='Exact collection:path; repeat for each reviewed document')
    args = parser.parse_args()
    print(prepare(args.seed, args.inventory, args.output,
                  expected_seed_sha256=args.expected_seed_sha256, reviewed_keys=args.reviewed_key))

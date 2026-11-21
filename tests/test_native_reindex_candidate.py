import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from ops.tools import prepare_native_reindex_candidate as builder


TIMELINE = 'demo:guides/short-guide.md'
ME = 'demo:guides/long-guide.md'
KEYS = (TIMELINE, ME)


def digest(text):
    return hashlib.sha256(text.encode('utf-8')).hexdigest()


def fixture():
    texts = {TIMELINE: 'short', ME: 'A' * 2100 + '\n\n' + 'B' * 2100}
    seed = {'schema_version': 1, 'documents': {
        key: {'kind': 'word_semantic_flow', 'html': '<p>reviewed</p>', 'indexed_body_sha256': digest(text)}
        for key, text in texts.items()}}
    rows = [{'col': 'demo', 'doc_path': key.split(':', 1)[1], 'text': text} for key, text in texts.items()]
    return seed, rows


class ReindexCandidateTests(unittest.TestCase):
    def test_rejoins_real_chunks_without_changing_any_other_field(self):
        seed, rows = fixture()
        expected = copy.deepcopy(seed)
        expected['documents'][ME]['indexed_body_sha256'] = 'f1059838e185981464273878abfa1e1dabb653ce78cfbb347909ec105f7fca87'
        rows.append({'col': 'web', 'doc_path': 'unrelated.md', 'text': 'outside the batch'})
        self.assertEqual(builder.project(seed, rows, reviewed_keys=KEYS), expected)
        self.assertEqual(seed, fixture()[0])

    def test_rejects_unreviewed_seed_missing_or_ambiguous_identity_and_changed_source(self):
        for case in ('extra-view', 'missing-view', 'wrong-kind', 'renamed', 'duplicate', 'ambiguous-route', 'drift', 'lossy-chunks'):
            with self.subTest(case=case):
                seed, rows = fixture()
                if case == 'extra-view':
                    seed['documents']['demo:other.md'] = dict(seed['documents'][ME])
                elif case == 'missing-view':
                    del seed['documents'][ME]
                elif case == 'wrong-kind':
                    seed['documents'][ME]['kind'] = 'legacy'
                elif case == 'renamed':
                    rows[0]['doc_path'] = 'wrong/' + rows[0]['doc_path']
                elif case == 'duplicate':
                    rows.append(dict(rows[0]))
                elif case == 'ambiguous-route':
                    rows.append(dict(rows[0], col='another-collection'))
                elif case == 'drift':
                    rows[1]['text'] += 'updated'
                else:
                    # Real chunk_doc inserts a separator when merging two short heading sections.
                    rows[1]['text'] = 'first\n## Second\nlast'
                    seed['documents'][ME]['indexed_body_sha256'] = digest(rows[1]['text'])
                with self.assertRaises(ValueError):
                    builder.project(seed, rows, reviewed_keys=KEYS)

    def test_file_boundary_rejects_changed_seed_duplicate_json_keys_and_preexisting_paths(self):
        reviewed, rows = fixture()
        pin = builder.canonical_digest(reviewed)
        def prepare(seed, inventory, output):
            return builder.prepare(seed, inventory, output,
                                   expected_seed_sha256=pin, reviewed_keys=KEYS)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            seed, inv, out = (root / n for n in ('views.json', 'inventory.json', 'candidate.json'))
            seed.write_text(json.dumps(reviewed), encoding='utf-8')
            inv.write_text(json.dumps({'documents': rows}), encoding='utf-8')
            original = root / 'original.docx'
            original.write_bytes(b'original')
            for target in (seed, inv, original):
                before = target.read_bytes()
                with self.assertRaises(FileExistsError):
                    prepare(seed, inv, target)
                self.assertEqual(target.read_bytes(), before)
            changed = copy.deepcopy(reviewed)
            changed['documents'][ME]['html'] = '<p>unreviewed change</p>'
            for payload, reason in ((json.dumps(changed), 'reviewed seed'), ('{"documents":{},"documents":{}}', 'Duplicate JSON')):
                seed.write_text(payload, encoding='utf-8')
                with self.assertRaisesRegex(ValueError, reason):
                    prepare(seed, inv, out)
                self.assertFalse(out.exists())
            seed.write_text(json.dumps(reviewed), encoding='utf-8')
            rows[0]['text'] = 'changed inventory'
            inv.write_text(json.dumps({'documents': rows}), encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'inventory text digest'):
                prepare(seed, inv, out)
            self.assertFalse(out.exists())

    def test_reviewed_synthetic_input_writes_only_the_requested_new_candidate(self):
        reviewed, rows = fixture()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            seed, inventory, output = (root / n for n in ('views.json', 'inventory.json', 'candidate.json'))
            seed.write_text(json.dumps(reviewed), encoding='utf-8')
            inventory.write_text(json.dumps({'documents': rows}), encoding='utf-8')
            before = seed.read_bytes(), inventory.read_bytes()
            result = builder.prepare(seed, inventory, output,
                                     expected_seed_sha256=builder.canonical_digest(reviewed), reviewed_keys=KEYS)
            self.assertEqual(result, hashlib.sha256(output.read_bytes()).hexdigest())
            self.assertEqual(json.loads(output.read_bytes()), builder.project(reviewed, rows, reviewed_keys=KEYS))
            self.assertEqual(before, (seed.read_bytes(), inventory.read_bytes()))

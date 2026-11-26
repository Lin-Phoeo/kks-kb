import copy
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

from generate import generate, registration_manifest


def fixture():
    root = ET.Element('manifest')
    ET.SubElement(root, 'guid').text = 'HarvexARC.ShaderPack'
    declarations = ET.SubElement(root, 'KK_MaterialEditor')
    entries = []
    for i in range(4):
        shader = ET.SubElement(declarations, 'Shader', Name=f'Synthetic-{i}', AssetBundle=f'shaders/{i}.unity3d', Asset=f'shader-{i}')
        entries.append('abdata/' + shader.attrib['AssetBundle'])
        for j in range(5):
            ET.SubElement(shader, 'Property', Name=f'Property{j}', Type='Float', DefaultValue=str(j))
    return root, entries


class RegistrationTests(unittest.TestCase):
    def test_preserves_every_shader_property_and_order(self):
        root, entries = fixture()
        original = ET.tostring(root)
        result = ET.fromstring(registration_manifest(original, entries))
        expected = copy.deepcopy(root.find('KK_MaterialEditor'))
        expected.tag = 'MaterialEditor'
        self.assertEqual(ET.tostring(result.find('MaterialEditor')), ET.tostring(expected))
        self.assertEqual(ET.tostring(root), original)
        self.assertEqual(result.findtext('guid'), 'codex.compat.harvex-shaderpack-kks')

    def test_missing_asset_is_rejected(self):
        root, entries = fixture()
        with self.assertRaisesRegex(ValueError, 'absent'):
            registration_manifest(ET.tostring(root), entries[:-1])

    def test_existing_current_registration_is_rejected(self):
        root, entries = fixture()
        ET.SubElement(root, 'MaterialEditor')
        with self.assertRaisesRegex(ValueError, 'does not need'):
            registration_manifest(ET.tostring(root), entries)

    def test_unreviewed_input_cannot_create_output(self):
        with tempfile.TemporaryDirectory() as directory:
            source, output = Path(directory) / 'original', Path(directory) / 'candidate'
            source.write_bytes(b'synthetic-unreviewed-pack')
            with self.assertRaisesRegex(ValueError, 'SHA-256'):
                generate(source, output)
            self.assertFalse(output.exists())


if __name__ == '__main__':
    unittest.main()

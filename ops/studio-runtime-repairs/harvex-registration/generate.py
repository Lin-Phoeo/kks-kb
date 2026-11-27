"""Generate registration metadata for an already installed Harvex shader pack."""
import argparse
import copy
import hashlib
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile


SOURCE_SHA256 = '32a6264fb2e9e8a40ce61dd18a37921f4f5fe87fa3a692c04904c5dbb1021c30'


def registration_manifest(manifest_bytes, entries):
    original = ET.fromstring(manifest_bytes)
    if original.findtext('guid') != 'HarvexARC.ShaderPack':
        raise ValueError('This is not the supported Harvex shader pack.')
    old = original.find('KK_MaterialEditor')
    if old is None or original.find('MaterialEditor') is not None:
        raise ValueError('The original does not need this registration migration.')
    shaders = old.findall('Shader')
    if len(shaders) != 4 or len(old.findall('.//Property')) != 20:
        raise ValueError('Shader declarations differ from the reviewed pack.')
    for shader in shaders:
        if 'abdata/' + shader.attrib['AssetBundle'] not in entries:
            raise ValueError('A declared shader asset is absent from the input archive.')
    manifest = ET.Element('manifest', {'schema-ver': '1'})
    metadata = {
        'guid': 'codex.compat.harvex-shaderpack-kks',
        'name': 'Harvex ShaderPack KKS registration compatibility',
        'version': '1.0.0',
        'author': 'Local compatibility record; shader assets by HarvexARC',
        'description': 'Registers the installed HarvexARC.ShaderPack 1.0.1 metadata in KKS MaterialEditor. Contains no shader assets. Requires the original package.',
        'game': 'Koikatsu Sunshine',
    }
    for key, value in metadata.items():
        ET.SubElement(manifest, key).text = value
    registration = copy.deepcopy(old)
    registration.tag = 'MaterialEditor'
    manifest.append(registration)
    return ET.tostring(manifest, encoding='utf-8', xml_declaration=True)


def generate(source, output):
    if hashlib.sha256(source.read_bytes()).hexdigest() != SOURCE_SHA256:
        raise ValueError('Input SHA-256 does not match the reviewed Harvex 1.0.1 archive.')
    with zipfile.ZipFile(source) as archive:
        xml = registration_manifest(archive.read('manifest.xml'), archive.namelist())
    entry = zipfile.ZipInfo('manifest.xml', date_time=(1980, 1, 1, 0, 0, 0))
    entry.compress_type = zipfile.ZIP_DEFLATED
    with output.open('xb') as stream, zipfile.ZipFile(stream, 'w') as archive:
        archive.writestr(entry, xml)
    return hashlib.sha256(output.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path, help='Reviewed original zipmod (read only)')
    parser.add_argument('output', type=Path, help='New output zipmod, outside the source repository')
    args = parser.parse_args()
    try:
        print('Generated SHA-256:', generate(args.source, args.output))
    except (ValueError, OSError, zipfile.BadZipFile) as exc:
        parser.exit(1, f'{exc}\n')


if __name__ == '__main__':
    main()

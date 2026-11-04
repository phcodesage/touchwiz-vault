"""Prepare an existing revival APK for Android's normal package installer.
Updates the target SDK, version code, and legacy Unity relocation compatibility.
"""
import argparse
from pathlib import Path
import struct
import subprocess
import tempfile
import zipfile
from legacy_unity import patch_unity


def patch_manifest(data, target_sdk=24, version_code=1382):
    result = bytearray(data)
    if struct.unpack_from('<H', result)[0] != 0x0003:
        raise ValueError('Expected binary AndroidManifest.xml')
    resources = []
    found = set()
    offset = struct.unpack_from('<H', result, 2)[0]
    while offset < len(result):
        kind, header, size = struct.unpack_from('<HHI', result, offset)
        if size < header or size == 0 or offset + size > len(result):
            raise ValueError('Invalid XML chunk')
        if kind == 0x0180:
            resources = list(struct.unpack_from('<' + 'I' * ((size - header)//4), result, offset + header))
        elif kind == 0x0102:
            node = offset + header
            attr_start, attr_size, attr_count = struct.unpack_from('<HHH', result, node + 8)
            for i in range(attr_count):
                attr = node + attr_start + i * attr_size
                name = struct.unpack_from('<I', result, attr + 4)[0]
                resource = resources[name] if name < len(resources) else None
                value = {0x01010270: target_sdk, 0x0101021b: version_code}.get(resource)
                if value is not None:
                    struct.pack_into('<I', result, attr + 8, 0xffffffff)  # no raw string
                    result[attr + 15] = 0x10  # TYPE_INT_DEC
                    struct.pack_into('<I', result, attr + 16, value)
                    found.add(resource)
        offset += size
    if found != {0x01010270, 0x0101021b}:
        raise ValueError('Manifest must contain targetSdkVersion and versionCode')
    return bytes(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--target-sdk', type=int, default=24)
    parser.add_argument('--version-code', type=int, default=1382)
    parser.add_argument('--build-tools', type=Path, default=Path.home()/'Library/Android/sdk/build-tools/36.0.0')
    parser.add_argument('--toolchain', type=Path, default=Path.home()/'Library/Android/sdk/ndk/28.2.13676358/toolchains/llvm/prebuilt/darwin-x86_64/bin')
    args = parser.parse_args()
    if args.target_sdk not in (24, 25):
        parser.error('Legacy Unity relocation compatibility supports target 24 or 25')
    with tempfile.TemporaryDirectory(prefix='punch-installable-') as folder:
        folder = Path(folder)
        unsigned, aligned = folder/'unsigned.apk', folder/'aligned.apk'
        with zipfile.ZipFile(args.input) as original, zipfile.ZipFile(unsigned, 'w') as output:
            for entry in original.infolist():
                if entry.filename.startswith('META-INF/'):
                    continue
                data = original.read(entry.filename)
                if entry.filename == 'AndroidManifest.xml':
                    data = patch_manifest(data, args.target_sdk, args.version_code)
                if entry.filename.endswith('/libunity.so'):
                    data = patch_unity(data, args.toolchain)
                output.writestr(entry, data)
        subprocess.run([str(args.build_tools/'zipalign'), '-f', '4', str(unsigned), str(aligned)], check=True)
        subprocess.run([str(args.build_tools/'apksigner'), 'sign', '--ks', str(Path.home()/'.android/debug.keystore'),
                        '--ks-pass', 'pass:android', '--key-pass', 'pass:android', '--out', str(args.output), str(aligned)], check=True)
        subprocess.run([str(args.build_tools/'apksigner'), 'verify', '--verbose', str(args.output)], check=True)
    print(args.output)


if __name__ == '__main__':
    main()

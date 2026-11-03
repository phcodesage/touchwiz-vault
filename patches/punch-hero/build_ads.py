"""Rebuild ads using the existing extracted/revived game workspace and SDK.
Example: python3 build_ads.py --workspace /path/to/ph_inspect --input game.apk --output game-ads-fixed.apk
Uses the Android debug key by default, matching the existing revival APK.
"""
import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('--workspace', type=Path, required=True)
parser.add_argument('--input', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--android-sdk', type=Path, default=Path.home()/'Library/Android/sdk')
parser.add_argument('--keystore', type=Path, default=Path.home()/'.android/debug.keystore')
args = parser.parse_args()
source = Path(__file__).resolve().parent
workspace = args.workspace.resolve()
managed = workspace/'assets/bin/Data/Managed'
sdk = args.android_sdk
build_tools = sdk/'build-tools/36.0.0'
android_jar = sdk/'platforms/android-36/android.jar'

def run(*command):
    subprocess.run([str(c) for c in command], check=True)

sdk_line = subprocess.check_output(['dotnet', '--list-sdks'], text=True).strip().splitlines()[-1]
version, location = sdk_line.split(' ', 1)
csc = Path(location.strip('[]'))/version/'Roslyn/bincore/csc.dll'
run('dotnet', csc, '/nologo', '/target:library', '/nostdlib+', '/optimize+',
    '/out:'+str(managed/'RevivalAdGate.dll'), '/reference:'+str(managed/'mscorlib.dll'),
    '/reference:'+str(managed/'UnityEngine.dll'), source/'RevivalAdGate.cs')
shutil.copy2(source/'Program.cs', workspace/'iltool/Program.cs')
with (workspace/'ad-patch-build.log').open('w') as log:
    subprocess.run(['dotnet', 'run', '--project', str(workspace/'iltool'), '--', str(managed)], stdout=log, stderr=subprocess.STDOUT, check=True)
with tempfile.TemporaryDirectory(prefix='punch-hero-ads-') as temp:
    temp = Path(temp)
    classes = temp/'classes'
    classes.mkdir()
    run('javac', '-source', '8', '-target', '8', '-cp', str(android_jar)+':'+str(workspace/'unity_sdk/classes.jar'),
        '-d', classes, source/'UnityLevelPlayBridge.java', workspace/'java_bridge/com/unity3d/player/UnityPlayer.java')
    bridge_jar = temp/'bridge.jar'
    with zipfile.ZipFile(bridge_jar, 'w') as jar:
        for entry in classes.rglob('*.class'):
            if 'com/touchwizvault/ads/' in str(entry):
                jar.write(entry, entry.relative_to(classes).as_posix())
    dex = temp/'dex'
    dex.mkdir()
    run(build_tools/'d8', '--release', '--min-api', '19', '--lib', android_jar, '--output', dex,
        workspace/'java_bridge/orig_classes.dex', workspace/'unity_sdk/classes.jar',
        *sorted((workspace/'unity_sdk/libs').glob('*.jar')), bridge_jar)
    replacement = {
        'classes.dex': dex/'classes.dex',
        'assets/bin/Data/Managed/Assembly-CSharp.dll': managed/'Assembly-CSharp.dll',
        'assets/bin/Data/Managed/RevivalAdGate.dll': managed/'RevivalAdGate.dll',
    }
    unsigned = temp/'unsigned.apk'
    with zipfile.ZipFile(args.input) as original, zipfile.ZipFile(unsigned, 'w') as output:
        for entry in original.infolist():
            if entry.filename.startswith('META-INF/') or entry.filename in replacement:
                continue
            output.writestr(entry, original.read(entry.filename))
        for name, path in replacement.items():
            output.writestr(name, path.read_bytes(), compress_type=zipfile.ZIP_DEFLATED)
    aligned = temp/'aligned.apk'
    run(build_tools/'zipalign', '-f', '4', unsigned, aligned)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    run(build_tools/'apksigner', 'sign', '--ks', args.keystore, '--ks-pass', 'pass:android',
        '--key-pass', 'pass:android', '--out', args.output, aligned)
    run(build_tools/'apksigner', 'verify', '--verbose', args.output)
print('Built', args.output)

"""Verify the offline APK's changes, identity, permissions, and DEX checksums."""
import argparse
import hashlib
from pathlib import Path
import struct
import subprocess
import zipfile
import zlib

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('original',type=Path)
parser.add_argument('offline',type=Path)
args=parser.parse_args()
with zipfile.ZipFile(args.original) as original,zipfile.ZipFile(args.offline) as patched:
    assert patched.testzip() is None
    changed={name for name in original.namelist() if not name.startswith('META-INF/') and original.read(name)!=patched.read(name)}
    assert changed=={'AndroidManifest.xml','classes.dex'},changed
    dex=patched.read('classes.dex')
    assert hashlib.sha1(dex[32:]).digest()==dex[12:32]
    assert zlib.adler32(dex[12:])&0xffffffff==struct.unpack_from('<I',dex,8)[0]
    assert len(dex)==len(original.read('classes.dex'))
print('PASS: original game assets/native libraries preserved; only manifest and DEX changed; valid DEX checksums')
tools=Path.home()/'Library/Android/sdk/build-tools/36.0.0'
info=subprocess.check_output([str(tools/'aapt'),'dump','badging',str(args.offline)],text=True)
assert "package: name='com.gamevil.plantswar.offline'" in info
assert "targetSdkVersion:'24'" in info
assert "application-label:'Plants War Offline'" in info
assert "name='com.gamevil.plantswar.glo.DRMLicensing'" in info
for permission in ['READ_PHONE_STATE','READ_CONTACTS','READ_CALL_LOG','GET_ACCOUNTS','WRITE_EXTERNAL_STORAGE','READ_EXTERNAL_STORAGE']:
    assert 'android.permission.'+permission not in info,permission
print('PASS: separate app identity, qualified launcher, target API 24, no personal-data or shared-storage permissions')
subprocess.run([str(tools/'apksigner'),'verify',str(args.offline)],check=True)
print('PASS: APK signature verified')

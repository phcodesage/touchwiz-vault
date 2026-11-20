"""Build a separately installed Plants War Offline APK from the archived 1.5.0 APK."""
import argparse
import hashlib
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import zipfile
import zlib
from manifest import patch_manifest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'punch-hero'))
from legacy_profile import patch_profile


def patch_gate(data):
    image=bytearray(data)
    def u32(at):return struct.unpack_from('<I',image,at)[0]
    def uleb(at):
        value=shift=0
        while True:
            b=image[at];at+=1;value|=(b&127)<<shift
            if not b&128:return value,at
            shift+=7
    strings=[]
    for i in range(u32(56)):
        at=u32(u32(60)+4*i);_,at=uleb(at)
        strings.append(bytes(image[at:image.index(0,at)]).decode('utf8','replace'))
    types=[strings[u32(u32(68)+4*i)] for i in range(u32(64))]
    class_name='Lcom/gamevil/lib/GvActivity;'
    fields=[struct.unpack_from('<HHI',image,u32(84)+8*i) for i in range(u32(80))]
    pend=next(i for i,(cls,type_,name) in enumerate(fields) if types[cls]==class_name and strings[name]=='pend' and types[type_]=='Z')
    methods=[struct.unpack_from('<HHI',image,u32(92)+8*i) for i in range(u32(88))]
    def signature(proto):
        at=u32(76)+12*proto
        ret=types[u32(at+4)]
        params=u32(at+8)
        args=[] if not params else [types[struct.unpack_from('<H',image,params+4+2*i)[0]] for i in range(u32(params))]
        return '('+''.join(args)+')'+ret
    def method_id(owner,name,sig):
        return next(i for i,(cls,proto,n) in enumerate(methods) if types[cls]==owner and strings[n]==name and signature(proto)==sig)
    def invoke(op,method,registers):
        return [op | (len(registers)<<12),method,sum(r<<(i*4) for i,r in enumerate(registers))]
    launch_code=[0x0022,types.index('Landroid/content/Intent;'),0x011c,types.index('Lcom/gamevil/plantswar/glo/GameActivity;')]
    launch_code+=invoke(0x70,method_id('Landroid/content/Intent;','<init>','(Landroid/content/Context;Ljava/lang/Class;)V'),[0,3,1])
    launch_code += [0x011a,strings.index('profileBundle')]
    launch_code += invoke(0x71,method_id('Lcom/gamevil/lib/profile/GvProfileData;','getProfileBundle','()Landroid/os/Bundle;'),[])
    launch_code += [0x020c]
    launch_code += invoke(0x6e,method_id('Landroid/content/Intent;','putExtra','(Ljava/lang/String;Landroid/os/Bundle;)Landroid/content/Intent;'),[0,1,2])
    launch_code += invoke(0x6e,method_id('Lcom/gamevil/lib/GvDrmActivity;','startActivity','(Landroid/content/Intent;)V'),[3,0])
    launch_code += invoke(0x6e,method_id('Lcom/gamevil/lib/GvDrmActivity;','finish','()V'),[3])
    launch_code += [0x000e]
    billing_owner='Lcom/gamevil/plantswar/glo/util/IabHelper;'
    billing_result='Lcom/gamevil/plantswar/glo/util/IabResult;'
    billing_listener='Lcom/gamevil/plantswar/glo/util/IabHelper$OnIabSetupFinishedListener;'
    billing_code=[0x0022,types.index(billing_result),0x3112,0x021a,strings.index('Error checking for billing v3 support.')]
    billing_code+=invoke(0x70,method_id(billing_result,'<init>','(ILjava/lang/String;)V'),[0,1,2])
    billing_code+=[0x0538,5]  # null setup listener: return
    billing_code+=invoke(0x72,method_id(billing_listener,'onIabSetupFinished','('+billing_result+')V'),[5,0])
    billing_code+=[0x000e]
    purchase_owner='Lcom/ideaBox/Library/InAppPurchaseManager;'
    fail_purchase=invoke(0x6e,method_id(purchase_owner,'onPurchaseFailure','()V'),[0])+[0x000e]
    patched=set()
    for i in range(u32(96)):
        at=u32(100)+32*i
        owner=types[u32(at)]
        if owner not in (class_name,'Lcom/gamevil/lib/GvDrmActivity;',billing_owner,purchase_owner):continue
        cursor=u32(at+24);counts=[]
        for _ in range(4):
            n,cursor=uleb(cursor);counts.append(n)
        for _ in range(counts[0]+counts[1]):
            _,cursor=uleb(cursor);_,cursor=uleb(cursor)
        for count in counts[2:]:
            method=0
            for _ in range(count):
                delta,cursor=uleb(cursor);method+=delta
                _,cursor=uleb(cursor);code,cursor=uleb(cursor)
                cls,proto,name=methods[method];name=strings[name]
                if not (owner==class_name and name in ('doTorchwood','doJellyBeen','registerC2DM','unregisterC2DM') or owner=='Lcom/gamevil/lib/GvDrmActivity;' and name=='startLauncherActivity' or owner==billing_owner and name=='startSetup' or owner==purchase_owner and name=='BuyProduct'):continue
                if types[u32(u32(76)+12*proto+4)]!='V':raise ValueError('Unexpected gate signature')
                registers,incoming,outgoing,tries,debug,size=struct.unpack_from('<HHHHII',image,code)
                words=billing_code if name=='startSetup' else fail_purchase if name=='BuyProduct' else launch_code if name=='startLauncherActivity' else [0x000e] if name in ('registerC2DM','unregisterC2DM') else [0x0012,0x006a,pend,0x000e]
                if registers<1 or size<len(words):raise ValueError('Unexpected patched method code size')
                if name=='startSetup':
                    struct.pack_into('<HHHHII',image,code,6,2,3,0,0,size)
                elif name=='BuyProduct':
                    struct.pack_into('<HHHHII',image,code,3,3,1,0,0,size)
                elif name=='startLauncherActivity':
                    struct.pack_into('<HHHHII',image,code,4,1,3,0,0,size)
                else:
                    struct.pack_into('<HHII',image,code+4,0,0,0,size)
                image[code+16:code+16+size*2]=b'\0'*(size*2)
                # GvActivity.pend=false; return. No server request or dialog.
                struct.pack_into('<'+'H'*len(words),image,code+16,*words)
                patched.add(name)
    if patched!={'doTorchwood','doJellyBeen','registerC2DM','unregisterC2DM','startLauncherActivity','startSetup','BuyProduct'}:raise ValueError('Missing startup check entry point')
    image[12:32]=hashlib.sha1(image[32:]).digest()
    struct.pack_into('<I',image,8,zlib.adler32(image[12:])&0xffffffff)
    print('Disabled obsolete startup check:',', '.join(sorted(patched)))
    return bytes(image)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    source_sha=hashlib.sha256(args.input.read_bytes()).hexdigest()
    if source_sha!='15e9684e2389890fcf99ea3673be9342f85b98a025255d47fdd2b975fbc5d318':
        raise ValueError('Use the verified original Plants War 1.5.0 APK')
    tools=Path.home()/'Library/Android/sdk/build-tools/36.0.0'
    with tempfile.TemporaryDirectory(prefix='plants-war-offline-') as folder:
        folder=Path(folder)
        unsigned,aligned=folder/'unsigned.apk',folder/'aligned.apk'
        with zipfile.ZipFile(args.input) as source,zipfile.ZipFile(unsigned,'w') as dest:
            for entry in source.infolist():
                if entry.filename.startswith('META-INF/'):continue
                data=source.read(entry.filename)
                if entry.filename=='AndroidManifest.xml':data=patch_manifest(data)
                elif entry.filename=='classes.dex':data=patch_gate(patch_profile(data))
                dest.writestr(entry,data)
        subprocess.run([str(tools/'zipalign'),'-f','4',str(unsigned),str(aligned)],check=True)
        subprocess.run([str(tools/'apksigner'),'sign','--ks',str(Path.home()/'.android/debug.keystore'),'--ks-pass','pass:android','--key-pass','pass:android','--out',str(args.output),str(aligned)],check=True)
        subprocess.run([str(tools/'apksigner'),'verify','--verbose',str(args.output)],check=True)
    print('Offline APK:',args.output)

if __name__=='__main__':main()

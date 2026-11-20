"""Small binary-XML editor preserving all existing string indices."""
import struct

ANDROID = 'http://schemas.android.com/apk/res/android'
OLD_PACKAGE = 'com.gamevil.plantswar.glo'
NEW_PACKAGE = 'com.gamevil.plantswar.offline'


def chunks(data, offset=8):
    while offset < len(data):
        kind, header, size = struct.unpack_from('<HHI', data, offset)
        if size < header or offset+size > len(data):
            raise ValueError('Invalid XML chunk')
        yield kind, header, data[offset:offset+size]
        offset += size


def read_length(data, offset, utf8):
    if utf8:
        n = data[offset]
        return (((n & 127) << 8) | data[offset+1], offset+2) if n & 128 else (n, offset+1)
    n = struct.unpack_from('<H', data, offset)[0]
    return (((n & 32767) << 16) | struct.unpack_from('<H', data, offset+2)[0], offset+4) if n & 32768 else (n, offset+2)


def write_length(n, utf8):
    if utf8:
        return bytes([128 | (n >> 8), n & 255]) if n > 127 else bytes([n])
    return struct.pack('<HH', 32768 | (n >> 16), n & 65535) if n > 32767 else struct.pack('<H', n)


def patch_manifest(data):
    parts = list(chunks(data))
    pool = next(c for kind, header, c in parts if kind == 1)
    count, styles, flags, strings_start, styles_start = struct.unpack_from('<5I', pool, 8)
    if styles:
        raise ValueError('Styled manifest strings are unsupported')
    utf8 = bool(flags & 256)
    strings = []
    pool_header = struct.unpack_from('<H', pool, 2)[0]
    for i in range(count):
        at = strings_start+struct.unpack_from('<I', pool, pool_header+4*i)[0]
        length, at = read_length(pool, at, utf8)
        if utf8:
            byte_length, at = read_length(pool, at, True)
            strings.append(pool[at:at+byte_length].decode('utf8'))
        else:
            strings.append(pool[at:at+length*2].decode('utf-16le'))
    def index(value):
        if value not in strings:
            strings.append(value)
        return strings.index(value)
    resources = []
    for kind, header, c in parts:
        if kind == 0x0180:
            resources = list(struct.unpack_from('<'+'I'*((len(c)-header)//4), c, header))
    target_index = index('targetSdkVersion')
    resources += [0]*max(0, target_index+1-len(resources))
    resources[target_index] = 0x01010270
    android = index(ANDROID)
    label = index('Plants War Offline')
    remove_permissions = {
        'android.permission.READ_PHONE_STATE', 'android.permission.READ_CONTACTS',
        'android.permission.READ_CALL_LOG', 'android.permission.GET_ACCOUNTS',
        'android.permission.READ_OWNER_DATA', 'android.permission.WRITE_OWNER_DATA',
        'android.permission.WRITE_EXTERNAL_STORAGE', 'android.permission.READ_EXTERNAL_STORAGE',
        'android.permission.GET_TASKS',
        'com.google.android.c2dm.permission.RECEIVE',
    }
    result = []
    skipping = False
    target_found = False
    for kind, header, chunk in parts:
        if kind in (1, 0x0180):
            result.append((kind, chunk))
            continue
        c = bytearray(chunk)
        if kind == 0x0102:
            tag = strings[struct.unpack_from('<I', c, header+4)[0]]
            attr_start, attr_size, attr_count = struct.unpack_from('<HHH', c, header+8)
            attrs = [bytearray(c[header+attr_start+i*attr_size:header+attr_start+(i+1)*attr_size]) for i in range(attr_count)]
            def get_string(attr):
                return strings[struct.unpack_from('<I',attr,16)[0]] if attr[15] == 3 else None
            names = {strings[struct.unpack_from('<I', a,4)[0]]:a for a in attrs}
            if tag == 'uses-permission' and get_string(names['name']) in remove_permissions:
                skipping = True
                continue
            def set_string(attr, value):
                n=index(value)
                struct.pack_into('<I',attr,8,n)
                attr[15]=3
                struct.pack_into('<I',attr,16,n)
            for a in attrs:
                name = strings[struct.unpack_from('<I', a,4)[0]]
                value = get_string(a)
                if tag == 'manifest' and name == 'package':
                    if value != OLD_PACKAGE:
                        raise ValueError('Unexpected source package')
                    set_string(a,NEW_PACKAGE)
                elif tag == 'manifest' and name == 'versionCode':
                    struct.pack_into('<I',a,16,153)
                elif name in ('permission','authorities') and value and value.startswith(OLD_PACKAGE):
                    set_string(a,value.replace(OLD_PACKAGE,NEW_PACKAGE,1))
                elif tag in ('permission','uses-permission') and name == 'name' and value and value.startswith(OLD_PACKAGE):
                    set_string(a,value.replace(OLD_PACKAGE,NEW_PACKAGE,1))
                elif tag in ('activity','activity-alias','service','receiver','provider','application') and name in ('name','targetActivity') and value:
                    if value.startswith('.'):
                        set_string(a,OLD_PACKAGE+value)
                    elif '.' not in value:
                        set_string(a,OLD_PACKAGE+'.'+value)
                if tag in ('application','activity') and name == 'label':
                    set_string(a,'Plants War Offline')
            if tag == 'uses-sdk':
                a=names.get('targetSdkVersion')
                if a is None:
                    a=bytearray(struct.pack('<IIIHBBI',android,target_index,0xffffffff,8,0,0x10,24))
                    attrs.append(a)
                else:
                    a[15]=0x10
                    struct.pack_into('<II',a,8,0xffffffff,0) # reset raw value/typed header below
                    struct.pack_into('<HBBI',a,12,8,0,0x10,24)
                target_found=True
            c=c[:header+attr_start]+b''.join(attrs)
            struct.pack_into('<H',c,header+12,len(attrs))
            struct.pack_into('<I',c,4,len(c))
        elif kind == 0x0103 and skipping:
            skipping=False
            continue
        result.append((kind,bytes(c)))
    if not target_found:
        raise ValueError('Source uses-sdk element is missing')
    # Serialize original and appended strings, retaining their numeric indices.
    encoded=bytearray(); offsets=[]
    for value in strings:
        offsets.append(len(encoded))
        length=len(value.encode('utf-16le'))//2
        if utf8:
            raw=value.encode('utf8');encoded+=write_length(length,True)+write_length(len(raw),True)+raw+b'\0'
        else:
            encoded+=write_length(length,False)+value.encode('utf-16le')+b'\0\0'
    encoded+=b'\0'*(-len(encoded)%4)
    start=28+4*len(strings)
    new_pool=struct.pack('<HHI5I',1,28,start+len(encoded),len(strings),0,flags & ~1,start,0)+struct.pack('<'+'I'*len(offsets),*offsets)+encoded
    new_resources=struct.pack('<HHI',0x0180,8,8+4*len(resources))+struct.pack('<'+'I'*len(resources),*resources)
    body=b''.join(new_pool if kind==1 else new_resources if kind==0x0180 else c for kind,c in result)
    return struct.pack('<HHI',3,8,8+len(body))+body

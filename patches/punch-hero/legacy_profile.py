"""Remove obsolete profile lookups that require phone/account permissions."""
import hashlib
import struct
import zlib


def patch_profile(data):
    image = bytearray(data)
    if image[:4] != b'dex\n':
        raise ValueError('Expected DEX')
    def u32(offset):
        return struct.unpack_from('<I', image, offset)[0]
    def uleb(offset):
        value, shift = 0, 0
        while True:
            byte = image[offset]
            offset += 1
            value |= (byte & 127) << shift
            if not byte & 128:
                return value, offset
            shift += 7
    strings = []
    for i in range(u32(56)):
        offset = u32(u32(60)+4*i)
        length, offset = uleb(offset)
        strings.append(bytes(image[offset:image.index(0, offset)]).decode('utf8', 'replace'))
    empty = strings.index('')
    if empty > 65535:
        raise ValueError('Empty string requires jumbo index')
    types = [strings[u32(u32(68)+4*i)] for i in range(u32(64))]
    methods = [struct.unpack_from('<HHI', image, u32(92)+8*i) for i in range(u32(88))]
    required = {'getPhoneNumber', 'getOldPhoneNumber', 'getSimSerialNumber', 'getGoogleAccount', 'getDeviceID'}
    # Collect code items for direct TelephonyManager calls outside GvUtils too.
    all_codes = set()
    for i in range(u32(96)):
        cursor = u32(u32(100)+32*i+24)
        if not cursor:
            continue
        counts = []
        for _ in range(4):
            value, cursor = uleb(cursor)
            counts.append(value)
        for _ in range(counts[0]+counts[1]):
            _, cursor = uleb(cursor)
            _, cursor = uleb(cursor)
        for _ in range(counts[2]+counts[3]):
            _, cursor = uleb(cursor)
            _, cursor = uleb(cursor)
            code, cursor = uleb(cursor)
            if code:
                all_codes.add(code)
    patched = set()
    for i in range(u32(96)):
        class_def = u32(100)+32*i
        if types[u32(class_def)] != 'Lcom/gamevil/lib/utils/GvUtils;':
            continue
        offset = u32(class_def+24)
        counts = []
        for _ in range(4):
            count, offset = uleb(offset)
            counts.append(count)
        for _ in range(counts[0]+counts[1]):
            _, offset = uleb(offset)
            _, offset = uleb(offset)
        for count in counts[2:]:
            index = 0
            for _ in range(count):
                delta, offset = uleb(offset)
                access, offset = uleb(offset)
                code, offset = uleb(offset)
                index += delta
                cls, proto, name = methods[index]
                name = strings[name]
                if name not in required:
                    continue
                ret = types[u32(u32(76)+12*proto+4)]
                if ret != 'Ljava/lang/String;' or not code:
                    raise ValueError('Unexpected profile method signature: '+name)
                registers, incoming, outgoing, tries, debug, instructions = struct.unpack_from('<HHHHII', image, code)
                if registers < 1 or instructions < 3:
                    raise ValueError('Profile method code is too small: '+name)
                struct.pack_into('<HHII', image, code+4, 0, 0, 0, instructions)
                image[code+16:code+16+instructions*2] = b'\0'*(instructions*2)
                # const-string v0, ""; return-object v0
                struct.pack_into('<HHH', image, code+16, 0x001a, empty, 0x0011)
                patched.add(name)
    if patched != required:
        raise ValueError('Missing profile methods: '+str(required-patched))
    private_getters = {i for i,(cls,proto,name) in enumerate(methods)
                       if types[cls] == 'Landroid/telephony/TelephonyManager;'
                       and strings[name] in {'getLine1Number', 'getDeviceId', 'getSubscriberId', 'getSimSerialNumber', 'getImei', 'getMeid'}}
    # Standard DEX instruction widths, in 16-bit code units.
    widths = [1]*256
    for op in [2,5,8,0x13,0x15,0x16,0x19,0x1a,0x1c,0x1f,0x20,0x22,0x23,0x29,0xfe,0xff]: widths[op]=2
    for start,end in [(0x2d,0x3d),(0x44,0x6d),(0x90,0xaf),(0xd0,0xe2)]:
        for op in range(start,end+1): widths[op]=2
    for op in [3,6,9,0x14,0x17,0x1b,0x24,0x25,0x26,0x2a,0x2b,0x2c,0xfc,0xfd]: widths[op]=3
    for start,end in [(0x6e,0x72),(0x74,0x78)]:
        for op in range(start,end+1): widths[op]=3
    widths[0x18]=5
    widths[0xfa]=widths[0xfb]=4
    direct = 0
    for code in all_codes:
        count = u32(code+12)
        start, cursor = code+16, 0
        while cursor < count:
            at = start+2*cursor
            word = struct.unpack_from('<H',image,at)[0]
            op = word & 255
            width = widths[op]
            if word == 0x0100:
                width = 4+2*struct.unpack_from('<H',image,at+2)[0]
            elif word == 0x0200:
                width = 2+4*struct.unpack_from('<H',image,at+2)[0]
            elif word == 0x0300:
                size = struct.unpack_from('<H',image,at+2)[0]*u32(at+4)
                width = 4+(size+1)//2
            if cursor+width > count:
                raise ValueError('Invalid DEX instruction boundary')
            if op in {*range(0x6e,0x73),*range(0x74,0x79)} and struct.unpack_from('<H',image,at+2)[0] in private_getters:
                if cursor+3 < count:
                    result = struct.unpack_from('<H',image,at+6)[0]
                else:
                    result = 0
                if result & 255 == 0x0c:  # move-result-object
                    dest = result >> 8
                    struct.pack_into('<HHHH',image,at,0x1a | (dest << 8),empty,0,0)
                    width = 4
                else:
                    image[at:at+6] = b'\0'*6
                direct += 1
            cursor += width
    print('Removed direct private telephony calls:', direct)
    image[12:32] = hashlib.sha1(image[32:]).digest()
    struct.pack_into('<I', image, 8, zlib.adler32(image[12:]) & 0xffffffff)
    print('Removed legacy profile reads: '+', '.join(sorted(patched)))
    return bytes(image)

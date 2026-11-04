"""Adapt Unity 3's text relocations for a target-24 package.

The loader maps the executable segment writable during relocation. A DT_INIT
trampoline flushes the instruction cache and restores RX before Unity's original
constructors run. No game instructions or relocation records are removed.
"""
from pathlib import Path
import struct
import subprocess
import tempfile


def patch_unity(data, toolchain):
    image = bytearray(data)
    if image[:7] != b'\x7fELF\x01\x01\x01' or struct.unpack_from('<H', image, 18)[0] != 40:
        raise ValueError('Expected little-endian ELF32 ARM')
    phoff = struct.unpack_from('<I', image, 28)[0]
    phsize, count = struct.unpack_from('<HH', image, 42)
    segments = []
    for i in range(count):
        pos = phoff + phsize*i
        fields = struct.unpack_from('<8I', image, pos)
        segments.append((pos, fields))
    dynamic = next(fields for pos, fields in segments if fields[0] == 2)
    entries = []
    for offset in range(dynamic[1], dynamic[1]+dynamic[4], 8):
        tag, value = struct.unpack_from('<II', image, offset)
        if tag == 0:
            break
        entries.append((offset, tag, value))
    textrel = [entry for entry in entries if entry[1] == 22]
    if not textrel:
        return data
    if len(textrel) != 1 or any(tag == 12 for offset, tag, value in entries):
        raise ValueError('Unexpected Unity initialization layout')
    code_pos, code = next((pos, f) for pos, f in segments if f[0] == 1 and f[6] & 1)
    if code[1] != 0 or code[2] != 0 or code[4] != code[5]:
        raise ValueError('Unsupported executable segment layout')
    thunk_offset = (code[4]+3) & ~3
    source = f'''
.syntax unified
.arm
.text
.global revival_restore_rx
revival_restore_rx:
.Lentry:
    push {{r0-r7, lr}}
    adr r4, .Lentry
    ldr r5, =0x{thunk_offset:x}
    sub r4, r4, r5
    mov r0, r4
    ldr r1, =revival_end
    add r1, r4, r1
    mov r2, #0
    ldr r7, =0x000f0002
    svc #0
    mov r0, r4
    ldr r1, =revival_end
    mov r2, #5
    mov r7, #125
    svc #0
    cmp r0, #0
    bne revival_failed
    pop {{r0-r7, pc}}
revival_failed:
    udf #0
.ltorg
'''
    # Literal value uses the page-rounded segment end. The code size is
    # independent of its value; determine it with a conservative one-page bound.
    protection_end = (thunk_offset+256+4095) & ~4095
    source = f'.equ revival_end, 0x{protection_end:x}\n' + source
    with tempfile.TemporaryDirectory(prefix='unity-rx-') as folder:
        folder = Path(folder)
        assembly, obj, raw = folder/'restore.S', folder/'restore.o', folder/'restore.bin'
        assembly.write_text(source)
        subprocess.run([str(toolchain/'armv7a-linux-androideabi24-clang'), '-c', str(assembly), '-o', str(obj)], check=True)
        relocations = subprocess.check_output([str(toolchain/'llvm-readelf'), '-r', str(obj)], text=True)
        if 'There are no relocations' not in relocations:
            raise ValueError('Restore trampoline contains unresolved relocations')
        subprocess.run([str(toolchain/'llvm-objcopy'), '-O', 'binary', '--only-section=.text', str(obj), str(raw)], check=True)
        thunk = raw.read_bytes()
    if len(thunk) > 256:
        raise ValueError('Trampoline exceeds reserved size')
    end = thunk_offset+len(thunk)
    next_load = min(f[1] for pos, f in segments if f[0] == 1 and f[1] > code[1])
    if end > next_load or any(image[thunk_offset:end]):
        raise ValueError('No unused executable-segment padding for restore trampoline')
    image[thunk_offset:end] = thunk
    struct.pack_into('<III', image, code_pos+16, end, end, code[6] | 2)
    # Replace DT_TEXTREL with DT_INIT. Preserve DT_NULL and all relocations.
    struct.pack_into('<II', image, textrel[0][0], 12, thunk_offset)
    for offset, tag, value in entries:
        if tag == 30:  # DT_FLAGS: remove DF_TEXTREL if present.
            struct.pack_into('<I', image, offset+4, value & ~4)
    print(f'Unity compatibility: restore-RX trampoline at 0x{thunk_offset:x}, {len(thunk)} bytes')
    return bytes(image)

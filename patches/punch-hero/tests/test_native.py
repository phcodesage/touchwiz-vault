"""Verify a candidate APK and emulate the native restore trampoline.
Run in an environment with unicorn installed, passing old and candidate APK paths.
"""
from pathlib import Path
import struct
import sys
import zipfile
from unicorn import Uc, UC_ARCH_ARM, UC_MODE_ARM, UC_HOOK_INTR
from unicorn.arm_const import *

old_path, candidate_path = sys.argv[1:]
with zipfile.ZipFile(old_path) as old, zipfile.ZipFile(candidate_path) as new:
    changed = [n for n in old.namelist() if not n.startswith('META-INF/') and old.read(n) != new.read(n)]
    assert set(changed) == {'AndroidManifest.xml', 'assets/libs/armeabi-v7a/libunity.so', 'lib/armeabi-v7a/libunity.so'}, changed
    data = new.read('lib/armeabi-v7a/libunity.so')
    original = old.read('lib/armeabi-v7a/libunity.so')
    asset = new.read('assets/libs/armeabi-v7a/libunity.so')
    original_asset = old.read('assets/libs/armeabi-v7a/libunity.so')
    before_diff = [(i,a,b) for i,(a,b) in enumerate(zip(original, original_asset)) if a != b]
    after_diff = [(i,a,b) for i,(a,b) in enumerate(zip(data, asset)) if a != b]
    assert before_diff == after_diff, 'Original crash-fix differences must remain intact'
phoff = struct.unpack_from('<I', data, 28)[0]
size, count = struct.unpack_from('<HH', data, 42)
segments = [struct.unpack_from('<8I', data, phoff+i*size) for i in range(count)]
code = next(f for f in segments if f[0] == 1 and f[6] & 1)
dynamic = next(f for f in segments if f[0] == 2)
entries = dict(struct.unpack_from('<II', data, o) for o in range(dynamic[1], dynamic[1]+dynamic[4], 8))
assert 22 not in entries
thunk = entries[12]
rel_start, rel_length = entries[17], entries[18]
assert original[rel_start:rel_start+rel_length] == data[rel_start:rel_start+rel_length]
regs = [UC_ARM_REG_R0, UC_ARM_REG_R1, UC_ARM_REG_R2, UC_ARM_REG_R3, UC_ARM_REG_R4, UC_ARM_REG_R5,
        UC_ARM_REG_R6, UC_ARM_REG_R7, UC_ARM_REG_R8, UC_ARM_REG_R9, UC_ARM_REG_R10, UC_ARM_REG_R11, UC_ARM_REG_R12]
for base in (0x10000000, 0x20000000, 0x50000000):
    uc = Uc(UC_ARCH_ARM, UC_MODE_ARM)
    uc.mem_map(base, (len(data)+4095) & ~4095)
    uc.mem_write(base, data)
    uc.mem_map(0x70000000, 0x2000)
    uc.reg_write(UC_ARM_REG_SP, 0x70001000)
    uc.reg_write(UC_ARM_REG_LR, base+0x100)
    initial = [0x12340000+i for i in range(len(regs))]
    for reg, value in zip(regs, initial):
        uc.reg_write(reg, value)
    calls = []
    def syscall(uc, number, user_data):
        calls.append(tuple(uc.reg_read(r) for r in (UC_ARM_REG_R7, UC_ARM_REG_R0, UC_ARM_REG_R1, UC_ARM_REG_R2)))
        uc.reg_write(UC_ARM_REG_R0, 0)
    uc.hook_add(UC_HOOK_INTR, syscall)
    uc.emu_start(base+thunk, base+0x100, count=100)
    end = (code[4]+4095) & ~4095
    assert calls == [(0xf0002, base, base+end, 0), (125, base, end, 5)], calls
    assert [uc.reg_read(r) for r in regs] == initial
    assert uc.reg_read(UC_ARM_REG_SP) == 0x70001000
    assert uc.reg_read(UC_ARM_REG_PC) == base+0x100
    print(f'PASS: relocated load at {base:#x}, cache flush, RX protection, register/stack preservation')
print('PASS: game assets and relocation records preserved; original differences between Unity copies preserved')

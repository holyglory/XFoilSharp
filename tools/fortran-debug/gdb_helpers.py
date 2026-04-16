"""
GDB Python helpers for XFoil Fortran debugging.
Usage: source /path/to/gdb_helpers.py inside GDB.

Provides commands to read COMMON block variables from XBL.INC and XFOIL.INC.
"""
import gdb
import struct

class XfoilState:
    """Read XFoil COMMON block state from memory."""

    # V_VAR1 layout (XBL.INC): each field is REAL (4 bytes)
    # X1=0, U1=4, T1=8, D1=12, S1=16, AMPL1=20, U1_UEI=24, U1_MS=28, DW1=32
    # H1=36, H1_T1=40, H1_D1=44
    # M1=48, M1_U1=52, M1_MS=56
    # R1=60, R1_U1=64, R1_MS=68
    # V1=72, V1_U1=76, V1_MS=80, V1_RE=84
    # HK1=88, HK1_U1=92, HK1_T1=96, HK1_D1=100, HK1_MS=104
    # HS1=108, HS1_U1=112, HS1_T1=116, HS1_D1=120, HS1_MS=124, HS1_RE=128
    V_VAR1_OFFSETS = {
        'X1': 0, 'U1': 4, 'T1': 8, 'D1': 12, 'S1': 16, 'AMPL1': 20,
        'HK1': 88, 'HS1': 108, 'CF1': 172, 'DI1': 196, 'RT1': 152, 'US1': 224,
    }
    V_VAR2_OFFSETS = {
        'X2': 0, 'U2': 4, 'T2': 8, 'D2': 12, 'S2': 16, 'AMPL2': 20,
        'HK2': 88, 'HS2': 108, 'CF2': 172, 'DI2': 196, 'RT2': 152, 'US2': 224,
    }

    @staticmethod
    def _read_real(addr, offset):
        """Read a REAL (float32) at addr+offset."""
        inf = gdb.selected_inferior()
        data = bytes(inf.read_memory(addr + offset, 4))
        return struct.unpack('<f', data)[0]

    @staticmethod
    def _read_real_hex(addr, offset):
        """Read a REAL as hex string."""
        inf = gdb.selected_inferior()
        data = bytes(inf.read_memory(addr + offset, 4))
        return data[::-1].hex().upper()  # big-endian hex

    @staticmethod
    def get_v_var1_addr():
        return int(gdb.parse_and_eval("&v_var1_"))

    @staticmethod
    def get_v_var2_addr():
        return int(gdb.parse_and_eval("&v_var2_"))

    @classmethod
    def read_station1(cls, var_name):
        """Read a V_VAR1 variable by name (e.g., 'T1', 'HK1')."""
        addr = cls.get_v_var1_addr()
        offset = cls.V_VAR1_OFFSETS[var_name]
        return cls._read_real(addr, offset)

    @classmethod
    def read_station2(cls, var_name):
        """Read a V_VAR2 variable by name (e.g., 'T2', 'HK2')."""
        addr = cls.get_v_var2_addr()
        offset = cls.V_VAR2_OFFSETS[var_name]
        return cls._read_real(addr, offset)

    @classmethod
    def dump_stations(cls):
        """Dump key variables at both stations."""
        v1 = cls.get_v_var1_addr()
        v2 = cls.get_v_var2_addr()
        for name, off in sorted(cls.V_VAR1_OFFSETS.items(), key=lambda x: x[1]):
            val = cls._read_real(v1, off)
            hex_str = cls._read_real_hex(v1, off)
            print(f"  {name:>6} = {val:15.8e}  [{hex_str}]")
        print("  ---")
        for name, off in sorted(cls.V_VAR2_OFFSETS.items(), key=lambda x: x[1]):
            val = cls._read_real(v2, off)
            hex_str = cls._read_real_hex(v2, off)
            print(f"  {name:>6} = {val:15.8e}  [{hex_str}]")


class ReadXfoilArray:
    """Read XFoil arrays from COMMON blocks."""

    @staticmethod
    def read_array_element(common_name, index, element_size=4):
        """Read element at index from a COMMON block array."""
        addr = int(gdb.parse_and_eval(f"&{common_name}"))
        inf = gdb.selected_inferior()
        data = bytes(inf.read_memory(addr + index * element_size, element_size))
        if element_size == 4:
            return struct.unpack('<f', data)[0]
        elif element_size == 8:
            return struct.unpack('<d', data)[0]


class XfoilBreak(gdb.Breakpoint):
    """Reusable conditional breakpoint for XFoil debugging."""

    def __init__(self, location, condition_fn=None, action_fn=None):
        super().__init__(location, internal=False)
        self.silent = True
        self.condition_fn = condition_fn
        self.action_fn = action_fn
        self._hit_count = 0

    def stop(self):
        self._hit_count += 1
        if self.condition_fn and not self.condition_fn(self._hit_count):
            return False
        if self.action_fn:
            self.action_fn(self._hit_count)
            return False  # continue running
        return True  # stop if no action


# Register convenience commands
class DumpBLState(gdb.Command):
    """Dump XFoil BL state variables at current breakpoint."""

    def __init__(self):
        super().__init__("xf-bl", gdb.COMMAND_USER)

    def invoke(self, arg, from_tty):
        try:
            XfoilState.dump_stations()
        except Exception as e:
            print(f"Error: {e}")

class DumpLocal(gdb.Command):
    """Dump a local Fortran variable as float hex."""

    def __init__(self):
        super().__init__("xf-var", gdb.COMMAND_USER)

    def invoke(self, arg, from_tty):
        try:
            val = float(gdb.parse_and_eval(arg))
            fval = struct.pack('<f', struct.unpack('<f', struct.pack('<f', val))[0])
            hex_str = fval[::-1].hex().upper()
            print(f"{arg} = {val:.15e}  [float hex: {hex_str}]")
        except Exception as e:
            print(f"Error reading {arg}: {e}")

# Register commands
DumpBLState()
DumpLocal()

print("XFoil GDB helpers loaded. Commands: xf-bl, xf-var <name>")

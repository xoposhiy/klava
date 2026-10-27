"""Keyboard event logger for debugging key chatter and layout switcher problems.

Reads key events from two sources at the same time:
  RAW  - Raw Input: events as the device driver delivered them, per physical device.
         If one physical press shows up here as two presses, the keyboard (switch,
         firmware or connection) is the problem, not the software.
  HOOK - Low-level keyboard hook: events that keyboard hooks see. Only injected
         (synthetic) events are printed by default, so you can see what Caramba,
         AutoHotkey and other tools send.

Also prints a line each time the keyboard layout of the foreground window changes.

Usage:
  python kbdlog.py              run until Ctrl+C in this console
  python kbdlog.py --all        also print physical HOOK events
  python kbdlog.py --seconds 60 stop automatically after 60 seconds

Mark a moment in the log (for example, right after a glitch): tap Right Ctrl 3 times quickly.
Do not type passwords while the logger runs: the log file contains every key.
"""

import argparse
import collections
import ctypes
import ctypes.wintypes as wt
import datetime
import os
import queue
import re
import threading
import time

CHATTER_MS = 60   # same key pressed again sooner than this after release -> likely chatter
SHORT_MS = 20     # press shorter than this is suspicious (humans rarely go below ~30 ms)
MARK_KEY = 0xA3   # Right Ctrl
MARK_TAPS = 3
MARK_WINDOW_S = 0.8

user32 = ctypes.WinDLL('user32', use_last_error=True)
kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)

LRESULT = ctypes.c_ssize_t
WPARAM = ctypes.c_size_t
LPARAM = ctypes.c_ssize_t
HOOKPROC = ctypes.WINFUNCTYPE(LRESULT, ctypes.c_int, WPARAM, LPARAM)
WNDPROC = ctypes.WINFUNCTYPE(LRESULT, wt.HWND, wt.UINT, WPARAM, LPARAM)
CTRLHANDLER = ctypes.WINFUNCTYPE(wt.BOOL, wt.DWORD)

WH_KEYBOARD_LL = 13
WM_INPUT = 0x00FF
WM_QUIT = 0x0012
RID_INPUT = 0x10000003
RIDI_DEVICENAME = 0x20000007
RIM_TYPEKEYBOARD = 1
RIDEV_INPUTSINK = 0x00000100
RI_KEY_BREAK = 0x01
RI_KEY_E0 = 0x02
LLKHF_LOWER_IL_INJECTED = 0x02
LLKHF_INJECTED = 0x10
LLKHF_UP = 0x80
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000


class KBDLLHOOKSTRUCT(ctypes.Structure):
    _fields_ = [('vkCode', wt.DWORD), ('scanCode', wt.DWORD), ('flags', wt.DWORD),
                ('time', wt.DWORD), ('dwExtraInfo', ctypes.c_size_t)]


class RAWINPUTHEADER(ctypes.Structure):
    _fields_ = [('dwType', wt.DWORD), ('dwSize', wt.DWORD), ('hDevice', wt.HANDLE), ('wParam', WPARAM)]


class RAWKEYBOARD(ctypes.Structure):
    _fields_ = [('MakeCode', wt.USHORT), ('Flags', wt.USHORT), ('Reserved', wt.USHORT),
                ('VKey', wt.USHORT), ('Message', wt.UINT), ('ExtraInformation', wt.ULONG)]


class RAWINPUT(ctypes.Structure):
    _fields_ = [('header', RAWINPUTHEADER), ('keyboard', RAWKEYBOARD)]


class RAWINPUTDEVICE(ctypes.Structure):
    _fields_ = [('usUsagePage', wt.USHORT), ('usUsage', wt.USHORT), ('dwFlags', wt.DWORD), ('hwndTarget', wt.HWND)]


class WNDCLASSW(ctypes.Structure):
    _fields_ = [('style', wt.UINT), ('lpfnWndProc', WNDPROC), ('cbClsExtra', ctypes.c_int),
                ('cbWndExtra', ctypes.c_int), ('hInstance', wt.HINSTANCE), ('hIcon', wt.HICON),
                ('hCursor', wt.HANDLE), ('hbrBackground', wt.HBRUSH), ('lpszMenuName', wt.LPCWSTR),
                ('lpszClassName', wt.LPCWSTR)]


user32.SetWindowsHookExW.argtypes = [ctypes.c_int, HOOKPROC, wt.HINSTANCE, wt.DWORD]
user32.SetWindowsHookExW.restype = wt.HHOOK
user32.CallNextHookEx.argtypes = [wt.HHOOK, ctypes.c_int, WPARAM, LPARAM]
user32.CallNextHookEx.restype = LRESULT
user32.UnhookWindowsHookEx.argtypes = [wt.HHOOK]
user32.DefWindowProcW.argtypes = [wt.HWND, wt.UINT, WPARAM, LPARAM]
user32.DefWindowProcW.restype = LRESULT
user32.RegisterClassW.argtypes = [ctypes.POINTER(WNDCLASSW)]
user32.RegisterClassW.restype = wt.ATOM
user32.CreateWindowExW.argtypes = [wt.DWORD, wt.LPCWSTR, wt.LPCWSTR, wt.DWORD, ctypes.c_int, ctypes.c_int,
                                   ctypes.c_int, ctypes.c_int, wt.HWND, wt.HMENU, wt.HINSTANCE, wt.LPVOID]
user32.CreateWindowExW.restype = wt.HWND
user32.RegisterRawInputDevices.argtypes = [ctypes.POINTER(RAWINPUTDEVICE), wt.UINT, wt.UINT]
user32.GetRawInputData.argtypes = [wt.HANDLE, wt.UINT, wt.LPVOID, ctypes.POINTER(wt.UINT), wt.UINT]
user32.GetRawInputData.restype = wt.UINT
user32.GetRawInputDeviceInfoW.argtypes = [wt.HANDLE, wt.UINT, wt.LPVOID, ctypes.POINTER(wt.UINT)]
user32.GetRawInputDeviceInfoW.restype = wt.UINT
user32.GetMessageW.argtypes = [ctypes.POINTER(wt.MSG), wt.HWND, wt.UINT, wt.UINT]
user32.TranslateMessage.argtypes = [ctypes.POINTER(wt.MSG)]
user32.DispatchMessageW.argtypes = [ctypes.POINTER(wt.MSG)]
user32.DispatchMessageW.restype = LRESULT
user32.PostThreadMessageW.argtypes = [wt.DWORD, wt.UINT, WPARAM, LPARAM]
user32.GetForegroundWindow.restype = wt.HWND
user32.GetWindowThreadProcessId.argtypes = [wt.HWND, ctypes.POINTER(wt.DWORD)]
user32.GetWindowThreadProcessId.restype = wt.DWORD
user32.GetKeyboardLayout.argtypes = [wt.DWORD]
user32.GetKeyboardLayout.restype = ctypes.c_size_t
kernel32.GetModuleHandleW.argtypes = [wt.LPCWSTR]
kernel32.GetModuleHandleW.restype = wt.HMODULE
kernel32.GetCurrentThreadId.restype = wt.DWORD
kernel32.SetConsoleCtrlHandler.argtypes = [CTRLHANDLER, wt.BOOL]
kernel32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
kernel32.OpenProcess.restype = wt.HANDLE
kernel32.QueryFullProcessImageNameW.argtypes = [wt.HANDLE, wt.DWORD, wt.LPWSTR, ctypes.POINTER(wt.DWORD)]
kernel32.CloseHandle.argtypes = [wt.HANDLE]

VK_NAMES = {
    0x08: 'Backspace', 0x09: 'Tab', 0x0D: 'Enter', 0x13: 'Pause', 0x14: 'CapsLock', 0x1B: 'Esc',
    0x20: 'Space', 0x21: 'PgUp', 0x22: 'PgDn', 0x23: 'End', 0x24: 'Home', 0x25: 'Left', 0x26: 'Up',
    0x27: 'Right', 0x28: 'Down', 0x2C: 'PrtSc', 0x2D: 'Ins', 0x2E: 'Del', 0x5B: 'LWin', 0x5C: 'RWin',
    0x5D: 'Menu', 0x90: 'NumLock', 0x91: 'ScrLock', 0xA0: 'LShift', 0xA1: 'RShift', 0xA2: 'LCtrl',
    0xA3: 'RCtrl', 0xA4: 'LAlt', 0xA5: 'RAlt', 0xBA: ';', 0xBB: '=', 0xBC: ',', 0xBD: '-', 0xBE: '.',
    0xBF: '/', 0xC0: '`', 0xDB: '[', 0xDC: '\\', 0xDD: ']', 0xDE: "'", 0xE7: 'Packet',
}
VK_NAMES.update({0x70 + i: f'F{i + 1}' for i in range(24)})
SHIFT_VKS = (0xA0, 0xA1)
LANG_NAMES = {0x0409: 'EN', 0x0809: 'EN-GB', 0x0419: 'RU', 0x0422: 'UA', 0x0423: 'BY', 0x0407: 'DE'}
# AutoHotkey marks its own injected events with these dwExtraInfo values.
AHK_EXTRA = range(0xFFC3D44D, 0xFFC3D451)

RED, YELLOW, GREEN, CYAN, DIM, BOLD, RESET = ('\x1b[91m', '\x1b[93m', '\x1b[92m', '\x1b[96m',
                                              '\x1b[90m', '\x1b[1m', '\x1b[0m')


def vk_name(vk):
    if 0x30 <= vk <= 0x39 or 0x41 <= vk <= 0x5A:
        return chr(vk)
    return VK_NAMES.get(vk, f'vk{vk:02X}')


def normalize_raw_vk(vk, make_code, flags):
    """Raw Input reports generic Shift/Ctrl/Alt; convert them to left/right codes like the hook does."""
    if vk == 0x10:
        return 0xA1 if make_code == 0x36 else 0xA0
    if vk == 0x11:
        return 0xA3 if flags & RI_KEY_E0 else 0xA2
    if vk == 0x12:
        return 0xA5 if flags & RI_KEY_E0 else 0xA4
    return vk


class Printer:
    def __init__(self, log_path):
        self.log = open(log_path, 'w', encoding='utf-8')
        self.last_t = None

    def line(self, t, text, color=''):
        clock = datetime.datetime.now().strftime('%H:%M:%S.%f')[:-3]
        delta = '' if self.last_t is None else f'{(t - self.last_t) * 1000:+9.1f}ms'
        self.last_t = t
        plain = f'{clock} {delta:>11}  {text}'
        print(f'{color}{plain}{RESET}' if color else plain, flush=True)
        self.log.write(plain + '\n')
        self.log.flush()

    def note(self, text, color=''):
        print(f'{color}{text}{RESET}' if color else text, flush=True)
        self.log.write(text + '\n')
        self.log.flush()


class Analyzer:
    def __init__(self, printer, show_all_hook):
        self.p = printer
        self.show_all_hook = show_all_hook
        self.devices = {}
        self.down_at = {}
        self.last_up = {}
        self.repeats = collections.Counter()
        self.presses = collections.Counter()
        self.chatter = collections.Counter()
        self.short = collections.Counter()
        self.shift_chord = {}
        self.last_shift_tap = None
        self.shift_taps = 0
        self.mark_taps = collections.deque(maxlen=MARK_TAPS)
        self.layout = None
        self.layout_hwnd = None
        self.proc_names = {}

    def device_label(self, handle):
        if handle in self.devices:
            return self.devices[handle]
        size = wt.UINT(0)
        user32.GetRawInputDeviceInfoW(handle, RIDI_DEVICENAME, None, ctypes.byref(size))
        buf = ctypes.create_unicode_buffer(size.value + 1)
        user32.GetRawInputDeviceInfoW(handle, RIDI_DEVICENAME, buf, ctypes.byref(size))
        name = buf.value.upper()
        # USB: VID_xxxx&PID_xxxx. Bluetooth LE HID ({00001812-...}): VID&02xxxx_PID&xxxx.
        usb = re.search(r'VID_([0-9A-F]{4})&PID_([0-9A-F]{4})', name)
        bt = re.search(r'VID&\d\d([0-9A-F]{4})_PID&([0-9A-F]{4})', name)
        if '00001812' in name or bt:
            label = f'BT {bt.group(1)}:{bt.group(2)}' if bt else 'BT'
        elif usb:
            label = f'USB {usb.group(1)}:{usb.group(2)}'
        elif 'ACPI' in name:
            label = 'Built-in'
        else:
            label = f'dev{handle:X}'
        self.devices[handle] = label
        self.p.note(f'# new device {label}: {buf.value}', DIM)
        return label

    def handle(self, ev):
        if ev[0] == 'RAW':
            self.handle_raw(*ev[1:])
        else:
            self.handle_hook(*ev[1:])

    def handle_raw(self, t, vk, make_code, flags, device):
        if not device or vk == 0xFF:
            return  # injected events have no device (the hook prints them); 0xFF is a fake key
        dev = self.device_label(device)
        vk = normalize_raw_vk(vk, make_code, flags)
        name = vk_name(vk)
        key = (device, vk)
        up = bool(flags & RI_KEY_BREAK)
        head = f'RAW  {dev:<14} {name:<9}'

        if not up:
            if key in self.down_at:
                self.repeats[key] += 1
                return
            for k in self.shift_chord:
                if k != key:
                    self.shift_chord[k] = True
            self.down_at[key] = t
            self.presses[name] += 1
            if vk in SHIFT_VKS:
                self.shift_chord[key] = False
            gap = (t - self.last_up[key]) * 1000 if key in self.last_up else None
            if gap is not None and gap < CHATTER_MS:
                self.chatter[name] += 1
                self.p.line(t, f'{head} DOWN  !!! CHATTER: pressed again {gap:.1f} ms after release', RED)
            else:
                self.p.line(t, f'{head} DOWN')
            return

        if key not in self.down_at:
            self.p.line(t, f'{head} up    (no matching DOWN - lost event?)', YELLOW)
            self.last_up[key] = t
            return
        held = (t - self.down_at.pop(key)) * 1000
        self.last_up[key] = t
        reps = self.repeats.pop(key, 0)
        info = f'held {held:6.1f} ms' + (f', {reps} autorepeats' if reps else '')
        if held < SHORT_MS:
            self.short[name] += 1
            self.p.line(t, f'{head} up    {info}  !!! very short press', RED)
        else:
            self.p.line(t, f'{head} up    {info}')

        if vk in SHIFT_VKS and not self.shift_chord.pop(key, True):
            self.shift_taps += 1
            since = '' if self.last_shift_tap is None else f', {(t - self.last_shift_tap) * 1000:.0f} ms after previous tap'
            self.last_shift_tap = t
            self.p.note(f'{"":24}>>> lone {name} tap #{self.shift_taps} (held {held:.0f} ms{since})', CYAN)
        else:
            self.shift_chord.pop(key, None)
            self.last_shift_tap = None if vk not in SHIFT_VKS else self.last_shift_tap

        if vk == MARK_KEY:
            self.mark_taps.append(t)
            if len(self.mark_taps) == MARK_TAPS and t - self.mark_taps[0] < MARK_WINDOW_S:
                self.mark_taps.clear()
                self.p.note('=' * 30 + ' MARK (user saw a glitch) ' + '=' * 30, BOLD + YELLOW)

    def handle_hook(self, t, vk, scan, flags, extra):
        injected = bool(flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED))
        if not injected and not self.show_all_hook:
            return
        up = 'up  ' if flags & LLKHF_UP else 'DOWN'
        if injected:
            e = extra & 0xFFFFFFFF
            who = 'AutoHotkey' if e in AHK_EXTRA else f'extra=0x{extra:X}'
            self.p.line(t, f'HOOK {"injected":<14} {vk_name(vk):<9} {up}  ({who})', DIM)
        else:
            self.p.line(t, f'HOOK {"physical":<14} {vk_name(vk):<9} {up}', DIM)

    def process_name(self, pid):
        if pid in self.proc_names:
            return self.proc_names[pid]
        name = f'pid {pid}'
        h = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
        if h:
            buf = ctypes.create_unicode_buffer(1024)
            size = wt.DWORD(len(buf))
            if kernel32.QueryFullProcessImageNameW(h, 0, buf, ctypes.byref(size)):
                name = os.path.basename(buf.value)
            kernel32.CloseHandle(h)
        self.proc_names[pid] = name
        return name

    def poll_layout(self):
        hwnd = user32.GetForegroundWindow()
        if not hwnd:
            return
        pid = wt.DWORD(0)
        tid = user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        lang = user32.GetKeyboardLayout(tid) & 0xFFFF
        if lang == self.layout:
            self.layout_hwnd = hwnd
            return
        old = LANG_NAMES.get(self.layout, f'{self.layout:04X}') if self.layout is not None else '?'
        new = LANG_NAMES.get(lang, f'{lang:04X}')
        reason = ' (foreground window changed)' if hwnd != self.layout_hwnd and self.layout is not None else ''
        self.layout, self.layout_hwnd = lang, hwnd
        self.p.line(time.perf_counter(), f'LAYOUT {old} -> {new}   in {self.process_name(pid.value)}{reason}', GREEN)

    def summary(self):
        self.p.note('\n===== SUMMARY =====', BOLD)
        self.p.note(f'Devices: {", ".join(sorted(set(self.devices.values()))) or "none"}')
        self.p.note(f'Lone Shift taps: {self.shift_taps}')
        if not self.chatter and not self.short:
            self.p.note('No chatter and no very short presses detected.', GREEN)
        for name in sorted(set(self.chatter) | set(self.short)):
            self.p.note(f'  {name:<9} presses={self.presses[name]:<5} chatter={self.chatter[name]:<3} '
                        f'very_short={self.short[name]}', RED)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--all', action='store_true', help='also print physical events seen by the hook')
    ap.add_argument('--seconds', type=float, default=0, help='stop after this many seconds')
    args = ap.parse_args()

    os.system('')  # enable ANSI colors in the Windows console
    log_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'logs')
    os.makedirs(log_dir, exist_ok=True)
    log_path = os.path.join(log_dir, datetime.datetime.now().strftime('kbd-%Y%m%d-%H%M%S.log'))
    printer = Printer(log_path)
    analyzer = Analyzer(printer, args.all)
    events = queue.SimpleQueue()
    main_tid = kernel32.GetCurrentThreadId()
    running = threading.Event()
    running.set()

    # Callbacks run on the message loop thread and must return fast, or Windows drops the hook.
    # So they only queue events; a separate thread analyzes and prints them.
    def hook_proc(n_code, w_param, l_param):
        if n_code == 0:
            k = KBDLLHOOKSTRUCT.from_address(l_param)
            events.put(('HOOK', time.perf_counter(), k.vkCode, k.scanCode, k.flags, k.dwExtraInfo))
        return user32.CallNextHookEx(None, n_code, w_param, l_param)

    raw_buf = ctypes.create_string_buffer(256)

    def wnd_proc(hwnd, msg, w_param, l_param):
        if msg == WM_INPUT:
            t = time.perf_counter()
            size = wt.UINT(ctypes.sizeof(raw_buf))
            if user32.GetRawInputData(l_param, RID_INPUT, raw_buf, ctypes.byref(size),
                                      ctypes.sizeof(RAWINPUTHEADER)) != 0xFFFFFFFF:
                ri = RAWINPUT.from_buffer(raw_buf)
                if ri.header.dwType == RIM_TYPEKEYBOARD:
                    kb = ri.keyboard
                    events.put(('RAW', t, kb.VKey, kb.MakeCode, kb.Flags, ri.header.hDevice or 0))
        return user32.DefWindowProcW(hwnd, msg, w_param, l_param)

    def on_ctrl(_ctrl_type):
        user32.PostThreadMessageW(main_tid, WM_QUIT, 0, 0)
        return True

    def worker():
        while running.is_set() or not events.empty():
            try:
                analyzer.handle(events.get(timeout=0.02))
            except queue.Empty:
                pass
            analyzer.poll_layout()

    hook_cb, wnd_cb, ctrl_cb = HOOKPROC(hook_proc), WNDPROC(wnd_proc), CTRLHANDLER(on_ctrl)
    h_inst = kernel32.GetModuleHandleW(None)
    wc = WNDCLASSW(lpfnWndProc=wnd_cb, hInstance=h_inst, lpszClassName='KbdDebugRawInput')
    if not user32.RegisterClassW(ctypes.byref(wc)):
        raise ctypes.WinError(ctypes.get_last_error())
    hwnd = user32.CreateWindowExW(0, wc.lpszClassName, 'kbdlog', 0, 0, 0, 0, 0, None, None, h_inst, None)
    if not hwnd:
        raise ctypes.WinError(ctypes.get_last_error())
    rid = RAWINPUTDEVICE(1, 6, RIDEV_INPUTSINK, hwnd)
    if not user32.RegisterRawInputDevices(ctypes.byref(rid), 1, ctypes.sizeof(RAWINPUTDEVICE)):
        raise ctypes.WinError(ctypes.get_last_error())
    hook = user32.SetWindowsHookExW(WH_KEYBOARD_LL, hook_cb, h_inst, 0)
    if not hook:
        raise ctypes.WinError(ctypes.get_last_error())
    kernel32.SetConsoleCtrlHandler(ctrl_cb, True)
    if args.seconds:
        threading.Timer(args.seconds, on_ctrl, args=(0,)).start()

    printer.note(f'Logging to {log_path}', BOLD)
    printer.note(f'Chatter threshold {CHATTER_MS} ms. Tap Right Ctrl x3 to put a MARK. Ctrl+C here to stop.\n')
    thread = threading.Thread(target=worker, daemon=True)
    thread.start()

    msg = wt.MSG()
    while user32.GetMessageW(ctypes.byref(msg), None, 0, 0) > 0:
        user32.TranslateMessage(ctypes.byref(msg))
        user32.DispatchMessageW(ctypes.byref(msg))

    user32.UnhookWindowsHookEx(hook)
    running.clear()
    thread.join()
    analyzer.summary()
    printer.log.close()


if __name__ == '__main__':
    main()

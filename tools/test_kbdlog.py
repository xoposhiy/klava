import unittest
from unittest.mock import Mock

from kbdlog import LLKHF_INJECTED, LLKHF_UP, UnsupportedAnalyzer, unsupported_key


class UnsupportedKeyTests(unittest.TestCase):
    def test_normal_keys_are_not_recorded(self):
        printer = Mock()
        analyzer = UnsupportedAnalyzer(printer, False)
        for vk in range(1, 255):
            self.assertFalse(unsupported_key(vk))
            analyzer.handle(('RAW', 1.0, vk, 0, 0, 0))
            analyzer.handle(('HOOK', 1.0, vk, 0, LLKHF_INJECTED, 0))
        analyzer.poll_layout()
        self.assertEqual([], printer.mock_calls)

    def test_unmapped_raw_key_keeps_its_device_and_scan_code(self):
        printer = Mock()
        analyzer = UnsupportedAnalyzer(printer, False)
        analyzer.device_label = Mock(return_value='Built-in')
        analyzer.handle(('RAW', 1.0, 255, 0x63, 0, 123))
        analyzer.device_label.assert_called_once_with(123)
        printer.line.assert_called_once_with(
            1.0, 'RAW unsupported vk=0xFF scan=0x63 flags=0x0 down device=Built-in')

    def test_hook_keeps_injection_flags_and_extra_information(self):
        printer = Mock()
        analyzer = UnsupportedAnalyzer(printer, False)
        analyzer.handle(('HOOK', 1.0, 255, 0, LLKHF_INJECTED | LLKHF_UP, 0x123))
        printer.line.assert_called_once_with(
            1.0, 'HOOK unsupported vk=0xFF scan=0x0 flags=0x90 up injected=True extra=0x123')

    def test_unsupported_codes_include_zero_and_both_out_of_range_boundaries(self):
        for vk in (-1, 0, 255, 256, 0xFFFFFFFF):
            self.assertTrue(unsupported_key(vk))


if __name__ == '__main__':
    unittest.main()

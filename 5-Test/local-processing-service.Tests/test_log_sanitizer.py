"""Contract tests for reversible local-processor log-value encoding."""

import unicodedata

from security.log_sanitizer import sanitize_log_value


def test_sanitize_log_value_when_value_is_none_returns_empty_string() -> None:
    result = sanitize_log_value(None)

    assert result == ""


def test_sanitize_log_value_when_value_contains_printable_unicode_preserves_full_value() -> None:
    input_value = f"Motorcycle 東京 é⚡😀 {'x' * 250}"

    result = sanitize_log_value(input_value)

    assert result == input_value


def test_sanitize_log_value_when_value_contains_backslash_before_line_feed_escapes_backslash_first() -> None:
    input_value = "path\\manual\nnext"

    result = sanitize_log_value(input_value)

    assert result == "path\\\\manual\\nnext"


def test_sanitize_log_value_when_value_contains_cr_lf_tab_nul_c0_and_c1_controls_uses_visible_reversible_escapes() -> None:
    input_value = "before\\after\r\n\t\0\x01\x1f\x7f\x85\x9fafter"

    result = sanitize_log_value(input_value)

    assert result == "before\\\\after\\r\\n\\t\\0\\u0001\\u001F\\u007F\\u0085\\u009Fafter"


def test_sanitize_log_value_when_value_contains_control_characters_returns_no_raw_control_characters() -> None:
    input_value = "\r\n\t\0\x01\x1f\x7f\x85\x9f"

    result = sanitize_log_value(input_value)

    assert all(unicodedata.category(character) != "Cc" for character in result)

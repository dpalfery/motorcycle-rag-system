"""Reversible encoding for values written to local processor logs."""


def sanitize_log_value(value: str | None) -> str:
    """Encode control characters so a value cannot forge log entries.

    Args:
        value: The optional string value intended for a log sink.

    Returns:
        The complete input value with backslashes and control characters
        represented by visible, reversible escape sequences.
    """
    if value is None:
        return ""

    sanitized_characters: list[str] = []
    for character in value:
        if character == "\\":
            sanitized_characters.append("\\\\")
        elif character == "\r":
            sanitized_characters.append("\\r")
        elif character == "\n":
            sanitized_characters.append("\\n")
        elif character == "\t":
            sanitized_characters.append("\\t")
        elif character == "\0":
            sanitized_characters.append("\\0")
        elif ord(character) <= 0x1F or 0x7F <= ord(character) <= 0x9F:
            sanitized_characters.append(f"\\u{ord(character):04X}")
        else:
            sanitized_characters.append(character)

    return "".join(sanitized_characters)

"""Validation helpers for outbound local-processing-service endpoints."""

from __future__ import annotations

import ipaddress
from urllib.parse import SplitResult, urlsplit, urlunsplit


_LOOPBACK_HOSTNAME = "localhost"
_SUPPORTED_SCHEMES = frozenset({"http", "https"})


def validate_endpoint_url(endpoint: str) -> str:
    """Validate an outbound HTTP(S) endpoint and return its normalized form.

    Remote endpoints must use HTTPS. HTTP is reserved for literal loopback
    hosts so local providers can operate without a development certificate.

    Args:
        endpoint: Configured provider or API endpoint.

    Returns:
        The validated endpoint without a trailing slash.

    Raises:
        ValueError: If the endpoint is malformed or violates outbound policy.
    """
    if not isinstance(endpoint, str):
        raise ValueError("Endpoint URL must be a string")

    value = endpoint.strip()
    if not value:
        raise ValueError("endpoint is required")

    try:
        parsed = urlsplit(value)
        host = parsed.hostname
        port = parsed.port
    except ValueError as exc:
        raise ValueError("Endpoint URL has a malformed authority") from exc

    _validate_url_parts(parsed, host, port)
    scheme = parsed.scheme.lower()
    path = parsed.path.rstrip("/")
    return urlunsplit((scheme, parsed.netloc, path, "", ""))


def _validate_url_parts(
    parsed: SplitResult,
    host: str | None,
    port: int | None,
) -> None:
    """Validate parsed endpoint components against the outbound policy.

    Args:
        parsed: Parsed endpoint URL.
        host: Parsed hostname.
        port: Parsed port, if explicitly specified.

    Raises:
        ValueError: If the parsed URL is unsafe or malformed.
    """
    scheme = parsed.scheme.lower()
    if scheme not in _SUPPORTED_SCHEMES:
        raise ValueError("Endpoint URL must use HTTP or HTTPS")
    if not parsed.netloc or host is None:
        raise ValueError("Endpoint URL must include a host authority")
    if parsed.username is not None or parsed.password is not None:
        raise ValueError("Endpoint URL must not include credentials")
    if parsed.query:
        raise ValueError("Endpoint URL must not include query parameters")
    if parsed.fragment:
        raise ValueError("Endpoint URL must not include a fragment")
    if port == 0:
        raise ValueError("Endpoint URL port must be between 1 and 65535")

    normalized_host = host.lower()
    is_loopback = _is_loopback_host(normalized_host)
    if scheme == "http" and not is_loopback:
        raise ValueError("HTTP endpoint URLs must target a literal loopback host")
    if not is_loopback:
        _reject_non_public_ip_address(normalized_host)


def _is_loopback_host(host: str) -> bool:
    """Return whether a hostname is the literal loopback name or address.

    Args:
        host: Normalized hostname parsed from an endpoint URL.

    Returns:
        True when the host is ``localhost`` or a loopback IP address.
    """
    if host == _LOOPBACK_HOSTNAME:
        return True

    try:
        return ipaddress.ip_address(host).is_loopback
    except ValueError:
        return False


def _reject_non_public_ip_address(host: str) -> None:
    """Reject non-loopback IP literals that are not globally routable.

    Args:
        host: Normalized hostname parsed from an endpoint URL.

    Raises:
        ValueError: If an IP literal is private, link-local, or otherwise
            not globally routable.
    """
    try:
        address = ipaddress.ip_address(host)
    except ValueError:
        return

    if not address.is_global:
        raise ValueError("Endpoint URL must not target a private or link-local host")

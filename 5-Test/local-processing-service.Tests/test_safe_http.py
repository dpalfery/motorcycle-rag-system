"""Black-box contracts for safe outbound HTTP endpoint handling."""

from __future__ import annotations

import httpx
import pytest

import security.safe_http as safe_http
from security.safe_http import (
    EndpointPolicy,
    EndpointPolicyError,
    RedirectBlockedError,
    UnsafeResolvedAddressError,
    create_api_https_async_client,
    create_model_discovery_async_client,
    create_public_https_async_client,
    require_loopback_addresses,
    require_non_redirect_success,
    require_public_addresses,
    validate_api_base_url,
    validate_model_discovery_endpoint,
)


class _AsyncResolver:
    def __init__(self, addresses: tuple[str, ...]) -> None:
        self._addresses = addresses
        self.calls: list[tuple[str, int]] = []

    async def resolve(self, host: str, port: int) -> tuple[str, ...]:
        self.calls.append((host, port))
        return self._addresses


@pytest.mark.parametrize(
    "raw_url",
    [
        "http://localhost:7215",
        "http://127.0.0.1:7215",
        "http://[::1]:7215",
        "http://api.example.test",
    ],
    ids=["localhost", "ipv4-loopback", "ipv6-loopback", "public-host"],
)
def test_validate_api_base_url_when_http_is_used_rejects_every_authority(
    raw_url: str,
) -> None:
    """The API client never permits cleartext, including its local development API."""
    with pytest.raises(EndpointPolicyError):
        validate_api_base_url(raw_url)


def test_validate_api_base_url_when_https_is_used_returns_normalized_url() -> None:
    result = validate_api_base_url("https://api.example.test:8443/base/")

    assert result == httpx.URL("https://api.example.test:8443/base")


def test_validate_api_base_url_when_https_targets_localhost_rejects() -> None:
    with pytest.raises(EndpointPolicyError):
        validate_api_base_url("https://localhost:7215")


@pytest.mark.parametrize(
    ("raw_url", "expected_policy"),
    [
        ("http://localhost:5272", EndpointPolicy.LOOPBACK_HTTP),
        ("http://127.0.0.1:5272", EndpointPolicy.LOOPBACK_HTTP),
        ("http://[::1]:5272", EndpointPolicy.LOOPBACK_HTTP),
        ("https://models.example.test:443", EndpointPolicy.PUBLIC_HTTPS),
    ],
    ids=["localhost", "ipv4-loopback", "ipv6-loopback", "public-https"],
)
def test_validate_model_discovery_endpoint_selects_only_supported_policy(
    raw_url: str,
    expected_policy: EndpointPolicy,
) -> None:
    endpoint, policy = validate_model_discovery_endpoint(raw_url)

    assert endpoint == httpx.URL(raw_url)
    assert policy is expected_policy


@pytest.mark.parametrize(
    "raw_url",
    [
        "http://models.example.test:5272",
        "http://127.0.0.1.example.test:5272",
        "http://10.0.0.1:5272",
    ],
    ids=["remote-host", "loopback-lookalike", "private-ip"],
)
def test_validate_model_discovery_endpoint_when_http_is_not_literal_loopback_rejects(
    raw_url: str,
) -> None:
    with pytest.raises(EndpointPolicyError):
        validate_model_discovery_endpoint(raw_url)


@pytest.mark.parametrize(
    "addresses",
    [
        ("127.0.0.1",),
        ("10.0.0.4",),
        ("93.184.216.34", "10.0.0.4"),
        ("93.184.216.34", "::1"),
    ],
    ids=["loopback", "private", "mixed-private", "mixed-loopback"],
)
async def test_public_https_client_when_dns_has_any_non_public_address_rejects_before_connecting(
    addresses: tuple[str, ...],
) -> None:
    resolver = _AsyncResolver(addresses)

    async with create_public_https_async_client(resolver=resolver) as client:
        with pytest.raises(UnsafeResolvedAddressError):
            await client.get("https://api.example.test/status")

    assert resolver.calls == [("api.example.test", 443)]


@pytest.mark.parametrize(
    "addresses",
    [
        ("127.0.0.1",),
        ("10.0.0.4",),
        ("93.184.216.34", "127.0.0.1"),
    ],
    ids=["loopback", "private", "mixed-public-and-loopback"],
)
async def test_api_https_client_when_dns_has_non_public_or_mixed_answers_rejects_before_connecting(
    addresses: tuple[str, ...],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """T9: API HTTPS traffic must not use DNS answers outside public Internet space."""

    class ConnectionMustNotBeCreated:
        def __init__(self, **_: object) -> None:
            raise AssertionError(
                "unsafe resolved address must be blocked before connecting"
            )

    monkeypatch.setattr(
        safe_http.httpcore,
        "AsyncConnectionPool",
        ConnectionMustNotBeCreated,
    )
    resolver = _AsyncResolver(addresses)

    async with create_api_https_async_client(resolver=resolver) as client:
        with pytest.raises(UnsafeResolvedAddressError):
            await client.get("https://api.example.test:8443/status")

    assert resolver.calls == [("api.example.test", 8443)]


async def test_api_https_client_uses_numeric_tcp_authority_and_preserves_original_host_and_sni(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """The verified endpoint name must survive numeric dialing at the transport boundary."""

    captured_requests = []

    async def response_body():
        yield b"{}"

    class RecordingAsyncConnectionPool:
        def __init__(self, **_: object) -> None:
            pass

        async def handle_async_request(self, request: object) -> object:
            captured_requests.append(request)
            return safe_http.httpcore.Response(200, content=response_body())

        async def aclose(self) -> None:
            pass

    monkeypatch.setattr(
        safe_http.httpcore,
        "AsyncConnectionPool",
        RecordingAsyncConnectionPool,
    )
    resolver = _AsyncResolver(("93.184.216.34",))

    async with create_api_https_async_client(resolver=resolver) as client:
        response = await client.get("https://api.example.test:8443/status")

    core_request = captured_requests[0]

    assert response.status_code == 200
    assert resolver.calls == [("api.example.test", 8443)]
    assert core_request.url.host == b"93.184.216.34"
    assert core_request.url.port == 8443
    assert [value for key, value in core_request.headers if key.lower() == b"host"] == [
        b"api.example.test:8443"
    ]
    assert core_request.extensions["sni_hostname"] == "api.example.test"


async def test_api_https_client_when_request_has_params_preserves_them_for_transport(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Request query parameters are safe after the configured base URL is validated."""

    captured_requests = []

    async def response_body():
        yield b"{}"

    class RecordingAsyncConnectionPool:
        def __init__(self, **_: object) -> None:
            pass

        async def handle_async_request(self, request: object) -> object:
            captured_requests.append(request)
            return safe_http.httpcore.Response(200, content=response_body())

        async def aclose(self) -> None:
            pass

    monkeypatch.setattr(
        safe_http.httpcore,
        "AsyncConnectionPool",
        RecordingAsyncConnectionPool,
    )
    resolver = _AsyncResolver(("93.184.216.34",))

    async with create_api_https_async_client(resolver=resolver) as client:
        response = await client.get(
            "https://api.example.test/status",
            params={"uploadId": "upload-1", "documentType": "manual-pdf"},
        )

    core_request = captured_requests[0]

    assert response.status_code == 200
    assert resolver.calls == [("api.example.test", 443)]
    assert core_request.url.target == (
        b"/status?uploadId=upload-1&documentType=manual-pdf"
    )


async def test_model_http_client_when_localhost_resolves_off_loopback_rejects_before_connecting() -> None:
    endpoint, policy = validate_model_discovery_endpoint("http://localhost:5272")
    resolver = _AsyncResolver(("93.184.216.34",))

    async with create_model_discovery_async_client(
        policy,
        resolver=resolver,
    ) as client:
        with pytest.raises(UnsafeResolvedAddressError):
            await client.get(endpoint)

    assert resolver.calls == [("localhost", 5272)]


def test_require_public_addresses_when_dns_results_are_public_returns_numeric_dial_targets() -> (
    None
):
    result = require_public_addresses(
        "api.example.test",
        ("93.184.216.34", "2606:2800:220:1:248:1893:25c8:1946"),
    )

    assert result == ("93.184.216.34", "2606:2800:220:1:248:1893:25c8:1946")


def test_require_loopback_addresses_when_all_dns_results_are_loopback_returns_numeric_dial_targets() -> (
    None
):
    result = require_loopback_addresses("localhost", ("127.0.0.1", "::1"))

    assert result == ("127.0.0.1", "::1")


def test_require_non_redirect_success_when_response_is_redirect_fails_without_following_location() -> (
    None
):
    request = httpx.Request("GET", "https://api.example.test/first")
    response = httpx.Response(
        302,
        headers={"Location": "https://169.254.169.254/latest/meta-data"},
        request=request,
    )

    with pytest.raises(RedirectBlockedError):
        require_non_redirect_success(response)

    assert response.request.url == request.url

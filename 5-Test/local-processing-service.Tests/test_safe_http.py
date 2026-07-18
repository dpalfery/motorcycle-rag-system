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


def test_require_non_redirect_success_when_response_is_ok_returns_response() -> None:
    request = httpx.Request("GET", "https://api.example.test/ok")
    response = httpx.Response(200, request=request, content=b"{}")

    assert require_non_redirect_success(response) is response


def test_require_non_redirect_success_when_response_is_http_error_raises() -> None:
    request = httpx.Request("GET", "https://api.example.test/fail")
    response = httpx.Response(500, request=request, content=b"err")

    with pytest.raises(httpx.HTTPStatusError):
        require_non_redirect_success(response)


def test_redirect_blocked_error_when_response_omitted_synthesizes_response() -> None:
    request = httpx.Request("GET", "https://api.example.test/redirect")

    error = RedirectBlockedError("blocked", request=request)

    assert error.response.status_code == 300
    assert error.request is request


def test_require_public_addresses_when_dns_returns_no_addresses_rejects() -> None:
    with pytest.raises(UnsafeResolvedAddressError, match="no addresses"):
        require_public_addresses("api.example.test", ())


def test_require_public_addresses_when_dns_returns_non_numeric_address_rejects() -> None:
    with pytest.raises(UnsafeResolvedAddressError, match="non-numeric"):
        require_public_addresses("api.example.test", ("not-an-ip",))


def test_validate_api_base_url_when_https_targets_private_ip_rejects() -> None:
    with pytest.raises(EndpointPolicyError, match="private or link-local"):
        validate_api_base_url("https://10.0.0.8/api")


def test_validate_model_discovery_endpoint_when_scheme_is_unsupported_rejects() -> None:
    with pytest.raises(EndpointPolicyError):
        validate_model_discovery_endpoint("ftp://localhost:5272")


def test_addresses_from_records_skips_non_tuple_sockaddrs_and_deduplicates() -> None:
    records = [
        (None, None, None, None, ("93.184.216.34", 443)),
        (None, None, None, None, "not-a-tuple"),
        (None, None, None, None, ()),
        (None, None, None, None, ("93.184.216.34", 443)),
        (None, None, None, None, ("2606:2800:220:1:248:1893:25c8:1946", 443, 0, 0)),
    ]

    assert safe_http._addresses_from_records(records) == (
        "93.184.216.34",
        "2606:2800:220:1:248:1893:25c8:1946",
    )


async def test_system_async_address_resolver_maps_getaddrinfo_records(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    async def fake_getaddrinfo(*_args: object, **_kwargs: object):
        return [
            (None, None, None, None, ("127.0.0.1", 80)),
            (None, None, None, None, ("::1", 80, 0, 0)),
        ]

    class FakeLoop:
        async def getaddrinfo(self, *args: object, **kwargs: object):
            return await fake_getaddrinfo(*args, **kwargs)

    monkeypatch.setattr(safe_http.asyncio, "get_running_loop", lambda: FakeLoop())
    resolver = safe_http._SystemAsyncAddressResolver()

    assert await resolver.resolve("localhost", 80) == ("127.0.0.1", "::1")


def test_system_address_resolver_maps_getaddrinfo_records(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        safe_http.socket,
        "getaddrinfo",
        lambda *_args, **_kwargs: [
            (None, None, None, None, ("127.0.0.1", 80)),
        ],
    )
    resolver = safe_http._SystemAddressResolver()

    assert resolver.resolve("localhost", 80) == ("127.0.0.1",)


async def test_public_https_client_when_connection_pool_raises_closes_pool(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    closed: list[bool] = []

    class FailingAsyncConnectionPool:
        def __init__(self, **_: object) -> None:
            pass

        async def handle_async_request(self, request: object) -> object:
            raise RuntimeError("dial failed")

        async def aclose(self) -> None:
            closed.append(True)

    monkeypatch.setattr(
        safe_http.httpcore,
        "AsyncConnectionPool",
        FailingAsyncConnectionPool,
    )
    resolver = _AsyncResolver(("93.184.216.34",))

    async with create_public_https_async_client(resolver=resolver) as client:
        with pytest.raises(RuntimeError, match="dial failed"):
            await client.get("https://api.example.test/status")

    assert closed == [True]


async def test_public_https_client_when_httpcore_returns_sync_stream_rejects(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class SyncOnlyStream:
        def __iter__(self):
            yield b"{}"

        def close(self) -> None:
            pass

    class SyncStreamAsyncConnectionPool:
        def __init__(self, **_: object) -> None:
            pass

        async def handle_async_request(self, request: object) -> object:
            return safe_http.httpcore.Response(200, content=SyncOnlyStream())

        async def aclose(self) -> None:
            pass

    monkeypatch.setattr(
        safe_http.httpcore,
        "AsyncConnectionPool",
        SyncStreamAsyncConnectionPool,
    )
    # httpcore.Response(content=...) may wrap differently; force a sync stream shape.
    original_response = safe_http.httpcore.Response

    class ResponseWithSyncStream(original_response):
        def __init__(self, *args: object, **kwargs: object) -> None:
            super().__init__(*args, **kwargs)
            object.__setattr__(self, "stream", SyncOnlyStream())

    monkeypatch.setattr(safe_http.httpcore, "Response", ResponseWithSyncStream)
    resolver = _AsyncResolver(("93.184.216.34",))

    async with create_public_https_async_client(resolver=resolver) as client:
        with pytest.raises(TypeError, match="synchronous stream"):
            await client.get("https://api.example.test/status")


def test_model_discovery_sync_client_dials_numeric_authority_and_preserves_host(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    captured_requests: list[object] = []

    class RecordingConnectionPool:
        def __init__(self, **_: object) -> None:
            pass

        def handle_request(self, request: object) -> object:
            captured_requests.append(request)
            return safe_http.httpcore.Response(200, content=[b"{}"])

        def close(self) -> None:
            pass

    class SyncResolver:
        def __init__(self) -> None:
            self.calls: list[tuple[str, int]] = []

        def resolve(self, host: str, port: int) -> tuple[str, ...]:
            self.calls.append((host, port))
            return ("127.0.0.1",)

    monkeypatch.setattr(
        safe_http.httpcore,
        "ConnectionPool",
        RecordingConnectionPool,
    )
    resolver = SyncResolver()

    with safe_http.create_model_discovery_client(
        EndpointPolicy.LOOPBACK_HTTP,
        resolver=resolver,
    ) as client:
        response = client.get("http://localhost:5272/v1/models")

    core_request = captured_requests[0]
    assert response.status_code == 200
    assert resolver.calls == [("localhost", 5272)]
    assert core_request.url.host == b"127.0.0.1"
    assert [value for key, value in core_request.headers if key.lower() == b"host"] == [
        b"localhost:5272"
    ]


def test_model_discovery_sync_client_when_connection_pool_raises_closes_pool(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    closed: list[bool] = []

    class FailingConnectionPool:
        def __init__(self, **_: object) -> None:
            pass

        def handle_request(self, request: object) -> object:
            raise RuntimeError("sync dial failed")

        def close(self) -> None:
            closed.append(True)

    class SyncResolver:
        def resolve(self, host: str, port: int) -> tuple[str, ...]:
            return ("127.0.0.1",)

    monkeypatch.setattr(safe_http.httpcore, "ConnectionPool", FailingConnectionPool)

    with safe_http.create_model_discovery_client(
        EndpointPolicy.LOOPBACK_HTTP,
        resolver=SyncResolver(),
    ) as client:
        with pytest.raises(RuntimeError, match="sync dial failed"):
            client.get("http://127.0.0.1:5272/v1/models")

    assert closed == [True]


def test_model_discovery_sync_client_when_httpcore_returns_async_stream_rejects(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class AsyncOnlyStream:
        def __aiter__(self):
            return self

        async def __anext__(self):
            raise StopAsyncIteration

        async def aclose(self) -> None:
            pass

    class AsyncStreamConnectionPool:
        def __init__(self, **_: object) -> None:
            pass

        def handle_request(self, request: object) -> object:
            response = safe_http.httpcore.Response(200, content=[b"{}"])
            object.__setattr__(response, "stream", AsyncOnlyStream())
            return response

        def close(self) -> None:
            pass

    class SyncResolver:
        def resolve(self, host: str, port: int) -> tuple[str, ...]:
            return ("127.0.0.1",)

    monkeypatch.setattr(
        safe_http.httpcore,
        "ConnectionPool",
        AsyncStreamConnectionPool,
    )

    with safe_http.create_model_discovery_client(
        EndpointPolicy.LOOPBACK_HTTP,
        resolver=SyncResolver(),
    ) as client:
        with pytest.raises(TypeError, match="async stream"):
            client.get("http://localhost:5272/v1/models")


async def test_loopback_policy_rejects_non_loopback_request_url() -> None:
    resolver = _AsyncResolver(("127.0.0.1",))

    async with create_model_discovery_async_client(
        EndpointPolicy.LOOPBACK_HTTP,
        resolver=resolver,
    ) as client:
        with pytest.raises(EndpointPolicyError, match="does not match"):
            await client.get("https://api.example.test/models")


async def test_public_https_request_policy_rejects_http_scheme() -> None:
    resolver = _AsyncResolver(("93.184.216.34",))

    async with create_public_https_async_client(resolver=resolver) as client:
        with pytest.raises(EndpointPolicyError, match="must use HTTPS"):
            await client.get("http://api.example.test/status")


async def test_public_https_request_policy_rejects_credentials_in_url() -> None:
    resolver = _AsyncResolver(("93.184.216.34",))

    async with create_public_https_async_client(resolver=resolver) as client:
        with pytest.raises(EndpointPolicyError, match="credentials"):
            await client.get("https://user:pass@api.example.test/status")


async def test_public_https_request_policy_rejects_fragment_in_url() -> None:
    resolver = _AsyncResolver(("93.184.216.34",))

    async with create_public_https_async_client(resolver=resolver) as client:
        with pytest.raises(EndpointPolicyError, match="fragment"):
            await client.get("https://api.example.test/status#section")


async def test_http_core_async_byte_stream_closes_underlying_stream_and_pool() -> None:
    closed: list[str] = []

    class ClosableStream:
        def __aiter__(self):
            return self

        async def __anext__(self):
            raise StopAsyncIteration

        async def aclose(self) -> None:
            closed.append("stream")

    class ClosablePool:
        async def aclose(self) -> None:
            closed.append("pool")

    stream = safe_http._HttpCoreAsyncByteStream(ClosableStream(), ClosablePool())
    assert [chunk async for chunk in stream] == []
    await stream.aclose()
    assert closed == ["stream", "pool"]


def test_http_core_byte_stream_closes_underlying_stream_and_pool() -> None:
    closed: list[str] = []

    class ClosableStream:
        def __iter__(self):
            yield b"a"
            return
            yield  # pragma: no cover

        def close(self) -> None:
            closed.append("stream")

    class ClosablePool:
        def close(self) -> None:
            closed.append("pool")

    stream = safe_http._HttpCoreByteStream(ClosableStream(), ClosablePool())
    assert list(stream) == [b"a"]
    stream.close()
    assert closed == ["stream", "pool"]

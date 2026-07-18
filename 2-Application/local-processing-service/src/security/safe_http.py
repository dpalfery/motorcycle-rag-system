"""Safe, policy-bound HTTP transports for local processor outbound calls."""

from __future__ import annotations

import asyncio
import ipaddress
import socket
from collections.abc import AsyncIterable, AsyncIterator, Iterable, Iterator, Sequence
from enum import Enum
from typing import Protocol, cast

import httpcore
import httpx

from security.url_validation import validate_endpoint_url


class EndpointPolicy(Enum):
    """The allowed outbound endpoint classes."""

    API_HTTPS = "api_https"
    LOOPBACK_HTTP = "loopback_http"
    PUBLIC_HTTPS = "public_https"


class EndpointPolicyError(ValueError):
    """Raised when an endpoint conflicts with its outbound-HTTP policy."""


class UnsafeResolvedAddressError(EndpointPolicyError):
    """Raised when DNS produces an address outside the selected policy."""


class RedirectBlockedError(httpx.HTTPStatusError):
    """Raised when a response attempts to redirect an outbound request."""

    def __init__(
        self,
        message: str,
        *,
        request: httpx.Request,
        response: httpx.Response | None = None,
    ) -> None:
        """Initialize the typed redirect error."""
        safe_response = response or httpx.Response(300, request=request)
        super().__init__(message, request=request, response=safe_response)


class AsyncAddressResolver(Protocol):
    """Resolve a hostname to all candidate numeric addresses."""

    async def resolve(self, host: str, port: int) -> tuple[str, ...]:
        """Resolve ``host`` and return every numeric address."""


class AddressResolver(Protocol):
    """Synchronously resolve a hostname to all candidate numeric addresses."""

    def resolve(self, host: str, port: int) -> tuple[str, ...]:
        """Resolve ``host`` and return every numeric address."""


class _SystemAsyncAddressResolver:
    async def resolve(self, host: str, port: int) -> tuple[str, ...]:
        loop = asyncio.get_running_loop()
        records = await loop.getaddrinfo(
            host,
            port,
            family=socket.AF_UNSPEC,
            type=socket.SOCK_STREAM,
        )
        return _addresses_from_records(records)


class _SystemAddressResolver:
    def resolve(self, host: str, port: int) -> tuple[str, ...]:
        records = socket.getaddrinfo(
            host,
            port,
            family=socket.AF_UNSPEC,
            type=socket.SOCK_STREAM,
        )
        return _addresses_from_records(records)


def _addresses_from_records(
    records: Sequence[tuple[object, ...]],
) -> tuple[str, ...]:
    addresses: list[str] = []
    for record in records:
        sockaddr = record[4]
        if not isinstance(sockaddr, tuple) or not sockaddr:
            continue
        address = str(sockaddr[0])
        if address not in addresses:
            addresses.append(address)
    return tuple(addresses)


def require_public_addresses(host: str, addresses: tuple[str, ...]) -> tuple[str, ...]:
    """Require that all DNS answers are globally routable numeric addresses."""
    return _require_addresses(host, addresses, require_loopback=False)


def require_loopback_addresses(
    host: str,
    addresses: tuple[str, ...],
) -> tuple[str, ...]:
    """Require that all DNS answers are numeric loopback addresses."""
    return _require_addresses(host, addresses, require_loopback=True)


def _require_addresses(
    host: str,
    addresses: tuple[str, ...],
    *,
    require_loopback: bool,
) -> tuple[str, ...]:
    if not addresses:
        raise UnsafeResolvedAddressError(
            f"DNS resolution for {host!r} returned no addresses"
        )

    validated: list[str] = []
    for value in addresses:
        try:
            address = ipaddress.ip_address(value)
        except ValueError as exc:
            raise UnsafeResolvedAddressError(
                f"DNS resolution for {host!r} returned a non-numeric address"
            ) from exc

        allowed = address.is_loopback if require_loopback else address.is_global
        if not allowed:
            required_kind = "loopback" if require_loopback else "public"
            raise UnsafeResolvedAddressError(
                f"DNS resolution for {host!r} included a non-{required_kind} address"
            )
        validated.append(str(address))
    return tuple(validated)


def validate_api_base_url(raw_url: str) -> httpx.URL:
    """Validate and normalize an HTTPS-only MotorcycleRAG API base URL."""
    url = _parse_endpoint_url(raw_url)
    _validate_public_https_url(url, "API endpoint URL")
    return url.copy_with(path=url.path.rstrip("/"))


def validate_model_discovery_endpoint(
    raw_url: str,
) -> tuple[httpx.URL, EndpointPolicy]:
    """Validate a model endpoint and select its allowed request policy."""
    url = _parse_endpoint_url(raw_url)
    if url.scheme == "https":
        _reject_non_public_ip_literal(url)
        return url, EndpointPolicy.PUBLIC_HTTPS
    if url.scheme == "http" and _is_literal_loopback_host(url.host):
        return url, EndpointPolicy.LOOPBACK_HTTP
    raise EndpointPolicyError(
        "HTTP model-discovery endpoint URLs must target a literal loopback host"
    )


def _parse_endpoint_url(raw_url: str) -> httpx.URL:
    """Parse a configured endpoint after shared structural URL validation.

    Delegates scheme/host/credential/query/fragment policy to
    ``validate_endpoint_url`` so ``url_validation`` remains the single
    structural authority. Callers apply transport-specific policy afterward
    (for example public-HTTPS-only API bases).
    """
    try:
        normalized = validate_endpoint_url(raw_url)
    except ValueError as exc:
        raise EndpointPolicyError(str(exc)) from exc
    try:
        return httpx.URL(normalized)
    except (httpx.InvalidURL, ValueError) as exc:
        raise EndpointPolicyError("Endpoint URL has a malformed authority") from exc


def _is_literal_loopback_host(host: str | None) -> bool:
    if host is None:
        return False
    if host.lower() == "localhost":
        return True
    try:
        return ipaddress.ip_address(host).is_loopback
    except ValueError:
        return False


def _reject_non_public_ip_literal(url: httpx.URL) -> None:
    assert url.host is not None
    if url.host.lower() == "localhost":
        raise EndpointPolicyError("Endpoint URL must not target localhost")
    try:
        address = ipaddress.ip_address(url.host)
    except ValueError:
        return
    if not address.is_global:
        raise EndpointPolicyError(
            "Endpoint URL must not target a private or link-local host"
        )


def require_non_redirect_success(response: httpx.Response) -> httpx.Response:
    """Return success responses and block redirects without following them."""
    if 300 <= response.status_code < 400:
        raise RedirectBlockedError(
            "Redirects are not allowed for outbound HTTP requests",
            request=response.request,
            response=response,
        )
    response.raise_for_status()
    return response


class _HttpCoreAsyncByteStream(httpx.AsyncByteStream):
    def __init__(
        self,
        stream: AsyncIterable[bytes],
        pool: httpcore.AsyncConnectionPool,
    ) -> None:
        self._stream = stream
        self._pool = pool

    async def __aiter__(self) -> AsyncIterator[bytes]:
        async for chunk in self._stream:
            yield chunk

    async def aclose(self) -> None:
        if hasattr(self._stream, "aclose"):
            await self._stream.aclose()
        await self._pool.aclose()


class _HttpCoreByteStream(httpx.SyncByteStream):
    def __init__(self, stream: Iterable[bytes], pool: httpcore.ConnectionPool) -> None:
        self._stream = stream
        self._pool = pool

    def __iter__(self) -> Iterator[bytes]:
        yield from self._stream

    def close(self) -> None:
        if hasattr(self._stream, "close"):
            self._stream.close()
        self._pool.close()


class _SafeAsyncTransport(httpx.AsyncBaseTransport):
    def __init__(self, policy: EndpointPolicy, resolver: AsyncAddressResolver) -> None:
        self._policy = policy
        self._resolver = resolver

    async def handle_async_request(self, request: httpx.Request) -> httpx.Response:
        _validate_request_policy(request.url, self._policy)
        original_host = request.url.host
        assert original_host is not None
        port = request.url.port or (443 if request.url.scheme == "https" else 80)
        addresses = await self._resolver.resolve(original_host, port)
        dial_targets = _validated_targets(self._policy, original_host, addresses)
        return await _send_async_request(request, dial_targets[0], original_host)


class _SafeTransport(httpx.BaseTransport):
    def __init__(self, policy: EndpointPolicy, resolver: AddressResolver) -> None:
        self._policy = policy
        self._resolver = resolver

    def handle_request(self, request: httpx.Request) -> httpx.Response:
        _validate_request_policy(request.url, self._policy)
        original_host = request.url.host
        assert original_host is not None
        port = request.url.port or (443 if request.url.scheme == "https" else 80)
        addresses = self._resolver.resolve(original_host, port)
        dial_targets = _validated_targets(self._policy, original_host, addresses)
        return _send_request(request, dial_targets[0], original_host)


def _validate_request_policy(url: httpx.URL, policy: EndpointPolicy) -> None:
    if policy in {EndpointPolicy.API_HTTPS, EndpointPolicy.PUBLIC_HTTPS}:
        _validate_public_https_url(url, "Outbound request URL")
        return
    if url.scheme != "http" or not _is_literal_loopback_host(url.host):
        raise EndpointPolicyError("Outbound request does not match its endpoint policy")


def _validate_public_https_url(url: httpx.URL, label: str) -> None:
    """Validate a public HTTPS URL without restricting request query parameters."""
    if url.scheme != "https":
        raise EndpointPolicyError(f"{label} must use HTTPS")
    if not url.host:
        raise EndpointPolicyError(f"{label} must include a host authority")
    if url.username or url.password:
        raise EndpointPolicyError(f"{label} must not include credentials")
    if url.fragment:
        raise EndpointPolicyError(f"{label} must not include a fragment")
    if url.port == 0:
        raise EndpointPolicyError(f"{label} port must be between 1 and 65535")
    _reject_non_public_ip_literal(url)


def _validated_targets(
    policy: EndpointPolicy,
    host: str,
    addresses: tuple[str, ...],
) -> tuple[str, ...]:
    if policy is EndpointPolicy.PUBLIC_HTTPS:
        return require_public_addresses(host, addresses)
    if policy is EndpointPolicy.API_HTTPS:
        return require_public_addresses(host, addresses)
    return require_loopback_addresses(host, addresses)


def _dial_url(request: httpx.Request, target: str) -> httpx.URL:
    return request.url.copy_with(host=target)


def _host_header(request: httpx.Request) -> str:
    return request.headers.get("host") or request.url.netloc.decode("ascii")


def _core_headers(request: httpx.Request) -> list[tuple[bytes, bytes]]:
    headers = [
        (key, value) for key, value in request.headers.raw if key.lower() != b"host"
    ]
    headers.append((b"host", _host_header(request).encode("ascii")))
    return headers


async def _send_async_request(
    request: httpx.Request,
    target: str,
    original_host: str,
) -> httpx.Response:
    pool = httpcore.AsyncConnectionPool(http2=False)
    core_request = httpcore.Request(
        method=request.method,
        url=str(_dial_url(request, target)),
        headers=_core_headers(request),
        content=request.stream,
        extensions={**request.extensions, "sni_hostname": original_host},
    )
    try:
        core_response = await pool.handle_async_request(core_request)
    except BaseException:
        await pool.aclose()
        raise
    stream = core_response.stream
    if not hasattr(stream, "__aiter__"):
        await pool.aclose()
        raise TypeError("httpcore returned a synchronous stream to an async transport")
    return httpx.Response(
        status_code=core_response.status,
        headers=core_response.headers,
        stream=_HttpCoreAsyncByteStream(cast(AsyncIterable[bytes], stream), pool),
        extensions=core_response.extensions,
        request=request,
    )


def _send_request(
    request: httpx.Request,
    target: str,
    original_host: str,
) -> httpx.Response:
    pool = httpcore.ConnectionPool(http2=False)
    core_request = httpcore.Request(
        method=request.method,
        url=str(_dial_url(request, target)),
        headers=_core_headers(request),
        content=request.stream,
        extensions={**request.extensions, "sni_hostname": original_host},
    )
    try:
        core_response = pool.handle_request(core_request)
    except BaseException:
        pool.close()
        raise
    stream = core_response.stream
    if not hasattr(stream, "__iter__"):
        pool.close()
        raise TypeError("httpcore returned an async stream to a sync transport")
    return httpx.Response(
        status_code=core_response.status,
        headers=core_response.headers,
        stream=_HttpCoreByteStream(cast(Iterable[bytes], stream), pool),
        extensions=core_response.extensions,
        request=request,
    )


def create_public_https_async_client(
    *,
    timeout: httpx.Timeout | float = httpx.Timeout(30.0),
    resolver: AsyncAddressResolver | None = None,
) -> httpx.AsyncClient:
    """Create a no-redirect public-HTTPS client backed by numeric dialing."""
    return httpx.AsyncClient(
        timeout=timeout,
        verify=True,
        follow_redirects=False,
        transport=_SafeAsyncTransport(
            EndpointPolicy.PUBLIC_HTTPS,
            resolver or _SystemAsyncAddressResolver(),
        ),
    )


def create_api_https_async_client(
    *,
    timeout: httpx.Timeout | float = httpx.Timeout(30.0),
    resolver: AsyncAddressResolver | None = None,
) -> httpx.AsyncClient:
    """Create an HTTPS API client for public API endpoints.

    Every DNS answer must be globally routable. Loopback, private, and mixed
    answers are rejected before any connection is attempted.

    Args:
        timeout: Request timeout applied by HTTPX.
        resolver: Optional resolver injection for deterministic validation.

    Returns:
        A no-redirect asynchronous client that preserves TLS SNI while
        connecting to a validated numeric address.
    """
    return httpx.AsyncClient(
        timeout=timeout,
        verify=True,
        follow_redirects=False,
        transport=_SafeAsyncTransport(
            EndpointPolicy.API_HTTPS,
            resolver or _SystemAsyncAddressResolver(),
        ),
    )


def create_model_discovery_async_client(
    policy: EndpointPolicy,
    *,
    timeout: httpx.Timeout | float = httpx.Timeout(10.0),
    resolver: AsyncAddressResolver | None = None,
) -> httpx.AsyncClient:
    """Create a no-redirect model-discovery client for a selected policy."""
    return httpx.AsyncClient(
        timeout=timeout,
        verify=True,
        follow_redirects=False,
        transport=_SafeAsyncTransport(
            policy,
            resolver or _SystemAsyncAddressResolver(),
        ),
    )


def create_model_discovery_client(
    policy: EndpointPolicy,
    *,
    timeout: httpx.Timeout | float = httpx.Timeout(10.0),
    resolver: AddressResolver | None = None,
) -> httpx.Client:
    """Create a synchronous safe model-discovery client."""
    return httpx.Client(
        timeout=timeout,
        verify=True,
        follow_redirects=False,
        transport=_SafeTransport(policy, resolver or _SystemAddressResolver()),
    )

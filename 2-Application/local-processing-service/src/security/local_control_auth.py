"""Authentication for the local processor HTTP control surface."""

import os
import secrets
from typing import Annotated

from fastapi import Header, HTTPException

LOCAL_PROCESSOR_CONTROL_TOKEN_ENV = "MCR_LOCAL_PROCESSOR_CONTROL_TOKEN"


async def require_local_control_token(
    authorization: Annotated[str | None, Header()] = None,
) -> None:
    """Require the configured bearer token for local processor HTTP routes.

    Args:
        authorization: The request Authorization header.

    Raises:
        HTTPException: If the runtime token is absent or the bearer token is
            missing, malformed, or invalid.
    """
    expected_token = os.getenv(LOCAL_PROCESSOR_CONTROL_TOKEN_ENV)
    if not expected_token:
        raise HTTPException(
            status_code=503,
            detail="Local processor control token is not configured.",
        )

    scheme, separator, provided_token = (authorization or "").partition(" ")
    if scheme != "Bearer" or not separator or not provided_token:
        raise HTTPException(
            status_code=401,
            detail="A valid bearer token is required.",
        )

    if not secrets.compare_digest(provided_token, expected_token):
        raise HTTPException(
            status_code=401,
            detail="A valid bearer token is required.",
        )

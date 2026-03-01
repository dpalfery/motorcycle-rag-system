"""Unit tests for AzureSearchDirectUploader."""

import os
from types import SimpleNamespace
from unittest.mock import MagicMock, patch

import pytest

from search.azure_search_uploader import AzureSearchDirectUploader


def _make_results(count: int, succeeded: bool = True) -> list:
    """Build a list of mock IndexingResult-like objects."""
    return [SimpleNamespace(succeeded=succeeded) for _ in range(count)]


# ---------------------------------------------------------------------------
# Construction tests
# ---------------------------------------------------------------------------


class TestAzureSearchDirectUploaderInit:
    def test_disabled_when_no_endpoint(self, monkeypatch):
        """No AZURE_SEARCH_ENDPOINT → uploader disabled, no SearchClient created."""
        monkeypatch.delenv("AZURE_SEARCH_ENDPOINT", raising=False)
        monkeypatch.delenv("AZURE_SEARCH_KEY", raising=False)

        with patch("search.azure_search_uploader.SearchClient") as mock_client_cls:
            uploader = AzureSearchDirectUploader()

        assert uploader._enabled is False
        assert uploader._client is None
        mock_client_cls.assert_not_called()

    def test_enabled_with_api_key(self, monkeypatch):
        """Endpoint + key → AzureKeyCredential used, SearchClient created."""
        monkeypatch.setenv("AZURE_SEARCH_ENDPOINT", "https://test.search.windows.net")
        monkeypatch.setenv("AZURE_SEARCH_KEY", "my-admin-key")
        monkeypatch.setenv("AZURE_SEARCH_INDEX", "motorcycle-index")

        with (
            patch("search.azure_search_uploader.SearchClient") as mock_client_cls,
            patch("search.azure_search_uploader.AzureKeyCredential") as mock_key_cred,
            patch(
                "search.azure_search_uploader.DefaultAzureCredential"
            ) as mock_default_cred,
        ):
            uploader = AzureSearchDirectUploader()

        assert uploader._enabled is True
        mock_key_cred.assert_called_once_with("my-admin-key")
        mock_default_cred.assert_not_called()
        mock_client_cls.assert_called_once()

    def test_enabled_with_default_credential(self, monkeypatch):
        """Endpoint set, no key → DefaultAzureCredential used."""
        monkeypatch.setenv("AZURE_SEARCH_ENDPOINT", "https://test.search.windows.net")
        monkeypatch.delenv("AZURE_SEARCH_KEY", raising=False)

        with (
            patch("search.azure_search_uploader.SearchClient") as mock_client_cls,
            patch("search.azure_search_uploader.AzureKeyCredential") as mock_key_cred,
            patch(
                "search.azure_search_uploader.DefaultAzureCredential"
            ) as mock_default_cred,
        ):
            uploader = AzureSearchDirectUploader()

        assert uploader._enabled is True
        mock_default_cred.assert_called_once()
        mock_key_cred.assert_not_called()
        mock_client_cls.assert_called_once()


# ---------------------------------------------------------------------------
# Upload behaviour tests
# ---------------------------------------------------------------------------


class TestAzureSearchDirectUploaderUpload:
    def _make_uploader_with_mock_client(
        self, monkeypatch
    ) -> tuple[AzureSearchDirectUploader, MagicMock]:
        """Create an enabled uploader with a mocked SearchClient."""
        monkeypatch.setenv("AZURE_SEARCH_ENDPOINT", "https://test.search.windows.net")
        monkeypatch.setenv("AZURE_SEARCH_KEY", "key")

        mock_client = MagicMock()

        with (
            patch(
                "search.azure_search_uploader.SearchClient", return_value=mock_client
            ),
            patch("search.azure_search_uploader.AzureKeyCredential"),
        ):
            uploader = AzureSearchDirectUploader()

        return uploader, mock_client

    def test_upload_single_batch(self, monkeypatch):
        """5 docs → merge_or_upload_documents called exactly once with those 5."""
        uploader, mock_client = self._make_uploader_with_mock_client(monkeypatch)
        docs = [{"id": str(i)} for i in range(5)]
        mock_client.merge_or_upload_documents.return_value = _make_results(5)

        uploader.upload(docs)

        mock_client.merge_or_upload_documents.assert_called_once_with(documents=docs)

    def test_upload_batches_of_100(self, monkeypatch):
        """250 docs → merge_or_upload_documents called 3 times (100+100+50)."""
        uploader, mock_client = self._make_uploader_with_mock_client(monkeypatch)
        docs = [{"id": str(i)} for i in range(250)]

        def side_effect(documents):
            return _make_results(len(documents))

        mock_client.merge_or_upload_documents.side_effect = side_effect

        uploader.upload(docs)

        assert mock_client.merge_or_upload_documents.call_count == 3
        calls = mock_client.merge_or_upload_documents.call_args_list
        assert len(calls[0].kwargs["documents"]) == 100
        assert len(calls[1].kwargs["documents"]) == 100
        assert len(calls[2].kwargs["documents"]) == 50

    def test_upload_continues_on_batch_error(self, monkeypatch):
        """First batch raises, second batch succeeds → no exception propagated."""
        uploader, mock_client = self._make_uploader_with_mock_client(monkeypatch)
        docs = [{"id": str(i)} for i in range(150)]

        call_count = 0

        def side_effect(documents):
            nonlocal call_count
            call_count += 1
            if call_count == 1:
                raise Exception("Azure Search unavailable")
            return _make_results(len(documents))

        mock_client.merge_or_upload_documents.side_effect = side_effect

        # Should NOT raise even though first batch fails
        uploader.upload(docs)

        assert mock_client.merge_or_upload_documents.call_count == 2

    def test_upload_noop_when_disabled(self, monkeypatch):
        """Disabled uploader → upload() returns without calling any Azure method."""
        monkeypatch.delenv("AZURE_SEARCH_ENDPOINT", raising=False)

        with patch("search.azure_search_uploader.SearchClient") as mock_client_cls:
            uploader = AzureSearchDirectUploader()
            mock_client_cls.reset_mock()

        uploader.upload([{"id": "1"}, {"id": "2"}])

        # _client is None — no Azure calls at all
        assert uploader._client is None
        mock_client_cls.assert_not_called()

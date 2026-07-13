"""Unit tests for BlobWriter — Azure Storage SDK fully mocked."""

import json
from unittest.mock import MagicMock, patch, call

import pytest

# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


@pytest.fixture()
def mock_blob_service_client():
    """Return a fully-mocked BlobServiceClient."""
    client = MagicMock()
    container_client = MagicMock()
    blob_client = MagicMock()
    container_client.get_blob_client.return_value = blob_client
    client.get_container_client.return_value = container_client
    client.get_service_properties.return_value = {}
    return client


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


class TestBlobWriterInstantiation:
    @patch("storage.blob_writer.DefaultAzureCredential")
    @patch("storage.blob_writer.BlobServiceClient")
    def test_instantiates_with_account_url(self, MockBSC, MockCred):
        """Uses DefaultAzureCredential when AZURE_STORAGE_ACCOUNT_URL is set."""
        with patch.dict(
            "os.environ",
            {"AZURE_STORAGE_ACCOUNT_URL": "https://fake.blob.core.windows.net"},
            clear=False,
        ):
            # Remove connection string to ensure URL path is taken
            import os

            env = os.environ.copy()
            env.pop("AZURE_STORAGE_CONNECTION_STRING", None)
            with patch.dict("os.environ", env, clear=True):
                with patch.dict(
                    "os.environ",
                    {"AZURE_STORAGE_ACCOUNT_URL": "https://fake.blob.core.windows.net"},
                ):
                    from importlib import reload
                    import storage.blob_writer as bw_mod

                    reload(bw_mod)
                    writer = bw_mod.BlobWriter()
                    assert writer._client is not None

    @patch("storage.blob_writer.BlobServiceClient")
    def test_instantiates_with_no_config(self, MockBSC):
        """Client is None when no env vars are set — service still starts."""
        with patch.dict("os.environ", {}, clear=True):
            from importlib import reload
            import storage.blob_writer as bw_mod

            reload(bw_mod)
            writer = bw_mod.BlobWriter()
            assert writer._client is None


class TestUploadJSONL:
    @patch("storage.blob_writer.DefaultAzureCredential")
    @patch("storage.blob_writer.BlobServiceClient")
    async def test_writes_newline_delimited_json(self, MockBSC, MockCred):
        with patch.dict(
            "os.environ",
            {"AZURE_STORAGE_ACCOUNT_URL": "https://fake.blob.core.windows.net"},
            clear=True,
        ):
            from importlib import reload
            import storage.blob_writer as bw_mod

            reload(bw_mod)

            writer = bw_mod.BlobWriter()

            # Replace internal client with a fully-controlled mock
            mock_client = MagicMock()
            container_client = MagicMock()
            blob_client = MagicMock()
            container_client.get_blob_client.return_value = blob_client
            mock_client.get_container_client.return_value = container_client
            writer._client = mock_client

            records = [
                {"id": "1", "content": "row one"},
                {"id": "2", "content": "row two"},
            ]
            await writer.upload_jsonl("test-container", "path/data.jsonl", records)

            # Verify upload_blob was called
            blob_client.upload_blob.assert_called_once()
            uploaded_data = blob_client.upload_blob.call_args[0][0]
            lines = uploaded_data.decode("utf-8").split("\n")
            assert len(lines) == 2
            for line in lines:
                parsed = json.loads(line)
                assert "id" in parsed
                assert "content" in parsed


class TestUploadJSON:
    @patch("storage.blob_writer.DefaultAzureCredential")
    @patch("storage.blob_writer.BlobServiceClient")
    async def test_writes_valid_json(self, MockBSC, MockCred):
        with patch.dict(
            "os.environ",
            {"AZURE_STORAGE_ACCOUNT_URL": "https://fake.blob.core.windows.net"},
            clear=True,
        ):
            from importlib import reload
            import storage.blob_writer as bw_mod

            reload(bw_mod)

            writer = bw_mod.BlobWriter()

            mock_client = MagicMock()
            container_client = MagicMock()
            blob_client = MagicMock()
            container_client.get_blob_client.return_value = blob_client
            mock_client.get_container_client.return_value = container_client
            writer._client = mock_client

            data = {"nodes": [{"id": "1"}], "edges": []}
            await writer.upload_json("test-container", "path/entities.json", data)

            blob_client.upload_blob.assert_called_once()
            uploaded_data = blob_client.upload_blob.call_args[0][0]
            parsed = json.loads(uploaded_data.decode("utf-8"))
            assert parsed == data


class TestDownloadBlob:
    @patch("storage.blob_writer.DefaultAzureCredential")
    @patch("storage.blob_writer.BlobServiceClient")
    async def test_returns_bytes_from_mock(self, MockBSC, MockCred):
        with patch.dict(
            "os.environ",
            {"AZURE_STORAGE_ACCOUNT_URL": "https://fake.blob.core.windows.net"},
            clear=True,
        ):
            from importlib import reload
            import storage.blob_writer as bw_mod

            reload(bw_mod)

            writer = bw_mod.BlobWriter()

            expected = b"file content bytes"
            mock_client = MagicMock()
            container_client = MagicMock()
            blob_client = MagicMock()
            blob_client.download_blob.return_value.readall.return_value = expected
            container_client.get_blob_client.return_value = blob_client
            mock_client.get_container_client.return_value = container_client
            writer._client = mock_client

            result = await writer.download_blob("test-container", "path/file.pdf")
            assert result == expected


class TestIsConnected:
    def test_returns_true_when_client_responds(self):
        from storage.blob_writer import BlobWriter

        writer = BlobWriter.__new__(BlobWriter)
        mock_client = MagicMock()
        mock_client.get_service_properties.return_value = {}
        writer._client = mock_client

        assert writer.is_connected() is True

    def test_returns_true_when_client_is_set(self):
        from storage.blob_writer import BlobWriter

        writer = BlobWriter.__new__(BlobWriter)
        mock_client = MagicMock()
        mock_client.get_service_properties.side_effect = ConnectionError("no service")
        writer._client = mock_client

        assert writer.is_connected() is True

    def test_returns_false_when_no_client(self):
        from storage.blob_writer import BlobWriter

        writer = BlobWriter.__new__(BlobWriter)
        writer._client = None

        assert writer.is_connected() is False

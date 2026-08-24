"""Unit tests for GraphExtractor — OpenAI-compatible LLM calls fully mocked."""

import json
import logging
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

import pytest


class TestGraphExtractorInstantiation:
    def test_instantiates_without_error(self):
        from extraction.graph_extractor import GraphExtractor

        extractor = GraphExtractor()
        assert extractor._endpoint == "http://localhost:9999/v1"
        assert extractor._model == "test-model"

    def test_raises_value_error_without_endpoint(self, monkeypatch):
        from extraction.graph_extractor import GraphExtractor

        monkeypatch.delenv("GRAPH_EXTRACTION_ENDPOINT", raising=False)
        with pytest.raises(ValueError, match="GRAPH_EXTRACTION_ENDPOINT"):
            GraphExtractor()

    def test_raises_value_error_without_model(self, monkeypatch):
        from extraction.graph_extractor import GraphExtractor

        monkeypatch.delenv("GRAPH_EXTRACTION_MODEL", raising=False)
        with pytest.raises(ValueError, match="GRAPH_EXTRACTION_MODEL"):
            GraphExtractor()


class TestExtract:
    @patch("extraction.graph_extractor.openai.AsyncOpenAI")
    async def test_returns_list_with_nodes_edges_on_valid_json(self, MockOpenAI):
        from extraction.graph_extractor import GraphExtractor

        valid_json = json.dumps(
            {
                "nodes": [
                    {
                        "id": "1",
                        "name": "Oil Filter",
                        "type": "Component",
                        "description": "",
                    }
                ],
                "edges": [
                    {
                        "fromNodeId": "1",
                        "toNodeId": "2",
                        "relationshipType": "PART_OF",
                        "weight": 1.0,
                        "context": "",
                    }
                ],
            }
        )

        mock_client = MockOpenAI.return_value
        mock_choice = SimpleNamespace(message=SimpleNamespace(content=valid_json))
        mock_response = SimpleNamespace(choices=[mock_choice])
        mock_client.chat.completions.create = AsyncMock(return_value=mock_response)

        extractor = GraphExtractor()
        result = await extractor.extract(
            [("chunk-1", "Change the oil filter on the Honda CB500.")]
        )

        assert isinstance(result, list)
        assert len(result) == 1
        assert "nodes" in result[0]
        assert "edges" in result[0]
        assert len(result[0]["nodes"]) == 1
        assert "sourceDocumentId" in result[0]["nodes"][0]

    @patch("extraction.graph_extractor.openai.AsyncOpenAI")
    async def test_returns_sourceDocumentId_on_nodes(self, MockOpenAI):
        from extraction.graph_extractor import GraphExtractor

        valid_json = json.dumps(
            {
                "nodes": [
                    {
                        "id": "1",
                        "name": "Brake Pad",
                        "type": "Component",
                        "description": "",
                    }
                ],
                "edges": [],
            }
        )

        mock_client = MockOpenAI.return_value
        mock_choice = SimpleNamespace(message=SimpleNamespace(content=valid_json))
        mock_response = SimpleNamespace(choices=[mock_choice])
        mock_client.chat.completions.create = AsyncMock(return_value=mock_response)

        extractor = GraphExtractor()
        result = await extractor.extract(
            [("chunk-1", "Replace brake pads")], source_document_id="doc-123"
        )

        assert len(result) == 1
        assert result[0]["nodes"][0]["sourceDocumentId"] == "doc-123"

    @patch("extraction.graph_extractor.openai.AsyncOpenAI")
    async def test_returns_empty_list_on_malformed_json(self, MockOpenAI):
        from extraction.graph_extractor import GraphExtractor

        mock_client = MockOpenAI.return_value
        mock_choice = SimpleNamespace(
            message=SimpleNamespace(content="This is not JSON {{{")
        )
        mock_response = SimpleNamespace(choices=[mock_choice])
        mock_client.chat.completions.create = AsyncMock(return_value=mock_response)

        extractor = GraphExtractor()
        result = await extractor.extract([("chunk-1", "Some motorcycle text")])

        assert result == []

    @patch("extraction.graph_extractor.openai.AsyncOpenAI")
    async def test_returns_empty_list_on_connection_error(self, MockOpenAI):
        from extraction.graph_extractor import GraphExtractor

        mock_client = MockOpenAI.return_value
        mock_client.chat.completions.create = AsyncMock(
            side_effect=ConnectionError("API unreachable")
        )

        extractor = GraphExtractor()
        result = await extractor.extract([("chunk-1", "Some text about brakes")])

        assert result == []

    async def test_extract_rejects_bare_str_with_type_error(self, monkeypatch):
        """extract() contract narrowed to list[tuple[str, str]]: a bare str
        is a contract violation and must raise TypeError (plan T6 / D-d3)."""
        from extraction.graph_extractor import GraphExtractor

        async def mock_query_llm(*args, **kwargs):
            return {"nodes": [], "edges": []}

        monkeypatch.setattr(
            "extraction.graph_extractor._query_llm_with_retry", mock_query_llm
        )

        extractor = GraphExtractor()
        with pytest.raises(TypeError):
            await extractor.extract("some text")  # pyright: ignore[reportArgumentType]

    # ------------------------------------------------------------------
    # New chunk-list contract (2026-08-01-vector-graph-anchor-id-contract,
    # T1): extract() takes chunks: list[tuple[str, str]] — (chunk_id,
    # chunk_text) — instead of a single text: str. Batching must never
    # split a chunk's text across two LLM calls, and merged nodes must
    # carry a sourceChunkIds sorted union of the contributing chunk ids.
    # ------------------------------------------------------------------

    @patch("extraction.graph_extractor.openai.AsyncOpenAI")
    async def test_extract_chunks_batches_never_split_a_single_chunk(
        self, MockOpenAI
    ):
        """3 chunks whose combined size exceeds one token-budget batch: the
        mocked LLM client must receive >= 2 calls, and every chunk's full
        text must appear entirely within exactly one call's prompt — never
        split across two calls.
        """
        from extraction.graph_extractor import GraphExtractor

        # BATCH_TOKEN_BUDGET = 4000 tokens -> ~16000 chars per batch budget.
        # Each chunk (7000 chars) fits within a single batch on its own,
        # but three of them combined (21000 chars) exceed one batch.
        chunks: list[tuple[str, str]] = [
            ("chunk-1", "A" * 7000),
            ("chunk-2", "B" * 7000),
            ("chunk-3", "C" * 7000),
        ]

        captured_prompts: list[str] = []

        async def fake_create(*args, **kwargs):
            messages = kwargs.get("messages", [])
            user_content = next(
                (m["content"] for m in messages if m.get("role") == "user"), ""
            )
            captured_prompts.append(user_content)
            empty_json = json.dumps({"nodes": [], "edges": []})
            return SimpleNamespace(
                choices=[SimpleNamespace(message=SimpleNamespace(content=empty_json))]
            )

        mock_client = MockOpenAI.return_value
        mock_client.chat.completions.create = AsyncMock(side_effect=fake_create)

        extractor = GraphExtractor()
        await extractor.extract(chunks, source_document_id="doc-multi-chunk")

        assert len(captured_prompts) >= 2, (
            "expected the mocked LLM client to receive at least 2 calls for "
            "3 chunks exceeding one token-budget batch"
        )

        for chunk_id, chunk_text in chunks:
            containing_prompts = [p for p in captured_prompts if chunk_text in p]
            assert len(containing_prompts) == 1, (
                f"{chunk_id} text must appear entirely within exactly one "
                f"call's prompt, found in {len(containing_prompts)}"
            )

    async def test_extract_merges_same_name_node_with_sorted_union_source_chunk_ids(
        self, monkeypatch
    ):
        """Two batches (one per chunk) that both mention a node with the
        same lowercased name must merge into exactly one node whose
        sourceChunkIds is the sorted union of the contributing chunk ids.
        """
        from extraction.graph_extractor import GraphExtractor

        call_count = 0

        async def mock_query_llm(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            if call_count == 1:
                return {
                    "nodes": [{"id": "n1", "name": "Oil Filter", "type": "Component"}],
                    "edges": [],
                }
            return {
                "nodes": [{"id": "n2", "name": "oil filter", "type": "Component"}],
                "edges": [],
            }

        monkeypatch.setattr(
            "extraction.graph_extractor._query_llm_with_retry", mock_query_llm
        )

        # Each chunk (9000 chars) alone fits a batch, but two combined
        # (18000 chars) exceed the ~16000-char budget, so batching must
        # place them in two separate, whole-chunk batches.
        chunks: list[tuple[str, str]] = [
            ("chunk-a", "A" * 9000),
            ("chunk-b", "B" * 9000),
        ]

        extractor = GraphExtractor()
        result = await extractor.extract(chunks, source_document_id="doc-merge")

        assert call_count == 2
        assert len(result) == 1

        nodes = result[0]["nodes"]
        matching = [n for n in nodes if n["name"].strip().lower() == "oil filter"]
        assert len(matching) == 1, "expected exactly one merged 'oil filter' node"

        merged_node = matching[0]
        assert merged_node["sourceChunkIds"] == sorted(["chunk-a", "chunk-b"])

    @patch("extraction.graph_extractor.openai.AsyncOpenAI")
    async def test_extract_empty_chunk_list_returns_empty_list(self, MockOpenAI):
        """extract([]) returns [] — replaces the old extract("") empty-string
        case now that extract() takes a chunk list instead of raw text."""
        from extraction.graph_extractor import GraphExtractor

        extractor = GraphExtractor()
        result = await extractor.extract([])

        assert result == []


# ============================================================================
# Module-level utility function tests
# ============================================================================


class TestApproxTokens:
    """Tests for _approx_tokens — rough char-to-token estimator."""

    def test_empty_string_returns_zero(self):
        from extraction.graph_extractor import _approx_tokens

        assert _approx_tokens("") == 0

    def test_four_chars_equals_one_token(self):
        from extraction.graph_extractor import _approx_tokens

        assert _approx_tokens("hello") == 1  # 5 // 4 == 1

    def test_no_remainder_truncation(self):
        from extraction.graph_extractor import _approx_tokens

        assert _approx_tokens("a" * 100) == 25  # 100 // 4 == 25


class TestTruncate:
    """Tests for _truncate — safe text truncation for logging."""

    def test_text_under_limit_returned_as_is(self):
        from extraction.graph_extractor import _truncate

        assert _truncate("hello", limit=2000) == "hello"

    def test_text_at_limit_returned_as_is(self):
        from extraction.graph_extractor import _truncate

        text = "x" * 2000
        assert _truncate(text, limit=2000) == text

    def test_text_over_limit_truncated_with_suffix(self):
        from extraction.graph_extractor import _truncate

        text = "a" * 3000
        result = _truncate(text, limit=2000)
        assert result.startswith("a" * 2000)
        assert result.endswith("...[truncated 1000 chars]")

    def test_empty_string_returns_empty(self):
        from extraction.graph_extractor import _truncate

        assert _truncate("") == ""


class TestMergeResults:
    """Tests for _merge_results — node/edge deduplication and id rewriting."""

    def test_empty_list_returns_empty_structure(self):
        from extraction.graph_extractor import _merge_results

        result = _merge_results([])
        assert result == {"nodes": [], "edges": []}

    def test_all_none_entries_returns_empty_structure(self):
        from extraction.graph_extractor import _merge_results

        result = _merge_results([None, None])
        assert result == {"nodes": [], "edges": []}

    def test_mixed_none_and_valid_batch(self):
        from extraction.graph_extractor import _merge_results

        batch = {
            "nodes": [{"id": "1", "name": "Oil Filter", "type": "Component"}],
            "edges": [],
        }
        result = _merge_results([None, batch, None])
        assert len(result["nodes"]) == 1

    def test_single_batch_passed_through(self):
        from extraction.graph_extractor import _merge_results

        batch = {
            "nodes": [{"id": "1", "name": "Oil Filter", "type": "Component"}],
            "edges": [
                {"fromNodeId": "1", "toNodeId": "2", "relationshipType": "PART_OF"}
            ],
        }
        result = _merge_results([batch])
        assert len(result["nodes"]) == 1
        assert len(result["edges"]) == 1

    def test_same_node_name_different_ids_dedupes_first_wins(self):
        from extraction.graph_extractor import _merge_results

        batch1 = {
            "nodes": [{"id": "n1", "name": "Oil Filter", "type": "Component"}],
            "edges": [],
        }
        batch2 = {
            "nodes": [{"id": "n2", "name": "Oil Filter", "type": "Thing"}],
            "edges": [],
        }
        result = _merge_results([batch1, batch2])
        assert len(result["nodes"]) == 1
        assert result["nodes"][0]["id"] == "n1"

    def test_edge_referencing_deduped_node_rewritten_to_canonical_id(self):
        from extraction.graph_extractor import _merge_results

        batch1 = {
            "nodes": [{"id": "n1", "name": "Oil Filter", "type": "Component"}],
            "edges": [],
        }
        batch2 = {
            "nodes": [
                {"id": "n2", "name": "Oil Filter", "type": "Component"},
                {"id": "n3", "name": "Engine", "type": "Component"},
            ],
            "edges": [
                {"fromNodeId": "n2", "toNodeId": "n3", "relationshipType": "PART_OF"}
            ],
        }
        result = _merge_results([batch1, batch2])
        assert len(result["edges"]) == 1
        # n2 deduped to n1
        assert result["edges"][0]["fromNodeId"] == "n1"
        assert result["edges"][0]["toNodeId"] == "n3"

    def test_duplicate_edges_by_key_deduped(self):
        from extraction.graph_extractor import _merge_results

        batch = {
            "nodes": [
                {"id": "1", "name": "A"},
                {"id": "2", "name": "B"},
            ],
            "edges": [
                {"fromNodeId": "1", "toNodeId": "2", "relationshipType": "PART_OF"},
                {"fromNodeId": "1", "toNodeId": "2", "relationshipType": "PART_OF"},
                {"fromNodeId": "1", "toNodeId": "2", "relationshipType": "REQUIRES"},
            ],
        }
        result = _merge_results([batch])
        assert len(result["edges"]) == 2  # two unique edge keys

    def test_edge_with_empty_from_node_id_dropped(self):
        from extraction.graph_extractor import _merge_results

        batch = {
            "nodes": [{"id": "1", "name": "Known"}],
            "edges": [
                {
                    "fromNodeId": "",
                    "toNodeId": "1",
                    "relationshipType": "PART_OF",
                }
            ],
        }
        result = _merge_results([batch])
        # fromNodeId is empty string → falsy → edge dropped
        assert len(result["edges"]) == 0

    def test_edge_with_missing_to_node_id_dropped(self):
        from extraction.graph_extractor import _merge_results

        batch = {
            "nodes": [{"id": "1", "name": "Known"}],
            "edges": [
                {
                    "fromNodeId": "1",
                    "relationshipType": "PART_OF",
                }
            ],
        }
        result = _merge_results([batch])
        # toNodeId is missing → .get returns "" → falsy → edge dropped
        assert len(result["edges"]) == 0

    def test_nodes_with_empty_name_skipped(self):
        from extraction.graph_extractor import _merge_results

        batch = {
            "nodes": [
                {"id": "1", "name": ""},
                {"id": "2", "name": "Valid Node"},
            ],
            "edges": [],
        }
        result = _merge_results([batch])
        assert len(result["nodes"]) == 1
        assert result["nodes"][0]["name"] == "Valid Node"

    def test_nodes_with_whitespace_only_name_skipped(self):
        from extraction.graph_extractor import _merge_results

        batch = {
            "nodes": [
                {"id": "1", "name": "   "},
                {"id": "2", "name": "Real"},
            ],
            "edges": [],
        }
        result = _merge_results([batch])
        assert len(result["nodes"]) == 1

    def test_node_name_case_insensitive_dedupe(self):
        from extraction.graph_extractor import _merge_results

        batch1 = {
            "nodes": [{"id": "a", "name": "OIL FILTER"}],
            "edges": [],
        }
        batch2 = {
            "nodes": [{"id": "b", "name": "oil filter"}],
            "edges": [],
        }
        result = _merge_results([batch1, batch2])
        assert len(result["nodes"]) == 1
        assert result["nodes"][0]["id"] == "a"

    def test_node_name_with_whitespace_normalized(self):
        from extraction.graph_extractor import _merge_results

        batch1 = {
            "nodes": [{"id": "a", "name": "  Oil Filter  "}],
            "edges": [],
        }
        batch2 = {
            "nodes": [{"id": "b", "name": "oil filter"}],
            "edges": [],
        }
        result = _merge_results([batch1, batch2])
        assert len(result["nodes"]) == 1


# ============================================================================
# Retry logic tests
# ============================================================================


class TestQueryLlmWithRetry:
    """Tests for _query_llm_with_retry — retry behaviour with mocked LLM calls."""

    @pytest.mark.asyncio
    async def test_success_on_first_attempt_returns_parsed_json(self):
        from extraction.graph_extractor import _query_llm_with_retry

        mock_client = MagicMock()
        valid_content = '{"nodes":[{"id":"1","name":"Test"}],"edges":[]}'
        mock_choice = SimpleNamespace(message=SimpleNamespace(content=valid_content))
        mock_response = SimpleNamespace(choices=[mock_choice])
        mock_client.chat.completions.create = AsyncMock(return_value=mock_response)

        result = await _query_llm_with_retry(
            mock_client, "test-model", "some text", 0, 1, "doc-1"
        )

        assert result is not None
        assert result["nodes"][0]["name"] == "Test"
        assert len(result["edges"]) == 0
        # Called exactly once
        assert mock_client.chat.completions.create.call_count == 1

    @pytest.mark.asyncio
    async def test_retry_on_timeout_then_succeeds(self, monkeypatch):
        from extraction.graph_extractor import _query_llm_with_retry

        # Prevent real sleep during retries
        monkeypatch.setattr("asyncio.sleep", AsyncMock())

        mock_client = MagicMock()
        valid_content = '{"nodes":[],"edges":[]}'
        mock_choice = SimpleNamespace(message=SimpleNamespace(content=valid_content))
        mock_response = SimpleNamespace(choices=[mock_choice])

        # Fail twice with APITimeoutError, then succeed
        call_count = 0

        async def side_effect(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            if call_count <= 2:
                import openai

                raise openai.APITimeoutError(
                    "timed out"
                )  # pyright: ignore[reportArgumentType]
            return mock_response

        mock_client.chat.completions.create = AsyncMock(side_effect=side_effect)

        result = await _query_llm_with_retry(
            mock_client, "test-model", "some text", 0, 1, "doc-1"
        )

        assert result is not None
        assert call_count == 3  # 2 failures + 1 success

    @pytest.mark.asyncio
    async def test_retry_on_first_timeout_succeeds_second_attempt(self, monkeypatch):
        from extraction.graph_extractor import _query_llm_with_retry

        monkeypatch.setattr("asyncio.sleep", AsyncMock())

        mock_client = MagicMock()
        valid_content = '{"nodes":[],"edges":[]}'
        mock_choice = SimpleNamespace(message=SimpleNamespace(content=valid_content))
        mock_response = SimpleNamespace(choices=[mock_choice])

        call_count = 0

        async def side_effect(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            if call_count <= 2:
                import openai

                raise openai.APITimeoutError(
                    "timed out"
                )  # pyright: ignore[reportArgumentType]
            return mock_response

        mock_client.chat.completions.create = AsyncMock(side_effect=side_effect)

        result = await _query_llm_with_retry(
            mock_client, "test-model", "some text", 0, 1, "doc-1"
        )

        assert result is not None
        # 2 timeouts + 1 success = 3 calls (both retries used—BATCH_MAX_RETRIES=2)
        assert call_count == 3

    @pytest.mark.asyncio
    async def test_all_retries_exhausted_returns_none(self, monkeypatch):
        from extraction.graph_extractor import _query_llm_with_retry

        monkeypatch.setattr("asyncio.sleep", AsyncMock())

        mock_client = MagicMock()

        async def always_timeout(*args, **kwargs):
            import openai

            raise openai.APITimeoutError(
                "always times out"
            )  # pyright: ignore[reportArgumentType]

        mock_client.chat.completions.create = AsyncMock(side_effect=always_timeout)

        result = await _query_llm_with_retry(
            mock_client, "test-model", "some text", 0, 1, "doc-1"
        )

        assert result is None
        # BATCH_MAX_RETRIES = 2, so: 1 initial + 2 retries = 3 attempts
        assert mock_client.chat.completions.create.call_count == 3

    @pytest.mark.asyncio
    async def test_non_retryable_error_returns_none_immediately(self, monkeypatch):
        from extraction.graph_extractor import _query_llm_with_retry

        monkeypatch.setattr("asyncio.sleep", AsyncMock())

        mock_client = MagicMock()

        async def raise_value_error(*args, **kwargs):
            raise ValueError("not a retryable error")

        mock_client.chat.completions.create = AsyncMock(side_effect=raise_value_error)

        result = await _query_llm_with_retry(
            mock_client, "test-model", "some text", 0, 1, "doc-1"
        )

        assert result is None
        # No retry for non-retryable errors — only 1 attempt
        assert mock_client.chat.completions.create.call_count == 1

    @pytest.mark.asyncio
    async def test_stale_choice_content_returns_empty_dict(self, monkeypatch):
        from extraction.graph_extractor import _query_llm_with_retry

        monkeypatch.setattr("asyncio.sleep", AsyncMock())

        mock_client = MagicMock()
        # choices is empty → .choices is falsy → content="" → parsed={}
        mock_response = SimpleNamespace(choices=[])
        mock_client.chat.completions.create = AsyncMock(return_value=mock_response)

        result = await _query_llm_with_retry(
            mock_client, "test-model", "some text", 0, 1, "doc-1"
        )

        # Empty content → parsed = {}, which is returned as-is (not None)
        assert result == {}

    @pytest.mark.asyncio
    async def test_retry_on_unreachable_string_pattern(self, monkeypatch):
        from extraction.graph_extractor import _query_llm_with_retry

        monkeypatch.setattr("asyncio.sleep", AsyncMock())

        mock_client = MagicMock()
        valid_content = '{"nodes":[],"edges":[]}'
        mock_choice = SimpleNamespace(message=SimpleNamespace(content=valid_content))
        mock_response = SimpleNamespace(choices=[mock_choice])

        call_count = 0

        async def side_effect(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            if call_count <= 1:
                raise ConnectionError("host unreachable network error")
            return mock_response

        mock_client.chat.completions.create = AsyncMock(side_effect=side_effect)

        result = await _query_llm_with_retry(
            mock_client, "test-model", "some text", 0, 1, "doc-1"
        )

        assert result is not None
        assert call_count == 2  # retried once then succeeded


# ============================================================================
# extract() orchestration tests
# ============================================================================


class TestExtractBatching:
    """Tests verifying extract() orchestrates batching, merging, and retry."""

    @pytest.mark.asyncio
    async def test_extract_with_short_text_returns_single_merged_result(
        self, monkeypatch
    ):
        from extraction.graph_extractor import GraphExtractor

        # Monkeypatch _query_llm_with_retry to return a known result
        async def mock_query_llm(*args, **kwargs):
            return {
                "nodes": [{"id": "n1", "name": "Brake Pad", "type": "Component"}],
                "edges": [],
            }

        monkeypatch.setattr(
            "extraction.graph_extractor._query_llm_with_retry", mock_query_llm
        )

        extractor = GraphExtractor()
        result = await extractor.extract(
            [("chunk-1", "Replace the brake pads.")], "doc-42"
        )

        assert len(result) == 1
        assert result[0]["nodes"][0]["name"] == "Brake Pad"
        assert result[0]["nodes"][0]["sourceDocumentId"] == "doc-42"

    @pytest.mark.asyncio
    async def test_extract_with_multi_batch_text_merges_results(self, monkeypatch):
        from extraction.graph_extractor import GraphExtractor

        call_count = 0

        async def mock_query_llm(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            if call_count == 1:
                return {
                    "nodes": [{"id": "a1", "name": "Oil Filter"}],
                    "edges": [],
                }
            else:
                return {
                    "nodes": [{"id": "a2", "name": "Oil Filter"}],
                    "edges": [],
                }

        monkeypatch.setattr(
            "extraction.graph_extractor._query_llm_with_retry", mock_query_llm
        )

        extractor = GraphExtractor()
        # Two 9000-char chunks: each fits one batch, but combined they exceed
        # the ~16000-char budget, so batching must split them into 2
        # whole-chunk batches (2 LLM calls).
        chunks: list[tuple[str, str]] = [
            ("chunk-a", "A" * 9000),
            ("chunk-b", "B" * 9000),
        ]
        result = await extractor.extract(chunks, "doc-1")

        assert call_count >= 2
        assert len(result) == 1
        # Node deduped: only one Oil Filter node
        assert len(result[0]["nodes"]) == 1
        assert result[0]["nodes"][0]["name"] == "Oil Filter"

    @pytest.mark.asyncio
    async def test_extract_all_batches_fail_returns_empty_list(self, monkeypatch):
        from extraction.graph_extractor import GraphExtractor

        async def mock_query_llm(*args, **kwargs):
            return None  # all batches fail

        monkeypatch.setattr(
            "extraction.graph_extractor._query_llm_with_retry", mock_query_llm
        )

        extractor = GraphExtractor()
        # Two 9000-char chunks → 2 whole-chunk batches; all fail.
        chunks: list[tuple[str, str]] = [
            ("chunk-a", "x" * 9000),
            ("chunk-b", "x" * 9000),
        ]
        result = await extractor.extract(chunks, "doc-1")

        assert result == []

    @pytest.mark.asyncio
    async def test_extract_partial_batch_failure_returns_remaining_results(
        self, monkeypatch
    ):
        from extraction.graph_extractor import GraphExtractor

        call_count = 0

        async def mock_query_llm(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            if call_count == 1:
                return {
                    "nodes": [{"id": "n1", "name": "Valid Node"}],
                    "edges": [],
                }
            return None  # batch 2 fails

        monkeypatch.setattr(
            "extraction.graph_extractor._query_llm_with_retry", mock_query_llm
        )

        extractor = GraphExtractor()
        # Two 9000-char chunks → 2 whole-chunk batches; batch 2 fails.
        chunks: list[tuple[str, str]] = [
            ("chunk-a", "x" * 9000),
            ("chunk-b", "x" * 9000),
        ]
        result = await extractor.extract(chunks, "doc-1")

        # Only batch 1 succeeded
        assert len(result) == 1
        assert result[0]["nodes"][0]["name"] == "Valid Node"


# ============================================================================
# Caplog / telemetry assertion tests
# ============================================================================


class TestCaplogTelemetry:
    """Tests verifying structured logging output from the extraction pipeline."""

    @pytest.mark.asyncio
    async def test_info_log_contains_structured_fields(self, monkeypatch, caplog):
        from extraction.graph_extractor import _query_llm_with_retry

        monkeypatch.setattr("asyncio.sleep", AsyncMock())

        caplog.set_level(logging.INFO)

        mock_client = MagicMock()
        valid_content = '{"nodes":[{"id":"1","name":"Test"}],"edges":[]}'
        mock_choice = SimpleNamespace(message=SimpleNamespace(content=valid_content))
        mock_response = SimpleNamespace(choices=[mock_choice])
        mock_client.chat.completions.create = AsyncMock(return_value=mock_response)

        await _query_llm_with_retry(
            mock_client, "test-model", "hello world", 0, 1, "doc-99"
        )

        info_records = [r for r in caplog.records if r.levelno == logging.INFO]
        assert len(info_records) >= 1

        info_msg = info_records[0].message
        assert "component=graph_extraction" in info_msg
        assert "model=test-model" in info_msg
        assert "result=ok" in info_msg
        assert "input_chars=" in info_msg
        assert "tokens_approx=" in info_msg
        assert "elapsed_ms=" in info_msg
        assert "batch_index=1/1" in info_msg

    @pytest.mark.asyncio
    async def test_debug_log_contains_prompt_and_response(self, monkeypatch, caplog):
        from extraction.graph_extractor import _query_llm_with_retry

        monkeypatch.setattr("asyncio.sleep", AsyncMock())

        caplog.set_level(logging.DEBUG)

        mock_client = MagicMock()
        valid_content = '{"nodes":[],"edges":[]}'
        mock_choice = SimpleNamespace(message=SimpleNamespace(content=valid_content))
        mock_response = SimpleNamespace(choices=[mock_choice])
        mock_client.chat.completions.create = AsyncMock(return_value=mock_response)

        await _query_llm_with_retry(
            mock_client, "test-model", "prompt text here", 0, 1, "doc-1"
        )

        debug_records = [r for r in caplog.records if r.levelno == logging.DEBUG]
        assert len(debug_records) >= 1

        debug_msg = debug_records[0].message
        assert "component=graph_extraction" in debug_msg
        assert "prompt=" in debug_msg
        assert "response=" in debug_msg
        assert "prompt text here" in debug_msg
        assert '{"nodes":[]' in debug_msg

    @pytest.mark.asyncio
    async def test_extract_logs_empty_text_info(self, monkeypatch, caplog):
        from extraction.graph_extractor import GraphExtractor

        caplog.set_level(logging.INFO)

        extractor = GraphExtractor()
        await extractor.extract([], "doc-e")

        info_records = [r for r in caplog.records if r.levelno == logging.INFO]
        assert len(info_records) >= 1

        info_msg = info_records[0].message
        assert "result=empty_text" in info_msg
        assert "returning_empty" in info_msg

    @pytest.mark.asyncio
    async def test_extract_logs_batch_summary_info(self, monkeypatch, caplog):
        from extraction.graph_extractor import GraphExtractor

        caplog.set_level(logging.INFO)

        async def mock_query_llm(*args, **kwargs):
            return {"nodes": [], "edges": []}

        monkeypatch.setattr(
            "extraction.graph_extractor._query_llm_with_retry", mock_query_llm
        )

        extractor = GraphExtractor()
        # Two 9000-char chunks → 2 whole-chunk batches (num_batches=2).
        chunks: list[tuple[str, str]] = [
            ("chunk-a", "x" * 9000),
            ("chunk-b", "x" * 9000),
        ]
        await extractor.extract(chunks, "doc-s")

        info_records = [r for r in caplog.records if r.levelno == logging.INFO]
        info_messages = [r.message for r in info_records]

        # One of the messages should contain the batch count
        batch_msg = [m for m in info_messages if "num_batches=" in m]
        assert len(batch_msg) >= 1
        assert "num_batches=2" in batch_msg[0]
        assert "total_chars=" in batch_msg[0]

    @pytest.mark.asyncio
    async def test_error_log_on_all_retries_exhausted(self, monkeypatch, caplog):
        from extraction.graph_extractor import _query_llm_with_retry

        monkeypatch.setattr("asyncio.sleep", AsyncMock())

        caplog.set_level(logging.ERROR)

        mock_client = MagicMock()

        async def always_fails(*args, **kwargs):
            import openai

            raise openai.APITimeoutError(
                "always fails"
            )  # pyright: ignore[reportArgumentType]

        mock_client.chat.completions.create = AsyncMock(side_effect=always_fails)

        await _query_llm_with_retry(
            mock_client, "test-model", "some text", 0, 1, "doc-err"
        )

        error_records = [r for r in caplog.records if r.levelno == logging.ERROR]
        assert len(error_records) >= 1

        error_msg = error_records[0].message
        assert "component=graph_extraction" in error_msg
        assert "result=error" in error_msg
        # openai.APITimeoutError.__str__ returns "Request timed out." regardless
        # of the constructor argument
        assert "Request timed out" in error_msg

    @pytest.mark.asyncio
    async def test_info_log_when_job_id_contains_controls_preserves_reversible_value(
        self, monkeypatch, caplog
    ):
        from extraction.graph_extractor import _query_llm_with_retry

        monkeypatch.setattr("asyncio.sleep", AsyncMock())
        mock_client = MagicMock()
        mock_choice = SimpleNamespace(
            message=SimpleNamespace(content='{"nodes":[],"edges":[]}')
        )
        mock_client.chat.completions.create = AsyncMock(
            return_value=SimpleNamespace(choices=[mock_choice])
        )
        unsafe_job_id = "external\\path\r\n\t\0\x01\x1f\x7f\x85\x9fvalue"
        escaped_job_id = (
            "external\\\\path\\r\\n\\t\\0\\u0001\\u001F\\u007F\\u0085\\u009Fvalue"
        )
        caplog.set_level(logging.INFO, logger="extraction.graph_extractor")

        await _query_llm_with_retry(
            mock_client, "test-model", "safe prompt", 0, 1, unsafe_job_id
        )

        target_records = [
            item for item in caplog.records if item.name == "extraction.graph_extractor"
        ]
        target_messages = [item.getMessage() for item in target_records]

        assert target_messages
        assert any(escaped_job_id in message for message in target_messages)
        assert all(
            not any(
                character in message for character in "\r\n\t\0\x01\x1f\x7f\x85\x9f"
            )
            for message in target_messages
        )

"""Unit tests for MetadataExtractor - LLM calls fully mocked via openai.AsyncOpenAI."""

import json
from types import SimpleNamespace
from typing import Optional
from unittest.mock import AsyncMock, patch

import pytest


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _mock_llm_response(content: Optional[str]):
    """Build a fake OpenAI chat completion response carrying ``content``."""
    mock_choice = SimpleNamespace(message=SimpleNamespace(content=content))
    return SimpleNamespace(choices=[mock_choice])


def _configure_mock_client(MockOpenAI, responses):
    """Wire ``responses`` (str | list[str] | side_effect) onto the mock client.

    ``responses`` may be a single JSON string (returned for every call) or a
    list of JSON strings (returned in order across iterations).
    """
    mock_client = MockOpenAI.return_value
    if isinstance(responses, list):
        mock_client.chat.completions.create = AsyncMock(
            side_effect=[_mock_llm_response(c) for c in responses]
        )
    else:
        mock_client.chat.completions.create = AsyncMock(
            return_value=_mock_llm_response(responses)
        )
    return mock_client


_FULL_RESULT = {
    "make": "Honda",
    "model": "CBR600RR",
    "year": 2023,
    "category": "sport",
    "tags": ["sport", "inline-4", "600cc"],
}

_TEN_PAGES = [f"Page {i + 1} content about a motorcycle." for i in range(10)]


# ---------------------------------------------------------------------------
# Instantiation
# ---------------------------------------------------------------------------


class TestMetadataExtractorInstantiation:
    def test_instantiates_without_error(self):
        from extraction.metadata_extractor import MetadataExtractor

        extractor = MetadataExtractor()
        assert extractor._endpoint == "http://localhost:9999/v1"
        assert extractor._model == "test-model"

    def test_raises_value_error_without_endpoint(self, monkeypatch):
        from extraction.metadata_extractor import MetadataExtractor

        monkeypatch.delenv("GRAPH_EXTRACTION_ENDPOINT", raising=False)
        with pytest.raises(ValueError, match="GRAPH_EXTRACTION_ENDPOINT"):
            MetadataExtractor()

    def test_raises_value_error_without_model(self, monkeypatch):
        from extraction.metadata_extractor import MetadataExtractor

        monkeypatch.delenv("GRAPH_EXTRACTION_MODEL", raising=False)
        with pytest.raises(ValueError, match="GRAPH_EXTRACTION_MODEL"):
            MetadataExtractor()

    def test_reads_env_overrides(self, monkeypatch):
        from extraction.metadata_extractor import MetadataExtractor

        monkeypatch.setenv("GRAPH_EXTRACTION_ENDPOINT", "http://override:9999/v1")
        monkeypatch.setenv("GRAPH_EXTRACTION_MODEL", "override-model")
        extractor = MetadataExtractor()
        assert extractor._endpoint == "http://override:9999/v1"
        assert extractor._model == "override-model"


# ---------------------------------------------------------------------------
# extract()
# ---------------------------------------------------------------------------


class TestExtract:
    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_happy_path_all_fields_on_first_try(self, MockOpenAI):
        from extraction.metadata_extractor import MetadataExtractor

        _configure_mock_client(MockOpenAI, json.dumps(_FULL_RESULT))

        extractor = MetadataExtractor()
        result = await extractor.extract(_TEN_PAGES)

        assert result["make"] == "Honda"
        assert result["model"] == "CBR600RR"
        assert result["year"] == 2023
        assert result["category"] == "sport"
        assert result["tags"] == ["sport", "inline-4", "600cc"]
        assert result["fill_rate"] == 1.0
        assert result["pages_sampled"] == 1
        # Should stop after the first sample (1 page) once fill rate is 1.0.
        assert MockOpenAI.return_value.chat.completions.create.await_count == 1

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_partial_fill_then_complete(self, MockOpenAI):
        from extraction.metadata_extractor import MetadataExtractor

        partial = {"make": "Honda", "model": "CBR", "year": 0, "category": ""}
        complete = _FULL_RESULT
        _configure_mock_client(
            MockOpenAI, [json.dumps(partial), json.dumps(complete)]
        )

        extractor = MetadataExtractor()
        result = await extractor.extract(_TEN_PAGES)

        # First sample (1 page) gave 2 fields; second sample (2 pages) filled
        # the rest, so extraction stops at 2 pages with 100% fill rate.
        assert result["make"] == "Honda"
        assert result["model"] == "CBR"
        assert result["year"] == 2023
        assert result["category"] == "sport"
        assert result["fill_rate"] == 1.0
        assert result["pages_sampled"] == 2
        assert MockOpenAI.return_value.chat.completions.create.await_count == 2

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_partial_fill_accumulates_across_iterations(self, MockOpenAI):
        """Fields discovered at 1 page are retained when more pages are sampled."""
        from extraction.metadata_extractor import MetadataExtractor

        first = {"make": "Yamaha", "model": "", "year": 0, "category": ""}
        second = {"make": "", "model": "MT-07", "year": 2021, "category": "naked"}
        _configure_mock_client(
            MockOpenAI, [json.dumps(first), json.dumps(second)]
        )

        extractor = MetadataExtractor()
        result = await extractor.extract(_TEN_PAGES)

        assert result["make"] == "Yamaha"  # retained from first sample
        assert result["model"] == "MT-07"
        assert result["year"] == 2021
        assert result["category"] == "naked"
        assert result["fill_rate"] == 1.0
        assert result["pages_sampled"] == 2

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_empty_response_from_llm(self, MockOpenAI):
        from extraction.metadata_extractor import MetadataExtractor

        _configure_mock_client(MockOpenAI, "{}")

        extractor = MetadataExtractor()
        result = await extractor.extract(_TEN_PAGES)

        assert result["make"] is None
        assert result["model"] is None
        assert result["year"] == 0
        assert result["category"] is None
        assert result["tags"] == []
        assert result["fill_rate"] == 0.0
        # Exhausts all sample sizes (1 -> 2 -> 3) since fill rate never
        # reaches 1.0.
        assert result["pages_sampled"] == 3
        assert MockOpenAI.return_value.chat.completions.create.await_count == 3

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_null_content_treated_as_empty(self, MockOpenAI):
        from extraction.metadata_extractor import MetadataExtractor

        mock_client = MockOpenAI.return_value
        mock_client.chat.completions.create = AsyncMock(
            return_value=_mock_llm_response(None)
        )

        extractor = MetadataExtractor()
        result = await extractor.extract(_TEN_PAGES)

        assert result["fill_rate"] == 0.0
        assert result["pages_sampled"] == 3

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_invalid_json_response(self, MockOpenAI):
        from extraction.metadata_extractor import MetadataExtractor

        _configure_mock_client(MockOpenAI, "This is not JSON {{{")

        extractor = MetadataExtractor()
        result = await extractor.extract(_TEN_PAGES)

        assert result["fill_rate"] == 0.0
        assert result["pages_sampled"] == 3
        assert result["make"] is None

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_max_pages_reached_without_full_fill(self, MockOpenAI):
        from extraction.metadata_extractor import MetadataExtractor

        partial = {"make": "Kawasaki", "model": "", "year": 0, "category": ""}
        _configure_mock_client(MockOpenAI, json.dumps(partial))

        extractor = MetadataExtractor()
        result = await extractor.extract(_TEN_PAGES)

        assert result["make"] == "Kawasaki"
        assert result["fill_rate"] == 0.25
        assert result["pages_sampled"] == 3
        assert MockOpenAI.return_value.chat.completions.create.await_count == 3

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_llm_connection_error(self, MockOpenAI):
        from extraction.metadata_extractor import MetadataExtractor

        mock_client = MockOpenAI.return_value
        mock_client.chat.completions.create = AsyncMock(
            side_effect=ConnectionError("LM Studio unreachable")
        )

        extractor = MetadataExtractor()
        result = await extractor.extract(_TEN_PAGES)

        # Connection errors trigger retry (3 attempts per sample size).
        # After exhausting retries, the exception propagates to extract(),
        # which catches it and returns early with whatever was accumulated.
        assert result["fill_rate"] == 0.0
        assert result["pages_sampled"] == 0
        assert result["make"] is None
        # First sample size (1 page) tried 3 times, then exception propagates.
        assert mock_client.chat.completions.create.await_count == 3

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_empty_pages_returns_empty_result_without_calling_llm(
        self, MockOpenAI
    ):
        from extraction.metadata_extractor import MetadataExtractor

        extractor = MetadataExtractor()
        result = await extractor.extract([])

        assert result["fill_rate"] == 0.0
        assert result["pages_sampled"] == 0
        assert result["make"] is None
        MockOpenAI.return_value.chat.completions.create.assert_not_called()

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_fewer_pages_than_sample_clamps(self, MockOpenAI):
        """A 4-page document clamps sample sizes and still completes when full."""
        from extraction.metadata_extractor import MetadataExtractor

        _configure_mock_client(MockOpenAI, json.dumps(_FULL_RESULT))

        extractor = MetadataExtractor()
        result = await extractor.extract(["p1", "p2", "p3", "p4"])

        assert result["fill_rate"] == 1.0
        # First sample size (3) <= 4 pages, so it succeeds immediately.
        assert result["pages_sampled"] == 1

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_short_document_skips_oversized_samples(self, MockOpenAI):
        """A 4-page doc that never fully fills samples 3 then 4 pages only.

        Sample sizes 9 and 10 clamp to 4, which does not exceed the already
        sampled 4 pages, so those iterations are skipped (no extra LLM calls).
        """
        from extraction.metadata_extractor import MetadataExtractor

        partial = {"make": "Honda", "model": "", "year": 0, "category": ""}
        _configure_mock_client(MockOpenAI, json.dumps(partial))

        extractor = MetadataExtractor()
        result = await extractor.extract(["p1", "p2", "p3", "p4"])

        assert result["make"] == "Honda"
        assert result["fill_rate"] == 0.25
        assert result["pages_sampled"] == 3
        # Only three calls: sample sizes 1, 2, 3 on a 4-page doc.
        assert MockOpenAI.return_value.chat.completions.create.await_count == 3

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_year_string_coerced_to_int(self, MockOpenAI):
        from extraction.metadata_extractor import MetadataExtractor

        result_json = json.dumps(
            {
                "make": "Suzuki",
                "model": "GSX-R750",
                "year": "2019",  # LLM returned a string
                "category": "sport",
            }
        )
        _configure_mock_client(MockOpenAI, result_json)

        extractor = MetadataExtractor()
        result = await extractor.extract(_TEN_PAGES)

        assert result["year"] == 2019
        assert result["fill_rate"] == 1.0

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_non_numeric_year_treated_as_unfilled(self, MockOpenAI):
        from extraction.metadata_extractor import MetadataExtractor

        result_json = json.dumps(
            {
                "make": "Ducati",
                "model": "Panigale",
                "year": "unknown",
                "category": "sport",
            }
        )
        _configure_mock_client(MockOpenAI, result_json)

        extractor = MetadataExtractor()
        result = await extractor.extract(_TEN_PAGES)

        # "unknown" cannot coerce to int -> year stays 0 (unfilled) -> 75%.
        assert result["year"] == 0
        assert result["fill_rate"] == 0.75


# ---------------------------------------------------------------------------
# source_path context (file-path hints forwarded to the LLM)
# ---------------------------------------------------------------------------


class TestSourcePathContext:
    """The original file path is prepended to the LLM user message verbatim."""

    def test_build_user_content_prepends_path_before_text(self):
        from extraction.metadata_extractor import MetadataExtractor

        content = MetadataExtractor._build_user_content(
            "PAGE TEXT", "/data/manuals/2023/Honda/CBR600RR/service-manual.pdf"
        )
        # Path context appears first, page text appears last.
        assert content.startswith("The original file path is: /data/manuals/2023/Honda/CBR600RR/service-manual.pdf")
        assert "This path may contain hints about year, make, and model." in content
        assert content.endswith("PAGE TEXT")
        # Ordering: path hint must come before the page text so the LLM sees it
        # as leading context.
        assert content.index("The original file path is:") < content.index("PAGE TEXT")

    def test_build_user_content_returns_plain_text_when_no_path(self):
        from extraction.metadata_extractor import MetadataExtractor

        assert MetadataExtractor._build_user_content("PAGE TEXT", None) == "PAGE TEXT"
        assert MetadataExtractor._build_user_content("PAGE TEXT", "") == "PAGE TEXT"

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_extract_forwards_source_path_into_user_message(self, MockOpenAI):
        from extraction.metadata_extractor import MetadataExtractor

        _configure_mock_client(MockOpenAI, json.dumps(_FULL_RESULT))
        path = "/data/manuals/2023/Honda/CBR600RR/service-manual.pdf"

        extractor = MetadataExtractor()
        await extractor.extract(_TEN_PAGES, source_path=path)

        create = MockOpenAI.return_value.chat.completions.create
        create.assert_awaited_once()
        messages = create.await_args.kwargs["messages"]
        user_content = messages[1]["content"]
        assert user_content.startswith(f"The original file path is: {path}")
        assert "This path may contain hints about year, make, and model." in user_content

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_extract_without_source_path_sends_plain_text(self, MockOpenAI):
        from extraction.metadata_extractor import MetadataExtractor

        _configure_mock_client(MockOpenAI, json.dumps(_FULL_RESULT))

        extractor = MetadataExtractor()
        await extractor.extract(_TEN_PAGES)

        create = MockOpenAI.return_value.chat.completions.create
        user_content = create.await_args.kwargs["messages"][1]["content"]
        # No path-context preamble when source_path is omitted.
        assert "The original file path is:" not in user_content
        assert user_content.startswith("Page 1 content")

    @patch("extraction.metadata_extractor.openai.AsyncOpenAI")
    async def test_extract_empty_pages_with_source_path_skips_llm(self, MockOpenAI):
        from extraction.metadata_extractor import MetadataExtractor

        extractor = MetadataExtractor()
        result = await extractor.extract([], source_path="/any/path.pdf")

        assert result["fill_rate"] == 0.0
        assert result["pages_sampled"] == 0
        MockOpenAI.return_value.chat.completions.create.assert_not_called()


# ---------------------------------------------------------------------------
# MetadataResult schema bridge
# ---------------------------------------------------------------------------


class TestMetadataResultModel:
    def test_from_extraction_dict_full(self):
        from models.schemas import MetadataResult

        data = {
            "make": "Honda",
            "model": "CBR600RR",
            "year": 2023,
            "category": "sport",
            "tags": ["sport"],
            "fill_rate": 1.0,
            "pages_sampled": 3,
        }
        result = MetadataResult.from_extraction_dict(data)
        assert result.make == "Honda"
        assert result.model == "CBR600RR"
        assert result.year == 2023
        assert result.category == "sport"
        assert result.tags == ["sport"]
        assert result.fill_rate == 1.0
        assert result.pages_sampled == 3

    def test_from_extraction_dict_missing_keys_default(self):
        from models.schemas import MetadataResult

        result = MetadataResult.from_extraction_dict({})
        assert result.make is None
        assert result.year == 0
        assert result.tags == []
        assert result.fill_rate == 0.0
        assert result.pages_sampled == 0

    def test_from_extraction_dict_coerces_string_year(self):
        from models.schemas import MetadataResult

        result = MetadataResult.from_extraction_dict({"year": "2024"})
        assert result.year == 2024

    def test_from_extraction_dict_bad_year_defaults_to_zero(self):
        from models.schemas import MetadataResult

        result = MetadataResult.from_extraction_dict({"year": "abc"})
        assert result.year == 0


# ---------------------------------------------------------------------------
# _parse_llm_json() - robust JSON parsing for LLM output
# ---------------------------------------------------------------------------


class TestParseLlmJson:
    """Tests for the robust JSON parser that handles LLM output quirks."""

    def test_pure_json(self):
        from extraction.metadata_extractor import MetadataExtractor

        result = MetadataExtractor._parse_llm_json('{"make":"Honda"}')
        assert result == {"make": "Honda"}

    def test_markdown_fences(self):
        from extraction.metadata_extractor import MetadataExtractor

        result = MetadataExtractor._parse_llm_json(
            '```json\n{"make":"Honda"}\n```'
        )
        assert result == {"make": "Honda"}

    def test_fences_without_language_tag(self):
        from extraction.metadata_extractor import MetadataExtractor

        result = MetadataExtractor._parse_llm_json(
            '```\n{"make":"Honda"}\n```'
        )
        assert result == {"make": "Honda"}

    def test_mixed_content(self):
        from extraction.metadata_extractor import MetadataExtractor

        result = MetadataExtractor._parse_llm_json(
            'Here is the metadata:\n{"make":"Honda"}'
        )
        assert result == {"make": "Honda"}

    def test_empty_string_returns_empty_dict(self):
        from extraction.metadata_extractor import MetadataExtractor

        assert MetadataExtractor._parse_llm_json("") == {}

    def test_whitespace_only_returns_empty_dict(self):
        from extraction.metadata_extractor import MetadataExtractor

        assert MetadataExtractor._parse_llm_json("   ") == {}

    def test_none_content_returns_empty_dict(self):
        from extraction.metadata_extractor import MetadataExtractor

        assert MetadataExtractor._parse_llm_json(None) == {}

    def test_trailing_commas_in_object(self):
        from extraction.metadata_extractor import MetadataExtractor

        result = MetadataExtractor._parse_llm_json('{"make":"Honda",}')
        assert result == {"make": "Honda"}

    def test_trailing_commas_in_array(self):
        from extraction.metadata_extractor import MetadataExtractor

        result = MetadataExtractor._parse_llm_json(
            '{"make":"Honda","tags":["sport","inline-4",]}'
        )
        assert result == {"make": "Honda", "tags": ["sport", "inline-4"]}

    def test_partial_json_returns_empty_dict(self):
        from extraction.metadata_extractor import MetadataExtractor

        result = MetadataExtractor._parse_llm_json('{"make":"Honda","model":')
        assert result == {}

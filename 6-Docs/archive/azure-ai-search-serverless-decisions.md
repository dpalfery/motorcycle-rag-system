# Azure AI Search Serverless Decision Summary

## Overview

Following the review of Azure AI Search options for the motorcycle manuals RAG workload, the team decided to proceed with the Serverless Developer tier for the initial implementation.

## Decisions

1. Use the Azure AI Search Serverless Developer tier
   - This is the preferred starting point for the current workload because query usage is expected to be light and the service is cost-efficient for intermittent usage.
   - The Serverless Developer tier is currently in preview and should be treated as a preview capability.
   - A region in the United States must be selected that supports the Serverless Developer tier.

2. Use a domain-based indexing strategy instead of year-based splitting
   - The initial approach will be to partition content by domain/category rather than by arbitrary year ranges.
   - Suggested domains:
     - Dirt
     - Touring
     - Sport
     - Cruiser
   - This gives better semantic grouping for retrieval and avoids awkward routing rules that break across year boundaries.
   - The application layer should handle routing to the appropriate index or perform a multi-index search when needed.

3. Reduce embedding dimensions to 1536
   - The embedding dimension will be changed to 1536.
   - This is expected to reduce vector storage pressure compared with the larger previous dimension while still providing strong retrieval quality for the current use case.
   - This change should be applied consistently across indexing and query-time embedding generation.

## Rationale

- Serverless is a good fit for low-query, lower-traffic usage patterns.
- The main constraint for this workload is index and vector storage growth, not query volume.
- Domain-based partitioning is more practical than year-based partitioning because motorcycle manuals often span multiple eras and categories, and users may ask questions that span more than one grouping.
- A 1536-dimensional embedding is a reasonable compromise for storage efficiency and retrieval quality.

## Implementation Notes

- Validate that the chosen Azure region supports the Serverless Developer tier.
- Keep the retrieval layer flexible enough to support future expansion to additional indexes if storage limits become a constraint.
- Monitor index growth and vector quota usage after initial ingestion.

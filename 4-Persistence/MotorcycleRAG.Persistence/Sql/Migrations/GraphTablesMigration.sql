-- =============================================================================
-- Migration: Graph RAG Tables for Motorcycle Manual Relationship Extraction
-- Target: SQL Server 2017+ (requires Graph Database feature)
-- Run: Execute once against the target database. Idempotent (uses IF NOT EXISTS).
-- =============================================================================

-- GraphNode: Represents entities extracted from manuals and spec datasets.
IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE name = N'GraphNode' AND is_node = 1
)
BEGIN
    CREATE TABLE [dbo].[GraphNode] (
        [Id]               UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [Name]             NVARCHAR(512)    NOT NULL,
        [Type]             NVARCHAR(128)    NOT NULL,
        [Description]      NVARCHAR(MAX)        NULL,
        [SourceDocumentId] UNIQUEIDENTIFIER     NULL,
        [CreatedAtUtc]     DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
        [UpdatedAtUtc]     DATETIME2(7)         NULL
    ) AS NODE;

    -- Primary key on the surrogate Id column
    ALTER TABLE [dbo].[GraphNode]
        ADD CONSTRAINT [PK_GraphNode] PRIMARY KEY CLUSTERED ([Id]);

    -- Index for fast lookup by source document
    CREATE NONCLUSTERED INDEX [IX_GraphNode_SourceDocumentId]
        ON [dbo].[GraphNode] ([SourceDocumentId])
        WHERE [SourceDocumentId] IS NOT NULL;

    -- Index for lookup by entity type
    CREATE NONCLUSTERED INDEX [IX_GraphNode_Type]
        ON [dbo].[GraphNode] ([Type]);
END;
GO

-- GraphEdge: Represents semantic relationships between two GraphNodes.
IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE name = N'GraphEdge' AND is_edge = 1
)
BEGIN
    CREATE TABLE [dbo].[GraphEdge] (
        [FromNodeId]       UNIQUEIDENTIFIER NOT NULL,
        [ToNodeId]         UNIQUEIDENTIFIER NOT NULL,
        [RelationshipType] NVARCHAR(256)    NOT NULL,
        [Weight]           FLOAT            NOT NULL DEFAULT 1.0,
        [Context]          NVARCHAR(MAX)        NULL,
        [CreatedAtUtc]     DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME()
    ) AS EDGE;

    -- Index on relationship type for graph traversal filtering
    CREATE NONCLUSTERED INDEX [IX_GraphEdge_RelationshipType]
        ON [dbo].[GraphEdge] ([RelationshipType]);

    -- Index to support deduplication check on (FromNodeId, ToNodeId, RelationshipType)
    CREATE NONCLUSTERED INDEX [IX_GraphEdge_FromTo_RelationshipType]
        ON [dbo].[GraphEdge] ([FromNodeId], [ToNodeId], [RelationshipType]);
END;
GO

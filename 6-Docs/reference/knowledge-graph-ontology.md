---
id: reference/knowledge-graph-ontology
title: MotorcycleRAG Knowledge Graph Ontology
doc-type: reference
status: draft
owner: Ingestion maintainers
last-reviewed: 2026-07-26
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# MotorcycleRAG Knowledge Graph Ontology

## Purpose and scope

This is the authoritative schema for the **domain** knowledge graph stored in the `dbo.GraphNode` / `dbo.GraphEdge` SQL Graph tables. It covers the motorcycle subject matter the agent answers on: mechanical, trivia, trips, groups, navigation, and points of interest.

It is not the documentation ontology. [`documentation-ontology.md`](../documentation-ontology.md) governs Markdown frontmatter and the documentation-to-code graph; the two graphs are separate and share no node types.

The type sets below are **closed**. The extractor's job at ingest is entity resolution and edge instantiation *against this vocabulary* — never label invention. A chunk that yields an unrecognised type is a quarantine event, not a new node type. Adding a node type or edge type is a change to this document, reviewed like any other interface change.

**Status: draft — vocabulary defined, not yet enforced.** `GraphNode.Type` and `GraphEdge.RelationshipType` are currently `NVARCHAR` free-form values passed through from the Python `graph_extractor` output. Enforcement work is listed under [Enforcement gap](#enforcement-gap).

## Storage model

The graph is physical SQL Server / Azure SQL Graph, deployed by the canonical schema file — see [schema deployment](../DevOps/database-schema.md).

| Table | Kind | Discriminator | Payload |
| --- | --- | --- | --- |
| `dbo.GraphNode` | `AS NODE` | `Type` | `Name`, `Description`, `SourceDocumentId` |
| `dbo.GraphEdge` | `AS EDGE` | `RelationshipType` | `Weight`, `Context` |

Two consequences follow from the single-table design and must be respected by authors:

1. **Typed attributes have no columns.** A torque figure, a latitude, or a model year is not a `GraphNode` column. Structured values are carried either as a `Spec` node (unit-bearing, queryable) or as a JSON object in `Description` (descriptive only, not queryable). Prefer the `Spec` node whenever the value will appear in a `WHERE` clause.
2. **The discriminator carries all typing.** `IX_GraphNode_Type` and `IX_GraphEdge_RelationshipType` are the only things making type-scoped traversal affordable, so every query SHALL filter on the discriminator rather than scanning by `Name`.

Relational tables remain authoritative where they already exist. `dbo.BikeModels` is the source of truth for make/model/year; `ModelYear` graph nodes are projections of it and SHALL NOT be created for a bike absent from that table. `dbo.IndexedChunks` is the source of truth for retrievable text; `Chunk` nodes are projections carrying `SourceDocumentId`.

## Node types

Identity is the deduplication key. Two extractions producing the same identity are the same node and SHALL be merged, not duplicated.

### Mechanical

| `Type` | Identity | Notes |
| --- | --- | --- |
| `Manufacturer` | normalized name | Honda, KTM. Aliases resolve to the canonical name. |
| `Model` | `Manufacturer` + model name | Year-independent nameplate. |
| `ModelYear` | `Model` + year (+ market) | **The join hub.** Projected from `dbo.BikeModels`. Most mechanical answers key off this node. |
| `System` | closed sub-vocabulary | `engine`, `fuel`, `electrical`, `brakes`, `suspension`, `drivetrain`, `cooling`, `exhaust`, `chassis`, `bodywork`, `controls`. |
| `Part` | OEM part number, else manufacturer + name | Aftermarket parts carry no OEM number; identity falls back to name. |
| `Spec` | owning node + spec key | Unit-bearing value: torque, capacity, pressure, gap, interval. |
| `Symptom` | canonical phrasing | "Will not start when hot". Extractor maps user phrasings onto the canonical node. |
| `Procedure` | canonical name + scope | Maintenance or repair operation. |
| `FaultCode` | manufacturer + code | DTC / MIL code. Manufacturer-scoped: the same code differs across marques. |
| `Tool` | canonical name | Service tools and consumables required by a `Procedure`. |

### Places and travel

| `Type` | Identity | Notes |
| --- | --- | --- |
| `Place` | name + coordinate | POI. Sub-vocabulary in `Description.category`: `fuel`, `food`, `lodging`, `dealer`, `repair`, `scenic`, `camping`, `museum`. |
| `RoadSegment` | name + start/end coordinate | A named piece of road. Reusable across routes; this is what makes navigation queries and POI proximity work. |
| `Route` | canonical name | An ordered, reusable, named ride — Tail of the Dragon, the Blue Ridge Parkway. |
| `Trip` | owning `Person` + start date | An instance somebody actually rode. Distinct from `Route`, which is the template. |
| `Region` | hierarchical path | `US/NC/Appalachians`. Country → state/province → area. |

### Social and knowledge

| `Type` | Identity | Notes |
| --- | --- | --- |
| `Person` | canonical name (+ disambiguator) | One label for riders, racers, designers, journalists. Role is carried by the edge, not by a separate node type. |
| `Group` | canonical name | Club, forum, brand community, owners' association. |
| `Event` | name + year | Rally, race, group ride, model launch. |
| `Fact` | statement hash | Trivia or historical claim with provenance. Keeps unverifiable colour out of the mechanical subgraph, where a wrong number is a safety problem. |
| `Chunk` | `IndexedChunks.Id` | Retrievable source text. Projection of `dbo.IndexedChunks`. |
| `Document` | `IndexedArtifacts.Id` | Source document. Projection of `dbo.IndexedArtifacts`. |

## Edge types

`From` and `To` are directional and SHALL NOT be reversed at ingest; traversal in the opposite direction is a query concern, not a storage concern.

### Mechanical

| `RelationshipType` | From → To | `Context` payload |
| --- | --- | --- |
| `MANUFACTURES` | `Manufacturer` → `Model` | — |
| `VARIANT_OF` | `ModelYear` → `Model` | — |
| `SUCCEEDS` | `ModelYear` → `ModelYear` | generation marker |
| `HAS_SYSTEM` | `ModelYear` → `System` | — |
| `HAS_PART` | `System` → `Part` | quantity |
| `FITS` | `Part` → `ModelYear` | year range, fitment caveats |
| `SUPERSEDED_BY` | `Part` → `Part` | supersession date |
| `HAS_SPEC` | `ModelYear` \| `System` \| `Part` → `Spec` | — |
| `EXHIBITS` | `ModelYear` → `Symptom` | prevalence |
| `INDICATES` | `Symptom` → `System` | diagnostic confidence |
| `RESOLVED_BY` | `Symptom` → `Procedure` | success rate |
| `APPLIES_TO` | `Procedure` → `ModelYear` | — |
| `REQUIRES_PART` | `Procedure` → `Part` | quantity |
| `REQUIRES_TOOL` | `Procedure` → `Tool` | — |
| `TRIGGERS` | `FaultCode` → `Symptom` | — |

### Navigation and travel

| `RelationshipType` | From → To | `Context` payload |
| --- | --- | --- |
| `LOCATED_IN` | `Place` \| `RoadSegment` \| `Event` → `Region` | — |
| `CONNECTS` | `RoadSegment` → `RoadSegment` | junction, direction |
| `NEAR` | `Place` → `RoadSegment` | distance in metres |
| `INCLUDES_SEGMENT` | `Route` → `RoadSegment` | sequence ordinal |
| `STOPS_AT` | `Route` → `Place` | sequence ordinal, purpose |
| `FOLLOWED` | `Trip` → `Route` | deviation notes |
| `VISITED` | `Trip` → `Place` | timestamp |
| `SUITABLE_FOR` | `Route` \| `Place` → `ModelYear` | reason — ground clearance, surface, range |

### Social and provenance

| `RelationshipType` | From → To | `Context` payload |
| --- | --- | --- |
| `RODE` | `Person` → `Trip` | role — lead, pillion |
| `OWNS` | `Person` → `ModelYear` | ownership period |
| `MEMBER_OF` | `Person` → `Group` | role, joined date |
| `FOCUSED_ON` | `Group` → `Model` \| `Region` \| `Event` | — |
| `ORGANIZES` | `Group` → `Event` | — |
| `HELD_AT` | `Event` → `Place` | — |
| `ATTENDED` | `Person` → `Event` | role — racer, entrant, spectator |
| `ABOUT` | `Fact` → any node | — |
| `SOURCED_FROM` | `Fact` \| `Spec` \| `Procedure` \| `Symptom` → `Chunk` | extraction confidence |
| `PART_OF` | `Chunk` → `Document` | ordinal |

## Invariants

These are the rules a validator enforces. They are the reason the ontology is worth having.

1. **Closed vocabulary.** Every `GraphNode.Type` and `GraphEdge.RelationshipType` value SHALL appear in this document. An unrecognised value is quarantined and surfaced for review; it is never written.
2. **Endpoint typing.** Every edge SHALL connect the node types named in its row. A `FITS` edge from a `Place` is a defect, not a novel insight.
3. **Provenance is mandatory for asserted content.** Every `Fact`, `Spec`, `Procedure`, and `Symptom` node SHALL carry at least one `SOURCED_FROM` edge. A torque spec the agent cannot cite is a torque spec the agent does not state.
4. **`ModelYear` is the fitment authority.** Fitment, specs, and procedures attach to `ModelYear`, never to `Model`. "Does this fit my bike?" has a year-specific answer, and answering it from the nameplate is how a rider ends up with the wrong part.
5. **Trivia is quarantined from mechanical.** A `Fact` reaches the mechanical subgraph only through `ABOUT`. It never becomes a `Spec`.
6. **Relational tables win.** Where `dbo.BikeModels`, `dbo.IndexedArtifacts`, or `dbo.IndexedChunks` hold the same entity, the relational row is authoritative and the graph node is a projection of it.
7. **Vocabulary growth is a document change.** New *values* — a new `Place` category, a new `System` — extend the sub-vocabularies here. New *types* require review. Neither is an extractor decision.

## Query patterns

Every traversal filters on the discriminator; these shapes are what the indexes were built for.

Parts that fit a specific bike:

```sql
SELECT p.Name, p.Description
FROM   GraphNode AS my, GraphEdge AS fits, GraphNode AS p
WHERE  MATCH(p-(fits)->my)
  AND  my.Type = N'ModelYear'
  AND  p.Type  = N'Part'
  AND  fits.RelationshipType = N'FITS'
  AND  my.Name = @modelYear;
```

Diagnose a symptom, with the procedure and its provenance:

```sql
SELECT s.Name AS Symptom, sys.Name AS System, proc.Name AS Procedure, src.SourceDocumentId
FROM   GraphNode AS s,      GraphEdge AS ind, GraphNode AS sys,
       GraphEdge AS res,    GraphNode AS proc,
       GraphEdge AS sourced, GraphNode AS src
WHERE  MATCH(sys<-(ind)-s-(res)->proc-(sourced)->src)
  AND  s.Type = N'Symptom' AND s.Name = @symptom
  AND  ind.RelationshipType     = N'INDICATES'
  AND  res.RelationshipType     = N'RESOLVED_BY'
  AND  sourced.RelationshipType = N'SOURCED_FROM'
ORDER BY ind.Weight DESC;
```

Fuel stops within reach of a route:

```sql
SELECT DISTINCT pl.Name, near.Context
FROM   GraphNode AS r,  GraphEdge AS inc, GraphNode AS seg,
       GraphEdge AS near, GraphNode AS pl
WHERE  MATCH(r-(inc)->seg<-(near)-pl)
  AND  r.Type = N'Route' AND r.Name = @route
  AND  inc.RelationshipType  = N'INCLUDES_SEGMENT'
  AND  near.RelationshipType = N'NEAR'
  AND  pl.Type = N'Place'
  AND  JSON_VALUE(pl.Description, '$.category') = N'fuel';
```

`SHORTEST_PATH` is available for multi-hop supersession chains (`SUPERSEDED_BY`) and segment connectivity (`CONNECTS`). Bound the hop count — an unbounded traversal on Azure SQL Basic tier will time out.

## Enforcement gap

The vocabulary above is not yet enforced anywhere in the pipeline. Three gaps, in the order they should close:

1. **Extractor prompt.** The Python `graph_extractor` prompt does not carry the closed type lists, so the model is free to invent labels. Injecting the vocabularies is the highest-value change and the cheapest.
2. **Ingest validation.** `GraphEntityIngestionService` maps `Type` and `RelationshipType` straight from JSON to the DTO with no check. It should reject or quarantine out-of-vocab values and log the rejection with the upload ID.
3. **Database constraint.** `GraphNode` and `GraphEdge` have no `CHECK` constraint or lookup-table foreign key on their discriminators, and `GraphEdge` has no `EDGE CONSTRAINT` restricting endpoint types. Adding both makes the invariants unbypassable rather than merely documented.

Until all three close, treat retrieved graph content as advisory and prefer the relational tables for anything safety-relevant.

## Related documentation

- [Database schema deployment](../DevOps/database-schema.md) — where `GraphNode` and `GraphEdge` are defined and how the schema is applied.
- [Documentation ontology](../documentation-ontology.md) — the separate frontmatter and code graph.
- [Architecture placement rules](../rules/architecture-general.md) — layer boundaries for ingestion and persistence code.

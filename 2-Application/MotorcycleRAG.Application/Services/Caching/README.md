# MotorcycleRAG Query Caching

This folder contains the application-layer implementations of `IQueryCacheService`. The API registers caching through `AddCachingAndOptimization` in `MotorcycleRAG.Application.Extensions`.

## Behavior

- `MemoryQueryCacheService` is used when no Redis connection string is configured.
- `DistributedQueryCacheService` is used when `ConnectionStrings:Redis` is configured.
- Both implementations use the `Cache` configuration section for expiration, entry limits, and optional response compression.
- Cache keys are generated from normalized `MotorcycleQueryRequest` data, and cache statistics are exposed through the shared contracts model.

## Boundaries

This folder owns cache storage behavior only. Interfaces belong in `MotorcycleRAG.Contracts`, configuration belongs in `MotorcycleRAG.Core`, and API registration belongs in the Application service-collection extensions.

## Documentation

- [Component catalog](../../../../6-Docs/catalog.md)
- [Architecture placement rules](../../../../6-Docs/rules/architecture-general.md)

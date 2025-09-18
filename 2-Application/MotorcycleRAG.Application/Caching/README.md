# Caching and Performance Optimization

This directory contains the caching and performance optimization features for the Motorcycle RAG system.

## Overview

The caching system is designed to improve response times and reduce costs by storing frequently accessed query results. The performance optimization features include vector compression, batch processing, and connection pooling.

## Features

### Query Caching

- **Memory Cache**: Fast in-memory caching using `IMemoryCache`
- **Distributed Cache**: Redis-based caching for multi-instance deployments
- **Compression**: Automatic compression of large responses
- **Smart Expiration**: Different expiration times based on response quality
- **Cache Statistics**: Monitoring and metrics for cache performance

### Vector Compression

- **Multiple Algorithms**: 4-bit, 8-bit, and scalar quantization
- **Batch Processing**: Efficient compression of multiple vectors
- **Configurable Levels**: 1-10 compression levels for different use cases
- **Statistics Tracking**: Compression ratios and performance metrics

### Batch Processing

- **Optimized Batching**: Automatic batch size optimization
- **Parallel Processing**: Multi-threaded processing with configurable parallelism
- **Error Handling**: Graceful handling of failed items
- **Retry Logic**: Configurable retry policies for transient failures

### Connection Pooling

- **HTTP Client Management**: Optimized HTTP client instances
- **Connection Reuse**: Efficient connection pooling and lifecycle management
- **Health Monitoring**: Connection health checks and statistics
- **Service-Specific Configuration**: Different settings per service

## Configuration

### Cache Configuration

```json
{
  "Cache": {
    "EnableCaching": true,
    "DefaultExpiration": "00:30:00",
    "LongTermExpiration": "24:00:00",
    "MaxMemorySizeMB": 100,
    "EnableCompression": true,
    "CompressionThreshold": 1024,
    "MaxCacheEntries": 1000
  }
}
```

### Redis Configuration (Optional)

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

## Usage

### Dependency Injection Setup

```csharp
// Add caching services
services.AddMotorcycleCaching(configuration);

// Add performance optimization services
services.AddPerformanceOptimization(configuration);

// Or add both
services.AddCachingAndOptimization(configuration);
```

### Using Query Cache Service

```csharp
public class MyService
{
    private readonly IQueryCacheService _cacheService;
    
    public MyService(IQueryCacheService cacheService)
    {
        _cacheService = cacheService;
    }
    
    public async Task<MotorcycleQueryResponse> ProcessQueryAsync(MotorcycleQueryRequest request)
    {
        var cacheKey = _cacheService.GenerateCacheKey(request);
        
        // Try to get from cache first
        var cachedResponse = await _cacheService.GetAsync(cacheKey);
        if (cachedResponse != null)
        {
            return cachedResponse;
        }
        
        // Process query...
        var response = await ProcessQueryInternal(request);
        
        // Cache the response
        await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(30));
        
        return response;
    }
}
```

### Using Vector Compression

```csharp
public class VectorService
{
    private readonly IVectorCompressionService _compressionService;
    
    public async Task<CompressedVector> CompressEmbeddingAsync(float[] embedding)
    {
        return _compressionService.CompressVector(embedding, compressionLevel: 5);
    }
    
    public async Task<float[]> DecompressEmbeddingAsync(CompressedVector compressed)
    {
        return _compressionService.DecompressVector(compressed);
    }
}
```

### Using Batch Processing

```csharp
public class DocumentProcessor
{
    private readonly IBatchProcessingService _batchService;
    
    public async Task<BatchProcessingResult<ProcessedDocument>> ProcessDocumentsAsync(
        IEnumerable<Document> documents)
    {
        var options = new BatchProcessingOptions
        {
            BatchSize = 100,
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            EnableRetry = true,
            MaxRetryAttempts = 3
        };
        
        return await _batchService.ProcessParallelBatchAsync(
            documents,
            ProcessSingleDocument,
            options);
    }
    
    private async Task<ProcessedDocument> ProcessSingleDocument(
        Document document, 
        CancellationToken cancellationToken)
    {
        // Process individual document
        return new ProcessedDocument();
    }
}
```

## Performance Targets

### Caching Performance

- **Cache Hit Response Time**: < 50ms
- **Cache Set Operation**: < 100ms
- **Cache Hit Ratio**: > 80% for common queries
- **Memory Usage**: Configurable with automatic eviction

### Vector Compression

- **Compression Ratio**: 50-80% size reduction
- **Compression Time**: < 100ms per vector
- **Decompression Time**: < 50ms per vector
- **Accuracy**: > 95% cosine similarity after compression

### Batch Processing

- **Throughput**: > 50 documents/second
- **Parallel Efficiency**: 80% of linear scaling
- **Error Rate**: < 5% for transient failures
- **Memory Usage**: Optimized batch sizes based on available memory

### Connection Pooling

- **Connection Reuse**: > 90% connection reuse rate
- **Health Check**: < 500ms response time
- **Pool Efficiency**: < 10% overhead vs direct connections

## Monitoring

### Cache Metrics

- Hit ratio and miss ratio
- Memory usage and entry count
- Average response times
- Eviction rates and reasons

### Compression Metrics

- Compression ratios by algorithm
- Processing times and throughput
- Accuracy measurements
- Storage savings

### Batch Processing Metrics

- Throughput and success rates
- Processing times by batch size
- Error rates and retry statistics
- Resource utilization

### Connection Pool Metrics

- Active and idle connection counts
- Request success and failure rates
- Average response times
- Pool health status

## Best Practices

### Caching

1. **Cache Key Design**: Use normalized, deterministic cache keys
2. **Expiration Strategy**: Use different expiration times based on data volatility
3. **Memory Management**: Monitor memory usage and configure appropriate limits
4. **Cache Warming**: Pre-populate cache with common queries

### Vector Compression

1. **Compression Level Selection**: Balance compression ratio vs accuracy
2. **Batch Processing**: Use batch operations for better performance
3. **Algorithm Selection**: Choose appropriate algorithm based on use case
4. **Accuracy Validation**: Monitor compression accuracy in production

### Batch Processing

1. **Batch Size Optimization**: Use automatic optimization based on system resources
2. **Error Handling**: Implement proper retry logic and error reporting
3. **Parallelism Tuning**: Configure parallelism based on workload characteristics
4. **Memory Management**: Monitor memory usage during batch processing

### Connection Pooling

1. **Pool Sizing**: Configure appropriate pool sizes per service
2. **Timeout Configuration**: Set reasonable timeouts for different operations
3. **Health Monitoring**: Implement regular health checks
4. **Resource Cleanup**: Ensure proper cleanup of unused connections

## Troubleshooting

### Common Issues

1. **High Memory Usage**: Check cache size limits and eviction policies
2. **Low Cache Hit Ratio**: Review cache key generation and expiration settings
3. **Slow Compression**: Consider using lower compression levels or batch processing
4. **Connection Pool Exhaustion**: Increase pool size or reduce connection lifetime
5. **Batch Processing Failures**: Check error handling and retry configuration

### Performance Tuning

1. **Cache Size**: Adjust based on available memory and hit ratio
2. **Compression Level**: Balance between size reduction and processing time
3. **Batch Size**: Use optimization algorithms or manual tuning
4. **Connection Limits**: Monitor connection usage and adjust limits accordingly

### Monitoring and Alerting

1. Set up alerts for low cache hit ratios
2. Monitor memory usage and set appropriate thresholds
3. Track compression accuracy and performance
4. Monitor batch processing success rates and throughput
5. Set up connection pool health alerts
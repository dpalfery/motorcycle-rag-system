# Embedding Model for Local Processing

## Required Model

The MAUI admin app requires an ONNX embedding model for local text vectorization.

### Recommended Model

**all-MiniLM-L6-v2** (ONNX format)
- Dimensions: 384
- Size: ~23 MB
- Source: sentence-transformers

### How to Obtain

1. Download the ONNX version of all-MiniLM-L6-v2:
   ```
   https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2
   ```

2. Convert to ONNX if needed (using optimum library):
   ```bash
   pip install optimum[onnxruntime]
   optimum-cli export onnx --model sentence-transformers/all-MiniLM-L6-v2 ./onnx-model
   ```

3. Copy the model file to this directory:
   ```
   1-Presentation/MotorcycleRAG.Admin/Resources/Raw/embedding-model.onnx
   ```

4. Update the .csproj file to include as embedded resource:
   ```xml
   <ItemGroup>
     <MauiAsset Include="Resources\Raw\embedding-model.onnx" />
   </ItemGroup>
   ```

### Alternative: Use API-based Embeddings

If local embedding generation is not needed, you can modify the `IngestionViewModel` to skip local embedding generation and rely solely on server-side processing.

### File Configuration

The model file should be named: `embedding-model.onnx`

The app expects this file in: `Resources/Raw/embedding-model.onnx`

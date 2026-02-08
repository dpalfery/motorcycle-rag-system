This PRD outlines the architecture for the **Motorcycle Oracle Ingestion Pipeline**, a hybrid cloud system leveraging **Microsoft Fabric** for heavy lifting and the **Microsoft Agent Framework** for orchestration.

---

# PRD: Motorcycle Oracle Ingestion Pipeline

## 1. Executive Summary

**Goal:** Build a cost-effective, high-fidelity RAG ingestion pipeline that fuses unstructured motorcycle service manuals (PDFs) with a structured bike specification dataset (100-column CSV).
**Key Constraint:** Stay within a **$50/month Azure budget** by leveraging Fabric Free Trial, Cosmos DB Free Tier, and a local RTX 5090 for LLM-based entity extraction.

---

## 2. Technical Stack

* **Orchestration:** Microsoft Agent Framework (Workflows/DAG).
* **Processing:** Microsoft Fabric (Spark Notebooks).
* **Vector Storage:** Azure AI Search (Free Tier - Metadata only) + Azure SQL (Free - Vector Data).
* **Graph Storage:** Azure Cosmos DB for Apache Gremlin (Free Tier).
* **Local Inference:** RTX 5090 running Ollama (Llama 3 / Mistral) via secure tunnel.

---

## 3. Ingestion Workflow & Requirements

### 3.1 Unstructured Data: PDF Manuals (100–800 pages)

* **Objective:** Extract maintenance procedures, torque specs, and fluid capacities while maintaining document hierarchy.
* **Fabric Processing (Spark):** * Utilize `PyMuPDF` or `Marker` for PDF-to-Markdown conversion.
* **Chunking Strategy:** Recursive character splitting with overlap, preserving header metadata (e.g., "Engine" > "Lubrication" > "Oil Change").


* **Local Agent Enrichment:** * Fabric sends chunks to the **Local Mechanic Agent** (5090 GPU).
* Agent extracts "Entities" (e.g., `Part: Oil Filter`, `Tool: 17mm Wrench`) and "Attributes" (e.g., `Torque: 20 Nm`).


* **Output:** * **Vectors:** Stored in Azure SQL/AI Search.
* **Nodes:** Created in Cosmos DB for each manual section.



### 3.2 Structured Data: Motorcycle Specs (100-Column CSV)

* **Objective:** Map every bike model to its technical specs and link to manual chunks.
* **Fabric Processing:** * Cleanse and normalize CSV (e.g., "YZF-R1M" vs "R1M").
* **Graph Mapping:** * **Vertex:** `BikeModel` (Properties: Year, Engine Type, Horsepower).
* **Edge:** `HAS_MANUAL_SECTION` (Connects `BikeModel` to manual nodes).
* **Edge:** `REQUIRES_SPEC` (Connects `BikeModel` to specific numeric attributes).





---

## 4. Database Schema Design

### 4.1 Cosmos DB (Gremlin Graph)

| Entity | Type | Properties |
| --- | --- | --- |
| **Motorcycle** | Vertex | `brand`, `model`, `year`, `vin_prefix` |
| **ManualChunk** | Vertex | `chunk_id`, `page_number`, `header_path` |
| **Component** | Vertex | `name` (e.g., "Brake Caliper"), `system` |
| **LINKED_TO** | Edge | Connects `Motorcycle` to `ManualChunk` |
| **SPECIFIES** | Edge | Connects `ManualChunk` to `Component` (Property: `torque_value`) |

### 4.2 Azure AI Search / SQL (Vector Index)

* **Index Name:** `motorcycle-manuals-v1`
* **Fields:** `id`, `text_preview`, `vector_embedding` (1536d), `metadata_json`.
* **Logic:** The vector search identifies the `chunk_id`, which the Agent Framework then uses to traverse the Cosmos Graph for related specs (e.g., "Find the bolt size for this torque spec").

---

## 5. Agent Framework Integration

The **Microsoft Agent Framework** orchestrates the "Ingestion Workflow" as a DAG:

1. **FabricExecutor:** Triggers Spark job in Fabric.
2. **EntityAgent (Local):** Receives text stream from Fabric, returns JSON of extracted graph entities.
3. **GraphWriter:** Commits nodes/edges to Cosmos DB.
4. **VectorWriter:** Commits embeddings to Azure AI Search.

---

## 6. Success Metrics & Costs

* **Accuracy:** 95% retrieval of correct torque specs for a given bike model.
* **Performance:** < 3s latency for "Graph-Augmented" queries.
* **Azure Spend:**
* Cosmos DB: $0 (Free Tier)
* Azure SQL: $0 (12-mo Free)
* AI Search: $0 (Free 50MB)
* **Actual Spend:** ~$5-10/mo for data egress and Function triggers.

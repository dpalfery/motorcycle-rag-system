# UI Technology Stack


### Core Technology Stack (Open WebUI Base)

This project leverages Open WebUI as the foundational interface, customized for the Motorcycle RAG System.

#### Primary Framework

* **SvelteKit** - Next-generation web framework for building high-performance UIs
* **TypeScript** - Type-safe development
* **Vite** - Lightning-fast build tool and dev server

#### UI Framework & Design System

* **Tailwind CSS** - For rapid, utility-first styling and theme customization (Svelte portions)
* **Pigment CSS** - Zero-runtime CSS-in-JS for React components (CSP-safe, no `unsafe-inline` required)
* **MUI v7** - React component library with Pigment CSS integration for type-safe, performant styling
* **Custom "High Performance" Design** - Racing-inspired aesthetic with neon orange and charcoal
* **Lucide Icons** - Clean, consistent iconography

#### Styling Architecture Decision

**React Components**: MUI v7 with **Pigment CSS** (replaces Emotion CSS)
- **Rationale**: Pigment CSS provides zero-runtime styling, eliminating the need for `unsafe-inline` CSP directives that Emotion requires
- **Security**: Full Content Security Policy compliance without compromising security
- **Performance**: CSS extracted at build time, reducing runtime overhead
- **Developer Experience**: Maintains familiar MUI API with improved type safety

**Svelte Components**: Continue using Tailwind CSS for utility-first styling

#### State Management

* **Svelte Stores** - Native state management for Svelte
* **TanStack Query (Svelte)** - Server state management and caching

#### Backend-for-Frontend (BFF)

* **FastAPI (Open WebUI Backend)** - Asynchronous Python API
* **Existing .NET API** - Integrated as an OpenAI-compatible provider or custom agent

#### Authentication

* **Cookie-based Auth (BFF Pattern)** - Secure, server-side session management
* **PKCE Flow** - Integrated within the BFF

### UI Architecture (Open WebUI Layout)

The project follows the Open WebUI structure, extended with custom "Valves" and "Tools" for motorcycle-specific RAG.

**Key Principles:**

* **Layers** - Strict unidirectional dependency flow
* **Slices** - Grouped by business domain, not technical role
* **Segments** - Internal structure (ui, api, model, lib)
* **Isolation** - Features don't depend on each other

### API Integration

**Auto-Generated TypeScript Types:**

* Backend API generates TypeScript definitions from Swagger/OpenAPI
* Command: `npm run generate:api`
* Location: `src/api/generated/types.ts`

**API Communication:**

* Custom fetch wrapper (lighter than axios)
* Automatic request/response interceptors
* Error handling and retry logic
* Development proxy to backend (`https://localhost:44306`)
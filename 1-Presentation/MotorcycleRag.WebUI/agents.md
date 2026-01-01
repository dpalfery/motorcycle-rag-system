# Agent Context: MotorcycleRag.WebUI

## Invariant Rules
- **Layer**: 1-Presentation (Frontend).
- **Stack**: React 19, TypeScript 5.8+, Vite 6+, Tailwind CSS.
- **Architecture**: **Feature-Sliced Design (FSD)** strictly enforced.
  - `app` → `pages` → `widgets` → `features` → `entities` → `shared`.
- **UI**: MUI v7, @pigment-css/react (zero-runtime styling). Use `css` prop over `sx`.
- **State**: TanStack Query v5 (server state), Zustand (client state).
- **Security**: [Security Rule: Active]. No sensitive data in `localStorage`. Use HTTP-only cookies.
- **Testing**: Vitest, Playwright (E2E).
- **Communication**: Communicates with BFF (`MotorcycleRag.WebUI.BFF`).

## Workflow Skills
- **Dev**: `npm run dev`
- **Build**: `npm run build`
- **Test**: `npm run test`
- **Lint**: `npm run lint`
- **API Types**: `npm run generate:api` (Update types from Swagger).
- **Analyze**: `speckit.analyze`
- **Plan**: `speckit.plan`
- **Implement**: `speckit.implement`

- Always use context7 when I need code generation, setup or configuration steps, or library/API documentation. This means you should automatically use the Context7 MCP tools to resolve library id and get library docs without me having to explicitly ask.
  Docs:
    TypeScript - /microsoft/typescript/v5.9.2
    Material UI - /mui/material-ui/v4.0.0
    React - /facebook/react/v19.1.0
    MSAL React - /azure/msal-react/v3.0.20
# React Frontend Dev Context

  ## Stack
  **Core:** React 19, TypeScript 5.8.3, Vite 6.3.5, Feature-Sliced Design (FSD)
  **UI:** MUI v7 (@mui/material, icons, data-grid-pro, charts, date-pickers), @pigment-css/react
  **State:** TanStack Query v5.85.5 (server), Zustand v5.0.8 (client)
  **Routing:** React Router v7.6.2
  **Auth:** Auth0 v2.4.0
  **i18n:** i18next v25.2.1, react-i18next v15.5.3
  **Testing:** Vitest v3.2.4, Testing Library, Storybook v9.0.15, Playwright v1.53.2
  **Utils:** date-fns v4.1.0, jwt-decode v4.0.0

  ## React 19 Features
  - Actions API for async forms (`useActionState`)
  - `use()` hook for reading Promises/Context (can be conditional)
  - React Compiler (auto-memoization, no manual `useMemo`/`useCallback`)
  - Server Components (`"use server"`)
  - Native form support (`action` prop, `useFormStatus`)

  ## FSD Architecture
  ```
  src/
  ├── app/       # Init, providers, config
  ├── pages/     # Route handlers
  ├── widgets/   # Composite UI blocks
  ├── features/  # User interactions
  ├── entities/  # Business models
  └── shared/    # Reusable utilities
  ```

  **Rules:**
  - Unidirectional: app→pages→widgets→features→entities→shared
  - Slice by domain, not tech role
  - Each slice: `ui/`, `api/`, `model/`, `lib/`
  - Public API via index.ts
  - No cross-feature dependencies

  ## TanStack Query v5
  ```typescript
  // v5 changes: isLoading→isPending, cacheTime→gcTime
  const { data, isPending } = useQuery({
  queryKey: ['todos'],
  queryFn: fetchTodos,
  gcTime: 5 * 60 * 1000,
  });

  // Suspense
  const { data } = useSuspenseQuery({ queryKey, queryFn });

  // Mutations
  const mutation = useMutation({
  mutationFn: updateTodo,
  onMutate: async (newTodo) => {
      await queryClient.cancelQueries({ queryKey: ['todos'] });
      const prev = queryClient.getQueryData(['todos']);
      queryClient.setQueryData(['todos'], old => [...old, newTodo]);
      return { prev };
  },
  onError: (err, vars, ctx) => {
      queryClient.setQueryData(['todos'], ctx.prev);
  },
  });
  ```

  ## State Management
  - **TanStack Query:** Server state (API, caching, sync)
  - **Zustand:** Client state (UI, preferences)
  - **Context:** Shared local (theme, auth, i18n)
  - **useState:** Component-specific

  ## Patterns

  **Loading:**
  ```typescript
  if (isPending) return <CircularProgress />;
  if (error) return <Alert severity="error">{error.message}</Alert>;
  ```

  **Protected Routes:**
  ```typescript
  function ProtectedRoute({ children }) {
  const { isAuthenticated } = useAuth0();
  return isAuthenticated ? children : <Navigate to="/login" />;
  }
  ```

  **i18n:**
  ```typescript
  const { t, i18n } = useTranslation();
  <h1>{t('welcome')}</h1>
  ```

  ## API Integration
  - Auto-generated types from Swagger: `npm run generate:api`
  - Custom fetch wrapper (lighter than axios)
  - Dev proxy: `https://localhost:44306`
  - Location: `src/api/generated/types.ts`

  ## TypeScript
  - Strict mode enabled
  - Interfaces for objects, types for unions
  - Explicit function params/returns
  - Leverage inference

  ## MUI with Pigment CSS
  - Use Pigment CSS for zero-runtime styling (CSP-safe, no `unsafe-inline` required)
  - Use `css` prop for component styles instead of `sx` prop
  - Styled components via Pigment CSS styled API for reusable components
  - Responsive breakpoints via Pigment CSS theme
  - Data-grid-pro for advanced features
  - Benefits: Better performance, CSP compliance, build-time CSS extraction

  ## Testing
  - **Unit (Vitest):** Logic, hooks
  - **Component (Testing Library):** User interactions, `screen.getByRole`
  - **E2E (Playwright):** Critical flows, Page Object Model
  - **Storybook:** Component docs, visual regression

  ## Performance
  - `React.lazy()` for code splitting
  - Virtual scrolling for lists
  - Lazy load images
  - React Compiler handles memoization
  - Throttle/debounce event handlers

  ## Code Style
  - Components: PascalCase
  - Hooks: camelCase with `use`
  - Constants: UPPER_SNAKE_CASE
  - Function components only
  - Composition over inheritance
  - Small, focused components

  ## Commands
  ```bash
  npm run dev          # Start dev
  npm run generate:api # Update types
  npm run build        # Production build
  npm run test         # Run tests
  npm run lint         # Check code
  ```

  ## Key Principles
  - Follow FSD strictly
  - TypeScript strict mode
  - Proper error handling
  - Test critical paths
  - Document complex logic
  - Accessibility first
  - Security: no localStorage for sensitive data, HTTP-only cookies

Docs:
  - [React 19](https://react.dev/blog/2024/12/05/react-19) 
  - [FSD](https://feature-sliced.design/) 
  - [TanStack Query v5](https://tanstack.com/query/v5)
  - [MUI](https://mui.com/)
  - [Pigment CSS](https://github.com/mui/pigment-css)
  - [TypeScript Handbook](https://www.typescriptlang.org/docs/)

  Once you have read the Securiy rule you **MUST** include `[I Read the WebUI Instructions]` at the beginning of your Task 

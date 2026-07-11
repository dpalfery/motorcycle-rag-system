# Admin Desktop — UI / Design Conventions

## Theme Tokens

Defined in `tailwind.config.js`:

- `background` `#1a1a1a`, `card` `#222222`, `border` `#333333`
- `primary` / `accent` `#ff6600`, `muted` `#999`, `secondary` `#2a2a2a`
- `success` `#28c840`, `danger` `#e24b4a`, `warning` `#ef9f27`

## Layout

- `AppShell` has a 180px fixed sidebar with `NavLink` active state (left orange border + `bg-secondary/60`).
- Content scrolls in `<main>`.
- Titlebar: a 36px drag region at the top (`data-tauri-drag-region` equivalent, class `drag`). Do not put interactive elements in this strip.

## Shared Primitives

Import from `@/components/ui`:

- `Button` — variants: default/primary/danger
- `Card`, `MetricCard`, `StatusPill`, `PageHeader` (supports `actions` slot), `Empty`

## Icons

lucide-react only.

## Navigation

No dashboard screen — the sidebar is the navigation.

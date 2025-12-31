# ADR-002: Pigment CSS for React UI Styling

## Status
Accepted (2025-12-31)

## Context
The Motorcycle RAG System requires a secure, performant UI framework for the React 19 web application. The system must meet OWASP ASVS v5.0.0 Level 2 security requirements, which include strict Content Security Policy (CSP) compliance.

MUI v7 is the chosen component library, but the default styling solution (Emotion CSS) poses security challenges:
- Emotion CSS requires runtime style injection using JavaScript
- This necessitates the `unsafe-inline` CSP directive for `style-src`
- Using `unsafe-inline` significantly weakens CSP protection and complicates ASVS Level 2 compliance (requirement 14.4.3)

## Decision
We will use **Pigment CSS** (@pigment-css/react) as the styling solution for MUI v7 components instead of Emotion CSS.

## Rationale

### Security Benefits
1. **CSP Compliance**: Pigment CSS extracts styles at build time, eliminating the need for `unsafe-inline` in CSP headers
2. **ASVS Level 2 Alignment**: Meets OWASP ASVS v5.0.0 requirement 14.4.3 for secure content policies
3. **Defense in Depth**: Reduces attack surface by preventing runtime style injection vectors

### Performance Benefits
1. **Zero Runtime**: All CSS is generated at build time, reducing JavaScript bundle size
2. **Faster Initial Load**: No runtime style generation or injection during page load
3. **Better Caching**: Static CSS files can be cached more effectively by browsers

### Developer Experience
1. **Familiar API**: Maintains similar API to MUI's `sx` prop with `css` prop
2. **Type Safety**: Improved TypeScript integration with build-time validation
3. **Build-Time Errors**: Styling errors caught during compilation rather than runtime
4. **MUI Compatibility**: Official MUI solution with full component library support

## Alternatives Considered

### Emotion CSS (Rejected)
- **Pros**: Default MUI styling solution, widely adopted, excellent developer experience
- **Cons**: Requires `unsafe-inline` CSP directive, runtime performance overhead, security risk
- **Reason for Rejection**: Incompatible with ASVS Level 2 security requirements

### Tailwind CSS Only (Rejected)
- **Pros**: CSP-safe, excellent for utility styling, good performance
- **Cons**: Lacks comprehensive component library, would require building custom components, inconsistent with MUI ecosystem
- **Reason for Rejection**: MUI provides superior component library for enterprise applications

### Styled Components (Rejected)
- **Pros**: Popular CSS-in-JS solution, good developer experience
- **Cons**: Same CSP issues as Emotion, runtime overhead
- **Reason for Rejection**: Shares same security concerns as Emotion CSS

## Consequences

### Positive
- Full CSP compliance without security compromises
- Better runtime performance with zero-runtime styling
- Alignment with OWASP ASVS Level 2 requirements
- Future-proof solution officially supported by MUI team

### Negative
- Slightly different API from standard MUI examples (uses `css` instead of `sx`)
- Smaller community compared to Emotion (fewer StackOverflow answers)
- Build configuration requires Pigment CSS plugin setup

### Neutral
- Team needs to learn Pigment CSS API differences from Emotion
- Existing MUI knowledge transfers with minor adjustments
- Documentation updates required across the codebase

## Implementation Notes

### Package Dependencies
```json
{
  "@mui/material": "^7.x",
  "@pigment-css/react": "^0.x",
  "@pigment-css/vite-plugin": "^0.x"
}
```

### Build Configuration
Vite configuration requires Pigment CSS plugin:
```typescript
import { pigment } from '@pigment-css/vite-plugin';

export default defineConfig({
  plugins: [
    pigment({
      theme: yourTheme,
    }),
  ],
});
```

### Migration Path
1. Install Pigment CSS packages
2. Configure Vite plugin with theme
3. Replace `sx` prop usage with `css` prop
4. Update styled components to use Pigment CSS styled API
5. Remove Emotion CSS packages

## References
- [Pigment CSS Documentation](https://github.com/mui/pigment-css)
- [MUI Pigment CSS Integration](https://mui.com/material-ui/experimental-api/pigment-css/)
- [OWASP ASVS v5.0.0](https://github.com/OWASP/ASVS)
- [Content Security Policy](https://developer.mozilla.org/en-US/docs/Web/HTTP/CSP)

## Related Documents
- `.claude/agents/frontend-dev.md` - Updated frontend development guidelines
- `6-Docs/ui-technology-stack.md` - UI technology stack documentation
- `specs/001-system-spec/research.md` - Research notes on UI framework decisions
- `AGENTS.md` - Project architecture and technology overview

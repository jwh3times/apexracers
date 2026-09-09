# Content Security Policy

`ContentSecurityPolicy` owns the browser resource policies; `SecurityHeadersMiddleware` emits the
SPA policy on API responses and static assets as well as client-side navigation fallbacks.

## Built SPA audit (#273)

- `web/index.html` contains only external scripts: the synchronous same-origin theme bootstrap and
  Vite's fingerprinted module. The bootstrap runs before React and safely falls back to the system
  theme when browser storage is unavailable. No per-response HTML rewriting or script nonce is needed.
- Inter, JetBrains Mono, Sora, and Material Symbols are bundled from pinned Fontsource packages.
  Their redistribution licenses and provenance ship under `/licenses/`. There are no Google Fonts
  stylesheet, preconnect, or font requests. Vite's asset inlining is disabled so small font subsets
  remain same-origin files instead of requiring `data:` in `font-src`.
- The production CSS is an external Vite asset; there are no SPA inline stylesheet elements.
  React's `style` props set individual DOM style properties for colors, chart geometry, progress
  widths, and backgrounds. These CSSOM assignments work under `style-src 'self'`; injected HTML
  style attributes and stylesheet text do not receive an exception. See the
  [browser behavior documented by MDN](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Content-Security-Policy/style-src).
- Login decorations use original CSS gradients and a telemetry grid. The previous opaque
  Google-hosted images had no recorded redistribution provenance and were replaced, not copied.
- All browser API calls are same-origin. External navigation links are not fetch dependencies.
  In particular, track-map links to the iRacing member-assets host do not require an image-source
  allowance. Catalog images use `https://images-static.iracing.com`; `data:` images support CSS/SVG
  image assets without permitting remote script, style, or font sources.
- Embedded objects and framing are denied; base URLs and form submissions are same-origin.
  The policy permits neither inline scripts nor JavaScript string evaluation.

## Development API reference

Only the mapped Scalar HTML handler inside `IsDevelopment()` selects the API-reference policy.
There is no middleware URL-prefix exception: nonexistent Scalar paths and production fallbacks
keep the SPA policy. OpenAPI JSON and the locally served Scalar JavaScript also retain the baseline.

The Scalar initializer receives a fresh cryptographic script nonce and its HTML is `no-store`.
Default remote fonts, Agent Scalar, and telemetry are disabled, so rendering the API reference
does not require third-party registry requests. Production does not map the reference endpoints.

The pinned Scalar bundle injects several stylesheets without nonce support, so its page alone permits
inline CSS. Scripts still require the nonce or same origin. Its bundled Zod code probes string
evaluation inside a caught capability check and then uses a non-eval fallback: that probe stays
blocked, and the E2E test expects exactly that violation rather than enabling `unsafe-eval`.

## Verification and future changes

Build the SPA (`npm run build` in `web/`) and serve it through the API's static-file host before
running `npx playwright test e2e/csp.spec.ts`. The regular E2E workflow already does this. Vite's
development server serves a different document with HMR and is not evidence that the deployed
policy works. Locally, `ASPNETCORE_WEBROOT` may point at the absolute `web/dist` directory.

The browser suite checks theme initialization, local assets, registration, authenticated navigation,
demo routes when `E2E_DEMO=1`, blocked script/style injection and outbound connections, and Scalar's
OpenAPI rendering. It explicitly leaves CSP enforcement enabled. Middleware unit tests pin the
baseline and prevent path-based policy relaxation.

When adding a resource dependency, inspect the built output and run these browser checks. Prefer
same-origin licensed assets and external scripts/styles to broad host or inline allowances. Keep
Development-only exceptions in the actual API-reference handler, not in the shared SPA policy.

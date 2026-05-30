# ApexRacers — Frontend

React + TypeScript + Vite frontend for ApexRacers. All `/api` requests are proxied to the backend API by the Vite dev server.

## Tech stack

- React 19, React Router v7
- TypeScript 6, Vite 8
- Tailwind CSS v4
- Vitest + Testing Library

## Dev servers

Run from this directory (`src/web/`):

```bash
npm install

npm run dev          # Proxy → http://localhost:5000  (local dotnet API)
npm run dev:all      # Starts dotnet API + Vite together via concurrently
npm run dev:docker   # Proxy → http://localhost:8080  (Docker Desktop API)
npm run dev:cloud    # Proxy → https://apexracers-api.azurewebsites.net
```

The dev server runs on `http://localhost:5173`. The proxy target is set by `API_TARGET` in the relevant `.env.*` file; the default (`dev` / `dev:all`) needs no env file.

## Building

```bash
npm run build    # tsc + Vite production build → dist/
npm run preview  # Serve the production build locally
npm run lint     # ESLint
```

## Testing

```bash
npm run test          # Vitest one-shot run
npm run test:watch    # Vitest in watch mode
npx vitest run --coverage   # Coverage report (80% threshold enforced)
```

Coverage is enforced at **80%** across statements, branches, functions, and lines in `vite.config.ts`. Keep all four metrics above the threshold when adding new source files.

## API client

All fetch calls go through `src/services/api.ts`. Response types there must stay in sync with `ResponseDtos.cs` in `src/ApexRacers.Api/Dtos/`.

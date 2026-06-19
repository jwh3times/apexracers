# 3.5 — Catalog explorer (cars & tracks) — design

**Date:** 2026-06-18
**Plan reference:** `private/plan.md` Part 5, slice 3.5
**Branch:** `feat/tier3-catalog-explorer`

## Goal

Browsable car & track encyclopedias with images/specs, plus a personal "your best laps here"
overlay. Public reference pages: `/cars`, `/cars/:carId`, `/tracks`, `/tracks/:trackId`.

## Decisions (from brainstorming)

- **Architecture: cache-only via the live iRacing API** (not the plan's persist+ingest). Consistent
  with 2.2/2.3 — no schema/migration/seeder/worker changes. The live API has the full catalog;
  the persisted `Car`/`Track` tables are only partially populated (Worker adds rows on-demand).
- **Personal overlay = "your best laps"** from the existing `PersonalLap` table. Percentile overlay
  (`CarPercentileResult`) is **deferred** — it's series/week-scoped and awkward on a catalog page.
- **Car detail shows car-class membership** (cheap local `CarClassCar`/`CarClass` join).
- **Two nav entries** — Cars and Tracks (not a single "Catalog" landing).
- Endpoints are **public** but personalize the overlay when a token is present (mirrors
  `ScheduleController`). 503 when iRacing creds are absent, like every other on-demand feature.

## Architecture

```
GET /api/cars[/{id}]  → CarsController (thin)  → CarCatalogService
GET /api/tracks[/{id}]→ TracksController (thin)→ TrackCatalogService
                                                      │
                         CachedIRacingClient (24h) ───┤  GetCarsAsync + GetCarAssetDetailsAsync
                         AppDbContext ────────────────┘  CarClassCar/CarClass + PersonalLap overlay
```

### Backend

**Pure mappers** (unit-tested directly, mirror `LeaderboardCsvParser`):

- `CarCatalogMapper` — `Aydsko.iRacingData.Cars.CarInfo` + `Cars.CarAssetDetail` → DTOs. Builds image
  URLs: `https://images-static.iracing.com` + `folder` + `/` + `small_image`/`large_image`; `logo` is
  a site-relative path (prefix the base). Categories/car-types passthrough.
- `TrackCatalogMapper` — `Tracks.Track` + `Tracks.TrackAssets` → DTOs. Same image-URL rule; `track_map`
  is already an absolute URL (passthrough).

**Services**

- `CarCatalogService(CachedIRacingClient cached, AppDbContext db)`
  - `ListAsync(ct)` → cached `GetCarsAsync` + `GetCarAssetDetailsAsync`, mapped to list DTOs (cache the
    mapped list).
  - `GetAsync(carId, userId?, ct)` → detail DTO: car + assets, car-class membership from local
    `CarClassCar`→`CarClass`, and the caller's `PersonalLap` bests in that car (per track) when
    `userId` is set.
- `TrackCatalogService(CachedIRacingClient cached, AppDbContext db)`
  - `ListAsync(ct)` and `GetAsync(trackId, userId?, ct)` (PersonalLap bests at that track, per car).

**DTOs** (reuse `PersonalLapDto` for "your best" rows):

- `CarClassRefDto(int CarClassId, string Name)`
- `CarCatalogItemDto(int CarId, string Name, string NameAbbreviated, string? Make, string? Model,
  int? Hp, int? Weight, bool RainEnabled, bool FreeWithSubscription, IReadOnlyList<string> Categories,
  string? SmallImageUrl)`
- `CarCatalogDetailDto(... item fields, string? LargeImageUrl, string? LogoUrl,
  IReadOnlyList<string> CarTypes, IReadOnlyList<CarClassRefDto> CarClasses,
  IReadOnlyList<PersonalLapDto> YourBestLaps)`
- `TrackCatalogItemDto(int TrackId, string Name, string ConfigName, string? Category,
  double? LengthMiles, int? CornersPerLap, string? Location, bool NightLighting, string? SmallImageUrl)`
- `TrackCatalogDetailDto(... item fields, string? LargeImageUrl, string? LogoUrl, string? TrackMapUrl,
  double? Latitude, double? Longitude, int? PitRoadSpeedLimit, int? NumberPitstalls, bool HasSvgMap,
  IReadOnlyList<PersonalLapDto> YourBestLaps)`

**Controllers (thin, public, optional personalization)**

- `CarsController` — `GET /api/cars`, `GET /api/cars/{id}`.
- `TracksController` — `GET /api/tracks`, `GET /api/tracks/{id}`.
  Both parse the `sub` claim if a token is present and pass the user id through for the overlay;
  no `[Authorize]`.

### Frontend

- `api.ts`: `CarCatalogItem` / `CarCatalogDetail` / `TrackCatalogItem` / `TrackCatalogDetail` interfaces
  + `getCars()` / `getCar(id)` / `getTracks()` / `getTrack(id)`.
- Pages (in `AppShell`, **public** like `/series` — not behind `RequireAuth`):
  - `CarsPage` `/cars`, `TracksPage` `/tracks` — image-card grids with a name search box + category
    filter chips.
  - `CarDetailPage` `/cars/:carId`, `TrackDetailPage` `/tracks/:trackId` — hero image, spec grid,
    car-classes / track-map, and a "Your best laps" card when signed in. 503/error → graceful
    "catalog unavailable" state.
- Nav: **Cars** (`directions_car`) and **Tracks** (`route`) in `AUTH_NAV` (visible in the app shell).
- Routes added to `App.tsx` inside `AppShell`, outside `RequireAuth`.

### Testing

- Backend: `CarCatalogMapperTests` / `TrackCatalogMapperTests` (URL building, field mapping, missing
  assets), `CarCatalogServiceTests` / `TrackCatalogServiceTests` (mocked `IDataClient` via
  `CachedIRacingClient` + EF for class/PersonalLap overlay, cache-hit assertions). ≥80% line + branch.
- Frontend: `CarsPage` / `CarDetailPage` / `TracksPage` / `TrackDetailPage` tests + `api.ts` cases.
  ≥80% all four metrics.

### Docs

CLAUDE.md (controllers, services, routing table, Sidebar nav, shared notes), `private/PRD.md`
(feature + screen inventory + API surface), and mark slice 3.5 done in `private/plan.md`.

## Notes

- SDK (verified against Aydsko 2603.0.0): `GetCarsAsync(ct)` → `DataResponse<CarInfo[]>`;
  `GetTracksAsync(ct)` → `DataResponse<Track[]>`; `GetCarAssetDetailsAsync(ct)` →
  `DataResponse<IReadOnlyDictionary<string, CarAssetDetail>>` (keyed by car id string);
  `GetTrackAssetsAsync(ct)` → `DataResponse<IReadOnlyDictionary<string, TrackAssets>>`.
- Image base: `https://images-static.iracing.com`. Example car small image:
  `…/img/cars/skipbarberformula2000/skipbarberformula2000-small.jpg`. Track `track_map` is absolute.
- Deviates from the plan's "expand Car/Track columns + ExpandCarTrackCatalog migration + seeder" —
  cache-only is lighter and matches the codebase's evolved pattern.

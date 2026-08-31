# TeamLab Simplification and Acceptance - 2026-08-10

## Objective

Remove the TeamLab-specific service injection feature, make a TeamLab-bound game use only its relevant management path, fix focus mode to occupy the complete viewport, then verify the mixed-runtime lifecycle on the isolated 10.0.7.118/125 test environment.

## Scope and Decisions

- TeamLab no longer stores, exposes, validates, plans, or executes service-profile references. The reusable Content bootstrap-profile capability remains outside TeamLab.
- Existing release JSON can still be decoded as historical input. Its removed `bootstrap` member is ignored and never executed.
- A game becomes TeamLab-managed only after it has a TeamLab binding. Its lifecycle controls remain on the TeamLab tab; generic challenge/runtime controls must not be offered for that binding.
- Focus mode covers the full browser viewport. It retains its own editor controls and exits cleanly through the existing toolbar action.

## Evidence Gate

- Build and focused unit/contract tests after each completed unit, then frontend validation and backend build once the integrated change is complete.
- On 118/125: inspect release identity, Agent inventory, queue health, and use an independently named mixed topology. Verify browser interaction and screenshots for editor focus mode and TeamLab game controls; verify runtime creation, remote access, traffic, pause/resume, reset, destroy, and cleanup where capacity permits.
- Do not alter the 10.24.0.27 demonstration environment or delete resources not conclusively linked to this acceptance run.

## Progress

- [x] Investigated current service injection, focus mode, and TeamLab game binding paths.
- [x] Remove TeamLab service injection contracts, persistence, API, UI, and runtime path. The generated OpenAPI contract no longer exposes service-profile endpoints.
- [ ] Separate TeamLab-bound game controls from generic competition controls.
- [ ] Repair and browser-verify full-viewport focus mode.
- [ ] Deploy to 118 and perform real environment acceptance.

## 2026-08-10 Simplification Evidence

- The authoring contract now retains only topology, asset resources, network interfaces, health checks, observability and router infrastructure. Image digests, certification, static maintenance credentials, distribution and runtime secrets remain template-library or platform responsibilities.
- The editor no longer exposes service injection, stateless recovery, publish-time baking, image digests, launch commands, environment variables, generated identifiers, display ordering or manual coordinates. Network collapse remains a direct topology-view action; drag and automatic layout own placement.
- New forward migrations remove the obsolete service-injection and authoring-override persistence. Historic `Scenario` template mode is mapped to compatible `Opaque` before the obsolete mode is removed.
- Verified locally: main application Release build, frontend TypeScript check, unit-test Release build, 265 TeamLab-focused unit tests and generated OpenAPI documentation host. Migration model check reports no pending model changes.
- Migration integration tests remain blocked by the local Docker engine being unavailable; the code path was not marked as infrastructure-verified and no server was deployed in this simplification task.

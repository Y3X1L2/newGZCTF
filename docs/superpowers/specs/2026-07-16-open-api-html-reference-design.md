# Open API HTML Reference Design

## Goal

Provide customers with a stable Swagger-style interactive HTML reference for the
platform Open API while preserving one machine-readable external contract.

## Contract Boundary

- `open-v1` remains the only external OpenAPI document.
- `/openapi/open-v1.json` is the live contract served by the application.
- `docs/commercialization/openapi/open-v1.json` is the versioned snapshot of that
  same contract.
- The internal `v1` document remains available only in Development.
- The HTML reference does not copy or transform the API schema. It reads the live
  `open-v1` document directly.

## Customer Experience

- Stable page: `/api-docs`
- Searchable operations and schemas
- Request and response examples generated from the contract
- Bearer token authentication through the document UI
- Online request execution against the current platform host
- Downloadable OpenAPI document
- TeamLab operations grouped into Topologies, Runtimes, and Traffic/Captures

## Runtime Design

NSwag registers `open-v1` in every environment. Production maps only that JSON
document and the Scalar HTML reference. Development additionally registers and
maps the internal `v1` document used by frontend generation and engineering.

Scalar is the existing UI dependency and is configured with exactly one document,
`open-v1`. Authentication state may persist in the browser to support customer
testing, but no token is embedded in HTML or server configuration.

## Verification

- Integration tests require `/api-docs` to return HTML and reference
  `/openapi/open-v1.json`.
- Existing contract snapshot comparison continues to prove that the live external
  document and the committed JSON are identical.
- A Release build verifies the production registration path compiles.


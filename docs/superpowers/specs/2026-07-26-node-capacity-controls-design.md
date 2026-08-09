# Node Capacity Controls Design

## Scope

Restore the missing node scheduling controls in the vNext node detail page without changing the runtime scheduler or Agent capability protocol.

## Ownership

- Agent facts remain read-only: Docker, KVM, TeamLab network, Fabric status, versions, and tool capabilities.
- Administrator policy remains editable: global schedulability, maximum Docker slots, and maximum VM slots.
- Setting one workload limit to zero disables new placements for that workload while leaving the other workload independent.

## UI

The existing Capacity tab keeps its utilization meters and adds a dedicated settings section with:

- Global scheduling toggle.
- Maximum Docker container slots.
- Maximum VM slots.
- Current allocated and reserved counts beside each input.
- One explicit save action with pending, success, and failure feedback.

The controls use the existing vNext form and action primitives and live in an independent component file.

## Validation And Data Flow

The form calls the existing `PATCH /api/v1/nodes/{id}` contract. Client validation requires integers and prevents a limit below the current allocated count. The server remains authoritative and rejects values below allocated plus reserved capacity, Docker limits above 10000, and VM limits above 1000. A successful save refreshes both the node detail and node list SWR caches without reloading the page.

## Compatibility

No database migration or scheduler change is required. Ordinary Docker, VM, TeamLab, image distribution, and capacity reservation flows continue to consume the same `WorkerNode.MaxContainers`, `WorkerNode.MaxVms`, and `WorkerNode.IsSchedulable` fields.

## Verification

- Component validation and save behavior.
- Node API adapter request and response contract.
- Type checking, vNext lint, focused tests, and production build.
- Browser verification on the deployed node detail page.

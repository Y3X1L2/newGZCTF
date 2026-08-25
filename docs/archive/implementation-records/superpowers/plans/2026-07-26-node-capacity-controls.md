# Node Capacity Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore editable Docker and VM scheduling limits in the vNext node detail page while keeping Agent capability facts authoritative.

**Architecture:** Add one focused capacity-settings component that owns form state and validation and delegates persistence through a callback. Reuse the existing node PATCH API and SWR refresh path; do not add fields, migrations, or scheduler branches.

**Tech Stack:** React 19, TypeScript, SWR, Vitest, Testing Library, CSS Modules, ASP.NET Core node API.

---

### Task 1: Capacity Settings Component

**Files:**
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/nodes/NodeCapacitySettings.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/nodes/NodeCapacitySettings.module.css`
- Test: `src/GZCTF/ClientApp/src/vnext/features/admin/nodes/NodeCapacitySettings.test.tsx`

- [ ] **Step 1: Add focused component tests**

Cover integer validation, the allocated-capacity lower bound, Docker/VM independence, and the exact save payload `{ isSchedulable, maxContainers, maxVms }`.

- [ ] **Step 2: Implement the component**

Use `TextField` with `type="number"`, `ToggleField`, `ActionButton`, and `InlineFeedback`. Initialize and resynchronize state from `NodeSummary`; use `allocatedContainers` and `allocatedVms` as authoritative lower bounds because they already include active reservations.

- [ ] **Step 3: Verify the component**

Run:

```powershell
pnpm exec vitest run src/vnext/features/admin/nodes/NodeCapacitySettings.test.tsx
```

Expected: all component tests pass.

### Task 2: Node Detail Integration

**Files:**
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/nodes/AdminNodeDetailPage.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/nodes/AdminNodeDetailPage.module.css`

- [ ] **Step 1: Wire the save callback**

Call:

```ts
await nodeAdminApi.update(node.id, request)
await refresh()
```

Pass the callback into `NodeCapacitySettings`; keep Agent capabilities and TeamLab status read-only.

- [ ] **Step 2: Correct capacity meter accounting**

Use `allocatedContainers` and `allocatedVms` directly as meter values. Do not add `reservedContainers` or `reservedVms` again because the server projection already includes reservations in allocated totals.

- [ ] **Step 3: Preserve no-refresh behavior**

Successful saves refresh the node detail and list SWR caches. Errors remain inline and the current form values are retained.

### Task 3: Consolidated Verification And Deployment

**Files:**
- No additional source files.

- [ ] **Step 1: Run focused and static checks**

```powershell
pnpm exec vitest run src/vnext/features/admin/nodes/NodeCapacitySettings.test.tsx src/vnext/features/admin/api/adminResourceAdapters.test.ts
pnpm check
pnpm lint:check
pnpm exec vite build
node scripts/check-bundle-budget.mjs
```

- [ ] **Step 2: Deploy the complete static artifact to `10.0.7.118`**

Atomically replace `wwwroot`, restart `gzctf.service` so the SPA fallback reloads the new index, and preserve the previous static directory for rollback.

- [ ] **Step 3: Browser acceptance**

Open a node detail page over HTTP, change Docker and VM limits independently, save without a page reload, verify the new values in the detail API, then restore the original values. Confirm capability labels remain unchanged.

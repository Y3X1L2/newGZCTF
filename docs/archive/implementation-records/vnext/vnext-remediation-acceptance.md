# YINYU vNext remediation acceptance

This checklist is the completion contract for the frontend audit remediation. An item is complete only after its code change, automated checks, and user-visible behavior have all been verified.

## Acceptance rules

- `pnpm lint:check`, `pnpm check`, `pnpm check:architecture`, `pnpm test`, and `pnpm build` must pass.
- Security and session changes require focused automated tests.
- Overlay, form, and responsive changes require browser interaction checks at desktop and mobile widths.
- No item may be marked complete from HTTP status checks alone.
- New vNext code must not import Mantine, legacy visual components, `yy-*` classes, or old page CSS.

## Remediation checklist

| ID | Problem | Required acceptance | Status |
| --- | --- | --- | --- |
| A1 | Private SWR data persisted without user isolation | No private persistent cache; all in-memory data invalidated on logout and password change; account-switch test passes | Complete |
| A2 | Hand-written Markdown sanitizer | DOMPurify allowlist is used; malicious SVG, event, protocol, and raw HTML fixtures are blocked | Complete |
| A3 | Global 10-second polling | Global refresh interval is zero; only explicitly live screens poll; configuration test passes | Complete |
| A4 | Duplicate drawer and confirmation implementations | Module, account, learner, and confirmation overlays share one accessible primitive; open/close/Escape/backdrop/focus tests pass | Complete |
| A5 | Cross-feature runtime and flag coupling | Competition and training depend on a neutral challenge-runtime domain, not on each other | Complete |
| A6 | Course environment view performs raw API work | Upload and registry requests live in a typed feature adapter; the view contains no `fetch` | Complete |
| A7 | Oversized components and brittle DOM/native dialog calls | High-risk pages are split; no `document.querySelector` form submission or `window.confirm/prompt` remains in vNext | Complete |
| A8 | Mantine remains in the vNext root | vNext root and runtime graph contain no Mantine provider, component, or stylesheet imports | Complete |
| A9 | Weak quality gates and no tests | vNext dependency/size rules, lint warnings-as-errors, and focused tests run in the build pipeline | Complete |

## Verification record

Verified on 2026-07-15:

- `pnpm build` passed the locale, lint, strict type, architecture, test, production build, artifact, and bundle gates.
- Seven test files and sixteen tests passed before the final account-switch extension; the final run includes the extended switch scenario.
- Production JavaScript and CSS contain no `@mantine/*`, `MantineProvider`, or `--mantine-scale` markers.
- Browser checks covered the authenticated home page, course learner management, learner detail drawer, course/chapter/challenge editors, and light/dark theme switching.
- Course, chapter, and challenge edit fields accepted live input without the former `Cannot read properties of null (reading 'value')` crash.
- The learner drawer scrolls vertically, has no horizontal overflow, and the page has no page-level horizontal overflow at the checked desktop viewport.
- The browser console contained no warning or error entries after the acceptance flow.

## Final browser acceptance

- Open and close the global module drawer, account drawer, learner detail drawer, and confirmation dialog repeatedly.
- Verify keyboard Escape, backdrop click, close button, focus restoration, and reduced-motion behavior.
- Log out normally and after changing password; verify all protected content disappears without a hard refresh.
- Switch between two accounts and verify no previous-account course, team, token, or management data flashes.
- Render representative game, course, chapter, challenge, theory, and post Markdown content, including malicious fixtures.
- Verify desktop widths 1366 and 1920, and mobile widths 390 and 430, without page-level horizontal overflow.

# Final YINYU Frontend Execution Plan

## Last Audit Result

最后审查发现仍有阻断项，所以执行前必须先落盘计划并按顺序处理，不能继续散改。

| Audit item | Result | Required action |
|---|---|---|
| Demo shell leakage | Real app previously used `content-section`; `YinyuDesignLab.css` still defines demo shell classes | Phase 1 isolates demo shell classes before page work |
| Layout risk | `YinyuDesignLab.css` has `content-visibility:auto` and `contain-intrinsic-size:720px` on demo `content-section` | Real app pages must use `yy-page-frame` instead |
| Brand/open-source remnants | `YinyuTheme.css` comment referenced legacy project name; OSINT locale is a challenge category | Fix comment; keep OSINT category |
| Temporary dirs ignored | Temporary design and tool dirs must not be committed | Add ignore entries |
| Formatting | Current touched files may contain trailing whitespace | Run formatter/check fix after code edits |
| Mojibake | Several previously touched files had corrupted Chinese text | Continue final grep during phase work |
| API preservation | API map is known; no backend change needed | Preserve endpoints, props, and route semantics |

## Execution Rules

- Preserve backend APIs, generated API contracts, routes, permissions, AWDP, VM, and Windows target startup flows.
- Reuse `design-lab/src/App.jsx` and `design-lab/src/styles.css` as the canonical visual source, but do not embed design-lab standalone shell classes directly into the real Mantine AppShell.
- Do not commit `design-lab/`, `gsap-skills-clone/`, `trae-bg-effect/`, `.codex-run/`, `.codegraph/`.
- Keep `ctf-screen/screen` independent display unchanged unless an import or build issue requires a minimal compatibility edit.

## Phase 1: Stabilize Global Shell

Status: done

Files:
- `src/GZCTF/ClientApp/src/App.tsx`
- `src/GZCTF/ClientApp/src/components/WithNavbar.tsx`
- `src/GZCTF/ClientApp/src/components/AppNavbar.tsx`
- `src/GZCTF/ClientApp/src/components/AppHeader.tsx`
- `src/GZCTF/ClientApp/src/components/AppFooter.tsx`
- `src/GZCTF/ClientApp/src/components/ErrorFallback.tsx`
- `src/GZCTF/ClientApp/src/styles/YinyuDesignLab.css`
- `src/GZCTF/ClientApp/src/styles/YinyuTheme.css`
- `.gitignore`

Actions:
- Restore `useBanner()` in `App.tsx`.
- Keep `SignalField` as one global singleton only.
- Ensure all background/decorative layers use `pointer-events:none`.
- Isolate design-lab standalone classes: `.app-shell`, `.home-nav`, `.admin-rail`, `.content-section`, `.app-footer`.
- Real app pages must use a new safe frame class, not design-lab standalone shell classes.
- Remove real-app dependency on `content-visibility:auto` and `contain-intrinsic-size`.
- Fix brand comments and `.gitignore`.
- Loading overlay should not blur the whole page unless the original flow intentionally blocks interaction.

Progress:
- Status: done
- Files touched: `.gitignore`, `src/GZCTF/ClientApp/src/App.tsx`, `src/GZCTF/ClientApp/src/components/AppFooter.tsx`, `src/GZCTF/ClientApp/src/hooks/useConfig.ts`, `src/GZCTF/ClientApp/src/pages/Index.tsx`, `src/GZCTF/ClientApp/src/pages/games/Index.tsx`, `src/GZCTF/ClientApp/src/pages/posts/Index.tsx`, `src/GZCTF/ClientApp/src/pages/Teams.tsx`, `src/GZCTF/ClientApp/src/pages/About.tsx`, `src/GZCTF/ClientApp/src/styles/YinyuTheme.css`
- Verification: `pnpm check` passed; real page usage of `app-shell/home-nav/admin-rail/content-section/app-footer` is gone except canonical definitions in `YinyuDesignLab.css`; mojibake grep on touched public shell files returned no matches.
- Notes: `useBanner()` was restored as a minimal YINYU brand console banner. The demo `content-section` class remains in `YinyuDesignLab.css` only as canonical design-lab source, while real pages now use `yy-page-frame`.

## Phase 2: Repair YINYU Design System Components

Status: done

Files:
- `src/GZCTF/ClientApp/src/components/yinyu/YinyuUI.tsx`
- `src/GZCTF/ClientApp/src/components/yinyu/SignalField.tsx`
- `src/GZCTF/ClientApp/src/components/yinyu/BrandMark.tsx`
- `src/GZCTF/ClientApp/src/components/yinyu/grid-distortion/*`
- `src/GZCTF/ClientApp/src/components/yinyu/trae-bg/*`
- `src/GZCTF/ClientApp/src/styles/YinyuTheme.css`

Actions:
- Directly reuse design-lab component classes: `panel-card`, `hex-field`, `status-pill`, `heartbeat-icon`, `data-bar`, `route-loader`, `state-card`, `state-page`, `admin-tab-card`.
- Add safe wrappers: `YinyuPageFrame`, `YinyuPanel`, `YinyuTableShell`, `YinyuModalShell`, `YinyuStatePage`, `YinyuAdminToolbar`, `YinyuFormSection`.
- Fix all default text and status detection.
- Cap expensive animation: background once, hover/focus only for card hex fields, no continuous table-row animation.

Progress:
- Status: done
- Files touched: `src/GZCTF/ClientApp/src/components/yinyu/YinyuUI.tsx`, `src/GZCTF/ClientApp/src/styles/YinyuTheme.css`
- Verification: `pnpm check` passed after wrapper additions; `SignalField` remains a single top-level component; reduced-motion CSS still disables major animations.
- Notes: Wrapper components bind directly to design-lab classes and provide safe real-app class names for future page migration.

## Phase 3: Public And Account Pages

Status: done

Files:
- `pages/Index.tsx`
- `pages/games/Index.tsx`
- `pages/posts/Index.tsx`
- `pages/posts/[postId]/Index.tsx`
- `pages/posts/[postId]/Edit.tsx`
- `pages/Teams.tsx`
- `pages/About.tsx`
- `pages/[...all].tsx`
- `pages/account/Login.tsx`
- `pages/account/Register.tsx`
- `pages/account/Recovery.tsx`
- `pages/account/Reset.tsx`
- `pages/account/Verify.tsx`
- `pages/account/Confirm.tsx`
- `pages/account/Pending.tsx`
- `pages/account/Profile.tsx`
- `components/AccountView.tsx`
- `components/PostCard.tsx`
- `components/mobile/PostCard.tsx`
- `components/RecentGame.tsx`
- `components/GameCard.tsx`
- `components/TeamCard.tsx`
- `components/TeamCreateModal.tsx`
- `components/TeamEditModal.tsx`

Actions:
- Preserve all existing API calls and fields.
- Reuse design-lab `home-feed`, `post-preview`, `recent-game-card`, `auth-stage`, `auth-form-card`, `auth-logo-panel`, `state-page`.
- Remove demo `home-nav`.
- Footer/logo appears only in normal document flow after scroll.
- Account pages: left normal form, right large transparent logo with grid distortion, no extra logo background box.

Progress:
- Status: done
- Files touched: `src/GZCTF/ClientApp/src/components/AccountView.tsx`, `src/GZCTF/ClientApp/src/components/PostCard.tsx`, `src/GZCTF/ClientApp/src/components/mobile/PostCard.tsx`, `src/GZCTF/ClientApp/src/components/TeamCard.tsx`, `src/GZCTF/ClientApp/src/components/TeamCreateModal.tsx`, `src/GZCTF/ClientApp/src/components/TeamEditModal.tsx`, `src/GZCTF/ClientApp/src/pages/Index.tsx`, `src/GZCTF/ClientApp/src/pages/games/Index.tsx`, `src/GZCTF/ClientApp/src/pages/posts/Index.tsx`, `src/GZCTF/ClientApp/src/pages/posts/[postId]/Index.tsx`, `src/GZCTF/ClientApp/src/pages/posts/[postId]/Edit.tsx`, `src/GZCTF/ClientApp/src/pages/Teams.tsx`, `src/GZCTF/ClientApp/src/pages/About.tsx`, `src/GZCTF/ClientApp/src/pages/[...all].tsx`, `src/GZCTF/ClientApp/src/pages/account/Confirm.tsx`, `src/GZCTF/ClientApp/src/pages/account/Profile.tsx`, `src/GZCTF/ClientApp/src/styles/YinyuTheme.css`
- Verification: `pnpm check` passed; public/account mojibake grep returned no matches; design-lab classes are now present on home, posts, games list, account, 404, team cards, and team/profile modals.
- Notes: Public/account work preserved original API calls, fields, route behavior, form submit handlers, team invite/member flows, and profile upload flows.

## Phase 4: Game Player Pages

Status: done

Files:
- `components/WithGameTab.tsx`
- `components/WithGameMonitor.tsx`
- `pages/games/[id]/Index.tsx`
- `pages/games/[id]/Challenges.tsx`
- `pages/games/[id]/Scoreboard.tsx`
- `pages/games/[id]/Awd.tsx`
- `pages/games/[id]/Theory.tsx`
- `pages/games/[id]/theory-scoreboard.tsx`
- `pages/games/[id]/monitor/*.tsx`
- `pages/game/ScenarioPlayer.tsx`
- `pages/game/IRChallengePlayer.tsx`
- `components/ChallengeCard.tsx`
- `components/ChallengePanel.tsx`
- `components/ChallengeModal.tsx`
- `components/GameChallengeModal.tsx`
- `components/GameJoinModal.tsx`
- `components/GameNoticePanel.tsx`
- `components/InstanceEntry.tsx`
- `components/VmInstanceEntry.tsx`
- `components/ScoreboardTable.tsx`
- `components/ScoreboardItemModal.tsx`
- `components/Awdp/AwdpWidgets.tsx`

Actions:
- Preserve game join/leave, challenge submit, container lifecycle, VM legacy endpoints, AWDP APIs, SignalR, theory submit, monitor exports.
- Apply design-lab `game-detail-draft`, `challenge-card`, `challenge-drawer-draft`, `score-row`, `service-map`, `round-stream`, `metric-tile`.
- Theory index uses aligned horizontal hex nodes with number, type, answered/current states.
- Do not modify VM/Windows startup flow.

Progress:
- Status: done
- Files touched: `src/GZCTF/ClientApp/src/components/WithGameTab.tsx`, `src/GZCTF/ClientApp/src/components/WithGameMonitor.tsx`, `src/GZCTF/ClientApp/src/components/ChallengeCard.tsx`, `src/GZCTF/ClientApp/src/components/ChallengePanel.tsx`, `src/GZCTF/ClientApp/src/components/ChallengeModal.tsx`, `src/GZCTF/ClientApp/src/components/GameChallengeModal.tsx`, `src/GZCTF/ClientApp/src/components/InstanceEntry.tsx`, `src/GZCTF/ClientApp/src/components/VmInstanceEntry.tsx`, `src/GZCTF/ClientApp/src/components/ScoreboardTable.tsx`, `src/GZCTF/ClientApp/src/components/Awdp/AwdpWidgets.tsx`, `src/GZCTF/ClientApp/src/pages/games/[id]/Index.tsx`, `src/GZCTF/ClientApp/src/pages/games/[id]/Challenges.tsx`, `src/GZCTF/ClientApp/src/pages/games/[id]/Scoreboard.tsx`, `src/GZCTF/ClientApp/src/pages/games/[id]/Awd.tsx`, `src/GZCTF/ClientApp/src/pages/games/[id]/Theory.tsx`, `src/GZCTF/ClientApp/src/pages/games/[id]/theory-scoreboard.tsx`, `src/GZCTF/ClientApp/src/pages/games/[id]/monitor/*.tsx`, `src/GZCTF/ClientApp/src/pages/game/ScenarioPlayer.tsx`, `src/GZCTF/ClientApp/src/pages/game/IRChallengePlayer.tsx`, `src/GZCTF/ClientApp/src/styles/YinyuTheme.css`
- Verification: `pnpm check` passed; VM/Windows startup and container lifecycle endpoints were not changed; game player mojibake grep returned no matches for targeted files.
- Notes: Player pages now use design-lab classes for challenge cards, challenge modal, scoreboard shell, theory hex index, AWDP service/round panels, instance/VM panels, monitor event rows, and Scenario/IR player panels. VM `POST /api/Game/{gameId}/Container/{challengeId}` and `DELETE /api/Game/{gameId}/Vm/{challengeId}` remain intact.

## Phase 5: Admin Pages

Status: done

Files:
- `components/admin/AdminPage.tsx`
- `components/admin/WithAdminTab.tsx`
- `components/admin/WithGameEditTab.tsx`
- all `pages/admin/**/*.tsx`
- all `components/admin/*.tsx`

Actions:
- Preserve all admin routes, table columns, forms, modals, upload flows, delete confirmations, status toggles.
- Apply design-lab `admin-shell` only as safe real-app adapted shell, not demo rail.
- Use `admin-tab-card`, `admin-toolbar`, `admin-panel`, `table-shell`, `task-row`, `deploy-step`, `image-row`.
- Finish coverage for game create, challenge create/edit, flags, notices, divisions, theory bank/paper/results, nodes, images, queue, logs, settings, users, teams, scenarios, IR.
- Screen display pages remain untouched except admin control surface styling.

Progress:
- Status: done
- Files touched: `src/GZCTF/ClientApp/src/components/admin/AttachmentUploadModal.tsx`, `src/GZCTF/ClientApp/src/components/admin/ChallengeEditCard.tsx`, `src/GZCTF/ClientApp/src/components/admin/DeployButton.tsx`, `src/GZCTF/ClientApp/src/components/admin/DivisionCard.tsx`, `src/GZCTF/ClientApp/src/components/admin/FlagEditPanel.tsx`, `src/GZCTF/ClientApp/src/components/admin/GameNoticeEditCard.tsx`, `src/GZCTF/ClientApp/src/components/admin/PostEditCard.tsx`, `src/GZCTF/ClientApp/src/components/admin/TeamWriteupCard.tsx`, `src/GZCTF/ClientApp/src/pages/admin/dashboard/Index.tsx`, `src/GZCTF/ClientApp/src/pages/admin/images/Index.tsx`, `src/GZCTF/ClientApp/src/pages/admin/Instances.tsx`, `src/GZCTF/ClientApp/src/pages/admin/Users.tsx`, `src/GZCTF/ClientApp/src/pages/admin/Teams.tsx`, `src/GZCTF/ClientApp/src/pages/admin/theory-bank.tsx`, `src/GZCTF/ClientApp/src/pages/admin/games/[id]/AwdServices.tsx`, `src/GZCTF/ClientApp/src/pages/admin/games/[id]/Phases.tsx`, `src/GZCTF/ClientApp/src/pages/admin/games/[id]/TheoryPaper.tsx`, `src/GZCTF/ClientApp/src/pages/admin/games/[id]/TheoryResults.tsx`, `src/GZCTF/ClientApp/src/pages/admin/scenarios/index.tsx`, `src/GZCTF/ClientApp/src/pages/admin/scenarios/new.tsx`, `src/GZCTF/ClientApp/src/pages/admin/ir-challenges/index.tsx`, `src/GZCTF/ClientApp/src/pages/admin/ir-challenges/new.tsx`, `src/GZCTF/ClientApp/src/pages/admin/nodes/[id]/Detail.tsx`, `src/GZCTF/ClientApp/src/pages/admin/games/[id]/challenges/[challengeId]/index.tsx`, `src/GZCTF/ClientApp/src/pages/admin/games/[id]/challenges/[challengeId]/flags/index.tsx`, `src/GZCTF/ClientApp/src/pages/admin/SubmissionReview.tsx`
- Verification: `pnpm check` passed; management `Paper/Card` grep now only reports PDF preview's paper surface and component/function names such as `NodeCard`, `DivisionCard`, `PaperQuestionEditor`; AWDP, node, image, queue, user, team, theory, scenario, IR, challenge edit, flag edit, and review routes keep their original API endpoints and fields.
- Notes: VM/Windows target startup paths were not changed. Node deployment still posts to `/api/v1/nodes` with `hostAddress`, `username`, `password`, and optional `nodeName`. PDFViewer keeps Mantine `Paper` because it represents rendered PDF pages rather than an app shell.

## Phase 6: Styles, Performance, And Cleanup

Status: done

Files:
- `src/GZCTF/ClientApp/src/styles/**/*.css`
- `src/GZCTF/ClientApp/src/utils/Brand.ts`
- `src/GZCTF/ClientApp/src/utils/I18n.tsx`
- `src/GZCTF/ClientApp/src/hooks/usePageTitle.ts`
- `src/GZCTF/ClientApp/src/hooks/useConfig.ts`

Actions:
- Remove broad global selectors that affect arbitrary Mantine children.
- Fix trailing whitespace and formatting.
- Ensure no project open-source metadata remains in UI.
- Keep OSINT challenge category translations.
- Keep `ctf-screen/screen` independent display unchanged unless needed by imports.
- Avoid changing backend API generated models except brand/open-source sanitation already present.

Progress:
- Status: done
- Files touched: `src/GZCTF/ClientApp/src/styles/**/*.css`, `src/GZCTF/ClientApp/src/hooks/useConfig.ts`, `src/GZCTF/ClientApp/src/utils/Brand.ts`, `src/GZCTF/ClientApp/src/utils/I18n.tsx`, `src/GZCTF/ClientApp/src/hooks/usePageTitle.ts`, plus formatted frontend files touched by Phases 1-5.
- Verification: `pnpm prettier`, `git diff --check`, `pnpm check`, and `pnpm build` passed. Shell-class grep has no real-app usage outside canonical `YinyuDesignLab.css`; mojibake grep is clean for TS/TSX source; brand/open-source grep only reports the legitimate OSINT challenge category `开源情报`.
- Notes: Prettier initially touched independent `ctf-screen/screen` files; those formatting-only changes were restored so the standalone big-screen display remains outside this refactor.

## Phase 7: Browser QA

Status: pending

Routes:
- Public/account routes from Phase 3.
- Game routes from Phase 4.
- Admin routes from Phase 5.

Interactions:
- Navigation, pagination, modals, forms, upload buttons, add-node modal, challenge modal, flag submit, instance controls, VM display, AWDP flag/patch forms, delete confirmations, route loading, error state.

Acceptance:
- No page wrong offset.
- No unclickable buttons.
- No footer/logo in first viewport unless content naturally reaches footer.
- No visible old-style page islands.
- No large scroll or hover jank.

Progress:
- Status: pending
- Files touched: pending
- Verification: pending
- Notes: pending

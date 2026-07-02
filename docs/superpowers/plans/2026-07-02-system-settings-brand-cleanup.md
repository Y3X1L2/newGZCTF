# System Settings Brand Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make admin system settings only expose effective brand/account/container options, connect platform name/slogans/logo to current public pages, and remove residual ineffective theme/footer/API-encryption/language-switch controls from the settings surface.

**Architecture:** Keep the existing `GlobalConfig.Slogan` storage field and encode multiple typewriter slogans as newline-separated text for backward compatibility. Add small parsing helpers on frontend and backend so existing single-slogan deployments keep working. Route all public brand marks through configurable `LogoBox`/brand components while preserving the current YINYU default appearance.

**Tech Stack:** ASP.NET Core config models/controllers, React + Mantine settings UI, SWR `/api/Config`, Vitest for frontend helpers, xUnit for backend config helpers.

---

### Task 1: Brand Config Helpers

**Files:**
- Modify: `src/GZCTF/Models/Internal/Configs.cs`
- Modify: `src/GZCTF/ClientApp/src/utils/Brand.ts`
- Test: `src/GZCTF.Test/UnitTests/Models/GlobalConfigTests.cs`
- Test: `src/GZCTF/ClientApp/src/utils/Brand.test.ts`

- [ ] Add backend helpers to normalize platform name and split/join newline slogans.
- [ ] Add frontend helpers mirroring backend behavior.
- [ ] Verify tests cover empty title, normal custom title, single slogan, multi-line slogans, comma/semicolon legacy input.

### Task 2: Settings Page Cleanup

**Files:**
- Modify: `src/GZCTF/ClientApp/src/pages/admin/Settings.tsx`
- Modify: `src/GZCTF/ClientApp/src/locales/zh-CN/admin.json`
- Modify equivalent locale files only for removed/incorrect text keys if needed.

- [ ] Replace slogan `TextInput` with editable list controls backed by newline-separated `globalConfig.slogan`.
- [ ] Hide API encryption control.
- [ ] Remove save-time clearing of `customTheme` and `footerInfo`; those fields are no longer exposed and should not be rewritten accidentally.
- [ ] Update platform-name description to remove `::CTF`.
- [ ] Keep current default three slogans when the config is still the old single default slogan.

### Task 3: Public Brand Consumption

**Files:**
- Modify: `src/GZCTF/ClientApp/src/pages/Index.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/About.tsx`
- Modify: `src/GZCTF/ClientApp/src/components/AppFooter.tsx`
- Modify: `src/GZCTF/ClientApp/src/components/IconHeader.tsx`
- Modify: `src/GZCTF/ClientApp/src/components/yinyu/BrandMark.tsx`
- Modify: `src/GZCTF/ClientApp/src/components/yinyu/grid-distortion/LogoDistortion.tsx`
- Modify: `src/GZCTF/ClientApp/src/components/GameCard.tsx`
- Modify: `src/GZCTF/ClientApp/src/components/PostCard.tsx`

- [ ] Use `useConfig()` for dynamic title/slogan/description on homepage, about page, footer, and icon header.
- [ ] Make BrandMark and LogoDistortion accept optional `src`, defaulting to current static YINYU image.
- [ ] Pass `config.logoUrl` into places that render brand/default icons.
- [ ] Keep visual structure and default YINYU display unchanged when no custom logo/config exists.

### Task 4: Residual Language Switch Audit

**Files:**
- Inspect: `src/GZCTF/ClientApp/src/utils/I18n.tsx`
- Inspect: `src/GZCTF/ClientApp/src/components/AppHeader.tsx`
- Inspect: `src/GZCTF/ClientApp/src/components/AppNavbar.tsx`

- [ ] Confirm whether a visible language selector still exists.
- [ ] If a visible selector exists in system/settings UI, remove it.
- [ ] Do not remove internal i18n infrastructure unless there is a concrete visible stale control.

### Task 5: Verification

- [ ] Run `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "GlobalConfigTests|Brand" --no-restore`.
- [ ] Run `pnpm --dir src/GZCTF/ClientApp test -- Brand.test.ts --run` if the project has Vitest wired.
- [ ] Run `pnpm --dir src/GZCTF/ClientApp check`.
- [ ] Run `dotnet build src/GZCTF/GZCTF.csproj --no-restore`.
- [ ] Run `git diff --check`.

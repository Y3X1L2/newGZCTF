<!-- SPECKIT START -->
## Current Feature

**Branch**: 001-ctf-scenario-engine
**Plan**: [specs/001-ctf-scenario-engine/plan.md](./specs/001-ctf-scenario-engine/plan.md)
**Spec**: [specs/001-ctf-scenario-engine/spec.md](./specs/001-ctf-scenario-engine/spec.md)

### Tech Stack

- Backend: ASP.NET Core (.NET 9+) / C#, Entity Framework Core, SignalR
- Frontend: React 19 + Mantine UI v9 + Tailwind CSS 4 + Vite + TypeScript 6
- Database: PostgreSQL 16+ (primary), Redis 7+ (cache/real-time)
- Infrastructure: Docker (Linux 靶机), KVM/QEMU + libvirt (Windows 靶机), Apache Guacamole (Web 桌面代理)
- Testing: xUnit (backend), Playwright (E2E)

### Key Design Decisions

1. Scenario & IRChallenge extend GZCTF's existing Challenge entity as subtypes (Game → Challenge hierarchy)
2. Windows VMs managed via KVM/QEMU + libvirt running on Linux host; Linux targets via Docker
3. Player access: Attack scenarios (self-pivoting), IR Windows targets (Guacamole web desktop proxy)
4. Time-slot reservation system for resource management (single server, max 20 concurrent environments)
5. Image management: Docker images from OCI Registry, VM disk images (.qcow2/.ova) uploaded via web admin
<!-- SPECKIT END -->

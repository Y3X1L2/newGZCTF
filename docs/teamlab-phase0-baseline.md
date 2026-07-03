# TeamLab Phase 0 Baseline

## Existing Flows That Must Not Regress

- Normal CTF Docker: create, public TCP proxy, destroy.
- AWDP: service scheduling and scoring remain on the existing path.
- VM/KVM: current libvirt default NAT and Guacamole management path remain usable.
- Existing penetration Docker fabric: existing games remain readable and deployable until migrated.
- Nginx/Redis TCP proxy: remains separate from TeamLab WireGuard UDP entry.

## Regression Commands

- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Fleet"`
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Vm"`
- `pnpm --dir src/GZCTF/ClientApp check`

## Manual Server Smoke Checks

- Create one normal Docker challenge container and verify public TCP entry.
- Destroy the container and verify Nginx/Redis mapping is released.
- Create one current VM challenge and verify Guacamole URL still resolves.
- Open one existing penetration game and verify it does not require TeamLab fields.


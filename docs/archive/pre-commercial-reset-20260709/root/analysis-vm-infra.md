# VM and Container Infrastructure Analysis

> Read-only research conducted 2026-05-19.
> All line numbers reference files under `src/GZCTF/`.

---

## 1. VmManager.cs - KVM/libvirt VM Lifecycle

**File:** `Services/VmManager.cs`

### Supported Hypervisors

KVM only. The domain XML at line 294 hardcodes `<domain type='kvm'>`. There is zero Hyper-V, VMware, VirtualBox, or ESXi support. The "Hyper-V enlightenments" in lines 306-310 are KVM-side para-virtualized features for Windows guests, not actual Hyper-V hosting.

### Remote Access Protocol

VNC exclusively. The XML at line 323: `<graphics type='vnc' port='-1' autoport='yes' listen='0.0.0.0'>`. No RDP support exists in VmManager itself. The `GetVncPort()` method (line 171) parses the allocated VNC display port.

### Abstraction

**None.** VmManager is a concrete class with no interface. CLAUDE.md mentions a planned `IVirtualMachineProvider` interface, but it does not exist in the codebase. This means:

- No way to swap Hyper-V for KVM without modifying VmManager
- No way to mock VmManager in unit tests
- No way to support multiple hypervisors side by side

### VM Creation Method

qcow2 copy-on-write template cloning (line 57). Then generates libvirt domain XML (line 69) and defines it (line 74).

Key points:
1. Template image is NOT modified (backing file)
2. Each VM gets a small overlay qcow2 delta
3. No pre-creation snapshot step - `SnapshotRevert` at line 151 assumes a snapshot already exists, but nothing in `CreateFromTemplate` or `Start` creates one

### Command Injection Risks

`RunCommandAsync` (line 235) concatenates arguments as a single string. The attack surface:
- `_libvirtUri` comes from config (`KvmSettings.LibvirtUri`) - config-level injection if attacker controls appsettings.json
- `templateImagePath` comes from `ImageTemplate.LocalFilePath` in the database
- `vmName` goes through `ToValidRFC1123String()` in `EnvironmentService`, which should strip dangerous characters

### IP Address Polling

**One-shot, no polling.** `GetIpAddress()` (line 205) makes a single `virsh domifaddr` call. Called immediately after `vmManager.Start()` at line 93:
- If VM is still booting, IP will be null
- If guest agent is not running, IP will be null
- No retry loop, no exponential backoff

### Missing virsh undefine

`Destroy()` (line 132) calls `virsh destroy` (force-stop) but there is NO `virsh undefine` or disk image cleanup. Resource leak.

---

## 2. ContainerOrchestrator.cs - Docker Orchestration

**File:** `Services/ContainerOrchestrator.cs`

### Remote Docker Host Support

Partially supported. `DockerClient` is configured via `DockerConfig.Uri` which can point to a remote host. However:
- Single `DockerClient` singleton - no connection pooling
- No node registry - every call goes to the same endpoint
- No health checks on the remote daemon endpoint
- No fallback if the remote host is unreachable

### Multi-Host Scenarios

**Not supported.** No swarm mode, no connection pooling across multiple daemons, no container placement logic, no host selection strategy.

### Network Isolation

`CreateIsolatedNetwork` (line 98) creates Docker bridge networks with `Internal = true`. Issues:
- Network creation is conditional on `stage.NetworkRules` being non-empty
- Partial failure leaves orphaned resources
- `RemoveNetwork` only targets one network

---

## 3. EnvironmentService.cs - VM/Container Router

**File:** `Services/EnvironmentService.cs`

### VM vs Container Decision

Lines 79-131: Routes based on `template.OSType`:
- `OSType.Windows` (line 81) -> `VmManager` (KVM)
- `OSType.Linux` (line 108) -> `ContainerOrchestrator` (Docker)

### Connection Protocol

- Windows VMs get `Protocol = "vnc"` (line 102)
- Linux containers get `Protocol = "docker"` (line 125)

**No RDP protocol option exists in the EnvironmentService path.** Even though `GuacamoleProxy` exists, EnvironmentService never calls it.

### Credential Management

`GenerateCredentials()` (line 266) returns `{ "username": "player", "password": <random> }`, but:
- These credentials are never applied to the VM
- VmManager has no API for setting guest OS credentials
- The credentials in GuacamoleProxy are hardcoded (see section 19)

### VM Name Reconstruction for Destroy/Reset

Lines 186-187 and 237-238 reconstruct VM names. Issues:
- `DestroyStageEnvironmentAsync` only destroys ONE VM, but multiple VMs per stage are possible
- `ResetEnvironmentAsync` only handles one VM
- If naming pattern changes, cleanup silently fails

### No Container Creation in Linux Branch

Lines 108-129: the `OSType.Linux` branch only calls `PullImageFromRegistryAsync`. It does NOT create any container.

---

## 4. Container Provider Interface

**File:** `Services/Container/Provider/IContainerProvider.cs`

Minimal generic interface: `GetProvider()` and `GetMetadata()`. More of a factory/accessor than a true provider abstraction.

---

## 5. Container Manager Interface

**File:** `Services/Container/Manager/IContainerManager.cs`

Two-method interface: `CreateContainerAsync` and `DestroyContainerAsync`. Lacks: `GetContainerStatus`, `RestartContainer`, `ExecInContainer`, `GetLogs`.

---

## 6. DockerProvider.cs - Docker Daemon Setup

**File:** `Services/Container/Provider/DockerProvider.cs`

- **TODO at line 60:** "After Docker.DotNet.Enhanced 3.132.0 is adapted by testcontainers"
- Self-attaches to networks if running inside a container
- Creates two networks: `open` and `isolated`
- Uses `.GetAwaiter().GetResult()` (sync-over-async anti-pattern) in constructor

---

## 7. KubernetesProvider.cs - K8s Cluster Setup

**File:** `Services/Container/Provider/KubernetesProvider.cs`

- Loads kube-config from file or detects in-cluster
- Creates namespace and two NetworkPolicies
- `InsertRegistrySecret` uses MD5 for naming (line 184) - deprecated and collision-prone
- Default `AllowCidr` of `["10.0.0.0/8"]` (line 173) blocks all RFC1918 internal access

---

## 8. DockerManager.cs - Container Lifecycle

**File:** `Services/Container/Manager/DockerManager.cs`

- Uses `goto` for retry logic (lines 104, 132)
- 3 retries for container creation, auto-pulls image on missing
- 2-hour hardcoded lifetime (line 221)
- `Console.WriteLine` on line 129 for image pull progress

---

## 9. KubernetesManager.cs - K8s Pod Lifecycle

**File:** `Services/Container/Manager/KubernetesManager.cs`

- Creates Pod + Service for each container
- DNS servers hardcoded to Chinese providers (line 95)
- `ImagePullPolicy = "Always"` (line 103) - unnecessary pulls
- `RestartPolicy = "Never"` (line 121) - no auto-restart on crash
- No readiness/liveness probes, no HPA, no PodDisruptionBudget

---

## 10. ImageStorage.cs - Disk Image Management

**File:** `Storage/ImageStorage.cs`

### Storage Location

Configurable via `KvmSettings.ImageStoragePath`, defaults to `/var/lib/gzctf/images`. Single local directory. No cloud storage, no distributed filesystem, no multi-node distribution.

### Image Format Support

Allowed extensions: `.qcow2`, `.ova`, `.vmdk`, `.img` (line 19). However, `VmManager.CreateFromTemplate` always calls `qemu-img create -f qcow2`. For `.vmdk` or `.ova` uploads:
- `.vmdk` needs conversion to qcow2 first
- `.ova` files are OVF archives, not raw disk images
- No conversion or extraction step exists

### Distribution Mechanism

**None.** Images are local. Multi-node deployment needs shared filesystem or explicit copy.

---

## 11. ContainerConfig Model

**File:** `Models/Internal/ContainerConfig.cs`

Contains: `Image`, `TeamId`, `ChallengeId`, `UserId`, `ExposedPort`, `Flag`, `MemoryLimit`, `CPUCount`, `StorageLimit`, `NetworkMode`.

**No VM-specific fields:** No hypervisor type, VM boot options, VNC/RDP port, disk image path.

---

## 12. KvmSettings Model

**File:** `Models/Internal/KvmSettings.cs`

Contains: `LibvirtUri`, `ImageStoragePath`, `DefaultVmMemoryMb`, `DefaultVmCpu`, `OperationTimeoutSeconds`, `MaxUploadSizeGb`.

**Missing:** Per-VM resource overrides, VNC bind address, TLS settings, networking config, storage pool name.

---

## 13. GuacamoleSettings Model

**File:** `Models/Internal/GuacamoleSettings.cs`

Contains: `GuacdHost`, `GuacamoleApiUrl`, `GuacamoleAuthToken`, `ConnectionTimeoutSeconds`.

---

## 14. Distributed/Multi-Node Capabilities

**There is ZERO code for distributed or multi-node management.**

Search across `src/GZCTF/Services/`:
- `Distributed` matches only refer to `Microsoft.Extensions.Caching.Distributed` (Redis cache)
- `Cluster` matches refer to `ClusterIP` and in-cluster config
- `Node` matches refer to Kubernetes NodePort
- `Remote` matches refer to `FileType.Remote` in attachments
- `Fleet`, `Agent` - no meaningful matches in service code
- No node registry, no health checks, no distributed locking, no multi-host scheduling

---

## 15. Complete VM Lifecycle (Current)

```
Import:   ImageStorage.SaveImageAsync()            -- saves to local disk
Create:   VmManager.CreateFromTemplate()            -- qemu-img clone + virsh define
Start:    VmManager.Start()                         -- virsh start
Access:   VmManager.GetVncPort() + GetIpAddress()   -- one-shot, no polling
          [Snapshot creation -- MISSING]
Reset:    VmManager.SnapshotRevert()                -- virsh snapshot-revert --current
Shutdown: VmManager.Shutdown()                      -- NOT called in destroy
Destroy:  VmManager.Destroy()                       -- virsh destroy (force power-off)
Delete:   [virsh undefine -- MISSING]
Cleanup:  [disk image delete -- MISSING]
```

**Key lifecycle gaps:**
1. **Snapshot creation** - `SnapshotRevert` assumes existing snapshot, but no `virsh snapshot-create-as` during provisioning
2. **virsh undefine** - domain definition stays after destroy
3. **Disk image cleanup** - cloned qcow2 never deleted
4. **Graceful shutdown** - `Shutdown()` defined but never called
5. **Multiple VMs** - destroy/reset only handle one VM

---

## 16. Windows VM Full Lifecycle - Missing Items

1. **No Hyper-V support** - VmManager is KVM-only
2. **No IVirtualMachineProvider interface** - planned but not implemented
3. **No RDP integration path** - GuacamoleProxy exists but never called
4. **VNC-only access** - EnvironmentService returns "vnc"
5. **Credentials never applied** - GenerateCredentials() returns random creds but never injected
6. **Hardcoded Guacamole credentials** - GuacamoleProxy hardcodes password
7. **No guest OS customization** - No sysprep, no unattended.xml, no cloud-init

---

## 17. RDP Protocol Support - Missing Items

1. EnvironmentService returns "vnc" (line 102) for Windows VMs
2. VmManager configures VNC in domain XML (line 323)
3. GuacamoleProxy can create RDP connections but is never called
4. No API endpoint to create Guacamole connection for a running VM
5. Guacamole connections not cleaned up when VMs destroyed

---

## 18. TODO, FIXME, NotImplementedException

| File | Line | Type | Content |
|---|---|---|---|
| DockerProvider.cs | 60 | TODO | After Docker.DotNet.Enhanced 3.132.0... |
| ContainerServiceExtension.cs | 36 | FIXME | custom IPortMapper |
| ExerciseController.cs | 16 | TODO | exercise mode support |
| CulturedLocalizer.cs | 27 | throw | NotImplementedException() |
| RepositoryBase.cs | 33 | FIXME | detect change |
| RepositoryBase.cs | 42 | throw | NotImplementedException() |
| MailSender.cs | 85-87 | TODO | Three email template items |

---

## 19. Security Concerns Summary

| Severity | Issue | Location |
|---|---|---|
| High | VNC exposed on 0.0.0.0 with no auth | VmManager.cs:323 |
| High | Hardcoded password for all Guacamole RDP connections | GuacamoleProxy.cs:78 |
| High | No virsh undefine + no disk cleanup after destroy | VmManager.cs (missing) |
| Medium | Config-level command injection via paths | VmManager.cs:237 |
| Medium | No content-type validation on image uploads | ImageStorage.cs:64 |
| Medium | MD5 for Kubernetes secret names | KubernetesProvider.cs:184 |
| Medium | Credentials from GenerateCredentials never reach VM | EnvironmentService.cs:266 |
| Low | Sync-over-async in DockerProvider constructor | DockerProvider.cs:96-155 |
| Low | Console.WriteLine for image pull progress | DockerManager.cs:129 |
| Low | No TLS for libvirt connection | VmManager.cs:30 |

---

## 20. Key Architectural Observations

1. **Container path is well-abstracted** (IContainerProvider + IContainerManager); VM path is concrete and monolithic.

2. **EnvironmentService is the single orchestrator** but has no error recovery - partial failures leave orphaned resources.

3. **GuacamoleProxy is isolated** - exists but nothing wires it into the lifecycle.

4. **Codebase assumes a single platform server** - no distributed primitives.

5. **Image format handling is inconsistent** - ImageStorage accepts vmdk/ova/img but VmManager only knows qcow2.

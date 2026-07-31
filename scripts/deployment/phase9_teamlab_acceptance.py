import argparse
import configparser
import hashlib
import io
import json
import os
import shlex
import subprocess
import sys
import tarfile
import time
import uuid
from pathlib import Path

import requests


RUNTIME_RUNNING = 5
RUNTIME_FAILED = {6, 7}
RUNTIME_DESTROYED = 10
CAPTURE_COMPLETED = 3
CAPTURE_FAILED = {4, 5, 6}


parser = argparse.ArgumentParser()
parser.add_argument("--base-url", default="http://10.0.7.118:8080")
parser.add_argument("--topology-id", required=True)
parser.add_argument("--entry-template-id", type=int)
parser.add_argument("--core-template-id", type=int)
parser.add_argument("--linux-template-id", type=int)
parser.add_argument("--windows-template-id", type=int)
parser.add_argument("--opaque-linux", action="store_true")
parser.add_argument("--opaque-windows", action="store_true")
parser.add_argument("--swap-vm-networks", action="store_true")
parser.add_argument(
    "--ad-bootstrap-profile-id",
    default="019f658f-a2e6-7516-a02a-8a1a6c21c38e",
)
parser.add_argument("--ad-bootstrap-version", type=int, default=2)
parser.add_argument("--ad-domain-fqdn", default="phase9.lab")
parser.add_argument("--ad-netbios-name", default="PHASE9")
parser.add_argument("--run-id", default=str(int(time.time())))
parser.add_argument("--traffic-url", action="append", default=[])
parser.add_argument("--traffic-command", action="append", default=[])
parser.add_argument("--wireguard-ssh-host")
parser.add_argument("--wireguard-ssh-user", default="whoami")
parser.add_argument("--wireguard-endpoint-host-override")
parser.add_argument("--wireguard-allow-ip", action="append", default=[])
parser.add_argument("--wireguard-deny-ip", action="append", default=[])
parser.add_argument("--wireguard-deny-url", action="append", default=[])
parser.add_argument("--minimum-flows", type=int, default=1)
parser.add_argument("--minimum-paths", type=int, default=1)
parser.add_argument("--network-smoke", action="store_true")
parser.add_argument("--evidence", type=Path, default=Path("artifacts/phase9-teamlab-acceptance.json"))
args = parser.parse_args()

api_token = os.environ.get("PHASE9_API_TOKEN")
ssh_password = os.environ.get("PHASE9_SSH_PASSWORD")
ad_dsrm_password = os.environ.get("PHASE9_AD_DSRM_PASSWORD")
if not api_token:
    parser.error("PHASE9_API_TOKEN is required.")

session = requests.Session()
session.headers["Authorization"] = f"Bearer {api_token}"
api = args.base_url.rstrip("/") + "/api/open/v1"
execution_id = uuid.uuid5(uuid.NAMESPACE_URL, f"gzctf-phase9:{args.run_id}").hex
evidence: dict[str, object] = {
    "runId": args.run_id,
    "executionId": execution_id,
    "topologyId": args.topology_id,
    "startedAt": time.time(),
    "steps": [],
}
runtime_id: str | None = None
runtime_destroyed = False
original_definition: dict | None = None
topology_modified = False
overlays = [
    {
        "assetKey": "entry-edge",
        "environment": {
            "NM_PORTAL_WEB_URL": "http://core-portal:8080",
            "NM_SUPPORT_UPLOAD_URL": "http://linux-service:8081",
        },
        "secrets": {},
    },
    {
        "assetKey": "core-portal",
        "environment": {
            "NM_AI_CONSOLE_API_URL": "http://linux-service:8081",
            "NM_AI_CONSOLE_API_HOST": "linux-service",
        },
        "secrets": {},
    },
    {
        "assetKey": "linux-service",
        "environment": {},
        "secrets": {"flag": "flag{phase9-linux-fabric-e2e}"},
    },
    {
        "assetKey": "ad-dc",
        "environment": {},
        "secrets": {},
    },
]


def save() -> None:
    args.evidence.parent.mkdir(parents=True, exist_ok=True)
    args.evidence.write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")


def record(step: str, value: object) -> None:
    evidence["steps"].append({"step": step, "at": time.time(), "value": value})
    save()
    print(step, json.dumps(value, ensure_ascii=False), flush=True)


def require(response: requests.Response) -> dict:
    if not response.ok:
        raise RuntimeError(f"HTTP {response.status_code}: {response.text}")
    return response.json()


def key(stage: str) -> str:
    return f"phase9-{stage}-{args.run_id}"


def result_value(result: dict, name: str):
    return result.get(name, result.get(name[0].upper() + name[1:]))


def operation_events(resource_id: str | None) -> None:
    if not resource_id:
        return
    try:
        response = session.get(f"{api}/teamlab/runtimes/{resource_id}/events", params={"limit": 100}, timeout=15)
        if response.ok:
            record("runtime-events", response.json())
    except requests.RequestException as exception:
        record("runtime-events-error", str(exception))


def wait_operation(operation_id: str, timeout: int = 1800) -> dict:
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        operation = require(session.get(f"{api}/operations/{operation_id}", timeout=15))
        state = (
            operation["status"],
            operation["stage"],
            operation.get("currentProgress"),
            operation.get("totalProgress"),
        )
        if state != last:
            print("operation", operation_id, state, flush=True)
            last = state
        if operation["status"] == 2:
            return operation
        if operation["status"] in (3, 4):
            record("operation-failed", operation)
            operation_events(operation.get("resourceId") or runtime_id)
            raise RuntimeError(json.dumps(operation, ensure_ascii=False))
        time.sleep(2)
    operation = require(session.get(f"{api}/operations/{operation_id}", timeout=15))
    record("operation-timeout", operation)
    operation_events(operation.get("resourceId") or runtime_id)
    raise TimeoutError(f"Operation {operation_id} did not complete.")


def get_runtime() -> dict:
    assert runtime_id is not None
    return require(session.get(f"{api}/teamlab/runtimes/{runtime_id}", timeout=30))


def wait_runtime(expected_status: int, minimum_generation: int, timeout: int = 900) -> dict:
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        runtime = get_runtime()
        state = (runtime["status"], runtime["stage"], runtime["generation"])
        if state != last:
            print("runtime", runtime_id, state, flush=True)
            last = state
        if runtime["status"] in RUNTIME_FAILED:
            record("runtime-failed", runtime)
            operation_events(runtime_id)
            raise RuntimeError(json.dumps(runtime, ensure_ascii=False))
        if runtime["status"] == expected_status and runtime["generation"] >= minimum_generation:
            return runtime
        time.sleep(2)
    runtime = get_runtime()
    record("runtime-timeout", runtime)
    operation_events(runtime_id)
    raise TimeoutError(f"Runtime {runtime_id} did not reach status {expected_status}.")


def wait_capture(capture_id: str, timeout: int = 300) -> dict:
    assert runtime_id is not None
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        capture = require(session.get(
            f"{api}/teamlab/runtimes/{runtime_id}/captures/{capture_id}", timeout=15))
        if capture["status"] == CAPTURE_COMPLETED:
            return capture
        if capture["status"] in CAPTURE_FAILED:
            record("capture-failed", capture)
            raise RuntimeError(json.dumps(capture, ensure_ascii=False))
        time.sleep(1)
    capture = require(session.get(
        f"{api}/teamlab/runtimes/{runtime_id}/captures/{capture_id}", timeout=15))
    record("capture-timeout", capture)
    raise TimeoutError(f"Capture {capture_id} did not complete.")


def create_access_configuration(stage: str) -> str:
    assert runtime_id is not None
    operation = require(session.post(
        f"{api}/teamlab/runtimes/{runtime_id}/access-grants",
        headers={"Idempotency-Key": key(stage)},
        json={"type": "WireGuard"},
        timeout=30,
    ))
    completed = wait_operation(operation["id"])
    grant = completed["result"]
    download_url = result_value(grant, "configurationDownloadUrl")
    if not download_url:
        raise RuntimeError("The access grant did not return a configuration download URL.")
    response = session.get(
        download_url if download_url.startswith("http") else args.base_url.rstrip("/") + download_url,
        timeout=30,
    )
    response.raise_for_status()
    record("access-grant-created", {
        "id": result_value(grant, "id"),
        "clientAddress": result_value(grant, "clientAddress"),
        "endpoint": result_value(grant, "endpoint"),
        "allowedIps": result_value(grant, "allowedIps"),
        "dns": result_value(grant, "dns"),
    })
    return response.text


def generate_wireguard_traffic(configuration: str, expect_success: bool = True) -> list[dict]:
    if not args.wireguard_ssh_host:
        raise RuntimeError("--wireguard-ssh-host is required for deterministic player-entry validation.")
    if not ssh_password:
        raise RuntimeError("PHASE9_SSH_PASSWORD is required for deterministic player-entry validation.")

    import paramiko

    parsed = configparser.ConfigParser(interpolation=None)
    parsed.read_file(io.StringIO(configuration))
    address = parsed["Interface"]["Address"].split(",", 1)[0].strip()
    private_key = parsed["Interface"]["PrivateKey"].strip()
    peer_public_key = parsed["Peer"]["PublicKey"].strip()
    endpoint = parsed["Peer"]["Endpoint"].strip()
    if args.wireguard_endpoint_host_override:
        endpoint = f"{args.wireguard_endpoint_host_override}:{endpoint.rsplit(':', 1)[1]}"
    allowed_ips = [item.strip() for item in parsed["Peer"]["AllowedIPs"].split(",") if item.strip()]
    suffix = execution_id[:8]
    namespace = f"tlacc-{suffix}"
    interface = f"wgacc-{suffix}"
    key_path = f"/tmp/{interface}.key"

    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(
        args.wireguard_ssh_host,
        username=args.wireguard_ssh_user,
        password=ssh_password,
        timeout=10,
    )
    try:
        sftp = client.open_sftp()
        try:
            with sftp.file(key_path, "w") as key_file:
                key_file.write(private_key + "\n")
            sftp.chmod(key_path, 0o600)
        finally:
            sftp.close()

        allowed_argument = ",".join(allowed_ips)
        routes = "\n".join(
            f"ip -n {shlex.quote(namespace)} route replace {shlex.quote(cidr)} dev {shlex.quote(interface)}"
            for cidr in allowed_ips
        )
        positive_checks = "\n".join(
            "ip netns exec {ns} curl -fsS -o /dev/null --connect-timeout 5 --max-time 20 "
            "-w 'allowed {url} status=%{{http_code}} bytes=%{{size_download}}\\n' {url}".format(
                ns=shlex.quote(namespace), url=shlex.quote(url))
            for url in args.traffic_url
        )
        positive_ip_checks = "\n".join(
            "ip netns exec {ns} ping -c 3 -i 1 -W 2 {target}".format(
                ns=shlex.quote(namespace), target=shlex.quote(target))
            for target in args.wireguard_allow_ip
        )
        negative_checks = "\n".join(
            "if ip netns exec {ns} curl -fsS -o /dev/null --connect-timeout 3 --max-time 8 {url}; then "
            "echo 'unexpected direct access: {url}' >&2; exit 41; else echo 'blocked {url}'; fi".format(
                ns=shlex.quote(namespace), url=shlex.quote(url))
            for url in args.wireguard_deny_url
        )
        negative_ip_checks = "\n".join(
            "if ip netns exec {ns} ping -c 1 -W 3 {target}; then "
            "echo 'unexpected direct access: {target}' >&2; exit 42; else echo 'blocked {target}'; fi".format(
                ns=shlex.quote(namespace), target=shlex.quote(target))
            for target in args.wireguard_deny_ip
        )
        script = f"""set -eu
cleanup() {{
  ip netns del {shlex.quote(namespace)} 2>/dev/null || true
  ip link del {shlex.quote(interface)} 2>/dev/null || true
  rm -f {shlex.quote(key_path)}
}}
trap cleanup EXIT
ip netns del {shlex.quote(namespace)} 2>/dev/null || true
ip link del {shlex.quote(interface)} 2>/dev/null || true
ip link add {shlex.quote(interface)} type wireguard
cat {shlex.quote(key_path)} | wg set {shlex.quote(interface)} private-key /dev/stdin peer {shlex.quote(peer_public_key)} allowed-ips {shlex.quote(allowed_argument)} endpoint {shlex.quote(endpoint)} persistent-keepalive 10
ip netns add {shlex.quote(namespace)}
ip link set {shlex.quote(interface)} netns {shlex.quote(namespace)}
ip -n {shlex.quote(namespace)} link set lo up
ip -n {shlex.quote(namespace)} address add {shlex.quote(address)} dev {shlex.quote(interface)}
ip -n {shlex.quote(namespace)} link set {shlex.quote(interface)} up
{routes}
{positive_ip_checks}
{positive_checks}
{negative_ip_checks}
{negative_checks}
"""
        stdin, stdout, stderr = client.exec_command(
            "sudo -S -p '' sh -c " + shlex.quote(script), timeout=90)
        stdin.write(ssh_password + "\n")
        stdin.flush()
        output = stdout.read().decode("utf-8", "replace")
        error = stderr.read().decode("utf-8", "replace")
        exit_code = stdout.channel.recv_exit_status()
        result = {
            "host": args.wireguard_ssh_host,
            "exitCode": exit_code,
            "stdout": output[-4000:],
            "stderr": error[-4000:],
            "expectedSuccess": expect_success,
        }
        if expect_success and exit_code != 0:
            raise RuntimeError(f"WireGuard player-entry validation failed: {json.dumps(result, ensure_ascii=False)}")
        if not expect_success and exit_code == 0:
            raise RuntimeError("A WireGuard configuration from the previous generation remained valid after reset.")
        return [result]
    finally:
        try:
            client.exec_command("rm -f " + shlex.quote(key_path), timeout=10)
        except Exception:
            pass
        client.close()


def generate_traffic(access_configuration: str | None = None) -> None:
    results = []
    if access_configuration is not None:
        results.extend(generate_wireguard_traffic(access_configuration))
    else:
        for url in args.traffic_url:
            response = requests.get(url, timeout=15)
            results.append({"url": url, "status": response.status_code, "bytes": len(response.content)})
            response.raise_for_status()
    for command in args.traffic_command:
        completed = subprocess.run(command, shell=True, check=False, capture_output=True, text=True, timeout=60)
        results.append({
            "command": command,
            "exitCode": completed.returncode,
            "stdout": completed.stdout[-4000:],
            "stderr": completed.stderr[-4000:],
        })
        if completed.returncode != 0:
            raise RuntimeError(f"Traffic command failed: {command}")
    record("traffic-generated", results)


def wait_traffic(timeout: int = 120) -> tuple[dict, dict]:
    assert runtime_id is not None
    deadline = time.monotonic() + timeout
    flows: dict = {"items": []}
    paths: dict = {"items": []}
    while time.monotonic() < deadline:
        flows = require(session.get(
            f"{api}/teamlab/runtimes/{runtime_id}/traffic/flows", params={"limit": 100}, timeout=15))
        paths = require(session.get(
            f"{api}/teamlab/runtimes/{runtime_id}/traffic/paths", params={"limit": 100}, timeout=15))
        if len(flows["items"]) >= args.minimum_flows and len(paths["items"]) >= args.minimum_paths:
            return flows, paths
        time.sleep(2)
    record("traffic-evidence-incomplete", {"flows": flows, "paths": paths})
    raise RuntimeError(
        f"Traffic evidence did not reach flows={args.minimum_flows}, paths={args.minimum_paths}.")


def destroy_runtime(raise_on_error: bool = True) -> None:
    global runtime_destroyed
    if runtime_id is None or runtime_destroyed:
        return
    try:
        operation = require(session.delete(
            f"{api}/teamlab/runtimes/{runtime_id}",
            headers={"Idempotency-Key": key("destroy")},
            timeout=30,
        ))
        destroyed = wait_operation(operation["id"])
        record("runtime-destroy-operation", destroyed)
        runtime = wait_runtime(RUNTIME_DESTROYED, 1, timeout=600)
        record("runtime-destroyed", runtime)
        runtime_destroyed = True
    except Exception as exception:
        record("runtime-destroy-error", str(exception))
        if raise_on_error:
            raise


def restore_topology(raise_on_error: bool = True) -> None:
    global topology_modified
    if original_definition is None or not topology_modified:
        return
    try:
        topology_url = f"{api}/teamlab/topologies/{args.topology_id}"
        current = require(session.get(topology_url, timeout=15))
        restore = json.loads(json.dumps(original_definition))
        restore["revision"] = current["revision"]
        restore["schemaVersion"] = current["schemaVersion"]
        operation = require(session.put(
            topology_url,
            headers={"Idempotency-Key": f"{key('topology-restore')}-{current['revision']}"},
            json=restore,
            timeout=30,
        ))
        restored = wait_operation(operation["id"])
        record("topology-restored", restored)
        topology_modified = False
    except Exception as exception:
        record("topology-restore-error", str(exception))
        if raise_on_error:
            raise


def verify_capture_archive(content: bytes, capture: dict) -> dict:
    segments = capture.get("segments", [])
    if not segments or any(item.get("status") != 5 for item in segments):
        raise RuntimeError("Capture did not upload every expected observation segment.")
    if sum(item.get("uploadedBytes", 0) for item in segments) > capture["maxBytes"]:
        raise RuntimeError("Capture segments exceeded the task-level MaxBytes budget.")

    with tarfile.open(fileobj=io.BytesIO(content), mode="r:*") as archive:
        members = {item.name: item for item in archive.getmembers() if item.isfile()}
        if "manifest.json" not in members:
            raise RuntimeError("Capture archive does not contain manifest.json.")
        manifest_file = archive.extractfile(members["manifest.json"])
        if manifest_file is None:
            raise RuntimeError("Capture manifest could not be read.")
        manifest = json.load(manifest_file)
        pcap_members = [item for name, item in members.items() if name.startswith("segments/")]
        if len(pcap_members) != len(segments):
            raise RuntimeError("Capture archive segment count does not match the API projection.")
        archive_digests = {}
        for member in pcap_members:
            stream = archive.extractfile(member)
            if stream is None:
                raise RuntimeError(f"Capture segment {member.name} could not be read.")
            payload = stream.read()
            if not payload:
                raise RuntimeError(f"Capture segment {member.name} is empty.")
            archive_digests[member.name] = hashlib.sha256(payload).hexdigest()
        expected = {item["sha256"] for item in segments}
        if set(archive_digests.values()) != expected:
            raise RuntimeError("Capture archive segment digests do not match persisted segment facts.")
        return {
            "manifest": manifest,
            "segmentCount": len(pcap_members),
            "segmentDigests": archive_digests,
        }


try:
    topology_url = f"{api}/teamlab/topologies/{args.topology_id}"
    detail = require(session.get(topology_url, timeout=15))
    original_definition = json.loads(json.dumps(detail["definition"]))
    definition = detail["definition"]
    topology_asset_keys = {asset["key"] for asset in definition["assets"]}
    overlays = [item for item in overlays if item["assetKey"] in topology_asset_keys]
    if (
        args.entry_template_id is not None
        or args.core_template_id is not None
        or args.linux_template_id is not None
        or args.windows_template_id is not None
        or args.opaque_linux
        or args.opaque_windows
    ):
        for asset in definition["assets"]:
            if asset["key"] == "entry-edge" and args.entry_template_id is not None:
                asset["imageTemplateId"] = args.entry_template_id
            elif asset["key"] == "core-portal" and args.core_template_id is not None:
                asset["imageTemplateId"] = args.core_template_id
            elif asset["key"] == "linux-service":
                if args.linux_template_id is not None:
                    asset["imageTemplateId"] = args.linux_template_id
                    asset["endpointObservation"] = 1
                if args.swap_vm_networks:
                    asset["interfaces"][0]["networkKey"] = "ad"
                    asset["interfaces"][0]["hostOffset"] = 20
                if args.opaque_linux:
                    asset["bootstrap"] = None
                    asset["endpointObservation"] = 0
            elif asset["key"] == "ad-dc":
                if args.windows_template_id is not None:
                    asset["imageTemplateId"] = args.windows_template_id
                    asset["resources"] = {
                        "cpuUnits": 40,
                        "memoryMiB": 8192,
                        "storageMiB": 30720,
                    }
                    if args.swap_vm_networks:
                        asset["interfaces"][0]["networkKey"] = "data"
                        asset["interfaces"][0]["hostOffset"] = 10
                    asset["bootstrap"] = {
                        "profileId": args.ad_bootstrap_profile_id,
                        "version": args.ad_bootstrap_version,
                        "parameters": {
                            "domain_fqdn": args.ad_domain_fqdn,
                            "netbios_name": args.ad_netbios_name,
                        },
                    }
                    asset["endpointObservation"] = 1
                    asset["bakeAtPublish"] = True
                if args.opaque_windows:
                    asset["bootstrap"] = None
                    asset["endpointObservation"] = 0
                    asset["bakeAtPublish"] = False

        ad_asset = next((asset for asset in definition["assets"] if asset["key"] == "ad-dc"), None)
        if ad_asset is not None and ad_asset.get("bootstrap") is not None:
            if not ad_dsrm_password:
                raise RuntimeError(
                    "PHASE9_AD_DSRM_PASSWORD is required when the ad-dc asset uses a bootstrap profile."
                )
            next(item for item in overlays if item["assetKey"] == "ad-dc")["secrets"][
                "safe_mode_password"
            ] = ad_dsrm_password
        update = dict(definition)
        update["revision"] = detail["revision"]
        update["schemaVersion"] = detail["schemaVersion"]
        operation = require(session.put(
            topology_url,
            headers={"Idempotency-Key": key("topology")},
            json=update,
            timeout=30,
        ))
        topology_modified = True
        record("topology-updated", wait_operation(operation["id"]))

    detail = require(session.get(topology_url, timeout=15))
    validation = require(session.post(f"{topology_url}/validate", timeout=30))
    record("topology-validation", validation)
    bake_asset_keys = {
        item["key"] for item in detail["definition"]["assets"]
        if item.get("bakeAtPublish")
    }
    scenario_overlays = [
        {"assetKey": item["assetKey"], "environment": {}, "secrets": item["secrets"]}
        for item in overlays
        if item["assetKey"] in bake_asset_keys and item.get("secrets")
    ]
    runtime_overlays = [
        item for item in overlays
        if item["assetKey"] not in bake_asset_keys
    ]

    operation = require(session.post(
        f"{topology_url}/releases",
        headers={"Idempotency-Key": key("release")},
        json={"revision": detail["revision"], "scenarioOverlays": scenario_overlays},
        timeout=30,
    ))
    published = wait_operation(operation["id"])
    release_id = published["resourceId"]
    record("release-published", published)

    plan = require(session.post(f"{topology_url}/releases/{release_id}/plan", timeout=60))
    if len(plan.get("shards", [])) < 2:
        raise RuntimeError("The acceptance plan did not produce at least two logical shards.")
    record("placement-plan", plan)

    operation = require(session.post(
        f"{api}/teamlab/runtimes",
        headers={"Idempotency-Key": key("runtime")},
        json={
            "releaseId": release_id,
            "externalReference": f"phase9-e2e-{args.run_id}",
            "overlays": runtime_overlays,
        },
        timeout=30,
    ))
    created = wait_operation(operation["id"])
    runtime_id = created["resourceId"]
    record("runtime-create-operation", created)
    runtime = wait_runtime(RUNTIME_RUNNING, 1)
    if len(runtime["shards"]) < 2 or any(item["status"] != RUNTIME_RUNNING for item in runtime["assets"]):
        raise RuntimeError("Runtime reached Running without all expected shards/assets ready.")
    record("runtime-ready", runtime)
    expected_networks = {
        item["key"]: (item["cidr"], item["gatewayIp"])
        for item in runtime["networks"]
    }
    expected_assets = {
        item["key"]: (item["kind"], item["primaryIp"])
        for item in runtime["assets"]
    }

    if args.network_smoke:
        access_configuration = create_access_configuration("access")
        generate_traffic(access_configuration)
        destroy_runtime()
        evidence["completedAt"] = time.time()
        evidence["success"] = True
        save()
        sys.exit(0)

    operation = require(session.post(
        f"{api}/teamlab/runtimes/{runtime_id}/captures",
        headers={"Idempotency-Key": key("capture-start")},
        json={"scope": "runtime", "networkKey": None, "maxSeconds": 180, "maxBytes": 268435456,
              "expiresInSeconds": 3600},
        timeout=30,
    ))
    capture_started = wait_operation(operation["id"])
    capture_id = capture_started["resourceId"]
    record("capture-started", capture_started)

    access_configuration = create_access_configuration("access")
    generate_traffic(access_configuration)
    flows, paths = wait_traffic()
    record("traffic-flows", flows)
    record("traffic-paths", paths)

    operation = require(session.post(
        f"{api}/teamlab/runtimes/{runtime_id}/captures/{capture_id}/stop",
        headers={"Idempotency-Key": key("capture-stop")},
        timeout=30,
    ))
    record("capture-stop-operation", wait_operation(operation["id"]))
    capture = wait_capture(capture_id)
    record("capture-completed", capture)
    archive = args.evidence.with_name(f"{args.evidence.stem}-{capture_id}.tar")
    response = session.get(
        f"{api}/teamlab/runtimes/{runtime_id}/captures/{capture_id}/download", timeout=120)
    response.raise_for_status()
    archive.write_bytes(response.content)
    archive_validation = verify_capture_archive(response.content, capture)
    record("capture-downloaded", {
        "path": str(archive.resolve()),
        "bytes": archive.stat().st_size,
        "sha256": hashlib.sha256(response.content).hexdigest(),
        "validation": archive_validation,
    })

    previous_generation = runtime["generation"]
    operation = require(session.post(
        f"{api}/teamlab/runtimes/{runtime_id}/reset",
        headers={"Idempotency-Key": key("reset")},
        json={"overlays": runtime_overlays},
        timeout=30,
    ))
    record("runtime-reset-operation", wait_operation(operation["id"]))
    reset_runtime = wait_runtime(RUNTIME_RUNNING, previous_generation + 1)
    if reset_runtime["generation"] != previous_generation + 1:
        raise RuntimeError("Runtime generation did not advance exactly once during reset.")
    if any(item["status"] != RUNTIME_RUNNING for item in reset_runtime["assets"]):
        raise RuntimeError("Reset reached Running without every asset ready.")
    reset_networks = {
        item["key"]: (item["cidr"], item["gatewayIp"])
        for item in reset_runtime["networks"]
    }
    reset_assets = {
        item["key"]: (item["kind"], item["primaryIp"])
        for item in reset_runtime["assets"]
    }
    if reset_networks != expected_networks or reset_assets != expected_assets:
        raise RuntimeError("Reset changed the runtime network or asset address contract.")
    record("runtime-reset-ready", reset_runtime)
    stale_access_result = generate_wireguard_traffic(access_configuration, expect_success=False)
    record("stale-access-grant-rejected", stale_access_result)
    reset_access_configuration = create_access_configuration("access-reset")
    generate_traffic(reset_access_configuration)

    destroy_runtime()
    restore_topology()
    evidence["completedAt"] = time.time()
    evidence["success"] = True
    save()
finally:
    if runtime_id is not None and not runtime_destroyed:
        operation_events(runtime_id)
        destroy_runtime(raise_on_error=False)
    if topology_modified:
        restore_topology(raise_on_error=False)
    if "success" not in evidence:
        evidence["completedAt"] = time.time()
        evidence["success"] = False
        save()

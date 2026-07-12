import http from 'k6/http';
import { check, sleep } from 'k6';
import exec from 'k6/execution';

const baseUrl = __ENV.BASE_URL || 'http://127.0.0.1:8080';
const adminToken = __ENV.ADMIN_TOKEN || '';
const gameId = __ENV.GAME_ID || '1';
const nodeIds = (__ENV.NODE_IDS || '').split(',').filter(Boolean);
const nodeTokens = (__ENV.NODE_TOKENS || '').split(',').filter(Boolean);
const flowIngestUrl = __ENV.FLOW_INGEST_URL || '';
const flowIngestToken = __ENV.FLOW_INGEST_TOKEN || '';
const revisionMutationUrl = __ENV.REVISION_MUTATION_URL || '';
const queueWakeupUrl = __ENV.QUEUE_WAKEUP_URL || '';

const scenarios = {
  scoreboards: { executor: 'constant-vus', vus: 500, duration: '2m', exec: 'scoreboard' },
  nodeHeartbeat: { executor: 'constant-arrival-rate', rate: 1000, timeUnit: '10s', duration: '2m', preAllocatedVUs: 50, exec: 'heartbeat' },
  queueReads: { executor: 'constant-arrival-rate', rate: 200, timeUnit: '1s', duration: '2m', preAllocatedVUs: 100, exec: 'queueRead' },
};

if (flowIngestUrl) {
  scenarios.flowIngest = {
    executor: 'constant-arrival-rate',
    rate: 200,
    timeUnit: '1s',
    duration: '2m',
    preAllocatedVUs: 100,
    maxVUs: 400,
    exec: 'flowIngest',
  };
}

if (revisionMutationUrl) {
  scenarios.revisionMutation = {
    executor: 'constant-arrival-rate', rate: 50, timeUnit: '1s', duration: '2m',
    preAllocatedVUs: 25, exec: 'revisionMutation',
  };
}

if (queueWakeupUrl) {
  scenarios.queueWakeup = {
    executor: 'constant-arrival-rate', rate: 50, timeUnit: '1s', duration: '2m',
    preAllocatedVUs: 25, exec: 'queueWakeup',
  };
}

export const options = {
  scenarios,
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{scenario:scoreboards}': ['p(95)<500'],
  },
};

const headers = adminToken ? { Authorization: `Bearer ${adminToken}` } : {};

export function scoreboard() {
  const response = http.get(`${baseUrl}/api/game/${gameId}/scoreboard`, { headers });
  check(response, { 'scoreboard available': (r) => r.status === 200 || r.status === 304 });
  sleep(10);
}

export function heartbeat() {
  if (nodeIds.length === 0) return;
  const nodeIndex = exec.scenario.iterationInTest % nodeIds.length;
  const nodeId = nodeIds[nodeIndex];
  const nodeToken = nodeTokens[nodeIndex] || '';
  const body = JSON.stringify({
    sequence: exec.scenario.iterationInTest + 1,
    observedAt: new Date().toISOString(),
    cpuLoad: 0.35,
    memoryLoad: 0.45,
    currentContainers: 1,
    currentVms: 0,
    usedPorts: 1,
  });
  const response = http.post(`${baseUrl}/api/v1/nodes/${nodeId}/heartbeat`, body,
    { headers: { Authorization: `Bearer ${nodeToken}`, 'Content-Type': 'application/json' } });
  check(response, { 'heartbeat accepted': (r) => r.status === 200 });
}

export function queueRead() {
  const response = http.get(`${baseUrl}/api/admin/deployment-queue?take=50`, { headers });
  check(response, { 'queue query bounded': (r) => r.status === 200 || r.status === 401 || r.status === 403 });
}

export function flowIngest() {
  const iteration = exec.scenario.iterationInTest;
  const capturedAt = new Date().toISOString();
  const samples = Array.from({ length: 100 }, (_, offset) => ({
    cursor: iteration * 100 + offset + 1,
    capturedAt,
    sourceIp: `10.10.${offset % 250}.2`,
    sourcePort: 40000 + (offset % 1000),
    destinationIp: `192.168.${offset % 250}.10`,
    destinationPort: 80,
    protocol: 'TCP',
    bytes: 512,
  }));
  const response = http.post(flowIngestUrl, JSON.stringify({ samples }), {
    headers: { Authorization: `Bearer ${flowIngestToken}`, 'Content-Type': 'application/json' },
  });
  check(response, { 'flow batch accepted': (r) => r.status >= 200 && r.status < 300 });
}

export function revisionMutation() {
  const response = http.post(revisionMutationUrl, JSON.stringify({
    iteration: exec.scenario.iterationInTest,
  }), { headers: { ...headers, 'Content-Type': 'application/json' } });
  check(response, { 'revision mutation accepted': (r) => r.status >= 200 && r.status < 300 });
}

export function queueWakeup() {
  const response = http.post(queueWakeupUrl, JSON.stringify({
    iteration: exec.scenario.iterationInTest,
  }), { headers: { ...headers, 'Content-Type': 'application/json' } });
  check(response, { 'queue ticket accepted': (r) => r.status >= 200 && r.status < 300 });
}

import http from 'k6/http';
import { check } from 'k6';
import { sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const baseUrl = (__ENV.BASE_URL || '').replace(/\/+$/, '');
const apiTokens = (__ENV.API_TOKENS || __ENV.API_TOKEN || '').split(',').map((token) => token.trim()).filter(Boolean);
const readTopologyIds = (__ENV.READ_TOPOLOGY_IDS || '').split(',').map((id) => id.trim()).filter(Boolean);
const validateTopologyIds = (__ENV.VALIDATE_TOPOLOGY_IDS || '').split(',').map((id) => id.trim()).filter(Boolean);
const planTargets = (__ENV.PLAN_TARGETS || '').split(',').map(parsePlanTarget).filter(Boolean);
const conflictTopologyId = __ENV.CONFLICT_TOPOLOGY_ID || '';
const duration = __ENV.DURATION || '20s';
const readRate = positiveInteger(__ENV.READ_RATE, 20);
const validateRate = positiveInteger(__ENV.VALIDATE_RATE, 5);
const planRate = positiveInteger(__ENV.PLAN_RATE, 2);
const readVUs = scenarioVUs(readRate);
const validateVUs = scenarioVUs(validateRate);
const planVUs = scenarioVUs(planRate);

const status2xx = new Counter('teamlab_status_2xx');
const status409 = new Counter('teamlab_status_409');
const status429 = new Counter('teamlab_status_429');
const status5xx = new Counter('teamlab_status_5xx');
const statusOther = new Counter('teamlab_status_other');
const requestDuration = new Trend('teamlab_control_request_duration', true);
const operationDurations = {
  read_topology_80: new Trend('teamlab_read_topology_80_duration', true),
  read_topology_100: new Trend('teamlab_read_topology_100_duration', true),
  validate_80: new Trend('teamlab_validate_80_duration', true),
  validate_100: new Trend('teamlab_validate_100_duration', true),
  plan_80: new Trend('teamlab_plan_80_duration', true),
  plan_100: new Trend('teamlab_plan_100_duration', true),
};

export const options = {
  scenarios: {
    large_topology_reads: {
      executor: 'constant-arrival-rate', exec: 'largeTopologyReads', rate: readRate,
      timeUnit: '1s', duration, preAllocatedVUs: readVUs, maxVUs: readVUs * 4,
    },
    large_topology_validation: {
      executor: 'constant-arrival-rate', exec: 'largeTopologyValidation', rate: validateRate,
      timeUnit: '1s', duration, preAllocatedVUs: validateVUs, maxVUs: validateVUs * 4,
    },
    large_plan_generation: {
      executor: 'constant-arrival-rate', exec: 'largePlanGeneration', rate: planRate,
      timeUnit: '1s', duration, preAllocatedVUs: planVUs, maxVUs: planVUs * 4,
    },
    same_resource_conflict: {
      executor: 'shared-iterations', exec: 'sameResourceConflict', vus: 1, iterations: 1,
      maxDuration: '45s',
    },
  },
  thresholds: { teamlab_status_5xx: ['count==0'] },
};

export function setup() {
  const required = { BASE_URL: baseUrl, API_TOKENS: apiTokens[0], CONFLICT_TOPOLOGY_ID: conflictTopologyId };
  const missing = Object.entries(required).filter(([, value]) => !value).map(([name]) => name);
  if (missing.length > 0) throw new Error(`Missing required environment variables: ${missing.join(', ')}`);
  if (readTopologyIds.length !== 2) throw new Error('READ_TOPOLOGY_IDS must contain the 80 and 100 asset topology IDs');
  if (validateTopologyIds.length !== 2) throw new Error('VALIDATE_TOPOLOGY_IDS must contain the 80 and 100 asset topology IDs');
  if (planTargets.length !== 2) throw new Error('PLAN_TARGETS must contain topologyId:releaseId:size for 80 and 100 assets');

  const response = http.get(`${baseUrl}/api/open/v1/teamlab/topologies/${conflictTopologyId}`, {
    headers: apiHeaders(apiTokens[0]),
  });
  if (response.status !== 200) throw new Error(`Conflict topology lookup returned HTTP ${response.status}`);

  return {
    conflictUpdate: updateBody(response.json()),
  };
}

export function largeTopologyReads(data) {
  const responses = http.batch(readTopologyIds.map((id, index) => [
    'GET', `${baseUrl}/api/open/v1/teamlab/topologies/${id}`, null,
    requestOptions(index === 0 ? 'read_topology_80' : 'read_topology_100'),
  ]));
  responses.forEach((response, index) => record(response, index === 0 ? 'read_topology_80' : 'read_topology_100'));
}

export function largeTopologyValidation(data) {
  const responses = http.batch(validateTopologyIds.map((id, index) => [
    'POST', `${baseUrl}/api/open/v1/teamlab/topologies/${id}/validate`, null,
    requestOptions(index === 0 ? 'validate_80' : 'validate_100'),
  ]));
  responses.forEach((response, index) => record(response, index === 0 ? 'validate_80' : 'validate_100'));
}

export function largePlanGeneration(data) {
  const target = planTargets[__ITER % planTargets.length];
  const response = http.post(
    `${baseUrl}/api/open/v1/teamlab/topologies/${target.topologyId}/releases/${target.releaseId}/plan`, null,
    requestOptions(`plan_${target.size}`, null, http.expectedStatuses(200, 409)),
  );
  record(response, `plan_${target.size}`);
}

export function sameResourceConflict(data) {
  const key = `p3-conflict-${Date.now()}`;
  const body = data.conflictUpdate;
  const responses = http.batch([
    ['PUT', `${baseUrl}/api/open/v1/teamlab/topologies/${conflictTopologyId}`,
      JSON.stringify(Object.assign({}, body, { name: `${body.name} A` })),
      requestOptions('same_resource_conflict', `${key}-a`)],
    ['PUT', `${baseUrl}/api/open/v1/teamlab/topologies/${conflictTopologyId}`,
      JSON.stringify(Object.assign({}, body, { name: `${body.name} B` })),
      requestOptions('same_resource_conflict', `${key}-b`)],
  ]);
  responses.forEach((response) => record(response, 'same_resource_conflict'));
  check(responses, {
    'both concurrent updates accepted for processing': (items) => items.every((item) => item.status === 202),
  });

  const outcomes = waitOperations(responses.map((response) => response.json('id')));
  check(outcomes, {
    'one update succeeds': (items) => items.filter((item) => item === 'succeeded').length === 1,
    'one stale update is rejected': (items) => items.filter((item) => item === 'failed').length === 1,
  });
}

function updateBody(detail) {
  return {
    revision: detail.revision, name: detail.definition.name, networks: detail.definition.networks,
    assets: detail.definition.assets, connections: detail.definition.connections, editor: detail.editor,
    infrastructure: detail.definition.infrastructure, observation: detail.definition.observation,
    schemaVersion: detail.schemaVersion,
  };
}

function record(response, operation) {
  requestDuration.add(response.timings.duration, { operation });
  if (operationDurations[operation]) operationDurations[operation].add(response.timings.duration);
  if (is2xx(response)) status2xx.add(1, { operation });
  else if (response.status === 409) status409.add(1, { operation });
  else if (response.status === 429) status429.add(1, { operation });
  else if (response.status >= 500) status5xx.add(1, { operation });
  else statusOther.add(1, { operation });
}

function apiHeaders(token) { return { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }; }
function requestOptions(operation, idempotencyKey, responseCallback) {
  const headers = apiHeaders(apiTokens[(__VU - 1) % apiTokens.length]);
  if (idempotencyKey) headers['Idempotency-Key'] = idempotencyKey;
  return { headers, tags: { operation }, responseCallback };
}
function waitOperations(ids) {
  let states = ids.map(() => 'pending');
  for (let attempt = 0; attempt < 300; attempt += 1) {
    const responses = http.batch(ids.map((id) => [
      'GET', `${baseUrl}/api/open/v1/operations/${id}`, null, requestOptions('conflict_result'),
    ]));
    states = responses.map((response) => {
      if (response.status !== 200) return 'unavailable';
      const status = String(response.json('status')).toLowerCase();
      if (status === '2' || status === 'succeeded') return 'succeeded';
      if (status === '3' || status === 'failed') return 'failed';
      return 'pending';
    });
    if (states.every((status) => status !== 'pending')) return states;
    sleep(0.1);
  }
  return states;
}
function parsePlanTarget(value) {
  const [topologyId, releaseId, size] = value.trim().split(':');
  return topologyId && releaseId && size ? { topologyId, releaseId, size } : null;
}
function is2xx(response) { return response.status >= 200 && response.status < 300; }
function positiveInteger(value, fallback) {
  const parsed = Number.parseInt(value || '', 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}
function scenarioVUs(rate) { return Math.max(2, Math.ceil(rate / 10)); }

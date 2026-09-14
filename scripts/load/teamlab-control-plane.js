import http from 'k6/http';
import { check } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const baseUrl = (__ENV.BASE_URL || '').replace(/\/+$/, '');
const apiToken = __ENV.API_TOKEN || '';
const readTopologyId = __ENV.READ_TOPOLOGY_ID || '';
const readRuntimeId = __ENV.READ_RUNTIME_ID || '';
const readOperationId = __ENV.READ_OPERATION_ID || '';
const distinctTopologyIds = (__ENV.DISTINCT_TOPOLOGY_IDS || '').split(',').map((id) => id.trim()).filter(Boolean);
const conflictTopologyId = __ENV.CONFLICT_TOPOLOGY_ID || '';
const duration = __ENV.DURATION || '30s';
const readRate = positiveInteger(__ENV.READ_RATE, 10);
const distinctRate = positiveInteger(__ENV.DISTINCT_RATE, 2);

const status2xx = new Counter('teamlab_status_2xx');
const status409 = new Counter('teamlab_status_409');
const status429 = new Counter('teamlab_status_429');
const status5xx = new Counter('teamlab_status_5xx');
const statusOther = new Counter('teamlab_status_other');
const controlRequestDuration = new Trend('teamlab_control_request_duration', true);
const acceptanceDuration = new Trend('teamlab_acceptance_duration', true);

const authHeaders = {
  Authorization: `Bearer ${apiToken}`,
  'Content-Type': 'application/json',
};
const expectedControlStatuses = http.expectedStatuses({ min: 200, max: 299 }, 409, 429);

export const options = {
  scenarios: {
    read_polling: {
      executor: 'constant-arrival-rate',
      exec: 'readPolling',
      rate: readRate,
      timeUnit: '1s',
      duration,
      preAllocatedVUs: Math.max(2, readRate),
      maxVUs: Math.max(10, readRate * 4),
    },
    distinct_resources: {
      executor: 'constant-arrival-rate',
      exec: 'distinctResources',
      rate: distinctRate,
      timeUnit: '1s',
      duration,
      preAllocatedVUs: Math.max(2, distinctRate),
      maxVUs: Math.max(10, distinctRate * 4),
    },
    same_resource_conflict: {
      executor: 'shared-iterations',
      exec: 'sameResourceConflict',
      vus: 1,
      iterations: 1,
      maxDuration: '30s',
    },
  },
  thresholds: {
    teamlab_status_5xx: ['count==0'],
  },
};

export function setup() {
  const required = {
    BASE_URL: baseUrl,
    API_TOKEN: apiToken,
    READ_TOPOLOGY_ID: readTopologyId,
    READ_RUNTIME_ID: readRuntimeId,
    READ_OPERATION_ID: readOperationId,
    CONFLICT_TOPOLOGY_ID: conflictTopologyId,
  };
  const missing = Object.entries(required).filter(([, value]) => !value).map(([name]) => name);
  if (missing.length > 0) throw new Error(`Missing required environment variables: ${missing.join(', ')}`);
  if (distinctTopologyIds.length < 2) throw new Error('DISTINCT_TOPOLOGY_IDS must contain at least two IDs');
  if (distinctTopologyIds.includes(conflictTopologyId)) {
    throw new Error('CONFLICT_TOPOLOGY_ID must not be included in DISTINCT_TOPOLOGY_IDS');
  }

  const response = http.get(`${baseUrl}/api/open/v1/teamlab/topologies/${conflictTopologyId}`, {
    headers: authHeaders,
    tags: { operation: 'conflict_setup' },
  });
  if (response.status !== 200) throw new Error(`Conflict topology lookup returned HTTP ${response.status}`);

  const topology = response.json();
  return {
    conflictUpdate: {
      revision: topology.revision,
      name: topology.definition.name,
      networks: topology.definition.networks,
      assets: topology.definition.assets,
      connections: topology.definition.connections,
      editor: topology.editor,
      infrastructure: topology.definition.infrastructure,
      observation: topology.definition.observation,
      schemaVersion: topology.schemaVersion,
    },
  };
}

export function readPolling() {
  const requests = [
    request('GET', `${baseUrl}/api/open/v1/teamlab/topologies/${readTopologyId}`, null, 'read_topology'),
    request('GET', `${baseUrl}/api/open/v1/teamlab/runtimes/${readRuntimeId}`, null, 'read_runtime'),
    request('GET', `${baseUrl}/api/open/v1/operations/${readOperationId}`, null, 'read_operation'),
  ];
  const responses = http.batch(requests);
  responses.forEach((response) => record(response, false, 'read_polling'));
  check(responses, { 'read polling returns 2xx': (items) => items.every(is2xx) });
}

export function distinctResources() {
  const responses = http.batch(distinctTopologyIds.map((id) =>
    request('POST', `${baseUrl}/api/open/v1/teamlab/topologies/${id}/validate`, null, 'validate_distinct')));
  responses.forEach((response) => record(response, false, 'validate_distinct'));
  check(responses, { 'distinct resource validation returns 2xx': (items) => items.every(is2xx) });
}

export function sameResourceConflict(data) {
  const key = `k6-conflict-${Date.now()}`;
  const first = { ...data.conflictUpdate, name: `${data.conflictUpdate.name} [k6-a]` };
  const second = { ...data.conflictUpdate, name: `${data.conflictUpdate.name} [k6-b]` };
  const responses = http.batch([
    request('PUT', `${baseUrl}/api/open/v1/teamlab/topologies/${conflictTopologyId}`,
      JSON.stringify(first), 'same_resource_conflict', key),
    request('PUT', `${baseUrl}/api/open/v1/teamlab/topologies/${conflictTopologyId}`,
      JSON.stringify(second), 'same_resource_conflict', key),
  ]);
  responses.forEach((response) => record(response, true, 'same_resource_conflict'));
  check(responses, {
    'same resource has one accepted request': (items) => items.filter(is2xx).length === 1,
    'same resource has one conflict response': (items) => items.filter((item) => item.status === 409).length === 1,
  });
}

function request(method, url, body, operation, idempotencyKey) {
  const headers = idempotencyKey
    ? { ...authHeaders, 'Idempotency-Key': idempotencyKey }
    : authHeaders;
  return {
    method,
    url,
    body,
    params: {
      headers,
      tags: { operation },
      responseCallback: expectedControlStatuses,
    },
  };
}

function record(response, acceptance, operation) {
  const tags = { operation };
  controlRequestDuration.add(response.timings.duration, tags);
  if (is2xx(response)) {
    status2xx.add(1, tags);
    if (acceptance) acceptanceDuration.add(response.timings.duration, tags);
  } else if (response.status === 409) {
    status409.add(1, tags);
  } else if (response.status === 429) {
    status429.add(1, tags);
  } else if (response.status >= 500) {
    status5xx.add(1, tags);
  } else {
    statusOther.add(1, tags);
  }
}

function is2xx(response) {
  return response.status >= 200 && response.status < 300;
}

function positiveInteger(value, fallback) {
  const parsed = Number.parseInt(value || '', 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

import useSWR from 'swr';

const fetcher = (url: string) => fetch(url).then(r => r.json());

export interface NodeInfo {
  id: string; name: string; hostAddress: string;
  status: string; capabilities: number;
  cpuLoad: number; memoryLoad: number;
  currentContainers: number; maxContainers: number;
  currentVms: number; maxVms: number;
  lastHeartbeat?: string;
}

export function useNodes() {
  const { data, error, isLoading, mutate } = useSWR<NodeInfo[]>('/api/v1/nodes', fetcher, { refreshInterval: 5000 });
  return { nodes: data, error, isLoading, mutate };
}

export function useDeploy() {
  return {
    deploy: async (composeFile?: string) => {
      const res = await fetch('/api/v1/docker/deploy', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ composeFile }) });
      return res.json();
    },
    cleanup: async (composeFile?: string) => {
      const res = await fetch('/api/v1/docker/cleanup', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ composeFile }) });
      return res.json();
    },
  };
}

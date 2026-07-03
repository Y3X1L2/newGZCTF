export interface EnableTeamLabNetworkResult {
  success?: boolean
  message?: string
  commands?: string[]
}

export async function enableTeamLabNetwork(nodeId: string, dryRun = true, tunnelIp?: string) {
  const res = await fetch(`/api/v1/nodes/${nodeId}/teamlab/enable`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ dryRun, tunnelIp: tunnelIp ?? null }),
  })
  const body = (await res.json().catch(() => ({}))) as EnableTeamLabNetworkResult & { title?: string }

  if (!res.ok) throw new Error(body.message || body.title || 'TeamLab network check failed')

  return body
}

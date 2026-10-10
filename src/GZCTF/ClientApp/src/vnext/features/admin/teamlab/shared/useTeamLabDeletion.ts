import { useRef, useState } from 'react'
import { useSWRConfig } from 'swr'
import { teamLabAdminApi, teamLabRuntimeApi } from '../api'

export type TeamLabDeleteTarget = { kind: 'scene' | 'runtime'; id: string; name: string }

export function useTeamLabDeletion(onDeleted: (target: TeamLabDeleteTarget) => void) {
  const { mutate } = useSWRConfig()
  const pending = useRef(false)
  const [target, setTarget] = useState<TeamLabDeleteTarget | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [isDeleting, setIsDeleting] = useState(false)

  const open = (next: TeamLabDeleteTarget) => {
    if (pending.current) return
    setError(null)
    setTarget(next)
  }
  const close = () => {
    if (!pending.current) setTarget(null)
  }
  const confirm = async () => {
    if (!target || pending.current) return
    pending.current = true
    setIsDeleting(true)
    setError(null)
    try {
      if (target.kind === 'scene') await teamLabAdminApi.deleteTopology(target.id)
      else await teamLabRuntimeApi.deleteRuntimeRecord(target.id)
    } catch (failure) {
      setError(failure)
      pending.current = false
      setIsDeleting(false)
      return
    }
    // DELETE already succeeded; a later read failure must not report the delete as failed.
    void mutate(key => Array.isArray(key) && typeof key[0] === 'string' &&
      ['vnext:admin:teamlab:topologies', 'vnext:admin:teamlab:runtimes', 'vnext:admin:teamlab:releases', 'teamlab:runtime-search'].includes(key[0]),
    undefined, { revalidate: true }).catch(() => undefined)
    setTarget(null)
    pending.current = false
    setIsDeleting(false)
    onDeleted(target)
  }
  return { target, error, isDeleting, open, close, confirm }
}

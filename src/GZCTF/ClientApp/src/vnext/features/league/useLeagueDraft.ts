import { useEffect, useState } from 'react'
import type { LeagueDraftModel, LeagueMatchDetail } from '@Api'

function draftFrom(detail?: LeagueMatchDetail) {
  return {
    name: detail?.match?.name ?? '',
    initialCoins: String(detail?.initialCoins ?? 0),
    topologyId: detail?.topologyId ?? '',
    releaseId: detail?.releaseId ?? '',
  }
}

export function useLeagueDraft(detail?: LeagueMatchDetail) {
  const [draft, setDraft] = useState(() => draftFrom(detail))
  const [baseline, setBaseline] = useState(() => JSON.stringify(draftFrom(detail)))
  const [revision, setRevision] = useState(detail?.match?.revision)
  const [error, setError] = useState('')
  const dirty = JSON.stringify(draft) !== baseline
  const stale = revision !== detail?.match?.revision
  function reset(next = detail) {
    const updated = draftFrom(next)
    setDraft(updated)
    setBaseline(JSON.stringify(updated))
    setRevision(next?.match?.revision)
    setError('')
  }
  useEffect(() => {
    if (!dirty && stale) reset(detail)
  }, [detail, dirty, stale])
  useEffect(() => {
    if (!dirty) return
    const warn = (event: BeforeUnloadEvent) => {
      event.preventDefault()
      event.returnValue = ''
    }
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])
  function payload(): LeagueDraftModel | undefined {
    const coins = Number(draft.initialCoins)
    if (
      !draft.name.trim() ||
      draft.name.length > 160 ||
      !/^\d+$/.test(draft.initialCoins) ||
      !Number.isSafeInteger(coins) ||
      coins > 2147483647
    ) {
      setError('请填写 1–160 字的场次名称，金币须为 0–2147483647 的整数。')
      return
    }
    if (Boolean(draft.topologyId) !== Boolean(draft.releaseId)) {
      setError('请选择该场景的发布版本，或将场景设为暂不配置。')
      return
    }
    setError('')
    return {
      name: draft.name.trim(),
      initialCoins: coins,
      topologyId: draft.topologyId || null,
      releaseId: draft.releaseId || null,
    }
  }
  return { draft, setDraft, revision, dirty, stale, error, payload, reset }
}

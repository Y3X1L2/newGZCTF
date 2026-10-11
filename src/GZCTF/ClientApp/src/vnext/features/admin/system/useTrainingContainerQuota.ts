import { useEffect, useState } from 'react'
import type { TrainingContainerPolicy } from '@Api'

export function useTrainingContainerQuota(policy: TrainingContainerPolicy | null | undefined) {
  const savedLimit = policy?.maxContainerCountPerUser
  const [draft, setDraft] = useState('')

  useEffect(() => {
    setDraft(savedLimit === undefined ? '' : String(savedLimit))
  }, [savedLimit])

  const limit = Number(draft)
  const available = savedLimit !== undefined
  const valid = draft.trim() !== '' && Number.isInteger(limit) && limit >= 0 && limit <= 2147483647

  return { draft, setDraft, limit, available, valid, dirty: available && (!valid || limit !== savedLimit) }
}

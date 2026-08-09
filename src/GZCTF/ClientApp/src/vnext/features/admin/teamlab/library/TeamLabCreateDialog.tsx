import { useEffect, useId, useState } from 'react'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import { teamLabAdminApi } from '../api'
import { compileTopologyDocument } from '../model/topologyCompiler'
import { createEmptyTopologyDocument } from '../model/topologyDocument'
import styles from './TeamLabLibraryPage.module.css'

export function TeamLabCreateDialog({
  open,
  onClose,
  onCreated,
}: {
  open: boolean
  onClose: () => void
  onCreated: (topologyId: string) => void
}) {
  const nameId = useId()
  const [name, setName] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    if (!open) {
      setName('')
      setError(null)
    }
  }, [open])

  const create = async () => {
    const normalizedName = name.trim()
    if (!normalizedName || submitting) return
    setSubmitting(true)
    setError(null)
    try {
      const topology = await teamLabAdminApi.createTopology(
        compileTopologyDocument(createEmptyTopologyDocument(normalizedName))
      )
      onCreated(topology.id)
    } catch (reason) {
      setError(reason)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <VNextDialog
      description="建立独立场景草稿，网络和资产在设计区维护。"
      eyebrow="NEW TEAMLAB SCENE"
      footer={
        <>
          <ActionButton disabled={submitting} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton disabled={!name.trim() || submitting} onClick={() => void create()} tone="primary" type="button">
            {submitting ? '正在创建' : '创建场景'}
          </ActionButton>
        </>
      }
      onClose={() => {
        if (!submitting) onClose()
      }}
      open={open}
      title="创建组网场景"
    >
      <div className={styles.dialogForm}>
        <label htmlFor={nameId}>场景名称</label>
        <input
          autoComplete="off"
          autoFocus
          id={nameId}
          maxLength={120}
          onChange={(event) => setName(event.currentTarget.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') void create()
          }}
          placeholder="例如：企业域渗透演练"
          value={name}
        />
        {error ? <InlineFeedback tone="danger">{errorMessage(error, '场景创建失败。')}</InlineFeedback> : null}
      </div>
    </VNextDialog>
  )
}

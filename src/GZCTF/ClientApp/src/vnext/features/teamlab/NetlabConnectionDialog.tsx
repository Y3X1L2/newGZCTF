import { useState } from 'react'
import { ActionButton, InlineFeedback, VNextDialog } from '../../shared/Interaction'
import { PasswordField, TextField } from '../../shared/FormControls'
import { errorMessage } from '../../shared/errors'
import { netlabApi, type NetlabConnection } from './api/netlabApi'
import styles from './NetlabWorkspace.module.css'

export function NetlabConnectionDialog({ connection, onClose, onSaved }: { connection?: NetlabConnection; onClose: () => void; onSaved: () => Promise<unknown> }) {
  const [endpoint, setEndpoint] = useState(connection?.endpoint ?? '')
  const [projectName, setProjectName] = useState(connection?.projectName ?? 'YINYU')
  const [token, setToken] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<unknown>()
  const save = async () => {
    setSaving(true)
    setError(undefined)
    try {
      await netlabApi.saveConnection({ endpoint: endpoint.trim(), projectName: projectName.trim(), ...(token ? { token } : {}) })
      setToken('')
      await onSaved()
      onClose()
    } catch (reason) { setError(reason) }
    finally { setSaving(false) }
  }
  return <VNextDialog open eyebrow="" title="连接底座" onClose={onClose} closeDisabled={saving} footer={<>
    <ActionButton onClick={onClose} disabled={saving}>取消</ActionButton>
    <ActionButton onClick={save} loading={saving} disabled={!endpoint.trim() || !projectName.trim() || (!connection?.configured && !token)} tone="primary">连接</ActionButton>
  </>}>
    <div className={styles.connectionForm}>
      <TextField label="底座地址" type="url" value={endpoint} onValueChange={setEndpoint} autoFocus />
      <TextField label="项目名称" value={projectName} onValueChange={setProjectName} />
      <PasswordField label="服务令牌" value={token} onValueChange={setToken} autoComplete="new-password" placeholder={connection?.configured ? '已保存' : ''} />
      {error ? <InlineFeedback tone="danger">{errorMessage(error, '连接失败')}</InlineFeedback> : null}
    </div>
  </VNextDialog>
}

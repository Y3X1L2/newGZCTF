import { useEffect, useState } from 'react'
import useSWR from 'swr'
import { ImageType, OSType } from '@Api'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../shared/Interaction'
import { errorMessage } from '../../../shared/errors'
import { imageTemplateAdminApi, type ImageTemplateSummary } from '../api'
import styles from './AdminImagesPage.module.css'

export function ImageRemoteAccessDialog({
  template,
  onClose,
}: {
  template: ImageTemplateSummary | null
  onClose: () => void
}) {
  const request = useSWR(template ? ['image-remote-access', template.id] : null, () =>
    imageTemplateAdminApi.remoteAccess(template!.id)
  )
  const [enabled, setEnabled] = useState(false)
  const [mode, setMode] = useState<'platformGenerated' | 'existingAccount'>('platformGenerated')
  const [username, setUsername] = useState('')
  const [credential, setCredential] = useState('')
  const [port, setPort] = useState(22)
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)

  useEffect(() => {
    if (!request.data) return
    setEnabled(request.data.enabled)
    const isUnconfiguredWindows =
      template?.osType === OSType.Windows && !request.data.enabled && !request.data.hasCredential
    setMode(isUnconfiguredWindows ? 'existingAccount' : request.data.credentialMode)
    setUsername(request.data.username ?? '')
    setCredential('')
    setPort(isUnconfiguredWindows ? 3389 : request.data.port)
  }, [request.data, template?.osType])

  const protocol =
    template?.imageType === ImageType.Docker ? 'containerTerminal' : template?.osType === OSType.Windows ? 'rdp' : 'ssh'
  const validPort = Number.isInteger(port) && port >= 1 && port <= 65535
  const needsCredential = enabled && mode === 'existingAccount'
  const missingCredential = needsCredential && !request.data?.hasCredential && !credential.trim()
  const canSave =
    !saving &&
    !request.isLoading &&
    (protocol === 'containerTerminal' || !enabled || validPort) &&
    (!needsCredential || Boolean(username.trim())) &&
    !missingCredential

  const save = async () => {
    if (!template || !canSave) return
    setSaving(true)
    setFailure(null)
    try {
      await imageTemplateAdminApi.updateRemoteAccess(template.id, {
        enabled,
        protocol,
        port: protocol === 'containerTerminal' ? 1 : port,
        username: mode === 'existingAccount' ? username : null,
        credentialMode: mode,
        credential: credential || null,
      })
      onClose()
    } catch (error) {
      setFailure(error)
    } finally {
      setSaving(false)
    }
  }

  return (
    <VNextDialog
      eyebrow="REMOTE OPERATIONS"
      footer={
        <>
          <ActionButton disabled={saving} onClick={onClose} type="button">
            取消
          </ActionButton>
          <ActionButton
            disabled={!canSave}
            onClick={() => void save()}
            tone="primary"
            type="button"
          >
            保存
          </ActionButton>
        </>
      }
      onClose={onClose}
      open={template !== null}
      title={template ? `配置 ${template.name} 的远程访问` : '配置远程访问'}
    >
      <div className={styles.remoteAccessForm}>
        {request.error ? (
          <InlineFeedback tone="danger">{errorMessage(request.error, '无法读取运维配置。')}</InlineFeedback>
        ) : null}
        {template?.osType === OSType.Windows && template.imageType !== ImageType.Docker ? (
          <InlineFeedback>普通 CTF Windows 题目使用镜像已有账号；平台不会为每个比赛实例注入新密码。</InlineFeedback>
        ) : null}
        <label>
          <input checked={enabled} onChange={(event) => setEnabled(event.target.checked)} type="checkbox" />{' '}
          启用远程访问
        </label>
        {enabled && protocol !== 'containerTerminal' ? (
          <>
            <label>
              <span>端口</span>
              <input
                max={65535}
                min={1}
                onChange={(event) => setPort(Number(event.target.value))}
                type="number"
                value={port}
              />
            </label>
            <label>
              <span>账号来源</span>
              <select onChange={(event) => setMode(event.target.value as typeof mode)} value={mode}>
                <option value="existingAccount">使用镜像已有账号（普通比赛）</option>
                <option value="platformGenerated">平台托管账号（TeamLab）</option>
              </select>
            </label>
            {mode === 'existingAccount' ? (
              <>
                <label>
                  <span>用户名</span>
                  <input onChange={(event) => setUsername(event.target.value)} value={username} />
                </label>
                <label>
                  <span>{protocol === 'rdp' ? '密码' : '密码或私钥'}</span>
                  <input
                    onChange={(event) => setCredential(event.target.value)}
                    placeholder={request.data?.hasCredential ? '留空保持现有凭据' : ''}
                    type="password"
                    value={credential}
                  />
                </label>
              </>
            ) : (
              <p>平台托管账号仅用于 TeamLab 等受管运行环境，不适用于普通 CTF Windows 题目。</p>
            )}
          </>
        ) : null}
        {enabled && protocol === 'containerTerminal' ? <p>容器使用平台网页终端，不需要保存镜像账号。</p> : null}
        {failure ? <InlineFeedback tone="danger">{errorMessage(failure, '保存失败。')}</InlineFeedback> : null}
      </div>
    </VNextDialog>
  )
}

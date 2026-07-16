import { Upload } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { FileField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../shared/Interaction'
import { errorMessage } from '../../../shared/errors'
import { gameAdminApi } from '../api'
import { validateGameImportFile } from './gamePresentation'
import styles from './GameDialogs.module.css'

function fileSize(value: number) {
  return value >= 1024 * 1024 ? `${(value / 1024 / 1024).toFixed(1)} MB` : `${Math.ceil(value / 1024)} KB`
}

export function GameImportDialog({
  open,
  onClose,
  onImported,
}: {
  open: boolean
  onClose: () => void
  onImported: (gameId: number) => void
}) {
  const [file, setFile] = useState<File | null>(null)
  const [progress, setProgress] = useState(0)
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)
  const issues = useMemo(() => (file ? validateGameImportFile(file) : []), [file])

  useEffect(() => {
    if (!open) return
    setFile(null)
    setProgress(0)
    setFailure(null)
  }, [open])

  const submit = async () => {
    if (!file) {
      setFailure('请选择比赛 ZIP 包。')
      return false
    }
    const validation = validateGameImportFile(file)
    if (validation.length) {
      setFailure(validation[0])
      return false
    }
    setSaving(true)
    setFailure(null)
    try {
      const gameId = await gameAdminApi.importGame(file, ({ loaded, total }) => {
        setProgress(total ? Math.round((loaded / total) * 100) : 0)
      })
      onImported(gameId)
      return true
    } catch (requestError) {
      setFailure(errorMessage(requestError, '比赛导入失败。'))
      return false
    } finally {
      setSaving(false)
    }
  }

  return (
    <VNextDialog
      description="当前服务器会直接执行导入；确认前请核对文件来自可信的赛事导出包。"
      eyebrow="GAME PACKAGE"
      footer={
        <>
          <ActionButton disabled={saving} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton disabled={saving || !file || issues.length > 0} icon={<Upload size={16} />} onClick={() => void submit()} tone="primary" type="button">
            {saving ? `正在导入${progress ? ` ${progress}%` : ''}` : '确认导入'}
          </ActionButton>
        </>
      }
      onClose={onClose}
      open={open}
      title="导入比赛"
    >
      <div className={styles.stack}>
        {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
        <FileField accept=".zip,application/zip,application/x-zip-compressed" label="比赛 ZIP 包" onChange={setFile} />
        {file ? (
          <dl className={styles.summaryGrid}>
            <div><dt>文件名</dt><dd>{file.name}</dd></div>
            <div><dt>大小</dt><dd>{fileSize(file.size)}</dd></div>
            <div><dt>类型</dt><dd>{file.type || '未知'}</dd></div>
          </dl>
        ) : null}
        {issues.length ? <InlineFeedback tone="danger">{issues[0]}</InlineFeedback> : null}
      </div>
    </VNextDialog>
  )
}

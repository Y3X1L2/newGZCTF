import { Download, RefreshCw, Trash2, Upload } from 'lucide-react'
import { useRef, useState } from 'react'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import type { TeamLabRuntime } from '../api'
import type { AssetFileEntry } from '../api/teamlabAssetFilesApi'
import { useAssetFiles } from './useAssetFiles'
import styles from './AssetTransferPanel.module.css'

export function AssetTransferPanel({ runtime, assetId }: { runtime: TeamLabRuntime; assetId: number }) {
  const files = useAssetFiles(runtime.id, runtime.generation, assetId)
  const upload = useRef<HTMLInputElement>(null)
  const [overwrite, setOverwrite] = useState<File | null>(null)
  const [remove, setRemove] = useState<AssetFileEntry | null>(null)
  const submitUpload = async (file: File, replace = false) => {
    if (!replace && files.entries?.some(item => item.name === file.name)) { setOverwrite(file); return }
    await files.upload(file, replace)
  }
  return <section aria-label="传文件" className={styles.tool}>
    <form onSubmit={event => { event.preventDefault(); void files.browse(files.draftPath) }}>
      <label>目录路径<input aria-label="目录路径" value={files.draftPath} onChange={event => files.setDraftPath(event.currentTarget.value)} /></label>
      <ActionButton aria-label="读取目录" icon={<RefreshCw size={16} />} disabled={files.busy} type="submit" />
    </form>
    <div className={styles.actions}><input aria-label="选择上传文件" ref={upload} type="file" hidden onChange={event => {
      const file = event.currentTarget.files?.[0]; if (file) void submitUpload(file); event.currentTarget.value = ''
    }} />
      <ActionButton icon={<Upload size={16} />} disabled={files.busy || !files.entries} onClick={() => upload.current?.click()} type="button">上传文件</ActionButton>
    </div>
    {files.error ? <InlineFeedback tone="danger">{errorMessage(files.error, '文件操作失败。')}</InlineFeedback> : null}
    {files.notice ? <InlineFeedback tone="success">{files.notice}</InlineFeedback> : null}
    {files.entries === null ? <DataState loading={files.busy} title={files.busy ? '正在读取目录' : '目录读取失败'} />
      : files.entries.length ? <ul className={styles.entries}>{files.entries.map(entry => <li key={entry.name}>
        <span>{entry.name}</span><small>{entry.kind === 'directory' ? '目录' : '文件'}</small>
        {entry.kind === 'directory' ? <ActionButton onClick={() => void files.browse(files.childPath(entry.name))} type="button">打开</ActionButton>
          : entry.kind === 'file' ? <ActionButton aria-label={`下载 ${entry.name}`} icon={<Download size={16} />} disabled={files.busy} onClick={() => void files.download(entry.name)} type="button" /> : null}
        {entry.kind !== 'restricted' ? <ActionButton aria-label={`删除 ${entry.name}`} icon={<Trash2 size={16} />} disabled={files.busy} onClick={() => setRemove(entry)} type="button" /> : null}
      </li>)}</ul> : <DataState title="空目录" />}
    <VNextConfirmDialog open={overwrite !== null} onClose={() => setOverwrite(null)} title={`覆盖 ${overwrite?.name ?? ''}`}
      message="现有文件会被替换。" confirmLabel="覆盖" tone="danger" onConfirm={async () => overwrite !== null && await files.upload(overwrite, true)} />
    <VNextConfirmDialog open={remove !== null} onClose={() => setRemove(null)} title={`删除 ${remove?.name ?? ''}`}
      message={remove?.kind === 'directory' ? '目录及其内容将被删除。' : '删除后无法恢复。'} confirmLabel="删除" tone="danger"
      onConfirm={async () => remove !== null && await files.remove(remove.name, remove.kind === 'directory')} />
  </section>
}

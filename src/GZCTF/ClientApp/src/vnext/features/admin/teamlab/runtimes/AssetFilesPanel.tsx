import { ArrowUp, Download, Folder, RefreshCw, Trash2, Upload } from 'lucide-react'
import { useId, useRef, useState } from 'react'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import type { TeamLabRuntime } from '../api'
import { useAssetFiles } from './useAssetFiles'
import styles from './RuntimePanels.module.css'

export function AssetFilesPanel({ runtime }: { runtime: TeamLabRuntime }) {
  const [selected, setSelected] = useState<number>()
  const assets = runtime.assets.filter(asset => asset.kind === 'docker' || asset.kind === 'vm')
  const asset = assets.find(item => item.id === selected) ?? assets[0]
  const title = useId()
  return <section className={styles.panel} aria-labelledby={title}>
    <header className={styles.panelHeader}><h3 id={title}>资产文件管理</h3></header>
    {!asset ? <DataState title="暂无可管理文件的资产" /> : <>
      <label className={styles.fileAssetSelector}>资产<select value={asset.id} onChange={event => setSelected(Number(event.target.value))}>
        {assets.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
      </select></label>
      {asset.kind === 'vm' ? <p>通过 SSH 运维账号访问虚拟机文件，权限与该账号一致。首次连接自动登记身份，单文件上限 8 MiB。</p> : null}
      <AssetFileWorkspace key={`${runtime.id}:${runtime.generation}:${asset.id}`} runtime={runtime} assetId={asset.id} />
    </>}
  </section>
}

function AssetFileWorkspace({ runtime, assetId }: { runtime: TeamLabRuntime; assetId: number }) {
  const files = useAssetFiles(runtime.id, runtime.generation, assetId)
  const input = useRef<HTMLInputElement>(null)
  const [pendingDelete, setPendingDelete] = useState<string | null>(null)
  const [pendingUpload, setPendingUpload] = useState<File | null>(null)
  const [resetIdentity, setResetIdentity] = useState(false)
  const unavailable = files.busy || !['running', 'failed'].includes(runtime.status)
  return <>
    <form className={styles.fileToolbar} onSubmit={event => { event.preventDefault(); void files.browse(files.draftPath) }}>
      <label>目录<input value={files.draftPath} disabled={files.busy} onChange={event => files.setDraftPath(event.target.value)} /></label>
      <ActionButton icon={<RefreshCw size={16} />} disabled={unavailable} type="submit">读取目录</ActionButton>
      <ActionButton icon={<ArrowUp size={16} />} disabled={unavailable || files.path === '/'} type="button"
        onClick={() => void files.browse(files.path.slice(0, files.path.lastIndexOf('/')) || '/')}>上级目录</ActionButton>
      <ActionButton icon={<Upload size={16} />} disabled={unavailable || !files.entries} type="button"
        onClick={() => input.current?.click()}>上传文件</ActionButton>
      {runtime.assets.find(item => item.id === assetId)?.kind === 'vm' ? <ActionButton disabled={unavailable} type="button"
        onClick={() => setResetIdentity(true)}>重新登记 SSH 身份</ActionButton> : null}
      <input ref={input} hidden type="file" aria-label="上传文件" onChange={event => {
        const file = event.target.files?.[0]
        event.target.value = ''
        if (!file) return
        if (files.entries?.some(entry => entry.name === file.name)) setPendingUpload(file)
        else void files.upload(file, false)
      }} />
    </form>
    {files.error ? <InlineFeedback tone="danger">{errorMessage(files.error, '文件操作失败。')}</InlineFeedback> : null}
    {files.notice ? <InlineFeedback tone="neutral">{files.notice}</InlineFeedback> : null}
    {files.busy ? <DataState loading title="正在处理文件" /> : files.entries === null ? <DataState title="尚未读取目录" />
      : !files.entries.length ? <DataState title="此目录为空" /> : <div className={styles.fileTable}>
        <table><thead><tr><th>名称</th><th>类型</th><th>字节数</th><th>操作</th></tr></thead>
          <tbody>{files.entries.map(entry => <tr key={entry.name}>
            <td>{entry.kind === 'directory' ? <button type="button" onClick={() => void files.browse(files.childPath(entry.name))}>
              <Folder size={16} />{entry.name}</button> : entry.name}</td>
            <td>{entry.kind === 'directory' ? '目录' : entry.kind === 'file' ? '文件' : '受限项'}</td><td>{entry.kind === 'file' ? entry.size : '-'}</td>
            <td>{entry.kind === 'file' ? <ActionButton icon={<Download size={16} />} type="button" disabled={unavailable}
              onClick={() => void files.download(entry.name)}>下载</ActionButton> : null}
              {entry.kind !== 'restricted' ? <ActionButton icon={<Trash2 size={16} />} type="button" tone="danger" disabled={unavailable}
                onClick={() => setPendingDelete(entry.name)}>删除</ActionButton> : null}</td>
          </tr>)}</tbody></table></div>}
    <VNextConfirmDialog open={pendingDelete !== null} onClose={() => setPendingDelete(null)} title="删除文件或空目录"
      description={`删除 ${pendingDelete ?? ''}，此操作不可撤销；非空目录不会递归删除。`} confirmLabel="确认删除" tone="danger"
      message="仅删除选中的文件或空目录。"
      onConfirm={async () => pendingDelete !== null && await files.remove(pendingDelete)} />
    <VNextConfirmDialog open={pendingUpload !== null} onClose={() => setPendingUpload(null)} title="覆盖同名文件"
      description={`覆盖 ${pendingUpload?.name ?? ''} 的现有内容，此操作不可撤销。`} confirmLabel="确认覆盖" tone="danger"
      message="新内容上传成功后才替换旧文件。"
      onConfirm={async () => pendingUpload !== null && await files.upload(pendingUpload, true)} />
    <VNextConfirmDialog open={resetIdentity} onClose={() => setResetIdentity(false)} title="重新登记 SSH 身份"
      description="适用于刚重装虚拟机或更新 SSH 密钥的情况，需要资产生命周期管理权限。"
      message="请确认连接目标仍是当前虚拟机。此操作不会重建虚拟机或修改磁盘内容。" confirmLabel="确认重新登记"
      onConfirm={files.resetIdentity} />
  </>
}

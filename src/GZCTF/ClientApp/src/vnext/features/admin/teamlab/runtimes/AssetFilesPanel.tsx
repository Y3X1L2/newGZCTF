import { ChevronRight, Download, File as FileIcon, Folder, FolderPlus, Move, Pencil, RefreshCw, Trash2, Upload } from 'lucide-react'
import { ActionMenu, type ActionMenuItem } from '../../../../shared/ActionMenu'
import { useRef, useState, type ReactNode } from 'react'
import { ActionButton, InlineFeedback, VNextConfirmDialog, VNextDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import type { TeamLabRuntime } from '../api'
import type { AssetFileEntry } from '../api/teamlabAssetFilesApi'
import { formatBytes } from './runtimePresentation'
import { useAssetFiles } from './useAssetFiles'
import styles from './AssetFilesPanel.module.css'

export function AssetFilesPanel({ runtime, assetId }: { runtime: TeamLabRuntime; assetId: number }) {
  return <AssetFileWorkspace key={`${runtime.id}:${runtime.generation}:${assetId}`} runtime={runtime} assetId={assetId} />
}

function AssetFileWorkspace({ runtime, assetId }: { runtime: TeamLabRuntime; assetId: number }) {
  const files = useAssetFiles(runtime.id, runtime.generation, assetId)
  const upload = useRef<HTMLInputElement>(null)
  const [selectedName, setSelectedName] = useState<string | null>(null)
  const [collapsed, setCollapsed] = useState<Set<string>>(() => new Set())
  const [menuName, setMenuName] = useState<string | null>(null)
  const [pendingDelete, setPendingDelete] = useState<AssetFileEntry | null>(null)
  const [pendingUpload, setPendingUpload] = useState<File | null>(null)
  const [resetIdentity, setResetIdentity] = useState(false)
  const [dialog, setDialog] = useState<'mkdir' | 'rename' | 'move' | null>(null)
  const [target, setTarget] = useState<AssetFileEntry | null>(null)
  const [value, setValue] = useState('')
  const entries = [...files.entries ?? []].sort((a, b) => Number(b.kind === 'directory') - Number(a.kind === 'directory') || a.name.localeCompare(b.name))
  const selected = entries.find(entry => entry.name === selectedName)
  const unavailable = files.busy || !['running', 'failed'].includes(runtime.status)
  const navigate = (path: string) => { setSelectedName(null); void files.browse(path) }
  const edit = (action: 'rename' | 'move', entry: AssetFileEntry) => {
    setTarget(entry); setValue(action === 'rename' ? entry.name : files.childPath(entry.name)); setDialog(action)
  }
  const submit = async () => {
    const ok = dialog === 'mkdir' ? await files.mkdir(value.trim()) : target && await files.move(target.name, dialog === 'rename' ? files.childPath(value.trim()) : value.trim())
    if (ok) { setDialog(null); setSelectedName(null) }
  }
  const folder = (path: string, name: string): ReactNode => <li key={path}>
    <div className={styles.treeRow}>
      <button aria-label={`${collapsed.has(path) ? '展开' : '折叠'} ${name}`} aria-expanded={!collapsed.has(path)} disabled={files.busy}
        onClick={() => { setCollapsed(current => { const next = new Set(current); if (next.has(path)) next.delete(path); else next.add(path); return next }); if (!files.directories[path]) navigate(path) }} type="button">
        <ChevronRight className={!collapsed.has(path) ? styles.expanded : undefined} size={14} />
      </button>
      <button aria-current={files.path === path ? 'location' : undefined} disabled={files.busy} onClick={() => navigate(path)} type="button"><Folder size={16} /><span>{name}</span></button>
    </div>
    {!collapsed.has(path) && files.directories[path] ? <ul>{files.directories[path].map(entry => folder(`${path === '/' ? '' : path}/${entry.name}`, entry.name))}</ul> : null}
  </li>
  const itemActions = (entry: AssetFileEntry): ActionMenuItem[] => [
    ...(entry.kind === 'file' ? [{ label: '下载', disabled: unavailable, icon: <Download size={15} />, onSelect: () => void files.download(entry.name) }] : []),
    { label: '重命名', disabled: unavailable || entry.kind === 'restricted', icon: <Pencil size={15} />, onSelect: () => edit('rename', entry) },
    { label: '移动', disabled: unavailable || entry.kind === 'restricted', icon: <Move size={15} />, onSelect: () => edit('move', entry) },
    { label: '删除', disabled: unavailable || entry.kind === 'restricted', danger: true, separator: true, icon: <Trash2 size={15} />, onSelect: () => setPendingDelete(entry) },
  ]

  return <section className={styles.workspace} aria-label="资产文件管理">
    <aside className={styles.directoryTree}><strong>目录</strong><ul>{folder('/', '/')}</ul></aside>
    <div className={styles.filePane}>
      <form className={styles.addressBar} onSubmit={event => { event.preventDefault(); navigate(files.draftPath) }}>
        <Folder size={17} /><input aria-label="目录路径" value={files.draftPath} disabled={files.busy} onChange={event => files.setDraftPath(event.currentTarget.value)} />
        <ActionButton aria-label="刷新目录" title="刷新目录" icon={<RefreshCw size={16} />} disabled={unavailable} type="submit" />
      </form>
      <div className={styles.toolbar}>
        <ActionButton icon={<Upload size={16} />} disabled={unavailable || files.entries === null} onClick={() => upload.current?.click()} type="button">上传</ActionButton>
        <ActionButton aria-label="新建目录" title="新建目录" icon={<FolderPlus size={17} />} disabled={unavailable || files.entries === null} onClick={() => { setValue(''); setDialog('mkdir') }} type="button" />
        {selected ? <div className={styles.selectionActions}>
          {selected.kind === 'file' ? <ActionButton aria-label="下载选中文件" title="下载" icon={<Download size={16} />} disabled={unavailable} onClick={() => void files.download(selected.name)} type="button" /> : null}
          <ActionButton aria-label="重命名选中项" title="重命名" icon={<Pencil size={16} />} disabled={unavailable || selected.kind === 'restricted'} onClick={() => edit('rename', selected)} type="button" />
          <ActionButton aria-label="移动选中项" title="移动" icon={<Move size={16} />} disabled={unavailable || selected.kind === 'restricted'} onClick={() => edit('move', selected)} type="button" />
          <ActionButton aria-label="删除选中项" title="删除" icon={<Trash2 size={16} />} disabled={unavailable || selected.kind === 'restricted'} onClick={() => setPendingDelete(selected)} type="button" />
        </div> : null}
        {runtime.assets.find(asset => asset.id === assetId)?.kind === 'vm' ? <ActionMenu label="文件连接设置" items={[
          { label: '重新登记 SSH 身份', disabled: unavailable, onSelect: () => setResetIdentity(true) },
        ]} /> : null}
      </div>
      <input hidden ref={upload} type="file" aria-label="上传文件" onChange={event => {
        const file = event.currentTarget.files?.[0]; event.currentTarget.value = ''
        if (file) { if (files.entries?.some(entry => entry.name === file.name)) setPendingUpload(file); else void files.upload(file, false) }
      }} />
      {files.error && !dialog ? <InlineFeedback tone="danger">{errorMessage(files.error, '文件操作失败。')}</InlineFeedback> : null}
      {files.notice ? <div className={styles.notice} role="status">{files.notice}</div> : null}
      {files.entries === null ? <DataState loading={files.busy} title={files.busy ? '正在读取目录' : '目录读取失败'} /> : !entries.length ? <DataState title="空目录" /> :
        <div className={styles.tableScroll} aria-busy={files.busy}><table className={styles.fileTable} aria-label="当前目录文件"><thead><tr><th>名称</th><th>类型</th><th>大小</th><th /></tr></thead>
          <tbody>{entries.map((entry, index) => <tr key={entry.name} aria-selected={selectedName === entry.name} onClick={() => setSelectedName(entry.name)}
            onDoubleClick={() => entry.kind === 'directory' && !unavailable && navigate(files.childPath(entry.name))}
            onContextMenu={event => { event.preventDefault(); setSelectedName(entry.name); setMenuName(entry.name) }}>
            <td><button className={styles.fileName} disabled={unavailable} type="button" onKeyDown={event => {
              if (event.key === 'ArrowDown' || event.key === 'ArrowUp') { event.preventDefault(); const next = entries[index + (event.key === 'ArrowDown' ? 1 : -1)]; if (next) { setSelectedName(next.name); event.currentTarget.closest('tbody')?.querySelectorAll<HTMLButtonElement>(`.${styles.fileName}`)[index + (event.key === 'ArrowDown' ? 1 : -1)]?.focus() } }
              if (event.key === 'Enter' && entry.kind === 'directory') navigate(files.childPath(entry.name))
              if (event.key === 'F2' && entry.kind !== 'restricted') { event.preventDefault(); edit('rename', entry) }
              if (event.key === 'Delete' && entry.kind !== 'restricted') setPendingDelete(entry)
            }} onFocus={() => setSelectedName(entry.name)}>{entry.kind === 'directory' ? <Folder size={18} /> : <FileIcon size={18} />}<span>{entry.name}</span></button></td>
            <td>{entry.kind === 'directory' ? '目录' : entry.kind === 'file' ? '文件' : '受限项'}</td><td>{entry.kind === 'file' ? formatBytes(entry.size) : '—'}</td>
            <td><ActionMenu label={`${entry.name} 的操作`} open={menuName === entry.name} onOpenChange={opened => setMenuName(opened ? entry.name : null)}
              disabled={unavailable} items={itemActions(entry)} /></td>
          </tr>)}</tbody></table></div>}
    </div>
    <VNextConfirmDialog open={pendingDelete !== null} onClose={() => setPendingDelete(null)} title={`删除 ${pendingDelete?.name ?? ''}`} confirmLabel="删除" tone="danger"
      message={pendingDelete?.kind === 'directory' ? '目录内的文件将一并删除。' : '删除后无法恢复。'} onConfirm={async () => pendingDelete !== null && await files.remove(pendingDelete.name, pendingDelete.kind === 'directory')} />
    <VNextConfirmDialog open={pendingUpload !== null} onClose={() => setPendingUpload(null)} title={`覆盖 ${pendingUpload?.name ?? ''}`} message="替换此文件的现有内容。" confirmLabel="覆盖" tone="danger"
      onConfirm={async () => pendingUpload !== null && await files.upload(pendingUpload, true)} />
    <VNextConfirmDialog open={resetIdentity} onClose={() => setResetIdentity(false)} title="重新登记 SSH 身份" message="确认当前虚拟机的 SSH 密钥已更新。" confirmLabel="重新登记" onConfirm={files.resetIdentity} />
    <VNextDialog eyebrow="" open={dialog !== null} onClose={() => setDialog(null)} title={dialog === 'mkdir' ? '新建目录' : dialog === 'rename' ? '重命名' : '移动'}
      footer={<><ActionButton onClick={() => setDialog(null)} type="button">取消</ActionButton><ActionButton disabled={files.busy || !value.trim()} onClick={() => void submit()} tone="primary" type="button">确认</ActionButton></>}>
      <label className={styles.dialogField}>{dialog === 'move' ? '目标路径' : '名称'}<input autoFocus value={value} onChange={event => setValue(event.currentTarget.value)} onKeyDown={event => { if (event.key === 'Enter' && value.trim() && !files.busy) void submit() }} /></label>
      {dialog && files.error ? <InlineFeedback tone="danger">{errorMessage(files.error, '文件操作失败。')}</InlineFeedback> : null}
    </VNextDialog>
  </section>
}

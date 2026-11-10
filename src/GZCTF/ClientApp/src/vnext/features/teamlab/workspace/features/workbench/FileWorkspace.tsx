import { ActionIcon, Button, Menu, Modal, Progress, TextInput } from '@mantine/core'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  ArrowUp,
  ChevronDown,
  ChevronRight,
  Download,
  File,
  Folder,
  FolderPlus,
  Link2,
  MoreHorizontal,
  Pencil,
  RefreshCw,
  Settings2,
  Trash2,
  Upload,
  X,
} from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useWorkspaceApi, ApiError, type Asset, type Schema } from '../../api/client'
import { ErrorMessage, Loading } from '../../foundation/Feedback'
import { dateTime } from '../../foundation/format'
import styles from './FileWorkspace.module.css'

type Entry = Schema<'FileEntry'>
const join = (base: string, name: string) => (base === '/' ? '' : base) + '/' + name
function useFiles(environmentId: string, assetId: string, path: string, enabled = true) {
  const api = useWorkspaceApi()
  return useQuery({
    queryKey: ['files', environmentId, assetId, path],
    enabled,
    retry: false,
    queryFn: ({ signal }) => api.files(environmentId, assetId, path, signal),
  })
}

export function FileWorkspace({
  environmentId,
  asset,
  onClose,
  onSettings,
}: {
  environmentId: string
  asset: Asset
  onClose: () => void
  onSettings?: () => void
}) {
  const api = useWorkspaceApi()
  const client = useQueryClient()
  const [path, setPath] = useState('/')
  const [selected, setSelected] = useState<Entry>()
  const [dialog, setDialog] = useState<'mkdir' | 'rename' | 'move' | 'remove'>()
  const [name, setName] = useState('')
  const [progress, setProgress] = useState<number>()
  const [context, setContext] = useState<{ x: number; y: number }>()
  const transfer = useRef<AbortController>(null)
  const chooser = useRef<HTMLInputElement>(null)
  const listing = useFiles(environmentId, asset.id, path)
  const refresh = () => client.invalidateQueries({ queryKey: ['files', environmentId, asset.id] })
  const command = useMutation({
    mutationFn: ({ target, input }: { target: string; input: Schema<'FileCommand'> }) =>
      api.fileCommand(environmentId, asset.id, target, input),
    onSuccess: () => {
      setSelected(undefined)
      setDialog(undefined)
      void refresh()
    },
  })
  const upload = useMutation({
    mutationFn: async (file: globalThis.File) => {
      const controller = new AbortController()
      transfer.current = controller
      setProgress(0)
      try {
        await api.uploadFile(environmentId, asset.id, join(path, file.name), file, setProgress, controller.signal)
      } finally {
        transfer.current = null
        setProgress(undefined)
      }
    },
    onSuccess: refresh,
  })
  useEffect(() => () => transfer.current?.abort(), [])
  const navigate = (next: string) => {
    setPath(next)
    setSelected(undefined)
    setContext(undefined)
    command.reset()
  }
  const entries = [...(listing.data ?? [])].sort(
    (a, b) => Number(b.kind === 'directory') - Number(a.kind === 'directory') || a.name.localeCompare(b.name)
  )
  const busy = command.isPending || upload.isPending
  const openDialog = (action: typeof dialog, item?: Entry) => {
    setDialog(action)
    if (item) setSelected(item)
    setName(action === 'rename' ? (item?.name ?? selected?.name ?? '') : action === 'move' ? path : '')
    setContext(undefined)
    command.reset()
  }
  const download = (item: Entry) =>
    window.open(api.fileURL(environmentId, asset.id, join(path, item.name)), '_blank', 'noopener')
  const actions = (item: Entry) => (
    <>
      {(item.kind === 'directory' || item.kind === 'link') && (
        <Menu.Item leftSection={<Folder size={14} />} onClick={() => navigate(join(path, item.name))}>
          打开目录
        </Menu.Item>
      )}
      {(item.kind === 'file' || item.kind === 'link') && (
        <Menu.Item leftSection={<Download size={14} />} onClick={() => download(item)}>
          下载
        </Menu.Item>
      )}
      <Menu.Item leftSection={<Pencil size={14} />} disabled={busy} onClick={() => openDialog('rename', item)}>
        重命名
      </Menu.Item>
      <Menu.Item disabled={busy} onClick={() => openDialog('move', item)}>
        移动
      </Menu.Item>
      <Menu.Divider />
      <Menu.Item
        color="red"
        leftSection={<Trash2 size={14} />}
        disabled={busy}
        onClick={() => openDialog('remove', item)}
      >
        删除
      </Menu.Item>
    </>
  )
  const submit = () => {
    if (dialog === 'mkdir') command.mutate({ target: join(path, name), input: { action: 'mkdir' } })
    else if (selected)
      command.mutate({
        target: join(path, selected.name),
        input: {
          action: dialog === 'remove' ? 'remove' : 'rename',
          destination: dialog === 'rename' ? join(path, name) : dialog === 'move' ? name : undefined,
        },
      })
  }
  return (
    <div className={styles.workspace} aria-label={asset.name + ' 文件'}>
      <div className={styles.toolbar}>
        <div className={styles.heading}>
          <Folder size={18} />
          <strong>{asset.name}</strong>
          <span>文件</span>
        </div>
        <div className={styles.tools}>
          <ActionIcon
            variant="subtle"
            aria-label="刷新文件"
            onClick={() => void refresh()}
            loading={listing.isFetching}
          >
            <RefreshCw size={16} />
          </ActionIcon>
          <Button
            variant="default"
            size="compact-sm"
            leftSection={<Upload size={14} />}
            disabled={busy}
            onClick={() => chooser.current?.click()}
          >
            上传
          </Button>
          <Button
            variant="default"
            size="compact-sm"
            leftSection={<FolderPlus size={14} />}
            disabled={busy}
            onClick={() => openDialog('mkdir')}
          >
            新建目录
          </Button>
          {onSettings && (
            <ActionIcon variant="subtle" aria-label="连接设置" onClick={onSettings}>
              <Settings2 size={17} />
            </ActionIcon>
          )}
          <ActionIcon variant="subtle" aria-label="关闭文件" onClick={onClose}>
            <X size={17} />
          </ActionIcon>
        </div>
      </div>
      <input
        ref={chooser}
        type="file"
        hidden
        onChange={(event) => {
          const file = event.currentTarget.files?.[0]
          event.currentTarget.value = ''
          if (file) upload.mutate(file)
        }}
      />
      <div className={styles.body}>
        <nav className={styles.tree} aria-label="目录树">
          <Directory environmentId={environmentId} assetId={asset.id} path="/" selected={path} onSelect={navigate} />
        </nav>
        <div className={styles.contents}>
          <div className={styles.breadcrumbs}>
            <ActionIcon
              variant="subtle"
              aria-label="上级目录"
              disabled={path === '/'}
              onClick={() => navigate(path.slice(0, path.lastIndexOf('/')) || '/')}
            >
              <ArrowUp size={16} />
            </ActionIcon>
            <button onClick={() => navigate('/')}>根目录</button>
            {path
              .split('/')
              .filter(Boolean)
              .map((part, index, parts) => (
                <span key={index}>
                  <ChevronRight size={12} />
                  <button onClick={() => navigate('/' + parts.slice(0, index + 1).join('/'))}>{part}</button>
                </span>
              ))}
          </div>
          {listing.error && (
            <div className={styles.feedback}>
              <ErrorMessage error={listing.error} />
              {onSettings && listing.error instanceof ApiError && listing.error.status === 409 && (
                <Button variant="default" size="compact-sm" onClick={onSettings}>
                  设置连接
                </Button>
              )}
            </div>
          )}
          {upload.error && (
            <div className={styles.feedback}>
              <ErrorMessage error={upload.error} />
            </div>
          )}
          {progress !== undefined && (
            <div className={styles.transfer}>
              <Progress value={progress} aria-label="上传进度" />
              <span>{progress}%</span>
              <ActionIcon variant="subtle" aria-label="取消上传" onClick={() => transfer.current?.abort()}>
                <X size={14} />
              </ActionIcon>
            </div>
          )}
          {listing.isPending ? (
            <Loading />
          ) : (
            <div className={styles.scroll}>
              <table className={styles.table} aria-label="文件列表">
                <thead>
                  <tr>
                    <th>名称</th>
                    <th>大小</th>
                    <th>修改时间</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {entries.map((item) => (
                    <tr
                      key={item.name}
                      tabIndex={0}
                      aria-selected={item.name === selected?.name}
                      onClick={() => setSelected(item)}
                      onDoubleClick={() =>
                        item.kind === 'directory'
                          ? navigate(join(path, item.name))
                          : item.kind === 'file'
                            ? download(item)
                            : undefined
                      }
                      onContextMenu={(event) => {
                        event.preventDefault()
                        setSelected(item)
                        setContext({ x: event.clientX, y: event.clientY })
                      }}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter' && item.kind === 'directory') navigate(join(path, item.name))
                        if (event.key === 'F2') openDialog('rename', item)
                        if (event.key === 'Delete') openDialog('remove', item)
                        if (event.key === 'F10' && event.shiftKey) {
                          event.preventDefault()
                          const rect = event.currentTarget.getBoundingClientRect()
                          setSelected(item)
                          setContext({ x: rect.left + 24, y: rect.top + 24 })
                        }
                      }}
                    >
                      <td>
                        <span className={styles.filename}>
                          {item.kind === 'directory' ? (
                            <Folder size={17} />
                          ) : item.kind === 'link' ? (
                            <Link2 size={16} />
                          ) : (
                            <File size={16} />
                          )}
                          <span>{item.name}</span>
                        </span>
                      </td>
                      <td>
                        {item.kind === 'directory'
                          ? '—'
                          : item.size < 1024
                            ? item.size + ' B'
                            : (item.size / 1024).toLocaleString('zh-CN', {
                                maximumFractionDigits: 1,
                              }) + ' KiB'}
                      </td>
                      <td>{dateTime(item.modifiedAt)}</td>
                      <td>
                        <Menu position="bottom-end">
                          <Menu.Target>
                            <ActionIcon variant="subtle" aria-label={item.name + ' 操作'}>
                              <MoreHorizontal size={16} />
                            </ActionIcon>
                          </Menu.Target>
                          <Menu.Dropdown>{actions(item)}</Menu.Dropdown>
                        </Menu>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {!entries.length && !listing.error && <div className={styles.empty}>空目录</div>}
            </div>
          )}
        </div>
      </div>
      {context && selected && (
        <Menu
          opened
          onChange={(opened) => {
            if (!opened) setContext(undefined)
          }}
          position="bottom-start"
        >
          <Menu.Target>
            <span
              style={{
                position: 'fixed',
                left: context.x,
                top: context.y,
                width: 1,
                height: 1,
              }}
            />
          </Menu.Target>
          <Menu.Dropdown>{actions(selected)}</Menu.Dropdown>
        </Menu>
      )}
      {dialog && (
        <Modal
          opened
          onClose={() => setDialog(undefined)}
          title={dialog === 'mkdir' ? '新建目录' : dialog === 'rename' ? '重命名' : dialog === 'move' ? '移动' : '删除'}
          centered
          size="sm"
        >
          <form
            className={styles.form}
            onSubmit={(event) => {
              event.preventDefault()
              submit()
            }}
          >
            <ErrorMessage error={command.error} />
            {dialog === 'remove' ? (
              <p className={styles.deleteName}>{selected?.name}</p>
            ) : (
              <TextInput
                label={dialog === 'move' ? '目标完整路径' : '名称'}
                value={name}
                onChange={(event) => setName(event.currentTarget.value)}
                autoFocus
                required
              />
            )}
            <div className={styles.dialogActions}>
              <Button variant="default" onClick={() => setDialog(undefined)}>
                取消
              </Button>
              <Button
                type="submit"
                color={dialog === 'remove' ? 'red' : undefined}
                loading={command.isPending}
                disabled={upload.isPending || (dialog !== 'remove' && !name.trim())}
              >
                {dialog === 'remove' ? '删除' : '确定'}
              </Button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}

function Directory({
  environmentId,
  assetId,
  path,
  selected,
  onSelect,
}: {
  environmentId: string
  assetId: string
  path: string
  selected: string
  onSelect: (path: string) => void
}) {
  const [expanded, setExpanded] = useState(path === '/')
  const listing = useFiles(environmentId, assetId, path, expanded)
  return (
    <div className={styles.branch}>
      <div className={[styles.directory, selected === path ? styles.active : ''].join(' ')}>
        <button aria-label={(expanded ? '收起' : '展开') + path} onClick={() => setExpanded(!expanded)}>
          {expanded ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
        </button>
        <button onClick={() => onSelect(path)}>
          <Folder size={15} />
          <span>{path === '/' ? '根目录' : path.split('/').at(-1)}</span>
        </button>
      </div>
      {expanded && (
        <div className={styles.children}>
          {listing.data
            ?.filter((item) => item.kind === 'directory')
            .sort((a, b) => a.name.localeCompare(b.name))
            .map((item) => (
              <Directory
                key={item.name}
                environmentId={environmentId}
                assetId={assetId}
                path={join(path, item.name)}
                selected={selected}
                onSelect={onSelect}
              />
            ))}
        </div>
      )}
    </div>
  )
}

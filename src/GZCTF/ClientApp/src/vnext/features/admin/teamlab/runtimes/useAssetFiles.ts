import { useRef, useState } from 'react'
import { executeAssetFile, type AssetFileEntry } from '../api/teamlabAssetFilesApi'

export function useAssetFiles(runtimeId: string, generation: number, assetId: number) {
  const [path, setPath] = useState('/')
  const [draftPath, setDraftPath] = useState('/')
  const [entries, setEntries] = useState<AssetFileEntry[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [notice, setNotice] = useState('')
  const active = useRef(false)
  const run = async (action: () => Promise<void>) => {
    if (active.current) return false
    active.current = true
    setBusy(true)
    setError(null)
    setNotice('')
    try { await action(); return true }
    catch (failure) { setError(failure); return false }
    finally { active.current = false; setBusy(false) }
  }
  const read = async (target: string) => {
    const normalized = target.replace(/\/+$/, '') || '/'
    setEntries(null)
    const next = await executeAssetFile(runtimeId, assetId, generation, 'list', normalized)
    setEntries(next)
    setPath(normalized)
    setDraftPath(normalized)
  }
  const childPath = (name: string) => `${path === '/' ? '' : path}/${name}`
  return { path, draftPath, setDraftPath, entries, busy, error, notice, childPath,
    resetIdentity: () => run(async () => {
      await executeAssetFile(runtimeId, assetId, generation, 'reset-ssh-identity', '/', undefined, true)
      setEntries(null)
      setNotice('SSH 身份已重新登记，请重新读取目录。')
    }),
    browse: (target: string) => run(() => read(target)),
    download: (name: string) => run(async () => { await executeAssetFile(runtimeId, assetId, generation, 'download', childPath(name)) }),
    remove: (name: string) => run(async () => {
      await executeAssetFile(runtimeId, assetId, generation, 'delete', childPath(name), undefined, true)
      setNotice('删除已完成。')
      await read(path)
    }),
    upload: (file: File, overwrite: boolean) => run(async () => {
      await executeAssetFile(runtimeId, assetId, generation, 'upload', childPath(file.name), file, overwrite)
      setNotice('上传已完成。')
      await read(path)
    }),
  }
}

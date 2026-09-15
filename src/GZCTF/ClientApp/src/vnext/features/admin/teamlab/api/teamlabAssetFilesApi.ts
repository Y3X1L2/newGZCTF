import { postRuntimeBinary, runtimeJsonClient } from '../../api/runtimeJsonClient'
import { teamLabParsing as parse } from './teamlabParsers'
import { downloadFileUrl } from '@Utils/downloadFileUrl'

export interface AssetFileEntry { name: string; kind: string; size: number }
export const assetFileLimit = 1024 * 1024 * 1024
export async function executeAssetFile(runtimeId: string, assetId: number, generation: number,
  operation: 'list' | 'upload' | 'download' | 'delete' | 'mkdir' | 'move' | 'reset-ssh-identity', path: string,
  file?: File, confirmed = false, destinationPath?: string, recursive = false) {
  if (operation === 'download') {
    const query = new URLSearchParams({ generation: String(generation), path })
    downloadFileUrl(`/api/admin/teamlab/runtimes/${encodeURIComponent(runtimeId)}/assets/${assetId}/files/download?${query}`, path.split('/').at(-1) || 'download')
    return []
  }
  if (operation === 'upload' && file) {
    if (file.size > assetFileLimit) throw new Error('单文件不能超过 1 GiB。')
    await postRuntimeBinary(
      `/api/admin/teamlab/runtimes/${encodeURIComponent(runtimeId)}/assets/${assetId}/files/upload`,
      file,
      { generation, path, overwrite: confirmed, confirmed }
    )
    return []
  }
  const result = parse.record(await runtimeJsonClient.postJson(
    `/api/admin/teamlab/runtimes/${encodeURIComponent(runtimeId)}/assets/${assetId}/files`,
    { generation, operation, path, overwrite: operation === 'upload' && confirmed, confirmed, destinationPath, recursive }
  ), '资产文件')
  return result.entries == null ? [] : parse.array(result.entries, '目录', (value, label): AssetFileEntry => {
    const entry = parse.record(value, label)
    return { name: parse.string(entry.name, '名称'), kind: parse.string(entry.kind, '类型'), size: parse.number(entry.size, '大小') }
  })
}

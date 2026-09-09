import { runtimeJsonClient } from '../../api/runtimeJsonClient'
import { teamLabParsing as parse } from './teamlabParsers'
import { downloadFileUrl } from '@Utils/downloadFileUrl'

export interface AssetFileEntry { name: string; kind: string; size: number }
export const assetFileLimit = 8 * 1024 * 1024
export async function executeAssetFile(runtimeId: string, assetId: number, generation: number,
  operation: 'list' | 'upload' | 'download' | 'delete' | 'reset-ssh-identity', path: string, file?: File, confirmed = false) {
  if (operation === 'download') {
    const query = new URLSearchParams({ generation: String(generation), path })
    downloadFileUrl(`/api/admin/teamlab/runtimes/${encodeURIComponent(runtimeId)}/assets/${assetId}/files/download?${query}`, path.split('/').at(-1) || 'download')
    return []
  }
  let content: string | undefined
  if (file) {
    if (file.size > assetFileLimit) throw new Error('单文件不能超过 8 MiB。')
    const bytes = new Uint8Array(await file.arrayBuffer())
    const chunks: string[] = []
    for (let index = 0; index < bytes.length; index += 8192)
      chunks.push(String.fromCharCode(...bytes.subarray(index, index + 8192)))
    content = btoa(chunks.join(''))
  }
  const result = parse.record(await runtimeJsonClient.postJson(
    `/api/admin/teamlab/runtimes/${encodeURIComponent(runtimeId)}/assets/${assetId}/files`,
    { generation, operation, path, content, overwrite: operation === 'upload' && confirmed, confirmed }
  ), '资产文件')
  return result.entries == null ? [] : parse.array(result.entries, '目录', (value, label): AssetFileEntry => {
    const entry = parse.record(value, label)
    return { name: parse.string(entry.name, '名称'), kind: parse.string(entry.kind, '类型'), size: parse.number(entry.size, '大小') }
  })
}

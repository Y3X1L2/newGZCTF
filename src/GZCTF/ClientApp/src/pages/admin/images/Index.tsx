import {
  Alert,
  ActionIcon,
  Badge,
  Button,
  FileInput,
  Group,
  Modal,
  Progress,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  mdiArchiveArrowUpOutline,
  mdiCubeOutline,
  mdiDeleteOutline,
  mdiDocker,
  mdiFileImportOutline,
  mdiMagnify,
  mdiRefresh,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { useMemo, useRef, useState } from 'react'
import useSWR from 'swr'
import { AdminPage } from '@Components/admin/AdminPage'
import {
  YinyuMetricTile,
  YinyuModalBody,
  YinyuPanel,
  YinyuRouteLoader,
  YinyuTableShell,
} from '@Components/yinyu/YinyuUI'

const fetcher = (url: string) => fetch(url).then((response) => response.json())

interface ImageTemplate {
  id: number
  name: string
  osType: string | number
  imageType: string | number
  fileSize: number
  status: string | number
  description?: string | null
  imageHash?: string | null
  uploadedAt?: string | null
  registryUrl?: string | null
  localFilePath?: string | null
}

interface DockerRegistryInfo {
  enabled: boolean
  address: string
  namespace: string
  maxUploadSizeGb: number
}

const imageTypeLabels: Record<string, string> = {
  '0': 'Docker',
  docker: 'Docker',
  '1': 'QCOW2',
  qcow2: 'QCOW2',
  '2': 'OVA',
  ova: 'OVA',
  '3': 'VMDK',
  vmdk: 'VMDK',
}

const osTypeLabels: Record<string, string> = {
  '0': 'Linux',
  linux: 'Linux',
  '1': 'Windows',
  windows: 'Windows',
}

const statusConfig: Record<string, { label: string; color: string; semantic: string }> = {
  '0': { label: '就绪', color: 'green', semantic: 'ready' },
  ready: { label: '就绪', color: 'green', semantic: 'ready' },
  '1': { label: '导入中', color: 'violet', semantic: 'importing' },
  importing: { label: '导入中', color: 'violet', semantic: 'importing' },
  '2': { label: '异常', color: 'red', semantic: 'error' },
  error: { label: '异常', color: 'red', semantic: 'error' },
}

function normalizeKey(value: string | number | undefined | null) {
  return String(value ?? '').toLowerCase()
}

function labelFor(map: Record<string, string>, value: string | number | undefined | null) {
  return map[normalizeKey(value)] ?? String(value ?? '-')
}

function statusFor(value: string | number | undefined | null) {
  return statusConfig[normalizeKey(value)] ?? { label: String(value ?? '未知'), color: 'gray', semantic: 'unknown' }
}

function osSemantic(value: string | number | undefined | null) {
  const key = normalizeKey(value)
  if (key === '1' || key === 'windows') return 'windows'
  if (key === '0' || key === 'linux') return 'linux'
  return 'neutral'
}

function imageTypeSemantic(value: string | number | undefined | null) {
  const key = normalizeKey(value)
  if (key === '0' || key === 'docker') return 'docker'
  if (key === '1' || key === 'qcow2') return 'vm-qcow2'
  if (key === '2' || key === 'ova') return 'vm-ova'
  if (key === '3' || key === 'vmdk') return 'vm-vmdk'
  return 'neutral'
}

function formatSize(bytes: number) {
  if (!Number.isFinite(bytes) || bytes <= 0) return '-'
  const units = ['B', 'KB', 'MB', 'GB']
  let value = bytes
  let index = 0

  while (value >= 1024 && index < units.length - 1) {
    value /= 1024
    index += 1
  }

  return `${value.toFixed(index === 0 ? 0 : 1)} ${units[index]}`
}

function typeIcon(value: string | number | undefined | null) {
  return labelFor(imageTypeLabels, value) === 'Docker' ? mdiDocker : mdiCubeOutline
}

function shortHash(hash?: string | null) {
  return hash ? `${hash.slice(0, 12)}...${hash.slice(-6)}` : '-'
}

function RegisterDockerModal({
  opened,
  onClose,
  onDone,
}: {
  opened: boolean
  onClose: () => void
  onDone: () => void
}) {
  const [name, setName] = useState('')
  const [url, setUrl] = useState('')
  const [osType, setOsType] = useState<string>('0')
  const [auth, setAuth] = useState('')
  const [loading, setLoading] = useState(false)

  const handleSubmit = async () => {
    if (!name.trim() || !url.trim()) return

    setLoading(true)
    try {
      const res = await fetch('/api/v1/image-templates/register-docker', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: name.trim(),
          registryUrl: url.trim(),
          osType: Number(osType),
          registryAuth: auth || null,
        }),
      })
      const data = await res.json().catch(() => ({}))

      if (res.ok) {
        notifications.show({ title: '注册成功', message: `Docker 镜像 ${name} 已开始拉取`, color: 'green' })
        setName('')
        setUrl('')
        setOsType('0')
        setAuth('')
        onDone()
        onClose()
      } else {
        notifications.show({
          title: '注册失败',
          message: data.message || '请检查镜像名称和 Registry 地址',
          color: 'red',
        })
      }
    } catch {
      notifications.show({ title: '注册失败', message: '网络错误', color: 'red' })
    } finally {
      setLoading(false)
    }
  }

  return (
    <Modal opened={opened} onClose={onClose} title="注册 Docker 镜像" radius="sm">
      <YinyuModalBody p="md">
        <Stack>
          <TextInput
            label="模板显示名称"
            required
            value={name}
            onChange={(event) => setName(event.currentTarget.value)}
            placeholder="alpine-test"
            description="在平台中显示的模板名称"
          />
          <TextInput
            label="Docker 镜像地址"
            required
            value={url}
            onChange={(event) => setUrl(event.currentTarget.value)}
            placeholder="docker.io/library/alpine:latest"
            description="完整镜像引用，例如 docker.io/library/nginx:latest"
          />
          <Select
            label="操作系统"
            data={[
              { value: '0', label: 'Linux' },
              { value: '1', label: 'Windows' },
            ]}
            value={osType}
            onChange={(value) => setOsType(value ?? '0')}
          />
          <TextInput
            label="Registry Auth"
            type="password"
            value={auth}
            onChange={(event) => setAuth(event.currentTarget.value)}
            placeholder="公开仓库可留空"
          />
          <Button fullWidth leftSection={<Icon path={mdiDocker} size={0.8} />} loading={loading} onClick={handleSubmit}>
            注册
          </Button>
        </Stack>
      </YinyuModalBody>
    </Modal>
  )
}

function UploadDockerArchiveModal({
  opened,
  onClose,
  onDone,
  registry,
}: {
  opened: boolean
  onClose: () => void
  onDone: () => void
  registry?: DockerRegistryInfo
}) {
  const [name, setName] = useState('')
  const [repository, setRepository] = useState('')
  const [tag, setTag] = useState('latest')
  const [sourceImage, setSourceImage] = useState('')
  const [osType, setOsType] = useState<string>('0')
  const [file, setFile] = useState<File | null>(null)
  const [loading, setLoading] = useState(false)

  const handleSubmit = async () => {
    if (!file || !name.trim() || !repository.trim()) return
    if (!registry?.enabled) {
      notifications.show({ title: '未配置 Registry', message: '请先在服务器配置内网 Docker Registry 地址', color: 'red' })
      return
    }

    setLoading(true)
    try {
      const formData = new FormData()
      formData.append('file', file)
      formData.append('name', name.trim())
      formData.append('repository', repository.trim())
      formData.append('tag', tag.trim() || 'latest')
      formData.append('sourceImage', sourceImage.trim())
      formData.append('osType', osType)

      const res = await fetch('/api/v1/image-templates/upload-docker', { method: 'POST', body: formData })
      const data = await res.json().catch(() => ({}))

      if (res.ok) {
        notifications.show({
          title: '上传成功',
          message: `镜像已推送到 ${data.registryUrl ?? '内网 Registry'}`,
          color: 'green',
        })
        setName('')
        setRepository('')
        setTag('latest')
        setSourceImage('')
        setOsType('0')
        setFile(null)
        onDone()
        onClose()
      } else {
        notifications.show({ title: '上传失败', message: data.message || '请检查 Docker 镜像包', color: 'red' })
      }
    } catch {
      notifications.show({ title: '上传失败', message: '网络错误', color: 'red' })
    } finally {
      setLoading(false)
    }
  }

  return (
    <Modal opened={opened} onClose={onClose} title="上传 Docker 镜像包" radius="sm">
      <YinyuModalBody p="md">
        <Stack>
          {registry?.enabled ? (
            <Alert color="blue" variant="light">
              当前内网 Registry：{registry.address}
              {registry.namespace ? `/${registry.namespace}` : ''}
            </Alert>
          ) : (
            <Alert color="yellow" variant="light">
              当前服务器未配置内网 Docker Registry，请在 DockerRegistrySettings 中配置 Address 后再上传。
            </Alert>
          )}
          <FileInput
            label="Docker 镜像包"
            required
            value={file}
            onChange={setFile}
            accept=".tar,.tar.gz,.tgz"
            placeholder="选择 docker save 生成的 .tar/.tgz 文件"
            description="请使用 docker save 导出镜像，上传后平台会推送到内网 Registry。"
          />
          <TextInput
            label="模板显示名称"
            required
            value={name}
            onChange={(event) => setName(event.currentTarget.value)}
            placeholder="web-flag-demo"
          />
          <Group grow>
            <TextInput
              label="仓库路径"
              required
              value={repository}
              onChange={(event) => setRepository(event.currentTarget.value)}
              placeholder="web/flag-demo"
              description={
                registry?.enabled
                  ? `最终地址会自动加上 ${registry.address}${registry.namespace ? `/${registry.namespace}` : ''} 前缀。`
                  : '最终地址会使用服务器配置的内网 Registry 前缀。'
              }
            />
            <TextInput
              label="Tag"
              required
              value={tag}
              onChange={(event) => setTag(event.currentTarget.value)}
              placeholder="v1"
            />
          </Group>
          <TextInput
            label="源镜像名"
            value={sourceImage}
            onChange={(event) => setSourceImage(event.currentTarget.value)}
            placeholder="留空时自动读取 docker load 输出"
            description="当镜像包只包含 image ID、没有 tag 时填写，例如 local/web:dev。"
          />
          <Select
            label="操作系统"
            data={[
              { value: '0', label: 'Linux' },
              { value: '1', label: 'Windows' },
            ]}
            value={osType}
            onChange={(value) => setOsType(value ?? '0')}
          />
          <Button
            fullWidth
            leftSection={<Icon path={mdiArchiveArrowUpOutline} size={0.8} />}
            loading={loading}
            disabled={!registry?.enabled}
            onClick={handleSubmit}
          >
            上传并推送
          </Button>
        </Stack>
      </YinyuModalBody>
    </Modal>
  )
}

function ImportLocalModal({ opened, onClose, onDone }: { opened: boolean; onClose: () => void; onDone: () => void }) {
  const [path, setPath] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [loading, setLoading] = useState(false)

  const handleSubmit = async () => {
    if (!path.trim()) return

    setLoading(true)
    try {
      const res = await fetch('/api/v1/image-templates/import-local', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ localPath: path.trim(), displayName: displayName.trim() || undefined }),
      })
      const data = await res.json().catch(() => ({}))

      if (res.ok) {
        notifications.show({ title: '导入成功', message: '镜像已从本地路径导入', color: 'green' })
        setPath('')
        setDisplayName('')
        onDone()
        onClose()
      } else {
        notifications.show({ title: '导入失败', message: data.message || '请检查路径和文件权限', color: 'red' })
      }
    } catch {
      notifications.show({ title: '导入失败', message: '网络错误', color: 'red' })
    } finally {
      setLoading(false)
    }
  }

  return (
    <Modal opened={opened} onClose={onClose} title="从本地路径导入" radius="sm">
      <YinyuModalBody p="md">
        <Stack>
          <TextInput
            label="服务器本地路径"
            required
            value={path}
            onChange={(event) => setPath(event.currentTarget.value)}
            placeholder="/var/lib/images/template.qcow2"
          />
          <TextInput
            label="显示名称"
            value={displayName}
            onChange={(event) => setDisplayName(event.currentTarget.value)}
            placeholder="可选"
          />
          <Button
            fullWidth
            leftSection={<Icon path={mdiFileImportOutline} size={0.8} />}
            loading={loading}
            onClick={handleSubmit}
          >
            导入
          </Button>
        </Stack>
      </YinyuModalBody>
    </Modal>
  )
}

function MetricTile({ label, value, tone }: { label: string; value: number; tone: string }) {
  const toneMap: Record<string, 'success' | 'warm' | 'danger' | 'neutral'> = {
    teal: 'success',
    green: 'success',
    blue: 'neutral',
    yellow: 'warm',
    red: 'danger',
    gray: 'neutral',
  }

  return <YinyuMetricTile label={label} value={value} detail={tone} tone={toneMap[tone] ?? 'neutral'} />
}

export default function ImagesPage() {
  const { data, isLoading, mutate } = useSWR('/api/v1/image-templates', fetcher)
  const { data: registry } = useSWR('/api/v1/image-templates/docker-registry', fetcher)
  const [dockerModalOpen, setDockerModalOpen] = useState(false)
  const [dockerUploadOpen, setDockerUploadOpen] = useState(false)
  const [localModalOpen, setLocalModalOpen] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [query, setQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState('all')
  const [typeFilter, setTypeFilter] = useState('all')
  const fileInputRef = useRef<HTMLInputElement>(null)

  const templates = useMemo(() => (data?.items ?? []) as ImageTemplate[], [data?.items])

  const stats = useMemo(
    () => ({
      total: templates.length,
      ready: templates.filter((item) => normalizeKey(item.status) === '0' || normalizeKey(item.status) === 'ready')
        .length,
      importing: templates.filter(
        (item) => normalizeKey(item.status) === '1' || normalizeKey(item.status) === 'importing'
      ).length,
      error: templates.filter((item) => normalizeKey(item.status) === '2' || normalizeKey(item.status) === 'error')
        .length,
    }),
    [templates]
  )

  const filteredTemplates = useMemo(() => {
    const keyword = query.trim().toLowerCase()

    return templates.filter((item) => {
      const status = normalizeKey(item.status)
      const type = normalizeKey(item.imageType)
      const matchedStatus =
        statusFilter === 'all' || status === statusFilter || statusConfig[status]?.label === statusFilter
      const matchedType =
        typeFilter === 'all' ||
        type === typeFilter ||
        labelFor(imageTypeLabels, item.imageType).toLowerCase() === typeFilter
      const matchedKeyword =
        !keyword ||
        item.name.toLowerCase().includes(keyword) ||
        item.registryUrl?.toLowerCase().includes(keyword) ||
        item.localFilePath?.toLowerCase().includes(keyword) ||
        item.imageHash?.toLowerCase().includes(keyword)

      return matchedStatus && matchedType && matchedKeyword
    })
  }, [query, statusFilter, templates, typeFilter])

  const handleUploadArchive = async (file: File | null) => {
    if (!file) return

    setUploading(true)
    try {
      const formData = new FormData()
      formData.append('file', file)
      const res = await fetch('/api/v1/image-templates/upload', { method: 'POST', body: formData })
      const data = await res.json().catch(() => ({}))

      if (res.ok) {
        notifications.show({ title: '上传成功', message: '镜像压缩包已上传并开始处理', color: 'green' })
        mutate()
      } else {
        notifications.show({ title: '上传失败', message: data.message || '请检查文件格式', color: 'red' })
      }
    } catch {
      notifications.show({ title: '上传失败', message: '网络错误', color: 'red' })
    } finally {
      setUploading(false)
      if (fileInputRef.current) fileInputRef.current.value = ''
    }
  }

  const handleDelete = async (id: number, name: string) => {
    if (!confirm(`确定删除模板 "${name}" 吗？`)) return

    try {
      const res = await fetch(`/api/v1/image-templates/${id}`, { method: 'DELETE' })
      const data = await res.json().catch(() => ({}))

      if (res.ok) {
        notifications.show({ title: '删除成功', message: `模板 ${name} 已删除`, color: 'green' })
        mutate()
      } else {
        notifications.show({ title: '删除失败', message: data.message || '请检查模板是否仍被题目使用', color: 'red' })
      }
    } catch {
      notifications.show({ title: '删除失败', message: '网络错误', color: 'red' })
    }
  }

  return (
    <AdminPage>
      <Stack gap="lg" w="100%">
        <Group justify="space-between" align="flex-start">
          <Stack gap={2}>
            <Title order={2}>环境模板</Title>
            <Text size="sm" className="yy-readable-text">
              管理 Docker 镜像、Windows/QCOW2 模板与本地导入资源。
            </Text>
          </Stack>
          <Group wrap="nowrap" style={{ overflowX: 'auto' }}>
            <Button variant="default" leftSection={<Icon path={mdiRefresh} size={0.8} />} onClick={() => mutate()}>
              刷新
            </Button>
            <Button leftSection={<Icon path={mdiDocker} size={0.8} />} onClick={() => setDockerModalOpen(true)}>
              注册 Docker
            </Button>
            <Button
              variant="default"
              leftSection={<Icon path={mdiArchiveArrowUpOutline} size={0.8} />}
              onClick={() => setDockerUploadOpen(true)}
            >
              上传 Docker 包
            </Button>
            <Button
              variant="default"
              leftSection={<Icon path={mdiFileImportOutline} size={0.8} />}
              onClick={() => setLocalModalOpen(true)}
            >
              本地导入
            </Button>
            <Button
              variant="default"
              leftSection={<Icon path={mdiArchiveArrowUpOutline} size={0.8} />}
              loading={uploading}
              onClick={() => fileInputRef.current?.click()}
            >
              上传 VM 归档
            </Button>
            <input
              ref={fileInputRef}
              type="file"
              accept=".zip,.tar.gz,.tgz,.tar.xz,.txz"
              style={{ display: 'none' }}
              onChange={(event) => handleUploadArchive(event.target.files?.[0] ?? null)}
            />
          </Group>
        </Group>

        <Group grow>
          <MetricTile label="全部模板" value={stats.total} tone="gray" />
          <MetricTile label="就绪" value={stats.ready} tone="teal" />
          <MetricTile label="导入中" value={stats.importing} tone="yellow" />
          <MetricTile label="异常" value={stats.error} tone={stats.error > 0 ? 'red' : 'gray'} />
        </Group>

        <YinyuPanel p="md">
          <Group justify="space-between" align="end">
            <TextInput
              leftSection={<Icon path={mdiMagnify} size={0.75} />}
              placeholder="搜索名称、路径、Registry 或 Hash"
              value={query}
              onChange={(event) => setQuery(event.currentTarget.value)}
              style={{ minWidth: 320 }}
            />
            <Group>
              <Select
                label="状态"
                value={statusFilter}
                onChange={(value) => setStatusFilter(value ?? 'all')}
                data={[
                  { value: 'all', label: '全部' },
                  { value: '0', label: '就绪' },
                  { value: '1', label: '导入中' },
                  { value: '2', label: '异常' },
                ]}
                w={140}
              />
              <Select
                label="类型"
                value={typeFilter}
                onChange={(value) => setTypeFilter(value ?? 'all')}
                data={[
                  { value: 'all', label: '全部' },
                  { value: '0', label: 'Docker' },
                  { value: '1', label: 'QCOW2' },
                  { value: '2', label: 'OVA' },
                  { value: '3', label: 'VMDK' },
                ]}
                w={140}
              />
            </Group>
          </Group>
        </YinyuPanel>

        <YinyuTableShell p={0}>
          {isLoading ? (
            <div className="yy-admin-inline-loader">
              <YinyuRouteLoader title="环境模板" description="正在读取镜像模板" />
            </div>
          ) : (
            <Table verticalSpacing="sm" highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>名称</Table.Th>
                  <Table.Th>类型</Table.Th>
                  <Table.Th>系统</Table.Th>
                  <Table.Th>大小</Table.Th>
                  <Table.Th>状态</Table.Th>
                  <Table.Th>来源</Table.Th>
                  <Table.Th>上传时间</Table.Th>
                  <Table.Th ta="right">操作</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {filteredTemplates.length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={8}>
                      <Stack align="center" py="xl" gap={4}>
                        <Text fw={700}>没有匹配的环境模板</Text>
                        <Text className="yy-readable-text" size="sm">
                          调整筛选条件，或导入新的模板。
                        </Text>
                      </Stack>
                    </Table.Td>
                  </Table.Tr>
                )}
                {filteredTemplates.map((img) => {
                  const status = statusFor(img.status)
                  const source = img.registryUrl || img.localFilePath || img.imageHash || '-'

                  return (
                    <Table.Tr key={img.id}>
                      <Table.Td>
                        <Stack gap={2}>
                          <Group gap="xs" wrap="nowrap">
                            <Icon path={typeIcon(img.imageType)} size={0.78} />
                            <Text fw={700} truncate>
                              {img.name}
                            </Text>
                          </Group>
                          {img.description && (
                            <Text size="xs" className="yy-readable-text">
                              {img.description}
                            </Text>
                          )}
                        </Stack>
                      </Table.Td>
                      <Table.Td>
                        <Badge
                          variant="light"
                          color={imageTypeSemantic(img.imageType) === 'docker' ? 'teal' : 'violet'}
                          className="yy-semantic-badge"
                          data-semantic={imageTypeSemantic(img.imageType)}
                        >
                          {labelFor(imageTypeLabels, img.imageType)}
                        </Badge>
                      </Table.Td>
                      <Table.Td>
                        <Badge
                          variant="light"
                          color={osSemantic(img.osType) === 'windows' ? 'blue' : 'lime'}
                          className="yy-semantic-badge"
                          data-semantic={osSemantic(img.osType)}
                        >
                          {labelFor(osTypeLabels, img.osType)}
                        </Badge>
                      </Table.Td>
                      <Table.Td>{formatSize(img.fileSize)}</Table.Td>
                      <Table.Td>
                        <Stack gap={4}>
                          <Badge
                            color={status.color}
                            variant="light"
                            className="yy-status-badge yy-semantic-badge"
                            data-semantic={status.semantic}
                          >
                            {status.label}
                          </Badge>
                          {normalizeKey(img.status) === '1' || normalizeKey(img.status) === 'importing' ? (
                            <Progress value={66} color="violet" size={3} radius="xs" animated />
                          ) : null}
                        </Stack>
                      </Table.Td>
                      <Table.Td maw={260}>
                        <Text size="xs" className="yy-readable-text" truncate title={source}>
                          {source}
                        </Text>
                        <Text size="xs" className="yy-readable-text" ff="monospace" truncate title={img.imageHash ?? undefined}>
                          {shortHash(img.imageHash)}
                        </Text>
                      </Table.Td>
                      <Table.Td>{img.uploadedAt ? dayjs(img.uploadedAt).format('YYYY-MM-DD HH:mm') : '-'}</Table.Td>
                      <Table.Td>
                        <Group justify="flex-end">
                          <Tooltip label="删除">
                            <ActionIcon color="red" variant="subtle" onClick={() => handleDelete(img.id, img.name)}>
                              <Icon path={mdiDeleteOutline} size={0.82} />
                            </ActionIcon>
                          </Tooltip>
                        </Group>
                      </Table.Td>
                    </Table.Tr>
                  )
                })}
              </Table.Tbody>
            </Table>
          )}
        </YinyuTableShell>

        <RegisterDockerModal
          opened={dockerModalOpen}
          onClose={() => setDockerModalOpen(false)}
          onDone={() => mutate()}
        />
        <UploadDockerArchiveModal
          opened={dockerUploadOpen}
          onClose={() => setDockerUploadOpen(false)}
          onDone={() => mutate()}
          registry={registry as DockerRegistryInfo | undefined}
        />
        <ImportLocalModal opened={localModalOpen} onClose={() => setLocalModalOpen(false)} onDone={() => mutate()} />
      </Stack>
    </AdminPage>
  )
}

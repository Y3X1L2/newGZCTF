import {
  ActionIcon,
  Badge,
  Button,
  Code,
  Group,
  Modal,
  MultiSelect,
  NumberInput,
  ScrollArea,
  Stack,
  Table,
  Text,
  TextInput,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { mdiContentCopy, mdiDeleteOutline, mdiKeyPlus } from '@mdi/js'
import { Icon } from '@mdi/react'
import { memo, useCallback, useMemo, useState } from 'react'
import api, { ApiTokenCreateModel } from '@Api'

interface ResourceForm {
  resourceType: string
  resourceId: string
}

type TokenForm = ApiTokenCreateModel & {
  scopes: string[]
  resources: ResourceForm[]
  requestsPerMinute: number
}

const scopeOptions = [
  { value: 'assets:read', label: '读取附件' },
  { value: 'assets:write', label: '写入附件' },
  { value: 'assets:delete', label: '删除附件' },
  { value: 'images:read', label: '读取镜像' },
  { value: 'images:write', label: '写入镜像' },
  { value: 'images:delete', label: '删除镜像' },
  { value: 'challenges:read', label: '读取比赛题目' },
  { value: 'challenges:write', label: '导入比赛题目' },
  { value: 'challenges:delete', label: '删除比赛题目' },
  { value: 'operations:read', label: '读取异步操作' },
]

const initialModel: TokenForm = {
  name: '',
  scopes: ['images:read'],
  resources: [],
  requestsPerMinute: 60,
  expiresAt: null,
}

function formatTime(value?: number | null) {
  return value ? new Date(value).toLocaleString() : '-'
}

export const ApiTokenManager = memo(function ApiTokenManager() {
  const { data = [], isLoading, mutate } = api.apiTokens.useApiTokensList()
  const [createOpened, setCreateOpened] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [model, setModel] = useState<TokenForm>(initialModel)
  const [resource, setResource] = useState<ResourceForm>({ resourceType: '', resourceId: '' })
  const [issuedSecret, setIssuedSecret] = useState<string | null>(null)

  const rows = useMemo(
    () =>
      data.map((token) => {
        const inactive = Boolean(token.revokedAt) || Boolean(token.expiresAt && token.expiresAt <= Date.now())
        return (
          <Table.Tr key={token.id}>
            <Table.Td>
              <Text fw={600}>{token.name}</Text>
              <Text size="xs" c="dimmed">
                {token.id}
              </Text>
            </Table.Td>
            <Table.Td>
              <Group gap={4}>
                {(token.scopes ?? []).map((scope) => (
                  <Badge key={scope} variant="light" size="sm">
                    {scope}
                  </Badge>
                ))}
              </Group>
            </Table.Td>
            <Table.Td>{token.requestsPerMinute}/min</Table.Td>
            <Table.Td>{formatTime(token.lastUsedAt)}</Table.Td>
            <Table.Td>{formatTime(token.expiresAt)}</Table.Td>
            <Table.Td>
              <Badge color={inactive ? 'gray' : 'teal'}>{inactive ? '已失效' : '有效'}</Badge>
            </Table.Td>
            <Table.Td>
              <Tooltip label="撤销">
                <ActionIcon
                  color="red"
                  variant="subtle"
                  disabled={inactive}
                  onClick={async () => {
                    if (!token.id) return
                    try {
                      await api.apiTokens.apiTokensRevoke(token.id)
                      await mutate()
                    } catch (error) {
                      notifications.show({ color: 'red', message: error instanceof Error ? error.message : '撤销失败' })
                    }
                  }}
                >
                  <Icon path={mdiDeleteOutline} size={0.85} />
                </ActionIcon>
              </Tooltip>
            </Table.Td>
          </Table.Tr>
        )
      }),
    [data, mutate]
  )

  const closeCreate = useCallback(() => {
    setCreateOpened(false)
    setModel(initialModel)
    setResource({ resourceType: '', resourceId: '' })
  }, [])

  const issue = useCallback(async () => {
    setSubmitting(true)
    try {
      const result = await api.apiTokens.apiTokensIssue(model)
      closeCreate()
      setIssuedSecret(result.data.plainTextToken ?? null)
      await mutate()
    } catch (error) {
      notifications.show({ color: 'red', message: error instanceof Error ? error.message : '创建失败' })
    } finally {
      setSubmitting(false)
    }
  }, [closeCreate, model, mutate])

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <div>
          <Text fw={700} size="lg">
            API Token
          </Text>
          <Text size="sm" c="dimmed">
            {data.length} 个 Token
          </Text>
        </div>
        <Button leftSection={<Icon path={mdiKeyPlus} size={0.85} />} onClick={() => setCreateOpened(true)}>
          创建 Token
        </Button>
      </Group>

      <ScrollArea type="auto" offsetScrollbars>
        <Table striped highlightOnHover miw={900}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>名称</Table.Th>
              <Table.Th>权限</Table.Th>
              <Table.Th>配额</Table.Th>
              <Table.Th>最后使用</Table.Th>
              <Table.Th>过期时间</Table.Th>
              <Table.Th>状态</Table.Th>
              <Table.Th w={56} />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>{isLoading ? null : rows}</Table.Tbody>
        </Table>
      </ScrollArea>

      <Modal opened={createOpened} onClose={closeCreate} title="创建 API Token">
        <Stack>
          <TextInput
            required
            label="名称"
            value={model.name}
            onChange={(event) => setModel((current) => ({ ...current, name: event.currentTarget.value }))}
          />
          <MultiSelect
            required
            label="权限范围"
            data={scopeOptions}
            value={model.scopes}
            onChange={(scopes) => setModel((current) => ({ ...current, scopes }))}
          />
          <NumberInput
            label="每分钟请求数"
            min={1}
            max={10000}
            value={model.requestsPerMinute}
            onChange={(value) =>
              setModel((current) => ({ ...current, requestsPerMinute: Number(value) || 60 }))
            }
          />
          <TextInput
            label="过期时间"
            type="datetime-local"
            value={model.expiresAt ? new Date(model.expiresAt).toISOString().slice(0, 16) : ''}
            onChange={(event) =>
              setModel((current) => ({
                ...current,
                expiresAt: event.currentTarget.value ? new Date(event.currentTarget.value).getTime() : null,
              }))
            }
          />
          <Group grow align="end">
            <TextInput
              label="资源类型"
              value={resource.resourceType}
              onChange={(event) => setResource((current) => ({ ...current, resourceType: event.currentTarget.value }))}
            />
            <TextInput
              label="资源 ID"
              value={resource.resourceId}
              onChange={(event) => setResource((current) => ({ ...current, resourceId: event.currentTarget.value }))}
            />
            <Button
              variant="default"
              disabled={!resource.resourceType.trim() || !resource.resourceId.trim()}
              onClick={() => {
                setModel((current) => ({ ...current, resources: [...current.resources, resource] }))
                setResource({ resourceType: '', resourceId: '' })
              }}
            >
              添加
            </Button>
          </Group>
          {model.resources.map((item) => (
            <Badge key={`${item.resourceType}:${item.resourceId}`} variant="outline">
              {item.resourceType}:{item.resourceId}
            </Badge>
          ))}
          <Group justify="flex-end">
            <Button variant="default" onClick={closeCreate}>
              取消
            </Button>
            <Button loading={submitting} disabled={!model.name.trim() || model.scopes.length === 0} onClick={issue}>
              创建
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={issuedSecret !== null}
        onClose={() => setIssuedSecret(null)}
        title="API Token"
        closeOnClickOutside={false}
      >
        <Stack>
          <Code block style={{ overflowWrap: 'anywhere' }}>
            {issuedSecret}
          </Code>
          <Button
            leftSection={<Icon path={mdiContentCopy} size={0.85} />}
            onClick={async () => issuedSecret && navigator.clipboard.writeText(issuedSecret)}
          >
            复制
          </Button>
        </Stack>
      </Modal>
    </Stack>
  )
})

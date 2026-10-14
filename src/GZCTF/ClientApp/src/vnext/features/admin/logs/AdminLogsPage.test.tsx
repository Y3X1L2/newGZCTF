import { fireEvent, render, screen, within } from '@testing-library/react'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import zh from '../../../../locales/zh-CN/admin.json'
import type { AdminLogEntry } from '../api'
import { AdminLogDrawer } from './AdminLogDrawer'
import { AdminLogsPage } from './AdminLogsPage'

const { history, stream } = vi.hoisted(() => ({ history: vi.fn(), stream: vi.fn() }))
vi.mock('./useAdminLogs', () => ({ useAdminLogs: history }))
vi.mock('./useAdminLogStream', () => ({ useAdminLogStream: stream }))
const i18n = createInstance()
await i18n.init({ lng: 'zh-CN', resources: { 'zh-CN': { admin: zh } } })
const log: AdminLogEntry = {
  id: 1,
  time: 1791946800000,
  name: 'Anonymous',
  actorKind: 'System',
  level: 'Information',
  ip: null,
  msg: 'Agent synchronization completed.',
  status: null,
  outcome: 'Observed',
  eventCode: 'node.capability.changed',
  resourceType: 'worker-node',
  workerNodeName: '训练节点一',
  resourceId: '8a587032-0487-43e3-b2bf-eb226fe12cc1',
  workerNodeId: '8a587032-0487-43e3-b2bf-eb226fe12cc1',
  logger: 'GZCTF.Modules.Fleet.AgentSync',
}
const live = {
  ...log,
  id: 0,
  time: log.time + 1,
  eventCode: 'runtime.capacity.reconciled',
  resourceDisplayName: '课程实验一',
  resourceType: 'course',
}
function page(url = '/admin/logs') {
  return render(
    <I18nextProvider i18n={i18n}>
      <MemoryRouter initialEntries={[url]}>
        <AdminLogsPage />
      </MemoryRouter>
    </I18nextProvider>
  )
}
beforeEach(() => {
  history.mockReturnValue({
    logs: { contract: 'cursor', items: [log], nextCursor: null },
    isLoading: false,
    isRefreshing: false,
    mutate: vi.fn(),
  })
  stream.mockReturnValue({ buffered: [], dropped: 0, connectionState: 'connected', retry: vi.fn(), consume: vi.fn() })
  window.scrollTo = vi.fn()
})
describe('Admin logs reading workflow', () => {
  it('shows localized facts and opens the drawer with keyboard row activation', () => {
    page()
    expect(screen.getAllByText('实时已连接')).toHaveLength(1)
    expect(screen.getByText('本页 1 条 · 异常 0 · 警告 0')).toBeVisible()
    expect(screen.getByText('节点能力变化')).toBeVisible()
    expect(screen.getByText('节点 · 训练节点一')).toBeVisible()
    expect(screen.getByText('关键词、操作人和结果仅筛选当前页；级别与关联条件查询全部历史。')).toBeVisible()
    const row = screen.getByText('节点能力变化').closest('tr')!
    fireEvent.keyDown(row, { key: 'Enter' })
    const drawer = screen.getByRole('dialog')
    expect(within(drawer).getByRole('heading', { name: '节点能力变化' })).toBeVisible()
    expect(within(drawer).queryByText('异常诊断')).toBeNull()
    expect(within(drawer).queryByText('执行节点')).toBeNull()
    expect(within(drawer).getByText(log.resourceId!)).toBeVisible()
    expect(within(drawer).getAllByText(log.resourceId!)).toHaveLength(1)
    expect(within(drawer).getByText(log.logger!)).toBeVisible()
    expect(drawer.querySelector('details')).toBeNull()
    expect(within(drawer).getByText('后台事件，无请求 IP')).toBeVisible()
  })
  it('searches semantic text and filters outcomes without inventing success', () => {
    page()
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: '能力变化' } })
    expect(screen.getByText('节点能力变化')).toBeVisible()
    fireEvent.change(screen.getByRole('combobox', { name: '筛选当前页结果' }), { target: { value: 'Succeeded' } })
    expect(screen.getByText('没有符合条件的日志')).toBeVisible()
  })
  it('leaves new records buffered until explicit merge, then reconciles persisted history', () => {
    const consume = vi.fn()
    stream.mockReturnValue({ buffered: [live], dropped: 0, connectionState: 'connected', retry: vi.fn(), consume })
    const view = page()
    expect(screen.queryByText('运行容量核对完成')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: '合并到第一页' }))
    expect(screen.getByText('运行容量核对完成')).toBeVisible()
    expect(consume).toHaveBeenCalledOnce()
    history.mockReturnValue({
      logs: { contract: 'cursor', items: [log, { ...live, id: 2 }], nextCursor: null },
      isLoading: false,
      isRefreshing: false,
      mutate: vi.fn(),
    })
    view.rerender(
      <I18nextProvider i18n={i18n}>
        <MemoryRouter>
          <AdminLogsPage />
        </MemoryRouter>
      </I18nextProvider>
    )
    expect(screen.getAllByText('运行容量核对完成')).toHaveLength(1)
  })
  it('keeps related URL filters scoped on the server and can clear them', () => {
    page('/admin/logs?correlationId=context-a&workerNodeId=node-a')
    expect(history).toHaveBeenLastCalledWith(
      expect.objectContaining({ correlationId: 'context-a', workerNodeId: 'node-a' })
    )
    fireEvent.click(screen.getByRole('button', { name: '清除关联筛选' }))
    expect(history).toHaveBeenLastCalledWith(
      expect.objectContaining({ correlationId: undefined, workerNodeId: undefined })
    )
  })
  it('keeps the current offset page when filtering its results', () => {
    history.mockReturnValue({
      logs: { contract: 'offset', items: [log], nextCursor: null },
      isLoading: false,
      isRefreshing: false,
      mutate: vi.fn(),
    })
    page('/admin/logs?page=2')
    fireEvent.change(screen.getByRole('combobox', { name: '筛选当前页结果' }), { target: { value: 'Succeeded' } })
    expect(history).toHaveBeenLastCalledWith(expect.objectContaining({ offset: 50 }))
  })
  it('shows actionable failure diagnostics and only one copy of safe detail', () => {
    render(
      <I18nextProvider i18n={i18n}>
        <AdminLogDrawer
          log={{
            ...log,
            outcome: 'Failed',
            errorCategory: 'AgentTransport',
            errorCode: 'agent.timeout',
            correlationId: 'context-a',
            retryable: true,
            detail: { phase: 'inventory' },
          }}
          onClose={vi.fn()}
        />
      </I18nextProvider>
    )
    expect(screen.getByText('异常诊断')).toBeVisible()
    expect(screen.getByText('Agent 通信')).toBeVisible()
    expect(screen.getByText('agent.timeout')).toBeVisible()
    expect(screen.getAllByText('context-a')).toHaveLength(1)
    expect(screen.getByText('context-a')).toBeVisible()
    expect(screen.getAllByText(/"phase": "inventory"/)).toHaveLength(1)
    expect(screen.getByRole('heading', { name: '技术详情' })).toBeVisible()
    expect(screen.getByRole('dialog').querySelector('details')).toBeNull()
  })
})

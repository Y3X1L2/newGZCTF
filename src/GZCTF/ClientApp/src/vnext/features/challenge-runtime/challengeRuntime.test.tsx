import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ContainerEntryStatus } from '@Api'
import { FlagSubmission } from './FlagSubmission'
import { InstanceControl } from './InstanceControl'
import { RuntimeInstanceController } from './types'

function controller(overrides: Partial<RuntimeInstanceController> = {}): RuntimeInstanceController {
  return {
    kind: 'docker',
    phase: 'idle',
    entry: null,
    entryStatus: null,
    entryReadyAt: null,
    entryError: null,
    closeTime: null,
    vmStatus: null,
    error: null,
    busy: false,
    create: vi.fn().mockResolvedValue(undefined),
    extend: vi.fn().mockResolvedValue(undefined),
    destroy: vi.fn().mockResolvedValue(undefined),
    refresh: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  }
}

describe('challenge runtime contract', () => {
  it('invokes the shared instance controller without feature-specific dependencies', async () => {
    const runtime = controller()
    const user = userEvent.setup()
    render(<InstanceControl controller={runtime} />)

    await user.click(screen.getByRole('button', { name: '创建实例' }))
    expect(runtime.create).toHaveBeenCalledOnce()
  })

  it('normalizes a bare host entry and exposes a safe open action', () => {
    render(<InstanceControl controller={controller({ phase: 'running', entry: '10.24.0.30:32768' })} />)

    expect(screen.getByRole('link', { name: '打开实例入口' })).toHaveAttribute('href', 'http://10.24.0.30:32768')
  })

  it('holds the public entry action until the gateway confirms the route', () => {
    render(
      <InstanceControl
        controller={controller({
          phase: 'provisioning',
          entry: null,
          entryStatus: ContainerEntryStatus.Pending,
          closeTime: Date.now() + 60_000,
        })}
      />
    )

    expect(screen.getByText('正在准备运行环境')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: '打开实例入口' })).not.toBeInTheDocument()
  })

  it('offers refresh and destroy when the gateway rejects the current route', () => {
    render(
      <InstanceControl
        controller={controller({
          phase: 'failed',
          entryStatus: ContainerEntryStatus.Error,
          entryError: 'Public gateway reload failed.',
          error: '公网入口发布失败。',
        })}
      />
    )

    expect(screen.getByRole('button', { name: '刷新状态' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '销毁实例' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '重新创建' })).not.toBeInTheDocument()
  })

  it('shows an unsafe entry as text without rendering an executable link', () => {
    render(<InstanceControl controller={controller({ phase: 'running', entry: 'javascript:alert(1)' })} />)

    expect(screen.getByText('javascript:alert(1)')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: '打开实例入口' })).not.toBeInTheDocument()
  })

  it('submits a trimmed non-empty Flag through the shared form contract', async () => {
    const onSubmit = vi.fn()
    const onValueChange = vi.fn()
    const user = userEvent.setup()
    const { rerender } = render(
      <FlagSubmission
        activeFlagId={1}
        challenge={{ attempts: 0, flags: [{ id: 1 }] }}
        disabledReason={null}
        feedback={null}
        onFlagChange={() => undefined}
        onSubmit={onSubmit}
        onValueChange={onValueChange}
        pending={false}
        solved={false}
        solvedFlagIds={new Set()}
        value=""
      />
    )

    expect(screen.getByRole('button', { name: '提交' })).toBeDisabled()
    rerender(
      <FlagSubmission
        activeFlagId={1}
        challenge={{ attempts: 0, flags: [{ id: 1 }] }}
        disabledReason={null}
        feedback={null}
        onFlagChange={() => undefined}
        onSubmit={onSubmit}
        onValueChange={onValueChange}
        pending={false}
        solved={false}
        solvedFlagIds={new Set()}
        value="flag{shared-runtime}"
      />
    )
    await user.click(screen.getByRole('button', { name: '提交' }))
    expect(onSubmit).toHaveBeenCalledOnce()
  })
})

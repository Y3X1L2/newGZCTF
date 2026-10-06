import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ImageStatus, ImageType, OSType } from '@Api'
import type { ImageTemplateSummary } from '../api'
import { AdminImagesPage } from './AdminImagesPage'

const { imagesHook, detail } = vi.hoisted(() => ({ imagesHook: vi.fn(), detail: vi.fn() }))
vi.mock('./useAdminImages', async importOriginal => ({ ...await importOriginal<object>(),
  useAdminImages: imagesHook, useDockerRegistry: () => ({ registry: null }) }))
vi.mock('../api', async importOriginal => ({ ...await importOriginal<object>(),
  imageTemplateAdminApi: { detail } }))
vi.mock('./ImageRemoteAccessDialog', () => ({ ImageRemoteAccessDialog: ({ template }: { template: ImageTemplateSummary | null }) =>
  template ? <div>远程设置对话框</div> : null }))

const template: ImageTemplateSummary = {
  id: 42, name: 'Linux 模板', osType: OSType.Linux, imageType: ImageType.Qcow2,
  fileSize: 1, status: ImageStatus.Ready, description: null, errorMessage: null,
  imageHash: null, uploadedAt: 1, registryUrl: null, canManage: false,
}

function mount() {
  return render(<MemoryRouter initialEntries={['/admin/images?template=42&remoteAccess=1&returnTo=%2Fadmin%2Fteamlab%2Fruntimes%2Frun-a']}>
    <AdminImagesPage />
  </MemoryRouter>)
}

describe('AdminImagesPage runtime settings deep link', () => {
  beforeEach(() => { detail.mockResolvedValue(template) })

  it('reports a template that no longer exists', async () => {
    imagesHook.mockReturnValue({ images: [], error: undefined, isLoading: false, isRefreshing: false, mutate: vi.fn() })
    mount()
    expect(await screen.findByText(/找不到指定的镜像模板/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '返回运行环境' })).toHaveAttribute('href', '/admin/teamlab/runtimes/run-a')
    expect(screen.queryByText('远程设置对话框')).toBeNull()
  })

  it('reports a template without management permission', async () => {
    imagesHook.mockReturnValue({ images: [template], error: undefined, isLoading: false, isRefreshing: false, mutate: vi.fn() })
    mount()
    expect(await screen.findByText(/当前账号无权修改该镜像模板/)).toBeInTheDocument()
    expect(screen.queryByText('远程设置对话框')).toBeNull()
  })
})

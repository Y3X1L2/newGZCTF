import { ImageStatus, ImageType, OSType } from '@Api'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { imageTemplateAdminApi } from '../../api'
import { listTeamLabImageOptions } from './teamlabImageCatalog'

vi.mock('../../api', () => ({
  imageTemplateAdminApi: {
    list: vi.fn(),
  },
}))

describe('TeamLab image catalog', () => {
  beforeEach(() => vi.clearAllMocks())

  it('keeps the artifact reference and VM network default in the same option', async () => {
    vi.mocked(imageTemplateAdminApi.list).mockResolvedValue({
      total: 1,
      page: 1,
      pageSize: 100,
      items: [
        {
          id: 519,
          name: 'Lab2 web',
          osType: OSType.Linux,
          imageType: ImageType.Qcow2,
          fileSize: 1024,
          status: ImageStatus.Ready,
          description: null,
          errorMessage: null,
          imageHash: 'sha256:abc',
          uploadedAt: 1,
          registryUrl: 'vm-images/lab2-web.qcow2',
          vmNetworkMode: 2,
          remoteAccessProtocol: 'ssh',
        },
      ],
    })

    await expect(listTeamLabImageOptions()).resolves.toEqual([
      expect.objectContaining({
        id: 519,
        artifactReference: 'vm-images/lab2-web.qcow2',
        deviceType: 'linux-vm',
        remoteAccessProtocol: 'ssh',
        vmNetworkMode: 'managed-static',
      }),
    ])
  })
})

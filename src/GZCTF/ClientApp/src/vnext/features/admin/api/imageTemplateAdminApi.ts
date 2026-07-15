import type { ImageType, OSType } from '@Api'
import { contractFailure, isBoolean, isNullableString, isNumber, isRecord, isString } from './contractParsers'
import type { DockerRegistrySummary, ImageTemplateIdentity, ImageTemplateSummary } from './contracts'
import { runtimeJsonClient, type RuntimeJsonClient, type RuntimeUploadOptions } from './runtimeJsonClient'

export interface ImageTemplateListQuery {
  osType?: OSType
  imageType?: ImageType
  search?: string
  page?: number
  pageSize?: number
}

export interface DockerTemplateRegistration {
  name: string
  registryUrl: string
  osType: OSType
  registryAuth?: string | null
}

function isImageTemplateIdentity(value: unknown): value is ImageTemplateIdentity {
  return (
    isRecord(value) && isNumber(value.id) && isString(value.name) && isNumber(value.osType) && isNumber(value.imageType)
  )
}

function isImageTemplateSummary(value: unknown): value is ImageTemplateSummary {
  return (
    isRecord(value) &&
    isNumber(value.id) &&
    isString(value.name) &&
    isNumber(value.osType) &&
    isNumber(value.imageType) &&
    isNumber(value.fileSize) &&
    isNumber(value.status) &&
    isNullableString(value.description) &&
    isNullableString(value.errorMessage) &&
    isNullableString(value.imageHash) &&
    isNumber(value.uploadedAt) &&
    isNullableString(value.registryUrl) &&
    (value.containsMalware === undefined || isBoolean(value.containsMalware))
  )
}

function parseTemplate(value: unknown, label: string) {
  if (!isImageTemplateSummary(value)) return contractFailure(label, value)
  return value
}

function parseIdentity(value: unknown, label: string) {
  if (!isImageTemplateIdentity(value)) return contractFailure(label, value)
  return value
}

function parseRegistry(value: unknown) {
  if (
    !isRecord(value) ||
    !isBoolean(value.enabled) ||
    !isString(value.address) ||
    !isString(value.namespace) ||
    !isNumber(value.maxUploadSizeGb)
  ) {
    return contractFailure('Docker registry settings', value)
  }
  return value as unknown as DockerRegistrySummary
}

export function createImageTemplateAdminApi(client: RuntimeJsonClient = runtimeJsonClient) {
  return {
    async list(query: ImageTemplateListQuery = {}) {
      const value = await client.get('/api/v1/image-templates', {
        osType: query.osType,
        imageType: query.imageType,
        search: query.search,
        page: query.page ?? 1,
        pageSize: query.pageSize ?? 20,
      })
      if (
        !isRecord(value) ||
        !isNumber(value.total) ||
        !isNumber(value.page) ||
        !isNumber(value.pageSize) ||
        !Array.isArray(value.items) ||
        !value.items.every(isImageTemplateSummary)
      ) {
        return contractFailure('Image template list', value)
      }
      return {
        total: value.total,
        page: value.page,
        pageSize: value.pageSize,
        items: value.items,
      }
    },

    async detail(id: number) {
      return parseTemplate(await client.get(`/api/v1/image-templates/${id}`), 'Image template detail')
    },

    async registry() {
      return parseRegistry(await client.get('/api/v1/image-templates/docker-registry'))
    },

    async registerDocker(data: DockerTemplateRegistration) {
      return parseIdentity(
        await client.postJson('/api/v1/image-templates/register-docker', data),
        'Docker registration'
      )
    },

    async uploadDockerArchive(
      data: { file: File; name: string; sourceImage?: string; osType: OSType },
      options?: RuntimeUploadOptions
    ) {
      return parseIdentity(
        await client.postForm('/api/v1/image-templates/upload-docker', data, undefined, options),
        'Docker archive upload'
      )
    },

    async uploadVm(file: File, options?: RuntimeUploadOptions) {
      return parseIdentity(
        await client.postForm('/api/v1/image-templates', { file }, undefined, options),
        'VM image upload'
      )
    },

    async uploadVmArchive(file: File, options?: RuntimeUploadOptions) {
      return parseIdentity(
        await client.postForm('/api/v1/image-templates/upload', { file }, undefined, options),
        'VM archive upload'
      )
    },

    async importLocal(data: { localPath: string; displayName?: string | null }) {
      return parseIdentity(await client.postJson('/api/v1/image-templates/import-local', data), 'Local image import')
    },

    async delete(id: number) {
      await client.delete(`/api/v1/image-templates/${id}`)
    },
  }
}

export const imageTemplateAdminApi = createImageTemplateAdminApi()

import type { Identity, Permission, Schema } from '../../api/client'

export function allowsProject(identity: Identity | undefined, permission: Permission, projectId?: string) {
  return Boolean(
    identity?.administrator ||
    identity?.grants?.some(
      (grant) =>
        grant.scopeKind === 'project' &&
        (projectId === undefined || grant.scopeId === projectId) &&
        grant.permissions.includes(permission)
    )
  )
}

export function allows(
  environment: Schema<'Environment'> | undefined,
  permission: Schema<'Permission'>,
  assetId?: string
) {
  return Boolean(
    environment?.permissions?.includes(permission) ||
    (assetId && environment?.assetPermissions?.[assetId]?.includes(permission))
  )
}

import { Role } from '@Api'
import type { AdminStatusTone } from '../shared/AdminWorkbench'

export function adminRoleLabel(role?: Role | null) {
  if (role === Role.SuperAdmin) return '超级管理员'
  if (role === Role.Admin) return '管理员'
  if (role === Role.Teacher) return '教师'
  if (role === Role.Banned) return '已停用'
  return '学员'
}

export function adminRoleTone(role?: Role | null): AdminStatusTone {
  if (role === Role.SuperAdmin || role === Role.Admin) return 'warning'
  if (role === Role.Teacher) return 'info'
  if (role === Role.Banned) return 'danger'
  return 'neutral'
}

export function assignableRoles(actorRole?: Role | null) {
  if (actorRole === Role.SuperAdmin) return [Role.Student, Role.Teacher, Role.Admin, Role.SuperAdmin, Role.Banned]
  if (actorRole === Role.Admin) return [Role.Student, Role.Teacher, Role.Banned]
  return []
}

export function isStudentRole(role?: Role | null) {
  return role === Role.Student
}

import { describe, expect, it } from 'vitest'
import { Role } from '@Api'
import { adminRoleLabel, adminRoleTone, assignableRoles, isStudentRole } from './userPresentation'

describe('user presentation', () => {
  it('maps roles to stable labels and tones', () => {
    expect(adminRoleLabel(Role.SuperAdmin)).toBe('超级管理员')
    expect(adminRoleTone(Role.Banned)).toBe('danger')
    expect(isStudentRole(Role.Student)).toBe(true)
  })

  it('limits role assignment to the actor capability', () => {
    expect(assignableRoles(Role.Admin)).toEqual([Role.Student, Role.Teacher, Role.Banned])
    expect(assignableRoles(Role.SuperAdmin)).toContain(Role.SuperAdmin)
    expect(assignableRoles(Role.Teacher)).toEqual([])
  })
})

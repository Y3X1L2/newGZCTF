import api, {
  type AdminTeamModel,
  type AdminUserInfoModel,
  type ConfigEditModel,
  type Role,
  type StudentGroupEditModel,
  type StudentGroupManagerEditModel,
  type StudentGroupMemberEditModel,
  type UserCreateModel,
} from '@Api'

const DEFAULT_PAGE_SIZE = 30

export interface AdminUserListQuery {
  page?: number
  pageSize?: number
  keyword?: string
  role?: Role
  groupId?: number
}

export interface AdminTeamListQuery {
  page?: number
  pageSize?: number
  keyword?: string
}

function pageParams(page = 1, pageSize = DEFAULT_PAGE_SIZE) {
  const normalizedPage = Math.max(1, page)
  const normalizedPageSize = Math.min(100, Math.max(1, pageSize))
  return {
    page: normalizedPage,
    pageSize: normalizedPageSize,
    skip: (normalizedPage - 1) * normalizedPageSize,
  }
}

export const commonAdminKeys = {
  users: (query: AdminUserListQuery = {}) => [
    'vnext:admin:users',
    query.page ?? 1,
    query.pageSize ?? DEFAULT_PAGE_SIZE,
    query.keyword ?? '',
    query.role ?? 'all',
    query.groupId ?? 'all',
  ] as const,
  teams: (query: AdminTeamListQuery = {}) => [
    'vnext:admin:teams',
    query.page ?? 1,
    query.pageSize ?? DEFAULT_PAGE_SIZE,
    query.keyword ?? '',
  ] as const,
  studentGroups: (keyword = '', includeArchived = false) => [
    'vnext:admin:student-groups',
    keyword,
    includeArchived,
  ] as const,
  studentGroup: (groupId: number | null) => ['vnext:admin:student-group', groupId] as const,
  systemConfig: ['vnext:admin:system-config'] as const,
}

export const commonAdminApi = {
  async users(query: AdminUserListQuery = {}) {
    const paging = pageParams(query.page, query.pageSize)
    const response = await api.admin.adminUsers({
      count: paging.pageSize,
      skip: paging.skip,
      keyword: query.keyword || undefined,
      role: query.role,
      groupId: query.groupId,
    })
    return {
      items: response.data.data,
      total: response.data.total ?? response.data.length,
      page: paging.page,
      pageSize: paging.pageSize,
    }
  },

  async createUser(payload: UserCreateModel) {
    await api.admin.adminAddUsers([payload])
  },

  async updateUser(userId: string, payload: AdminUserInfoModel) {
    await api.admin.adminUpdateUserInfo(userId, payload)
  },

  async resetUserPassword(userId: string) {
    return (await api.admin.adminResetPassword(userId)).data
  },

  async deleteUser(userId: string) {
    await api.admin.adminDeleteUser(userId)
  },

  async teams(query: AdminTeamListQuery = {}) {
    const keyword = query.keyword?.trim()
    const paging = pageParams(query.page, query.pageSize)
    if (keyword) {
      const response = await api.admin.adminSearchTeams({ hint: keyword })
      return {
        items: response.data.data,
        total: response.data.total ?? response.data.length,
        page: 1,
        pageSize: Math.max(1, response.data.length),
        searchResult: true,
      }
    }

    const response = await api.admin.adminTeams({ count: paging.pageSize, skip: paging.skip })
    return {
      items: response.data.data,
      total: response.data.total ?? response.data.length,
      page: paging.page,
      pageSize: paging.pageSize,
      searchResult: false,
    }
  },

  async updateTeam(teamId: number, payload: AdminTeamModel) {
    await api.admin.adminUpdateTeam(teamId, payload)
  },

  async deleteTeam(teamId: number) {
    await api.admin.adminDeleteTeam(teamId)
  },

  async studentGroups(keyword = '', includeArchived = false) {
    return (await api.studentGroupAdmin.studentGroupAdminGetGroups({ keyword: keyword || undefined, includeArchived })).data
  },

  async studentGroup(groupId: number) {
    return (await api.studentGroupAdmin.studentGroupAdminGetGroup(groupId)).data
  },

  async createStudentGroup(payload: StudentGroupEditModel) {
    return (await api.studentGroupAdmin.studentGroupAdminCreateGroup(payload)).data
  },

  async updateStudentGroup(groupId: number, payload: StudentGroupEditModel) {
    await api.studentGroupAdmin.studentGroupAdminUpdateGroup(groupId, payload)
  },

  async archiveStudentGroup(groupId: number) {
    await api.studentGroupAdmin.studentGroupAdminArchiveGroup(groupId)
  },

  async addStudentGroupMember(groupId: number, payload: StudentGroupMemberEditModel) {
    await api.studentGroupAdmin.studentGroupAdminAddMember(groupId, payload)
  },

  async removeStudentGroupMember(groupId: number, studentId: string) {
    await api.studentGroupAdmin.studentGroupAdminRemoveMember(groupId, studentId)
  },

  async addStudentGroupManager(groupId: number, payload: StudentGroupManagerEditModel) {
    await api.studentGroupAdmin.studentGroupAdminAddManager(groupId, payload)
  },

  async removeStudentGroupManager(groupId: number, teacherId: string) {
    await api.studentGroupAdmin.studentGroupAdminRemoveManager(groupId, teacherId)
  },

  async systemConfig() {
    return (await api.admin.adminGetConfigs()).data
  },

  async updateSystemConfig(payload: ConfigEditModel) {
    await api.admin.adminUpdateConfigs(payload)
  },

  async uploadLogo(file: File) {
    await api.admin.adminUpdateLogo({ file })
  },

  async resetLogo() {
    await api.admin.adminResetLogo()
  },
}

import api, { ContentType } from '@Api'

const request = api.request

export interface StudentGroupBriefModel {
  id: number
  name: string
  description: string
  isArchived: boolean
  memberCount: number
  managerCount: number
  updatedAt: number
}

export interface StudentGroupDetailModel extends StudentGroupBriefModel {
  members: StudentGroupMemberModel[]
  managers: StudentGroupManagerModel[]
}

export interface StudentGroupMemberModel {
  studentId: string
  userName: string
  realName: string
  stdNumber: string
  avatar?: string | null
  note: string
  joinedAt: number
}

export interface StudentGroupManagerModel {
  teacherId: string
  userName: string
  realName: string
  roleInGroup: string
}

export interface StudentGroupEditModel {
  name: string
  description: string
}

export const studentGroupAdminApi = {
  groups: () =>
    request<StudentGroupBriefModel[], unknown>({
      path: '/api/admin/student-groups',
      method: 'GET',
    }),

  group: (groupId: number) =>
    request<StudentGroupDetailModel, unknown>({
      path: `/api/admin/student-groups/${groupId}`,
      method: 'GET',
    }),

  createGroup: (data: StudentGroupEditModel) =>
    request<StudentGroupDetailModel, unknown>({
      path: '/api/admin/student-groups',
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  addGroupMember: (groupId: number, data: { studentId: string; note?: string }) =>
    request<void, unknown>({
      path: `/api/admin/student-groups/${groupId}/members`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  removeGroupMember: (groupId: number, studentId: string) =>
    request<void, unknown>({
      path: `/api/admin/student-groups/${groupId}/members/${studentId}`,
      method: 'DELETE',
    }),
}

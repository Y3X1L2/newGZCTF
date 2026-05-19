export type SubmissionType = 'Flag' | 'Writeup' | 'IP' | 'Credential' | 'Custom';

export interface SubmissionCreatePayload {
  answer: string; submissionType: SubmissionType;
  challengeId: number; gameId: number;
  teamId: number; participationId: number;
  content?: string;
}

import { RuntimeApiError } from '../../api/runtimeJsonClient'

export class TeamLabContractError extends RuntimeApiError {
  constructor(label: string, payload: unknown) {
    super(`${label} returned an unexpected response shape.`, {
      kind: 'contract',
      code: 'invalid_teamlab_response_shape',
      payload,
    })
    this.name = 'TeamLabContractError'
  }
}

export function teamLabContractFailure(label: string, payload: unknown): never {
  throw new TeamLabContractError(label, payload)
}

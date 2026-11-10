import { RuntimeApiError } from './runtimeJsonClient'

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

type UnknownRecord = Record<string, unknown>
function record(value: unknown, label: string): UnknownRecord {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return teamLabContractFailure(label, value)
  return value as UnknownRecord
}

function string(value: unknown, label: string): string {
  if (typeof value !== 'string') return teamLabContractFailure(label, value)
  return value
}

function number(value: unknown, label: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) return teamLabContractFailure(label, value)
  return value
}

function boolean(value: unknown, label: string): boolean {
  if (typeof value !== 'boolean') return teamLabContractFailure(label, value)
  return value
}

function nullableString(value: unknown, label: string): string | null {
  return value === null || value === undefined ? null : string(value, label)
}

function nullableNumber(value: unknown, label: string): number | null {
  return value === null || value === undefined ? null : number(value, label)
}

function array<T>(value: unknown, label: string, parser: (item: unknown, itemLabel: string) => T): T[] {
  if (!Array.isArray(value)) return teamLabContractFailure(label, value)
  return value.map((item, index) => parser(item, `${label}[${index}]`))
}

function enumValue<T extends string>(value: unknown, values: Record<string, T>, label: string): T {
  const parsed = values[String(value)]
  if (!parsed) return teamLabContractFailure(label, value)
  return parsed
}

export const teamLabParsing = {
  record,
  string,
  number,
  boolean,
  nullableString,
  nullableNumber,
  array,
  enumValue,
}

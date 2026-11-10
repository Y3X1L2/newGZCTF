import { keepPreviousData, useQuery } from '@tanstack/react-query'
import ELK from 'elkjs/lib/elk-api.js'
import LayoutWorker from 'elkjs/lib/elk-worker.min.js?worker'
import type { EnvironmentSpec, Template } from '../../api/client'
import { topology } from './topology'

const engine = new ELK({ workerFactory: () => new LayoutWorker() })

export function useTopology(spec: EnvironmentSpec, templates: Template[]) {
  return useQuery({
    queryKey: ['topology', spec, templates.map(({ id, kind }) => ({ id, kind }))],
    queryFn: () => topology(spec, templates, engine),
    placeholderData: keepPreviousData,
    staleTime: Infinity,
    gcTime: 0,
    retry: false,
  })
}

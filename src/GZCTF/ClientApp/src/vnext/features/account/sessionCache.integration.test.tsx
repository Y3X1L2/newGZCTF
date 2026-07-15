import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useEffect, useRef } from 'react'
import useSWR, { SWRConfig, useSWRConfig } from 'swr'
import { describe, expect, it } from 'vitest'
import { clearAccountSessionCache } from './sessionCache'

const accountKey = '/api/account/profile'
const progressKey = '/api/training/courses/3/progress'
const oldAccount = { userName: 'old-user' }
const newAccount = { userName: 'new-user' }
const oldProgress = { title: 'old-course' }
const newProgress = { title: 'new-course' }

function AccountConsumers() {
  const primary = useSWR<typeof oldAccount>(accountKey, null, { keepPreviousData: false })
  const secondary = useSWR<typeof oldAccount>(accountKey, null, { keepPreviousData: false })
  const progress = useSWR<typeof oldProgress>(progressKey, null, { keepPreviousData: false })
  const { mutate } = useSWRConfig()
  const seeded = useRef(false)

  useEffect(() => {
    if (seeded.current) return
    seeded.current = true
    void primary.mutate(oldAccount, { revalidate: false })
    void progress.mutate(oldProgress, { revalidate: false })
  }, [primary, progress])

  return (
    <>
      <output aria-label="primary-account">{primary.data?.userName ?? 'signed-out'}</output>
      <output aria-label="secondary-account">{secondary.data?.userName ?? 'signed-out'}</output>
      <output aria-label="course-progress">{progress.data?.title ?? 'no-progress'}</output>
      <button onClick={() => void clearAccountSessionCache(primary.mutate, mutate)} type="button">
        clear session
      </button>
      <button
        onClick={() => {
          void primary.mutate(newAccount, { revalidate: false })
          void progress.mutate(newProgress, { revalidate: false })
        }}
        type="button"
      >
        start new session
      </button>
    </>
  )
}

describe('account session cache integration', () => {
  it('invalidates every subscriber immediately without a reload', async () => {
    const user = userEvent.setup()
    render(
      <SWRConfig value={{ provider: () => new Map(), refreshInterval: 0 }}>
        <AccountConsumers />
      </SWRConfig>
    )

    await waitFor(() => expect(screen.getByLabelText('primary-account')).toHaveTextContent('old-user'))
    expect(screen.getByLabelText('secondary-account')).toHaveTextContent('old-user')
    expect(screen.getByLabelText('course-progress')).toHaveTextContent('old-course')

    await user.click(screen.getByRole('button', { name: 'clear session' }))

    await waitFor(() => expect(screen.getByLabelText('primary-account')).toHaveTextContent('signed-out'))
    expect(screen.getByLabelText('secondary-account')).toHaveTextContent('signed-out')
    expect(screen.getByLabelText('course-progress')).toHaveTextContent('no-progress')

    await user.click(screen.getByRole('button', { name: 'start new session' }))

    await waitFor(() => expect(screen.getByLabelText('primary-account')).toHaveTextContent('new-user'))
    expect(screen.getByLabelText('secondary-account')).toHaveTextContent('new-user')
    expect(screen.getByLabelText('course-progress')).toHaveTextContent('new-course')
    expect(screen.queryByText('old-user')).not.toBeInTheDocument()
    expect(screen.queryByText('old-course')).not.toBeInTheDocument()
  })
})

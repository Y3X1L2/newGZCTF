import { ModalProps } from '@mantine/core'
import { useInputState } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose, mdiLoading } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChallengeModal } from '@Components/ChallengeModal'
import { encryptApiData } from '@Utils/Crypto'
import { showErrorMsg } from '@Utils/Shared'
import { ChallengeCategoryItemProps } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import api, { AnswerResult, ChallengeType, SubmissionType } from '@Api'

interface GameChallengeModalProps extends ModalProps {
  gameId: number
  gameTitle: string
  gameEnded: boolean
  practiceMode?: boolean
  cateData: ChallengeCategoryItemProps
  title: string
  score: number
  challengeId: number
  status?: SubmissionType
}

export const GameChallengeModal: FC<GameChallengeModalProps> = (props) => {
  const { gameId, gameTitle, gameEnded, practiceMode, challengeId, cateData, status, title, score, ...modalProps } =
    props

  const { data: challenge, mutate } = api.game.useGameGetChallenge(gameId, challengeId, {
    refreshInterval: 120 * 1000,
  })

  const { config } = useConfig()
  const { t } = useTranslation()

  const wrongFlagHints = t('challenge.content.wrong_flag_hints', {
    returnObjects: true,
  }) as string[]

  const isDynamic =
    challenge?.type === ChallengeType.StaticContainer || challenge?.type === ChallengeType.DynamicContainer

  useEffect(() => {
    if ((challenge?.flags?.length ?? 0) > 1 && activeFlagId === null) {
      setActiveFlagId(challenge!.flags![0].id!)
    }
  }, [challenge?.flags])

  const [disabled, setDisabled] = useState(false)
  const [flag, setFlag] = useInputState('')
  const [solvedChallengeId, setSolvedChallengeId] = useState<number | null>(null)
  const [activeFlagId, setActiveFlagId] = useState<number | null>(null)

  const isLimitReached = (challenge?.limit && (challenge.attempts ?? 0) >= challenge.limit) || false

  const onCreate = async () => {
    if (!challengeId || disabled) return
    setDisabled(true)

    try {
      const res = await api.game.gameCreateContainer(gameId, challengeId)
      mutate({
        ...challenge,
        context: {
          ...challenge?.context,
          closeTime: res.data.expectStopAt,
          instanceEntry: res.data.entry,
        },
      }, false) // don't revalidate — API returns stale data until DB is updated
      showNotification({
        color: 'teal',
        title: t('challenge.notification.instance.created.title'),
        message: t('challenge.notification.instance.created.message'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const requestDestroy = async () => {
    try {
      if (!challenge?.context?.instanceEntry) return

      await api.game.gameDeleteContainer(gameId, challengeId)
      mutate({
        ...challenge,
        context: {
          ...challenge?.context,
          closeTime: null,
          instanceEntry: null,
        },
      }, false) // don't revalidate after destroy
      showNotification({
        color: 'teal',
        title: t('challenge.notification.instance.destroyed.title'),
        message: t('challenge.notification.instance.destroyed.message'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const onDestroy = async () => {
    if (!challengeId || disabled) return
    setDisabled(true)

    await requestDestroy()

    setDisabled(false)
  }

  const onExtend = async () => {
    if (!challengeId || disabled) return
    setDisabled(true)

    try {
      const res = await api.game.gameExtendContainerLifetime(gameId, challengeId)
      mutate({
        ...challenge,
        context: {
          ...challenge?.context,
          closeTime: res.data.expectStopAt,
        },
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const onSubmit = async () => {
    if (!challengeId || !flag) {
      showNotification({
        color: 'red',
        message: t('challenge.notification.flag.empty'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    setDisabled(true)

    try {
      const res = await api.game.gameSubmit(gameId, challengeId, {
        flag: await encryptApiData(t, flag, config.apiPublicKey),
        ...((challenge?.flags?.length ?? 0) > 1 && activeFlagId ? { flagId: activeFlagId } : {}),
      })

      const nxt = (challenge?.attempts ?? 0) + 1
      const attempts = challenge?.limit && challenge.limit > 0 ? Math.min(nxt, challenge.limit) : nxt

      mutate({
        attempts,
        ...challenge,
      })

      setDisabled(false)
      setFlag('')
      checkDataFlag(res.data.id, res.data.status)
    } catch (e) {
      showErrorMsg(e, t)
      setDisabled(false)
    }
  }

  useEffect(() => {
    if (challengeId !== solvedChallengeId) return

    if (status !== SubmissionType.Unaccepted && status !== undefined) {
      // status has been updated, reset solved challenge id
      setSolvedChallengeId(null)
    }
  }, [status, challengeId, challenge])

  const checkDataFlag = async (id: number, data: string) => {
    if (data === AnswerResult.Accepted) {
      setSolvedChallengeId(challengeId)
      showNotification({
        color: 'teal',
        title: t('challenge.notification.flag.accepted.title'),
        message: gameEnded
          ? t('challenge.notification.flag.accepted.ended')
          : t('challenge.notification.flag.accepted.message'),
        icon: <Icon path={mdiCheck} size={1} />,
        autoClose: 8000,
      })
      if (isDynamic && challenge.context?.instanceEntry) await requestDestroy()
      props.onClose()
    } else if (data === AnswerResult.WrongAnswer) {
      showNotification({
        color: 'red',
        title: t('challenge.notification.flag.wrong'),
        message: wrongFlagHints[Math.floor(Math.random() * wrongFlagHints.length)],
        icon: <Icon path={mdiClose} size={1} />,
        autoClose: 8000,
      })
    } else {
      showNotification({
        color: 'yellow',
        title: t('challenge.notification.flag.unknown.title'),
        message: t('challenge.notification.flag.unknown.message', {
          id,
        }),
        icon: <Icon path={mdiLoading} size={1} />,
        autoClose: false,
        withCloseButton: true,
      })
    }
  }

  return (
    <ChallengeModal
      {...modalProps}
      gameTitle={gameTitle}
      challenge={challenge ?? { title, score }}
      cateData={cateData}
      solved={(status !== SubmissionType.Unaccepted && status !== undefined) || solvedChallengeId === challengeId}
      flag={flag}
      setFlag={setFlag}
      onCreate={onCreate}
      onDestroy={onDestroy}
      onSubmitFlag={onSubmit}
      disabled={disabled || isLimitReached}
      onExtend={onExtend}
      gameEnded={gameEnded}
      practiceMode={practiceMode}
      activeFlagId={activeFlagId}
      setActiveFlagId={setActiveFlagId}
    />
  )
}

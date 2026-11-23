# Triage Labels

The skills speak in terms of five canonical triage roles. This file maps those roles to the actual label strings used in this repo's issue tracker.

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`          | `ready-for-agent`    | Fully specified, ready for an AFK agent  |
| `ready-for-human`          | `ready-for-human`    | Requires human implementation            |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label string from this table.

Edit the right-hand column to match whatever vocabulary you actually use.

## Смысл меток в агентском цикле

Конвейер описан в [`sdlc.md`](./sdlc.md), решение — в ADR-0023.

- `needs-triage` — черновик идеи ждёт сессии опроса; ставится шаблоном issue «Идея».
- `needs-info` — **приглашение к сессии опроса**: опрос отложен, в комментарии
  Triage Notes записано, что установлено и что осталось. Продолжение —
  синхронная сессия `/triage #N`; вопросы в комментариях асинхронно не ведутся.
- `ready-for-agent` — чек-лист готовности пройден; ставит только человек
  (подтверждением в сессии или вручную).
- `ready-for-human` — реализация требует человека.

Метки треков (`track:*`) не вводятся: трек виден по артефакту — бриф агента
(Срез), issue-спека (Спека), `wayfinder:map` (Карта).

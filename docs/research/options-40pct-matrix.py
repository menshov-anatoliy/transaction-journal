# -*- coding: utf-8 -*-
"""Матрица решений для плана «40% годовых от счёта 3 000 USDT» на продаже
опционной премии (Bybit, закрытый риск).

Модель одной конструкции:
  выигрыш: +g * R   (g — payoff: средний профит / средний убыток, в долях риска R)
  проигрыш: -R     (полный плановый риск; сценарий B: -0.75*R — досрочное
                    закрытие ног, как в фактической истории владельца)

Фиксированные параметры плана (см. options-40pct-plan.md):
  база 3 000 USDT, цель +1 200 USDT/год (~100/мес), лимит просадки 20% от пика,
  риск на конструкцию R = 150/195/240 USDT (5–8% счёта), не более 2 конструкций
  одновременно, DTE входа 60–90+, удержание ~30–60 дней → пропускная
  способность 12–24 конструкции/год.

Вывод: аналитическая матрица (требуемое N конструкций/мес) и матрицы
Монте-Карло: P(достичь +40% за год) и P(просадка >= 20%).
"""
import numpy as np

rng = np.random.default_rng(42)

BASE = 3000.0
TARGET = 1200.0          # 40% годовых от базы
DD_LIMIT = 600.0         # 20% от базы
MONTHLY = TARGET / 12.0  # 100 USDT

WINRATES = [0.55, 0.62, 0.70, 0.78, 0.85]
PAYOFFS = [0.30, 0.50, 0.75, 1.00, 1.50]
RISKS = {"R150 (5%)": 150.0, "R195 (6.5%)": 195.0, "R240 (8%)": 240.0}
TRADES_PER_YEAR = 24     # пропускной потолок: 2 конструкции/мес
SIMS = 20000


def expectancy(p, g, risk):
    """Матожидание одной конструкции, USDT (проигрыш = полный риск)."""
    return risk * (p * g - (1.0 - p))


def simulate(p, g, risk, n_trades, loss_mult=1.0, sims=SIMS):
    """Монте-Карло года: P(цели), P(просадка >= 20%), медиана PnL."""
    outcomes = rng.random((sims, n_trades)) < p
    pnl = np.where(outcomes, g * risk, -loss_mult * risk)
    equity = BASE + np.cumsum(pnl, axis=1)
    equity = np.hstack([np.full((sims, 1), BASE), equity])
    running_max = np.maximum.accumulate(equity, axis=1)
    max_dd = (running_max - equity).max(axis=1)
    year_pnl = equity[:, -1] - BASE
    return (year_pnl >= TARGET).mean(), (max_dd >= DD_LIMIT).mean(), np.median(year_pnl)


def md_table(title, rows, header):
    print(f"\n### {title}\n")
    print("| " + " | ".join(header) + " |")
    print("|" + "---|" * len(header))
    for r in rows:
        print("| " + " | ".join(str(x) for x in r) + " |")


# --- 1. Аналитическая матрица: требуемое N конструкций/мес для 100 USDT/мес ---
for label, risk in RISKS.items():
    rows = []
    for p in WINRATES:
        row = [f"p={p:.0%}"]
        for g in PAYOFFS:
            e = expectancy(p, g, risk)
            n = MONTHLY / e if e > 0 else float("inf")
            row.append("—" if n > 6 else f"{n:.1f}")
        rows.append(row)
    md_table(f"Требуемое N конструкций/мес (риск {label}, цель 100 USDT/мес)",
             rows, ["винрейт \\ payoff"] + [f"g={g:.2f}" for g in PAYOFFS])

# --- 2. Монте-Карло при 24 конструкциях/год, проигрыш = полный риск ---
for label, risk in RISKS.items():
    rows = []
    for p in WINRATES:
        row = [f"p={p:.0%}"]
        for g in PAYOFFS:
            pt, pd_, med = simulate(p, g, risk, TRADES_PER_YEAR)
            row.append(f"{pt:.0%} / {pd_:.0%}")
        rows.append(row)
    md_table(f"P(достичь +40%) / P(просадка >= 20%) — 24 конструкции/год, риск {label}",
             rows, ["винрейт \\ payoff"] + [f"g={g:.2f}" for g in PAYOFFS])

# --- 3. Сценарий B: досрочное закрытие ног (проигрыш 0.75*R), риск 195 ---
rows = []
for p in WINRATES:
    row = [f"p={p:.0%}"]
    for g in PAYOFFS:
        pt, pd_, med = simulate(p, g, 195.0, TRADES_PER_YEAR, loss_mult=0.75)
        row.append(f"{pt:.0%} / {pd_:.0%}")
    rows.append(row)
md_table("Сценарий B: P(цели) / P(просадка >= 20%) — проигрыш 0.75*R, риск 195",
         rows, ["винрейт \\ payoff"] + [f"g={g:.2f}" for g in PAYOFFS])

# --- 4. Чувствительность к пропускной способности (базовый риск 195, g=1.0) ---
rows = []
for p in [0.62, 0.70, 0.78]:
    for n_yr, name in [(12, "1/мес (12/год)"), (18, "1.5/мес (18/год)"), (24, "2/мес (24/год)")]:
        pt, pd_, med = simulate(p, 1.0, 195.0, n_yr)
        rows.append([f"p={p:.0%}", name, f"{pt:.0%}", f"{pd_:.0%}", f"{med:+.0f}"])
md_table("Чувствительность к числу конструкций (g=1.0, риск 195)",
         rows, ["винрейт", "темп", "P(цели)", "P(DD>=20%)", "медиана PnL"])

# --- 5. Фактическая точка владельца (калибровка по закрытым ордерам) ---
p0, g0 = 0.62, 1.56
for loss_mult, tag in [(1.0, "полный риск"), (0.75, "убыток 0.75R")]:
    for n_yr, name in [(12, "1/мес"), (18, "1.5/мес"), (24, "2/мес")]:
        pt, pd_, med = simulate(p0, g0, 195.0, n_yr, loss_mult=loss_mult)
        print(f"\nФактическая точка p=62%, g=1.56, риск 195, {name}, {tag}: "
              f"P(цели)={pt:.0%}, P(DD>=20%)={pd_:.0%}, медиана PnL={med:+.0f}")
e0 = expectancy(p0, g0, 195.0)
print(f"\nФактическая точка: E={e0:+.1f} USDT/конструкцию, "
      f"требуемое N={MONTHLY / e0:.1f}/мес, запас цели = {e0 * 24 - TARGET:+.0f} USDT/год при 24 конструкциях")

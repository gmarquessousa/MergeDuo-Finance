import { useMemo, useState } from 'react';
import { CATEGORY_META, type TransactionCategory } from '../types';
import { CategoryIcon } from './CategoryIcon';
import { formatBRL, hasNegativeSign, isNeutralZero } from '../utils';
import { useYearData, type MonthSummary } from '../useYearData';
import { transactionCacheKey, useFinance } from '../store';
import { useYearAggregateBalances } from '../useYearAggregateBalances';
import { useValuesHidden } from '../valuesVisibilityContext';

const MONTH_LABELS = [
  'Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho',
  'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro',
];

const MONTH_SHORT = [
  'Jan', 'Fev', 'Mar', 'Abr', 'Mai', 'Jun',
  'Jul', 'Ago', 'Set', 'Out', 'Nov', 'Dez',
];

const EXPENSE_BAR_COLORS: Record<TransactionCategory, string> = {
  income: 'rgb(var(--accent-pos))',
  investment: 'rgb(var(--accent-invest))',
  fixed_expense: 'rgb(var(--accent-neg))',
  credit_card: 'rgb(217 119 87)',
  variable_expense: 'rgb(195 87 130)',
  loan: 'rgb(140 50 60)',
};

interface Props {
  year: number;
}

export function AnnualView({ year }: Props) {
  const {
    months,
    yearTotals,
    patrimonioTotal,
    baseBeforeYear,
    investedBeforeYear,
    topTransactions,
  } = useYearData(year);
  const hidden = useValuesHidden();
  const { ownerFilter, transactionLoads } = useFinance();
  const aggregateByMonth = useYearAggregateBalances(year);

  const correctionReady = aggregateByMonth !== null && aggregateByMonth.size > 0;
  const yearLoadStates = MONTH_LABELS.map((_, monthIdx) =>
    transactionLoads[transactionCacheKey({
      yearMonth: `${year}-${String(monthIdx + 1).padStart(2, '0')}`,
      owner: ownerFilter,
    })],
  );
  const isLoadingYear = yearLoadStates.some((state) => state?.status === 'loading');
  const yearError = yearLoadStates.find((state) => state?.status === 'error')?.error;

  const today = new Date();
  const isCurrentYear = today.getFullYear() === year;

  const [expandedMonth, setExpandedMonth] = useState<number | null>(
    isCurrentYear ? today.getMonth() : null
  );

  const patrimonioInicial = baseBeforeYear + investedBeforeYear;

  const displayAccumulatedFor = (m: MonthSummary): number => {
    if (correctionReady) {
      return aggregateByMonth!.get(m.monthIdx)?.saldo ?? m.accumulated;
    }
    return m.accumulated;
  };
  const displayPatrimonioFor = (m: MonthSummary): number => {
    if (correctionReady) {
      return aggregateByMonth!.get(m.monthIdx)?.patrimonio ?? m.patrimonio;
    }
    return m.patrimonio;
  };
  const currentPatrimonio = months.length > 0
    ? displayPatrimonioFor(months[months.length - 1])
    : patrimonioTotal;
  const patrimonioDelta = currentPatrimonio - patrimonioInicial;

  const expenseBreakdown = useMemo(() => {
    const cats: TransactionCategory[] = [
      'fixed_expense', 'variable_expense', 'credit_card', 'loan',
    ];
    const entries = cats.map((c) => ({
      category: c,
      total: yearTotals.byCategory[c] ?? 0,
    }));
    const totalOut = entries.reduce((a, e) => a + e.total, 0) || 1;
    return entries
      .map((e) => ({ ...e, pct: e.total / totalOut }))
      .sort((a, b) => b.total - a.total);
  }, [yearTotals.byCategory]);

  const chart = useMemo(() => {
    const values = months.map((m) => displayPatrimonioFor(m));
    const w = 300;
    const h = 72;
    if (values.length === 0) return { points: '', area: '', w, h };

    const allValues = values;
    const min = Math.min(...allValues);
    const max = Math.max(...allValues);
    const range = max - min || 1;
    const step = w / (values.length - 1 || 1);
    const points = values
      .map((v, i) => {
        const x = (i * step).toFixed(1);
        const y = (h - ((v - min) / range) * h).toFixed(1);
        return `${x},${y}`;
      })
      .join(' ');
    const area = `${points} ${w},${h} 0,${h}`;
    return { points, area, w, h };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [months, aggregateByMonth, correctionReady]);

  const savingsRate =
    yearTotals.entradas > 0
      ? Math.max(0, Math.min(1, (yearTotals.entradas - yearTotals.saidas) / yearTotals.entradas))
      : 0;

  return (
    <div className="bg-paper rounded-t-2xl pb-bottom-nav">
      <div className="px-4 pt-4 pb-2 sm:px-5 md:px-8 lg:px-10">
        <div className="text-[11px] uppercase tracking-[0.2em] text-ink-muted">
          Visão anual · {year}
        </div>
      </div>

      {(isLoadingYear || yearError) && (
        <div className={`mx-4 mb-3 rounded-xl border px-3 py-2 text-[12px] sm:mx-5 md:mx-8 lg:mx-10 ${
          yearError
            ? 'border-accent-neg/30 bg-accent-neg/5 text-accent-neg'
            : 'border-paper-line bg-paper-card text-ink-muted'
        }`}>
          {yearError ?? 'Carregando lançamentos do ano...'}
        </div>
      )}

      <div className="px-4 space-y-4 sm:px-5 md:grid md:grid-cols-2 md:items-start md:gap-4 md:space-y-0 md:px-8 lg:px-10">
        <Card className="md:col-span-2">
          <div className="flex items-center justify-between mb-1">
            <div className="text-[11px] uppercase tracking-wider text-ink-muted">
              Evolução do patrimônio
            </div>
            <div className={`text-xs font-medium ${hasNegativeSign(patrimonioDelta) ? 'text-accent-neg' : 'text-accent-pos'}`}>
              {hidden ? 'R$ ••••' : `${hasNegativeSign(patrimonioDelta) ? '−' : '+'} ${formatBRL(Math.abs(patrimonioDelta))}`}
            </div>
          </div>
          <div className="text-[11px] text-ink-muted mb-3">
            Atual:{' '}
            <span className="text-ink font-medium">
              {hidden ? 'R$ ••••' : formatBRL(currentPatrimonio)}
            </span>
          </div>
          <svg viewBox={`0 0 ${chart.w} ${chart.h}`} preserveAspectRatio="none" className="w-full h-[72px]">
            <defs>
              <linearGradient id="yearGrad" x1="0" x2="0" y1="0" y2="1">
                <stop offset="0%" stopColor="rgb(var(--ink))" stopOpacity="0.2" />
                <stop offset="100%" stopColor="rgb(var(--ink))" stopOpacity="0" />
              </linearGradient>
            </defs>
            {chart.area && <polyline points={chart.area} fill="url(#yearGrad)" stroke="none" />}
            {chart.points && <polyline points={chart.points} fill="none" stroke="rgb(var(--ink))" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" />}
          </svg>
          <div className="flex justify-between mt-1 px-0">
            {months.map((month) => (
              <span
                key={month.monthIdx}
                className={`text-[9px] ${
                  isCurrentYear && today.getMonth() === month.monthIdx
                    ? 'font-semibold text-ink'
                    : 'text-ink-muted'
                }`}
              >
                {MONTH_SHORT[month.monthIdx]}
              </span>
            ))}
          </div>
        </Card>

        <Card className="md:col-span-2">
          <div className="flex items-center justify-between mb-3">
            <div className="text-[11px] uppercase tracking-wider text-ink-muted">
              Entradas vs Saídas por mês
            </div>
            <div className="flex items-center gap-3 text-[10px] text-ink-muted">
              <span className="inline-flex items-center gap-1">
                <span className="w-2 h-2 rounded-sm bg-accent-pos" /> Entradas
              </span>
              <span className="inline-flex items-center gap-1">
                <span className="w-2 h-2 rounded-sm bg-accent-neg" /> Saídas
              </span>
            </div>
          </div>
          <MonthlyBarChart months={months} year={year} />
        </Card>

        <div className="grid grid-cols-1 gap-3 min-[360px]:grid-cols-2 md:col-span-2">
          <KpiCard
            label="Reserva de emergência"
            value={`${(savingsRate * 100).toFixed(0)}%`}
            hint={hidden ? 'R$ ••••' : `${formatBRL(yearTotals.entradas - yearTotals.saidas)} líquido`}
            tone={savingsRate >= 0.2 ? 'pos' : savingsRate > 0 ? 'neutral' : 'neg'}
          />
          <KpiCard
            label="Total em aportes"
            value={hidden ? 'R$ ••••' : formatBRL(yearTotals.aportes)}
            hint={`no ano de ${year}`}
            tone="invest"
          />
        </div>

        <Card className="md:col-span-2">
          <div className="flex items-center justify-between mb-3">
            <div className="text-[11px] uppercase tracking-wider text-ink-muted">
              Meses do ano
            </div>
            <div className="text-[10px] text-ink-muted">Toque para expandir</div>
          </div>
          <div className="divide-y divide-paper-line">
            {months.map((m) => {
              const netColor =
                hasNegativeSign(m.net)
                  ? 'text-accent-neg'
                  : m.net > 0
                  ? 'text-accent-pos'
                  : 'text-ink-muted';
              const isCurrent = isCurrentYear && today.getMonth() === m.monthIdx;
              const isFuture = isCurrentYear && m.monthIdx > today.getMonth();
              const isExpanded = expandedMonth === m.monthIdx;
              return (
                <div key={m.monthIdx} className={isFuture ? 'opacity-40' : ''}>
                  <button
                    onClick={() =>
                      setExpandedMonth(isExpanded ? null : m.monthIdx)
                    }
                    className="w-full grid grid-cols-[1fr_auto_auto_16px] items-center gap-3 py-2.5 text-left"
                  >
                    <div className="flex items-center gap-2 min-w-0">
                      {isCurrent && (
                        <span className="w-1.5 h-1.5 rounded-full bg-ink shrink-0" />
                      )}
                      <span className={`text-sm truncate ${isCurrent ? 'font-semibold text-ink' : 'text-ink'}`}>
                        {MONTH_LABELS[m.monthIdx]}
                      </span>
                      {m.txCount > 0 && (
                        <span className="text-[10px] text-ink-muted">
                          {m.txCount} lanç.
                        </span>
                      )}
                    </div>
                    <div className={`text-sm font-medium tabular-nums text-right ${netColor}`}>
                      {isNeutralZero(m.net)
                        ? '—'
                        : hidden ? 'R$ ••••' : `${isFuture ? '~ ' : ''}${hasNegativeSign(m.net) ? '−' : '+'} ${formatBRL(Math.abs(m.net))}`}
                    </div>
                    <div className="text-sm font-semibold text-ink tabular-nums text-right min-w-[56px]">
                      {hidden ? 'R$ ••••' : `${isFuture ? '~ ' : ''}${formatBRL(displayAccumulatedFor(m))}`}
                    </div>
                    <svg
                      width="14" height="14" viewBox="0 0 24 24"
                      fill="none" stroke="currentColor" strokeWidth="2"
                      strokeLinecap="round" strokeLinejoin="round"
                      className={`text-ink-muted transition-transform ${isExpanded ? 'rotate-180' : ''}`}
                    >
                      <polyline points="6 9 12 15 18 9" />
                    </svg>
                  </button>
                  {isExpanded && <MonthDetails month={m} />}
                </div>
              );
            })}
          </div>
          <div className="grid grid-cols-[1fr_auto_auto_16px] gap-3 mt-2 pt-2 border-t border-paper-line items-center">
            <div />
            <div className="text-[10px] uppercase tracking-wider text-ink-muted text-right">Saldo</div>
            <div className="flex items-center gap-2 justify-end min-w-[56px]">
              <span className="text-[10px] uppercase tracking-wider text-ink-muted text-right">Total</span>
            </div>
            <div />
          </div>
        </Card>

        <Card>
          <div className="flex items-center justify-between mb-3">
            <div className="text-[11px] uppercase tracking-wider text-ink-muted">
              Distribuição de saídas no ano
            </div>
            <div className="text-xs font-semibold text-ink tabular-nums">
              {hidden ? 'R$ ••••' : formatBRL(yearTotals.saidas)}
            </div>
          </div>
          {yearTotals.saidas === 0 ? (
            <div className="text-sm text-ink-muted py-2">Nenhuma saída no ano.</div>
          ) : (
            <div className="space-y-3.5">
              {expenseBreakdown.map((e) => {
                if (e.total === 0) return null;
                const meta = CATEGORY_META[e.category];
                const color = EXPENSE_BAR_COLORS[e.category];
                const maxValue = Math.max(...expenseBreakdown.map((x) => x.total), 1);
                const barPct = (e.total / maxValue) * 100;
                const pctLabel = Math.round(e.pct * 100);
                return (
                  <div key={e.category}>
                    <div className="flex items-center justify-between text-sm mb-1">
                      <span className="inline-flex items-center gap-2 min-w-0">
                        <span className="grid place-items-center w-6 h-6 rounded-md" style={{ background: `${color}1f`, color }}>
                          <CategoryIcon category={e.category} size={13} />
                        </span>
                        <span className="text-ink truncate">{meta.label}</span>
                        <span className="text-[10px] text-ink-muted shrink-0">{pctLabel}%</span>
                      </span>
                      <span className="text-ink tabular-nums font-medium shrink-0">{hidden ? 'R$ ••••' : formatBRL(e.total)}</span>
                    </div>
                    <div className="h-2 rounded-full bg-paper-line overflow-hidden">
                      <div
                        className="h-full rounded-full transition-all"
                        style={{ width: `${Math.max(barPct, 2)}%`, background: color }}
                      />
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </Card>

        {topTransactions.length > 0 && (
          <Card>
            <div className="text-[11px] uppercase tracking-wider text-ink-muted mb-2">
              Maiores saídas do ano
            </div>
            <ul className="divide-y divide-paper-line">
              {topTransactions.map((t) => {
                const meta = CATEGORY_META[t.category];
                const monthName = MONTH_SHORT[new Date(t.date + 'T00:00').getMonth()];
                return (
                  <li key={t.id} className="py-2.5 flex items-center gap-3">
                    <div className={`w-9 h-9 rounded-full grid place-items-center bg-paper-line ${meta.color}`}>
                      <CategoryIcon category={t.category} size={16} />
                    </div>
                    <div className="flex-1 min-w-0 text-left">
                      <div className="text-sm text-ink truncate">{t.description}</div>
                      <div className="flex items-center gap-1.5 mt-0.5">
                        <span className="text-[11px] text-ink-muted">{meta.label} · {monthName}</span>
                        {t.owner && <OwnerChip owner={t.owner} />}
                      </div>
                    </div>
                    <div className="text-sm font-medium text-accent-neg">
                      {hidden ? 'R$ ••••' : `− ${formatBRL(t.amount)}`}
                    </div>
                  </li>
                );
              })}
            </ul>
          </Card>
        )}
      </div>
    </div>
  );
}

function Card({
  children,
  className = '',
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={`rounded-2xl bg-paper-card border border-paper-line p-4 shadow-soft ${className}`}>
      {children}
    </div>
  );
}

function OwnerChip({ owner }: { owner: string }) {
  const { currentUser } = useFinance();
  const isMe = owner === currentUser?.name;
  const first = owner.split(' ')[0];
  const initials = owner.split(' ').slice(0, 2).map((w: string) => w[0]).join('').toUpperCase();
  return (
    <span className={`inline-flex items-center gap-1 text-[10px] font-medium rounded-full px-1.5 h-4 leading-none ${
      isMe ? 'bg-paper-line text-ink-muted' : 'bg-accent-invest/10 text-accent-invest'
    }`}>
      <span className={`w-3 h-3 rounded-full grid place-items-center text-[8px] font-semibold ${
        isMe ? 'bg-ink-muted/30 text-ink' : 'bg-accent-invest/20 text-accent-invest'
      }`}>{initials}</span>
      {isMe ? 'Você' : first}
    </span>
  );
}

function KpiCard({
  label, value, hint, tone,
}: {
  label: string;
  value: string;
  hint: string;
  tone: 'pos' | 'neg' | 'neutral' | 'invest';
}) {
  const color =
    tone === 'pos' ? 'text-accent-pos'
    : tone === 'neg' ? 'text-accent-neg'
    : tone === 'invest' ? 'text-accent-invest'
    : 'text-ink';
  return (
    <Card>
      <div className="text-[10px] uppercase tracking-wider text-ink-muted">{label}</div>
      <div className={`text-xl font-semibold mt-1 ${color}`}>{value}</div>
      <div className="text-[11px] text-ink-muted mt-0.5">{hint}</div>
    </Card>
  );
}

function MonthDetails({ month }: { month: MonthSummary }) {
  const hidden = useValuesHidden();
  const topExpenses = [...month.transactions]
    .filter((t) => CATEGORY_META[t.category].kind === 'out')
    .sort((a, b) => b.amount - a.amount)
    .slice(0, 3);

  const expenseCats: TransactionCategory[] = [
    'fixed_expense', 'variable_expense', 'credit_card', 'loan',
  ];
  const breakdown = expenseCats
    .map((c) => ({ category: c, total: month.byCategory[c] ?? 0 }))
    .filter((e) => e.total > 0);
  const totalOut = breakdown.reduce((a, e) => a + e.total, 0) || 1;

  if (month.txCount === 0) {
    return (
      <div className="px-0 py-3 text-sm text-ink-muted text-center">
        Nenhuma movimentação neste mês
      </div>
    );
  }

  return (
    <div className="pb-3 pt-1 space-y-3">
      <div className="grid grid-cols-3 gap-2">
        <MiniStat label="Entradas" value={month.entradas} tone="pos" />
        <MiniStat label="Saídas"   value={month.saidas}   tone="neg" />
        <MiniStat label="Aportes"  value={month.aportes}  tone="invest" />
      </div>

      {breakdown.length > 0 && (
        <div className="space-y-2.5">
          <div className="text-[10px] uppercase tracking-wider text-ink-muted">
            Saídas por categoria
          </div>
          {breakdown.map((e) => {
            const meta = CATEGORY_META[e.category];
            const pct = e.total / totalOut;
            const color = EXPENSE_BAR_COLORS[e.category];
            const barPct = (e.total / Math.max(...breakdown.map((x) => x.total), 1)) * 100;
            return (
              <div key={e.category}>
                <div className="flex items-center justify-between text-xs mb-1">
                  <span className="inline-flex items-center gap-1.5">
                    <span className="grid place-items-center w-5 h-5 rounded-md" style={{ background: `${color}1f`, color }}>
                      <CategoryIcon category={e.category} size={11} />
                    </span>
                    <span className="text-ink">{meta.label}</span>
                    <span className="text-[10px] text-ink-muted">{Math.round(pct * 100)}%</span>
                  </span>
                  <span className="text-ink tabular-nums font-medium">{hidden ? 'R$ ••••' : formatBRL(e.total)}</span>
                </div>
                <div className="h-1.5 rounded-full bg-paper-line overflow-hidden">
                  <div
                    className="h-full rounded-full transition-all"
                    style={{ width: `${Math.max(barPct, 2)}%`, background: color }}
                  />
                </div>
              </div>
            );
          })}
        </div>
      )}

      {topExpenses.length > 0 && (
        <div>
          <div className="text-[10px] uppercase tracking-wider text-ink-muted mb-1">
            Maiores saídas do mês
          </div>
          <ul className="divide-y divide-paper-line">
            {topExpenses.map((t) => {
              const meta = CATEGORY_META[t.category];
              const dayNum = new Date(t.date + 'T00:00').getDate();
              return (
                <li key={t.id} className="py-2 flex items-center gap-3">
                  <div className={`w-7 h-7 rounded-full grid place-items-center bg-paper-line ${meta.color}`}>
                    <CategoryIcon category={t.category} size={14} />
                  </div>
                  <div className="flex-1 min-w-0 text-left">
                    <div className="text-sm text-ink truncate">{t.description}</div>
                    <div className="flex items-center gap-1.5 mt-0.5">
                      <span className="text-[10px] text-ink-muted">{meta.label} · dia {dayNum}</span>
                      {t.owner && <OwnerChip owner={t.owner} />}
                    </div>
                  </div>
                  <div className="text-sm font-medium text-accent-neg">
                    {hidden ? 'R$ ••••' : `− ${formatBRL(t.amount)}`}
                  </div>
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
}

function MiniStat({
  label, value, tone,
}: {
  label: string;
  value: number;
  tone: 'pos' | 'neg' | 'invest';
}) {
  const hidden = useValuesHidden();
  const color =
    tone === 'pos' ? 'text-accent-pos'
    : tone === 'neg' ? 'text-accent-neg'
    : 'text-accent-invest';
  const dot =
    tone === 'pos' ? 'bg-accent-pos'
    : tone === 'neg' ? 'bg-accent-neg'
    : 'bg-accent-invest';
  return (
    <div className="rounded-xl bg-paper border border-paper-line p-2">
      <div className="flex items-center gap-1 text-[9px] uppercase tracking-wider text-ink-muted">
        <span className={`w-1 h-1 rounded-full ${dot}`} />
        {label}
      </div>
      <div className={`text-xs font-semibold mt-0.5 tabular-nums ${color}`}>
        {hidden ? 'R$ ••••' : formatBRL(value)}
      </div>
    </div>
  );
}

function MonthlyBarChart({ months, year }: { months: MonthSummary[]; year: number }) {
  const today = new Date();
  const isCurrentYear = today.getFullYear() === year;
  const max = Math.max(
    ...months.map((m) => Math.max(m.entradas, m.saidas)),
    1,
  );
  const padded: (MonthSummary | { monthIdx: number; placeholder: true })[] = [];
  const firstMonth = months[0]?.monthIdx ?? 0;
  for (let i = 0; i < firstMonth; i++) {
    padded.push({ monthIdx: i, placeholder: true });
  }
  padded.push(...months);

  return (
    <div>
      <div className="flex items-end justify-between gap-1 h-32 px-0.5">
        {padded.map((m) => {
          if ('placeholder' in m) {
            return (
              <div key={`p-${m.monthIdx}`} className="flex-1 min-w-0 h-full" />
            );
          }
          const isFuture = isCurrentYear && m.monthIdx > today.getMonth();
          const isCurrent = isCurrentYear && m.monthIdx === today.getMonth();
          const inH = (m.entradas / max) * 100;
          const outH = (m.saidas / max) * 100;
          return (
            <div key={m.monthIdx} className="flex-1 min-w-0 h-full flex flex-col justify-end">
              <div className={`flex items-end justify-center gap-[2px] h-full ${isFuture ? 'opacity-30' : ''}`} title={`${MONTH_SHORT[m.monthIdx]}: +${formatBRL(m.entradas)} / -${formatBRL(m.saidas)}`}>
                <div
                  className={`w-full max-w-[10px] rounded-t bg-accent-pos transition-all ${m.entradas === 0 ? 'opacity-0' : ''}`}
                  style={{ height: `${Math.max(inH, m.entradas > 0 ? 2 : 0)}%` }}
                />
                <div
                  className={`w-full max-w-[10px] rounded-t bg-accent-neg transition-all ${m.saidas === 0 ? 'opacity-0' : ''}`}
                  style={{ height: `${Math.max(outH, m.saidas > 0 ? 2 : 0)}%` }}
                />
              </div>
              <div className={`mt-1.5 text-center text-[9px] ${isCurrent ? 'font-semibold text-ink' : 'text-ink-muted'}`}>
                {MONTH_SHORT[m.monthIdx]}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

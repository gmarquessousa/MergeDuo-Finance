import { useMemo, useState } from 'react';
import type { SummaryDisplayStatus } from '../summaryResolver';
import type { DailyRunwayState } from '../dailyRunway';
import { formatBRL } from '../utils';
import { useValuesHidden } from '../valuesVisibilityContext';

interface Props {
  status: SummaryDisplayStatus;
  error?: string | null;
  patrimonio: number;
  saldo: number;
  investido: number;
  dailyRunwayStates: DailyRunwayState[];
  mesEntradas: number;
  mesSaidas: number;
  mesAportes: number;
  period: 'monthly' | 'annual';
  periodLabel: string;
  isCurrentPeriod: boolean;
  isProjected: boolean;
  onRefresh?: () => void;
  refreshing?: boolean;
}

export function SummaryHeader({
  status,
  error,
  patrimonio,
  saldo,
  investido,
  dailyRunwayStates,
  mesEntradas,
  mesSaidas,
  mesAportes,
  period,
  periodLabel,
  isCurrentPeriod,
  isProjected,
  onRefresh,
  refreshing,
}: Props) {
  const hidden = useValuesHidden();
  const showValues = status !== 'loading';
  const isBusy = refreshing || status === 'updating' || status === 'loading';
  const showBackgroundRefresh = showValues && status === 'updating';
  const isCurrentMonth = period === 'monthly' && isCurrentPeriod;
  const headlineLabel = isCurrentMonth ? 'Patrimônio atual' : 'Patrimônio total';
  const saldoLabel = isCurrentMonth ? 'Saldo hoje' : 'Saldo em conta';
  const investidoLabel = isCurrentMonth ? 'Investido hoje' : 'Investido';

  return (
    <div className="relative mx-4 mb-4 overflow-hidden rounded-3xl hero-surface shadow-hero p-5 sm:mx-5 sm:p-6 md:mx-8 lg:mx-10 animate-slide-up">
      {showBackgroundRefresh && (
        <div className="pointer-events-none absolute inset-x-0 top-0 z-20 h-0.5 overflow-hidden rounded-t-3xl">
          <div className="h-full w-full animate-shimmer opacity-50" />
        </div>
      )}
      <div className="relative z-10 flex items-center justify-between gap-2">
        <div className="text-[10px] uppercase tracking-[0.22em] text-white/55">
          {headlineLabel} - {periodLabel}
        </div>
        <div className="flex items-center gap-2">
          {showBackgroundRefresh ? (
            <div className="inline-flex items-center gap-1 text-[10px] font-medium text-white/60">
              <span className="w-1.5 h-1.5 rounded-full bg-white/70 animate-pulse" />
              Atualizando em segundo plano
            </div>
          ) : status === 'error' && error ? (
            <div className="inline-flex items-center gap-1 text-[10px] font-medium text-white/60">
              <span className="w-1.5 h-1.5 rounded-full bg-accent-neg" />
              Dados parciais
            </div>
          ) : isProjected && (
            <div className="inline-flex items-center gap-1 text-[10px] font-medium text-white/60">
              <span className="w-1.5 h-1.5 rounded-full bg-white/50" />
              Projetado
            </div>
          )}
          {onRefresh && (
            <button
              type="button"
              onClick={onRefresh}
              disabled={isBusy}
              aria-label="Atualizar dados"
              title="Atualizar dados"
              className={`inline-flex h-7 w-7 items-center justify-center rounded-full border border-white/15 bg-white/5 text-white/80 transition-colors hover:bg-white/10 disabled:opacity-60 disabled:cursor-not-allowed ${isBusy ? 'cursor-progress' : ''}`}
            >
              <svg
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
                className={isBusy ? 'animate-spin' : ''}
              >
                <polyline points="23 4 23 10 17 10" />
                <polyline points="1 20 1 14 7 14" />
                <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10" />
                <path d="M20.49 15a9 9 0 0 1-14.85 3.36L1 14" />
              </svg>
            </button>
          )}
        </div>
      </div>

      <div className="relative z-10 mt-1.5 break-words text-3xl font-semibold tracking-tight tabular-nums min-[360px]:text-[34px]">
        {showValues ? (hidden ? 'R$ ••••' : formatBRL(patrimonio)) : <SkeletonLine className="h-9 w-44" />}
      </div>

      {showBackgroundRefresh && (
        <div className="relative z-10 mt-3 rounded-2xl border border-white/10 bg-white/[0.06] px-3 py-2 text-[11px] leading-5 text-white/72">
          Mantendo os últimos valores visíveis enquanto uma nova sincronização acontece.
        </div>
      )}

      <div className="relative z-10 mt-3 grid grid-cols-2 gap-2.5">
        <SubBalance
          label={saldoLabel}
          value={saldo}
          loading={!showValues}
          dotClass="bg-white/80"
        />
        <SubBalance
          label={investidoLabel}
          value={investido}
          loading={!showValues}
          dotClass="bg-accent-invest"
        />
      </div>

      <SobraPrevistaPanel states={dailyRunwayStates} />

      <div className="relative z-10 mt-4 grid grid-cols-3 divide-x divide-white/8 rounded-2xl bg-white/[0.05] ring-1 ring-white/8 p-3">
        <Stat label="Entradas" value={mesEntradas} loading={!showValues} tone="pos" />
        <Stat label="Saídas" value={mesSaidas} loading={!showValues} tone="neg" />
        <Stat label="Aportes" value={mesAportes} loading={!showValues} tone="invest" />
      </div>
    </div>
  );
}

function SubBalance({
  label,
  value,
  loading,
  dotClass,
}: {
  label: string;
  value: number;
  loading: boolean;
  dotClass: string;
}) {
  const hidden = useValuesHidden();
  return (
    <div className="rounded-xl bg-white/[0.05] ring-1 ring-white/10 px-3 py-2">
      <div className="flex items-center gap-1.5 text-[10px] uppercase tracking-wider text-white/55">
        <span className={`w-1.5 h-1.5 rounded-full ${dotClass}`} />
        {label}
      </div>
      <div className="mt-0.5 text-[14px] font-semibold tabular-nums">
        {loading ? <SkeletonLine className="h-4 w-20" /> : hidden ? 'R$ ••••' : formatBRL(value)}
      </div>
    </div>
  );
}

function SobraPrevistaPanel({ states }: { states: DailyRunwayState[] }) {
  const hidden = useValuesHidden();
  const orderedStates = useMemo(
    () => [...states].sort((a, b) => a.horizonMonths - b.horizonMonths),
    [states],
  );
  const horizons = orderedStates.map((state) => state.horizonMonths);
  const defaultHorizon = horizons[0] ?? 3;
  const [selectedHorizon, setSelectedHorizon] = useState<number>(defaultHorizon);
  const activeHorizon = horizons.includes(selectedHorizon) ? selectedHorizon : defaultHorizon;
  const active = orderedStates.find((state) => state.horizonMonths === activeHorizon)
    ?? orderedStates[0];

  if (!active) return null;

  const hasNegativeDay = active.minProjectedTotal != null && active.minProjectedTotal < 0;
  const isFinalNegative = active.remainingTotal != null && active.remainingTotal < 0;
  const isAlert = hasNegativeDay || isFinalNegative;
  const dotClass = isAlert ? 'bg-accent-neg' : 'bg-white';
  const horizonLabel = active.horizonDays > 0 ? `${active.horizonDays} dias` : `${active.horizonMonths} meses`;
  const isLoading = !active.ready;

  return (
    <div className="relative z-10 mt-2 rounded-2xl bg-white/[0.05] ring-1 ring-white/10 px-3 py-2.5">
      <div className="flex items-center justify-between gap-2 text-[10px] uppercase tracking-wider text-white/55">
        <div className="inline-flex items-center gap-1.5">
          <span className={`w-1.5 h-1.5 rounded-full ${dotClass}`} />
          Sobra prevista
        </div>
        <span className="text-white/40 normal-case tracking-normal text-[10px]">{horizonLabel}</span>
      </div>

      <div
        role="tablist"
        aria-label="Horizonte da sobra prevista"
        className="mt-2 flex w-full gap-1 rounded-full bg-white/[0.04] p-0.5 ring-1 ring-white/10"
      >
        {orderedStates.map((state) => {
          const isActive = state.horizonMonths === active.horizonMonths;
          return (
            <button
              key={state.horizonMonths}
              type="button"
              role="tab"
              aria-selected={isActive}
              onClick={() => setSelectedHorizon(state.horizonMonths)}
              className={`flex-1 min-w-0 h-7 rounded-full text-[11px] font-medium tracking-wide transition ${
                isActive
                  ? 'bg-white/15 text-white shadow-sm'
                  : 'text-white/55 hover:text-white/80'
              }`}
            >
              {state.horizonMonths}m
            </button>
          );
        })}
      </div>

      <div className="mt-2 grid grid-cols-2 gap-x-3 gap-y-2.5">
        <RunwayMetric
          label="Ao final"
          caption={active.horizonEndDate ? `até ${formatShortDate(active.horizonEndDate)}` : null}
          loading={isLoading}
          value={active.remainingTotal}
          tone={isFinalNegative ? 'neg' : 'neutral'}
          size="lg"
        />
        <RunwayMetric
          label="Total seguro"
          caption={active.minProjectedDate ? `até ${formatShortDate(active.minProjectedDate)}` : null}
          loading={isLoading}
          value={active.minProjectedTotal}
          tone={hasNegativeDay ? 'neg' : 'neutral'}
          size="lg"
          align="right"
        />
        <RunwayMetric
          label="Por dia"
          caption={active.horizonEndDate ? `até ${formatShortDate(active.horizonEndDate)}` : null}
          loading={isLoading}
          value={active.averagePerDay}
          tone={isFinalNegative ? 'neg' : 'neutral'}
          size="sm"
        />
        <RunwayMetric
          label="Por dia (seguro)"
          caption={active.minProjectedDate ? `até ${formatShortDate(active.minProjectedDate)}` : null}
          loading={isLoading}
          value={active.value}
          tone={hasNegativeDay ? 'neg' : 'neutral'}
          size="sm"
          align="right"
        />
      </div>

      {!isLoading && hasNegativeDay && active.minProjectedTotal != null ? (
        <div className="mt-2 flex items-start gap-1.5 rounded-lg bg-accent-neg/10 px-2 py-1.5 text-[10px] leading-snug text-accent-neg ring-1 ring-accent-neg/30">
          <span aria-hidden>⚠</span>
          <span>
            Dias em vermelho previstos no período. Pior saldo projetado:{' '}
            <span className="font-semibold tabular-nums">{hidden ? 'R$ ••••' : formatBRL(active.minProjectedTotal)}</span>
            {active.minProjectedDate ? <> em {formatShortDate(active.minProjectedDate)}</> : null}.
          </span>
        </div>
      ) : null}
    </div>
  );
}

function RunwayMetric({
  label,
  caption,
  loading,
  value,
  tone,
  size,
  align = 'left',
}: {
  label: string;
  caption: string | null;
  loading: boolean;
  value: number | null;
  tone: 'neg' | 'neutral';
  size: 'sm' | 'lg';
  align?: 'left' | 'right';
}) {
  const hidden = useValuesHidden();
  const valueClass = tone === 'neg' ? 'text-accent-neg' : 'text-white';
  const sizeClass = size === 'lg'
    ? 'text-[18px] font-semibold'
    : 'text-[14px] font-semibold';
  const skeletonClass = size === 'lg' ? 'h-5 w-24' : 'h-4 w-16';
  const alignClass = align === 'right' ? 'text-right items-end' : 'text-left items-start';

  return (
    <div className={`min-w-0 flex flex-col ${alignClass}`}>
      <div className="text-[9px] uppercase tracking-wider text-white/45">{label}</div>
      <div className={`mt-0.5 break-words tabular-nums ${sizeClass} ${valueClass}`}>
        {loading || value == null ? (
          <SkeletonLine className={skeletonClass} />
        ) : hidden ? 'R$ ••••' : (
          formatBRL(value)
        )}
      </div>
      {caption ? (
        <div className="mt-0.5 text-[9px] text-white/40 normal-case">{caption}</div>
      ) : null}
    </div>
  );
}

function formatShortDate(isoDate: string): string {
  const [yearStr, monthStr, dayStr] = isoDate.split('-');
  const year = Number(yearStr);
  const monthIdx = Number(monthStr) - 1;
  const day = Number(dayStr);
  if (!Number.isFinite(year) || !Number.isFinite(monthIdx) || !Number.isFinite(day)) {
    return isoDate;
  }
  return new Date(year, monthIdx, day).toLocaleDateString('pt-BR', {
    day: '2-digit',
    month: 'short',
  });
}

function Stat({
  label,
  value,
  loading,
  tone,
}: {
  label: string;
  value: number;
  loading: boolean;
  tone: 'pos' | 'neg' | 'invest';
}) {
  const hidden = useValuesHidden();
  const dot =
    tone === 'pos'
      ? 'bg-accent-pos'
      : tone === 'neg'
        ? 'bg-accent-neg'
        : 'bg-accent-invest';
  const valueColor =
    tone === 'pos'
      ? 'text-accent-pos'
      : tone === 'neg'
        ? 'text-accent-neg'
        : 'text-accent-invest';
  return (
    <div className="min-w-0 px-2 text-center first:pl-0 last:pr-0">
      <div className="flex items-center justify-center gap-1.5 text-[10px] uppercase tracking-wider text-white/55">
        <span className={`w-1.5 h-1.5 rounded-full ${dot}`} />
        {label}
      </div>
      <div className={`mt-1 break-words text-[13px] font-semibold tabular-nums ${valueColor}`}>
        {loading ? <SkeletonLine className="mx-auto h-4 w-16" /> : hidden ? 'R$ ••••' : formatBRL(value)}
      </div>
    </div>
  );
}

function SkeletonLine({ className }: { className: string }) {
  return (
    <span
      className={`inline-block rounded-full bg-white/15 animate-pulse align-middle ${className}`}
      aria-hidden="true"
    />
  );
}

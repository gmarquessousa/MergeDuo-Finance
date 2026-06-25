import { useEffect, useRef, useState } from 'react';
import { useFocusTrap } from '../useFocusTrap';

export type SimEntryKind = 'in' | 'out' | 'invest';
export type SimEntryFrequency = 'once' | 'installments' | 'recurring';

export interface SimulatorEntryDraft {
  kind: SimEntryKind;
  description: string;
  amount: number; // for 'installments' this is the value PER installment
  startDate: string; // ISO yyyy-mm-dd
  frequency: SimEntryFrequency;
  installmentsUntil?: string; // ISO yyyy-mm-dd of the last installment month
  recurringUntil?: string; // ISO yyyy-mm-dd (last occurrence date)
}

interface Props {
  open: boolean;
  defaultDate: string;
  yearEnd: string; // ISO last day of current year
  onClose: () => void;
  onSubmit: (entry: SimulatorEntryDraft) => void;
}

const KIND_OPTIONS: { value: SimEntryKind; label: string; color: string }[] = [
  { value: 'out', label: 'Saída', color: 'text-accent-neg' },
  { value: 'in', label: 'Entrada', color: 'text-accent-pos' },
  { value: 'invest', label: 'Aporte', color: 'text-accent-invest' },
];

const FREQ_OPTIONS: { value: SimEntryFrequency; label: string; hint: string }[] = [
  { value: 'once', label: 'Única', hint: 'Um lançamento isolado' },
  { value: 'installments', label: 'Parcelada', hint: 'Período em meses' },
  { value: 'recurring', label: 'Recorrente', hint: 'Repete todo mês' },
];

function monthsBetweenInclusive(startIso: string, endIso: string): number {
  if (!startIso || !endIso) return 0;
  const [sy, sm] = startIso.split('-').map(Number);
  const [ey, em] = endIso.split('-').map(Number);
  return (ey - sy) * 12 + (em - sm) + 1;
}

function addMonthsIso(iso: string, months: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  const total = (y * 12 + (m - 1)) + months;
  const ny = Math.floor(total / 12);
  const nm = (total % 12) + 1;
  return `${ny.toString().padStart(4, '0')}-${nm.toString().padStart(2, '0')}-${d.toString().padStart(2, '0')}`;
}

function formatCurrencyInput(raw: string): { display: string; value: number } {
  const digits = raw.replace(/\D/g, '');
  if (!digits) return { display: '', value: 0 };
  const numeric = parseInt(digits, 10) / 100;
  const display = numeric.toLocaleString('pt-BR', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
  return { display, value: numeric };
}

function prettyDate(iso: string): string {
  if (!iso) return '';
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString('pt-BR', {
    weekday: 'short',
    day: '2-digit',
    month: 'long',
  });
}

export function SimulatorEntrySheet({ open, defaultDate, yearEnd, onClose, onSubmit }: Props) {
  const [kind, setKind] = useState<SimEntryKind>('out');
  const [frequency, setFrequency] = useState<SimEntryFrequency>('once');
  const [description, setDescription] = useState('');
  const [amountStr, setAmountStr] = useState('');
  const [amountValue, setAmountValue] = useState(0);
  const [startDate, setStartDate] = useState(defaultDate);
  const [installmentsUntil, setInstallmentsUntil] = useState(() => addMonthsIso(defaultDate, 2));
  const [recurringUntil, setRecurringUntil] = useState(yearEnd);
  const [showErrors, setShowErrors] = useState(false);
  const descRef = useRef<HTMLInputElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  useFocusTrap(panelRef, open);

  useEffect(() => {
    if (!open) return;
    const reset = window.setTimeout(() => {
      setKind('out');
      setFrequency('once');
      setDescription('');
      setAmountStr('');
      setAmountValue(0);
      setStartDate(defaultDate);
      setInstallmentsUntil(addMonthsIso(defaultDate, 2));
      setRecurringUntil(yearEnd);
      setShowErrors(false);
    }, 0);
    const focus = window.setTimeout(() => descRef.current?.focus(), 220);
    return () => {
      window.clearTimeout(reset);
      window.clearTimeout(focus);
    };
  }, [open, defaultDate, yearEnd]);

  useEffect(() => {
    if (!open) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = prev;
    };
  }, [open]);

  if (!open) return null;

  const installmentsCount = monthsBetweenInclusive(startDate, installmentsUntil);
  const descriptionValid = description.trim().length > 0;
  const amountValid = amountValue > 0;
  const installmentsValid = frequency !== 'installments' || installmentsCount >= 2;
  const recurringValid = frequency !== 'recurring' || recurringUntil >= startDate;
  const canSubmit = descriptionValid && amountValid && installmentsValid && recurringValid;

  function submit() {
    if (!canSubmit) {
      setShowErrors(true);
      return;
    }
    onSubmit({
      kind,
      description: description.trim(),
      amount: amountValue,
      startDate,
      frequency,
      installmentsUntil: frequency === 'installments' ? installmentsUntil : undefined,
      recurringUntil: frequency === 'recurring' ? recurringUntil : undefined,
    });
    onClose();
  }

  const installmentsTotal = installmentsCount > 0 ? amountValue * installmentsCount : 0;

  return (
    <div
      className="fixed inset-0 z-50 flex items-end justify-center"
      role="dialog"
      aria-modal="true"
      aria-labelledby="sim-tx-title"
    >
      <div className="absolute inset-0 bg-black/50 animate-[fadeIn_150ms_ease]" onClick={onClose} />
      <div ref={panelRef} className="relative w-full max-w-md bg-paper-card rounded-t-3xl shadow-elevated animate-[slideUp_240ms_cubic-bezier(0.22,1,0.36,1)] flex flex-col max-h-[92vh]">
        <div className="pt-2 px-5 shrink-0">
          <div className="w-10 h-1 bg-paper-line rounded-full mx-auto mb-3" />
          <div className="flex items-start justify-between gap-3 pb-3">
            <div>
              <h2 id="sim-tx-title" className="text-base font-semibold text-ink leading-tight">
                Adicionar simulação
              </h2>
              <p className="text-[11px] text-ink-muted mt-0.5">
                Os valores não são salvos. Aportes reduzem caixa e aumentam o investido.
              </p>
            </div>
            <button
              onClick={onClose}
              className="w-11 h-11 -mt-2 -mr-2 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line transition"
              aria-label="Fechar"
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
            </button>
          </div>
        </div>

        <div className="px-5 pb-3 space-y-5 overflow-y-auto flex-1">
          <section>
            <SectionLabel>Tipo</SectionLabel>
            <div className="flex flex-wrap gap-1.5 mt-2">
              {KIND_OPTIONS.map((opt) => {
                const selected = kind === opt.value;
                return (
                  <button
                    key={opt.value}
                    onClick={() => setKind(opt.value)}
                    className={`px-3 h-9 rounded-full text-xs font-medium border transition active:scale-[0.97] ${
                      selected
                        ? 'bold-surface border-transparent shadow-soft-sm'
                        : 'bg-paper border-paper-line text-ink hover:border-ink/40'
                    }`}
                  >
                    <span className={selected ? 'text-white' : opt.color}>● </span>
                    {opt.label}
                  </button>
                );
              })}
            </div>
          </section>

          <section>
            <SectionLabel>Frequência</SectionLabel>
            <div className="grid grid-cols-3 gap-1.5 mt-2">
              {FREQ_OPTIONS.map((opt) => {
                const selected = frequency === opt.value;
                return (
                  <button
                    key={opt.value}
                    onClick={() => setFrequency(opt.value)}
                    className={`p-2 rounded-xl border text-left transition active:scale-[0.98] ${
                      selected
                        ? 'border-ink bg-ink/[0.04] shadow-soft-sm'
                        : 'border-paper-line bg-paper hover:border-ink/30'
                    }`}
                  >
                    <div className="text-[12px] font-medium text-ink">{opt.label}</div>
                    <div className="text-[10px] text-ink-muted leading-snug mt-0.5">{opt.hint}</div>
                  </button>
                );
              })}
            </div>
          </section>

          <section>
            <SectionLabel htmlFor="sim-description">Descrição</SectionLabel>
            <input
              id="sim-description"
              ref={descRef}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Ex.: Notebook novo, Bônus, Streaming…"
              className={`mt-1.5 w-full h-11 px-3 rounded-xl bg-paper border text-sm text-ink outline-none transition placeholder:text-ink-muted/70 focus:border-ink/60 ${
                showErrors && !descriptionValid ? 'border-accent-neg/60' : 'border-paper-line'
              }`}
            />
            {showErrors && !descriptionValid && (
              <p role="alert" className="mt-1 text-[11px] text-accent-neg">Informe uma descrição.</p>
            )}
          </section>

          <section>
            <SectionLabel htmlFor="sim-amount">
              {frequency === 'installments' ? 'Valor da parcela' : frequency === 'recurring' ? 'Valor mensal' : 'Valor'}
            </SectionLabel>
            <div
              className={`mt-1.5 flex items-center h-14 px-3 rounded-xl bg-paper border transition ${
                showErrors && !amountValid ? 'border-accent-neg/60' : 'border-paper-line focus-within:border-ink/60'
              }`}
            >
              <span className="text-ink-muted text-base mr-2">R$</span>
              <input
                id="sim-amount"
                inputMode="numeric"
                value={amountStr}
                onChange={(e) => {
                  const { display, value } = formatCurrencyInput(e.target.value);
                  setAmountStr(display);
                  setAmountValue(value);
                }}
                placeholder="0,00"
                className="flex-1 bg-transparent text-xl font-semibold tabular-nums text-ink outline-none placeholder:text-ink-muted/40 placeholder:font-normal"
              />
            </div>
            {showErrors && !amountValid && (
              <p className="mt-1 text-[11px] text-accent-neg">Informe um valor maior que zero.</p>
            )}
          </section>

          <section>
            <SectionLabel htmlFor="sim-start">
              {frequency === 'recurring' ? 'A partir de' : frequency === 'installments' ? '1ª parcela' : 'Data'}
            </SectionLabel>
            <input
              id="sim-start"
              type="date"
              value={startDate}
              onChange={(e) => setStartDate(e.target.value)}
              className="mt-1.5 w-full h-11 px-3 rounded-xl bg-paper border border-paper-line text-sm text-ink outline-none focus:border-ink/60"
            />
            {startDate && (
              <p className="mt-1 text-[11px] text-ink-muted capitalize">{prettyDate(startDate)}</p>
            )}
          </section>

          {frequency === 'installments' && (
            <section className="animate-[fadeIn_180ms_ease]">
              <div className="flex items-center justify-between mb-1.5">
                <SectionLabel htmlFor="sim-inst-until">Última parcela</SectionLabel>
                {installmentsValid && amountValid && (
                  <span className="text-[11px] text-ink-muted">
                    {installmentsCount}x de{' '}
                    {amountValue.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}
                    {' · total '}
                    {installmentsTotal.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}
                  </span>
                )}
              </div>
              <input
                id="sim-inst-until"
                type="date"
                value={installmentsUntil}
                min={startDate}
                onChange={(e) => setInstallmentsUntil(e.target.value)}
                className={`w-full h-11 px-3 rounded-xl bg-paper border text-sm text-ink outline-none focus:border-ink/60 ${
                  showErrors && !installmentsValid ? 'border-accent-neg/60' : 'border-paper-line'
                }`}
              />
              {showErrors && !installmentsValid && (
                <p role="alert" className="mt-1 text-[11px] text-accent-neg">A última parcela deve ser ao menos um mês após a primeira.</p>
              )}
            </section>
          )}

          {frequency === 'recurring' && (
            <section className="animate-[fadeIn_180ms_ease]">
              <SectionLabel htmlFor="sim-until">Até</SectionLabel>
              <input
                id="sim-until"
                type="date"
                value={recurringUntil}
                min={startDate}
                onChange={(e) => setRecurringUntil(e.target.value)}
                className="mt-1.5 w-full h-11 px-3 rounded-xl bg-paper border border-paper-line text-sm text-ink outline-none focus:border-ink/60"
              />
              {showErrors && !recurringValid && (
                <p role="alert" className="mt-1 text-[11px] text-accent-neg">A data final deve ser posterior à inicial.</p>
              )}
            </section>
          )}
        </div>

        <div className="px-5 pt-3 pb-6 border-t border-paper-line/70 shrink-0">
          <button
            onClick={submit}
            onMouseEnter={() => {
              if (!canSubmit) setShowErrors(true);
            }}
            className="w-full h-12 rounded-xl bold-surface text-sm font-medium transition active:scale-[0.99] shadow-soft-sm"
          >
            Adicionar à simulação
          </button>
        </div>
      </div>

      <style>{`
        @keyframes fadeIn { from {opacity:0} to {opacity:1} }
        @keyframes slideUp { from {transform:translateY(100%)} to {transform:translateY(0)} }
      `}</style>
    </div>
  );
}

function SectionLabel({ children, htmlFor }: { children: React.ReactNode; htmlFor?: string }) {
  return (
    <label
      htmlFor={htmlFor}
      className="text-[11px] uppercase tracking-wider text-ink-muted font-medium"
    >
      {children}
    </label>
  );
}

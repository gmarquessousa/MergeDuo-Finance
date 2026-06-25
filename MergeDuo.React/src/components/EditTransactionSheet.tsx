import { useEffect, useRef, useState } from 'react';
import { useFocusTrap } from '../useFocusTrap';
import { CATEGORY_META, type Card, type Transaction, type TransactionCategory } from '../types';
import type { CardsStatus } from '../store';
import { CategoryIcon } from './CategoryIcon';
import { DatePicker } from './DatePicker';
import { TagInput } from './TagInput';

interface Props {
  open: boolean;
  tx: Transaction | null;
  onClose: () => void;
  onSubmit: (data: {
    description: string;
    amount: number;
    date: string;
    category: TransactionCategory;
    cardId?: string | null;
    tags: string[];
    notes: string | null;
  }) => Promise<void> | void;
  tagSuggestions?: string[];
  cards: Card[];
  cardsStatus: CardsStatus;
  cardsError: string | null;
  onCreateCard?: (data: { title: string; closingDay: number; dueDay: number }) => Promise<Card> | Card;
}

const CATEGORY_OPTIONS: TransactionCategory[] = [
  'income',
  'credit_card',
  'loan',
  'fixed_expense',
  'variable_expense',
  'investment',
];

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
  const dt = new Date(y, m - 1, d);
  return dt.toLocaleDateString('pt-BR', { weekday: 'short', day: '2-digit', month: 'long' });
}

export function EditTransactionSheet({
  open,
  tx,
  onClose,
  onSubmit,
  tagSuggestions = [],
  cards,
  cardsStatus,
  cardsError,
  onCreateCard,
}: Props) {
  useEffect(() => {
    if (!open) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = prev;
    };
  }, [open]);

  if (!open || !tx) return null;

  return (
    <EditTransactionContent
      key={`${tx.userId ?? 'local'}:${tx.yearMonth ?? tx.date.slice(0, 7)}:${tx.id}`}
      tx={tx}
      onClose={onClose}
      onSubmit={onSubmit}
      tagSuggestions={tagSuggestions}
      cards={cards}
      cardsStatus={cardsStatus}
      cardsError={cardsError}
      onCreateCard={onCreateCard}
    />
  );
}

function EditTransactionContent({
  tx,
  onClose,
  onSubmit,
  tagSuggestions = [],
  cards,
  cardsStatus,
  cardsError,
  onCreateCard,
}: Omit<Props, 'open' | 'tx'> & { tx: Transaction }) {
  const initialAmount = tx.amount.toLocaleString('pt-BR', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
  const [category, setCategory] = useState<TransactionCategory>(() => tx.category);
  const [description, setDescription] = useState(() => tx.description);
  const [amountStr, setAmountStr] = useState(() => initialAmount);
  const [amountValue, setAmountValue] = useState(() => tx.amount);
  const [date, setDate] = useState(() => tx.purchaseDate ?? tx.date);
  const [cardId, setCardId] = useState<string | null>(() => tx.cardId ?? null);
  const [tags, setTags] = useState<string[]>(() => tx.tags ?? []);
  const [notes, setNotes] = useState(() => tx.notes ?? '');
  const [quickCardOpen, setQuickCardOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const descriptionRef = useRef<HTMLInputElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  useFocusTrap(panelRef, true);

  useEffect(() => {
    const focus = window.setTimeout(() => descriptionRef.current?.focus(), 220);
    return () => window.clearTimeout(focus);
  }, []);

  const isCreditCard = category === 'credit_card';
  const cardsLoading = cardsStatus === 'idle' || cardsStatus === 'loading';
  const cardsUnavailable = cardsStatus === 'error';
  const needsCardRegistration = isCreditCard && cardsStatus === 'ready' && cards.length === 0;
  const cardMissing = isCreditCard && cardsStatus === 'ready' && cards.length > 0 && !cardId;
  const descriptionValid = description.trim().length > 0;
  const amountValid = amountValue > 0;
  const dateValid = /^\d{4}-\d{2}-\d{2}$/.test(date);
  const canSubmit =
    descriptionValid &&
    amountValid &&
    dateValid &&
    !submitting &&
    !cardsLoading &&
    !cardsUnavailable &&
    !needsCardRegistration &&
    !cardMissing;

  async function submit() {
    if (!canSubmit) return;
    setSubmitError(null);
    setSubmitting(true);
    try {
      await onSubmit({
        description: description.trim(),
        amount: amountValue,
        date,
        category,
        cardId: isCreditCard ? cardId : null,
        tags,
        notes: notes.trim() ? notes.trim() : null,
      });
      onClose();
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : 'Não foi possível salvar.');
    } finally {
      setSubmitting(false);
    }
  }

  function handleAmountChange(raw: string) {
    const { display, value } = formatCurrencyInput(raw);
    setAmountStr(display);
    setAmountValue(value);
  }

  if (tx.installments) {
    return (
      <div className="fixed inset-0 z-50 flex items-end justify-center" role="dialog" aria-modal="true" aria-labelledby="edit-tx-title">
        <div className="absolute inset-0 sheet-backdrop" onClick={onClose} />
        <div ref={panelRef} className="relative w-full max-w-md rounded-t-[28px] bg-paper-card p-5 shadow-hero animate-sheet-up">
          <div className="mx-auto mb-3 h-1.5 w-9 rounded-full bg-paper-line" />
          <h2 id="edit-tx-title" className="text-base font-semibold text-ink">Compra parcelada</h2>
          <p className="mt-2 text-sm leading-5 text-ink-muted">
            Parcelas não podem ser editadas individualmente nesta versão. Abra os detalhes do lançamento para excluir a compra inteira e lançar novamente.
          </p>
          <button onClick={onClose} className="mt-5 h-12 w-full rounded-2xl bg-accent-invest text-white text-sm font-semibold tap-surface active:scale-[0.97]">
            Entendi
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center" role="dialog" aria-modal="true" aria-labelledby="edit-tx-title">
      <div className="absolute inset-0 sheet-backdrop" onClick={onClose} />
      <div ref={panelRef} className="relative w-full max-w-md bg-paper-card rounded-t-[28px] shadow-hero animate-sheet-up flex flex-col max-h-[92vh]">
        <div className="pt-2.5 px-5 shrink-0">
          <div className="w-9 h-1.5 bg-paper-line rounded-full mx-auto mb-3" />
          <div className="flex items-start justify-between gap-3 pb-3">
            <div>
              <h2 id="edit-tx-title" className="text-base font-semibold text-ink leading-tight">
                Editar lançamento
              </h2>
              <p className="text-[11px] text-ink-muted mt-0.5 capitalize">{prettyDate(date)}</p>
            </div>
            <button onClick={onClose} aria-label="Fechar" className="w-11 h-11 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
            </button>
          </div>
        </div>

        <div className="px-5 pb-5 overflow-y-auto space-y-4">
          <div>
            <label className="text-[11px] uppercase tracking-wider text-ink-muted">Tipo</label>
            <div className="mt-2 flex flex-wrap gap-1.5">
              {CATEGORY_OPTIONS.map((option) => {
                const meta = CATEGORY_META[option];
                const selected = category === option;
                return (
                  <button
                    key={option}
                    type="button"
                    onClick={() => {
                      setCategory(option);
                      if (option !== 'credit_card') setCardId(null);
                    }}
                    className={`inline-flex h-9 items-center gap-1.5 rounded-full border px-3 text-xs font-medium transition ${
                      selected ? 'bold-surface border-transparent' : 'border-paper-line bg-paper text-ink hover:border-ink/40'
                    }`}
                  >
                    <span className={selected ? 'text-white' : meta.color}>
                      <CategoryIcon category={option} size={14} />
                    </span>
                    {meta.label}
                  </button>
                );
              })}
            </div>
          </div>

          {isCreditCard && (
            <div>
              <div className="mb-2 flex items-center justify-between">
                <label className="text-[11px] uppercase tracking-wider text-ink-muted">Cartão</label>
                <button
                  type="button"
                  onClick={() => setQuickCardOpen((value) => !value)}
                  className="text-[11px] font-medium text-ink-muted hover:text-ink"
                >
                  Novo cartão
                </button>
              </div>
              {cardsLoading ? (
                <div className="rounded-xl border border-dashed border-paper-line p-3 text-[11px] text-ink-muted">Carregando cartões...</div>
              ) : cardsUnavailable ? (
                <div className="rounded-xl border border-dashed border-paper-line p-3 text-[11px] text-ink-muted">{cardsError ?? 'Não foi possível carregar os cartões.'}</div>
              ) : cards.length === 0 && !quickCardOpen ? (
                <div className="rounded-xl border border-dashed border-paper-line p-3 text-[11px] text-ink-muted">Cadastre um cartão para continuar.</div>
              ) : cards.length > 0 ? (
                <div className="flex flex-wrap gap-2">
                  {cards.map((card) => (
                    <button
                      key={card.id}
                      type="button"
                      onClick={() => setCardId(card.id)}
                      className={`h-9 rounded-full border px-3 text-xs font-medium transition ${
                        cardId === card.id ? 'bold-surface border-transparent' : 'border-paper-line bg-paper text-ink hover:border-ink/40'
                      }`}
                    >
                      {card.title}
                    </button>
                  ))}
                </div>
              ) : null}
              {quickCardOpen && onCreateCard && (
                <div className="mt-3">
                  <QuickCardForm
                    onCreate={onCreateCard}
                    onCreated={(card) => {
                      setCardId(card.id);
                      setQuickCardOpen(false);
                    }}
                  />
                </div>
              )}
            </div>
          )}

          <div>
            <label className="text-[11px] uppercase tracking-wider text-ink-muted">Descrição</label>
            <input
              ref={descriptionRef}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              className="mt-1.5 w-full h-12 px-3.5 rounded-2xl bg-paper border border-paper-line text-[15px] text-ink outline-none transition-colors focus:border-accent-invest/60 focus:bg-accent-invest/[0.03]"
            />
          </div>

          <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_160px]">
            <div>
              <label className="text-[11px] uppercase tracking-wider text-ink-muted">{isCreditCard ? 'Data da compra' : 'Data'}</label>
              <div className="mt-1">
                <DatePicker
                  value={date}
                  onChange={setDate}
                  ariaLabel={isCreditCard ? 'Data da compra' : 'Data do lançamento'}
                />
              </div>
            </div>
            <div>
              <label className="text-[11px] uppercase tracking-wider text-ink-muted">Valor</label>
              <div className="mt-1.5 flex items-center h-12 px-3.5 rounded-2xl bg-paper border border-paper-line transition-colors focus-within:border-accent-invest/60 focus-within:bg-accent-invest/[0.03]">
                <span className="text-ink-muted text-[15px] mr-2">R$</span>
                <input
                  inputMode="decimal"
                  value={amountStr}
                  onChange={(e) => handleAmountChange(e.target.value)}
                  placeholder="0,00"
                  className="flex-1 min-w-0 bg-transparent text-[15px] font-semibold tabular-nums text-ink outline-none"
                />
              </div>
            </div>
          </div>

          <TagInput tags={tags} onChange={setTags} suggestions={tagSuggestions} />

          <div>
            <label className="text-[11px] uppercase tracking-wider text-ink-muted">Observações</label>
            <textarea
              value={notes}
              onChange={(event) => setNotes(event.target.value)}
              maxLength={1000}
              className="mt-1.5 w-full min-h-[4.5rem] rounded-2xl bg-paper border border-paper-line px-3.5 py-2.5 text-[15px] text-ink outline-none transition-colors focus:border-accent-invest/60 focus:bg-accent-invest/[0.03]"
              placeholder="Opcional"
            />
          </div>

          {submitError && (
            <div role="alert" className="rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg">{submitError}</div>
          )}

          <div className="flex gap-2 justify-end pt-2">
            <button onClick={onClose} disabled={submitting} className="h-11 px-4 rounded-2xl border border-paper-line text-sm font-semibold text-ink-muted hover:bg-paper-line disabled:opacity-40 transition tap-surface">Cancelar</button>
            <button onClick={() => void submit()} disabled={!canSubmit} className="h-11 px-6 rounded-2xl bg-accent-invest text-white text-sm font-semibold disabled:opacity-40 transition tap-surface active:scale-[0.97] shadow-elevated">{submitting ? 'Salvando...' : 'Salvar'}</button>
          </div>
        </div>
      </div>
    </div>
  );
}

function QuickCardForm({
  onCreate,
  onCreated,
}: {
  onCreate: (data: { title: string; closingDay: number; dueDay: number }) => Promise<Card> | Card;
  onCreated: (card: Card) => void;
}) {
  const [title, setTitle] = useState('');
  const [closingDay, setClosingDay] = useState(27);
  const [dueDay, setDueDay] = useState(5);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const canCreate = !!title.trim() && closingDay >= 1 && closingDay <= 31 && dueDay >= 1 && dueDay <= 31 && !saving;

  async function submit() {
    if (!canCreate) return;
    setSaving(true);
    setError(null);
    try {
      const card = await onCreate({ title: title.trim(), closingDay, dueDay });
      onCreated(card);
      setTitle('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Não foi possível cadastrar o cartão.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="rounded-2xl border border-paper-line bg-paper p-3">
      <div className="grid gap-2 sm:grid-cols-[minmax(0,1fr)_70px_70px]">
        <input
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          placeholder="Nome do cartão"
          className="h-10 rounded-xl border border-paper-line bg-paper-card px-3 text-sm text-ink outline-none focus:border-ink/50"
        />
        <DayInput value={closingDay} onChange={setClosingDay} ariaLabel="Dia de fechamento" />
        <DayInput value={dueDay} onChange={setDueDay} ariaLabel="Dia de vencimento" />
      </div>
      <button type="button" onClick={() => void submit()} disabled={!canCreate} className="mt-2 h-10 w-full rounded-xl bold-surface text-xs font-semibold disabled:opacity-40">
        {saving ? 'Salvando...' : 'Salvar cartão'}
      </button>
      {error && (
        <div role="alert" className="mt-2 rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg">
          {error}
        </div>
      )}
    </div>
  );
}

function DayInput({
  value,
  onChange,
  ariaLabel,
}: {
  value: number;
  onChange: (value: number) => void;
  ariaLabel: string;
}) {
  return (
    <input
      type="number"
      min={1}
      max={31}
      inputMode="numeric"
      value={value}
      onChange={(event) => onChange(clampDay(parseInt(event.target.value, 10)))}
      aria-label={ariaLabel}
      className="h-10 rounded-xl border border-paper-line bg-paper-card px-2 text-center text-sm font-semibold text-ink outline-none focus:border-ink/50"
    />
  );
}

function clampDay(value: number) {
  if (!Number.isFinite(value)) return 1;
  return Math.max(1, Math.min(31, Math.round(value)));
}

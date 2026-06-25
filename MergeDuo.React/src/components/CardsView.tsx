import { useCallback, useEffect, useState } from 'react';
import {
  CardsApiError,
  createCard,
  getCard,
  listCards,
  patchCard,
} from '../api/cards';
import { useFinance } from '../store';
import { useRefresh } from '../refreshContext';
import { CategoryIcon } from './CategoryIcon';
import { DeleteCardSheet } from './DeleteCardSheet';
import type { Card } from '../types';

export function CardsView({
  accessToken,
  onBack,
  onOpenInvoice,
}: {
  accessToken: string;
  onBack: () => void;
  onOpenInvoice: (cardId: string) => void;
}) {
  const {
    cards,
    cardsStatus,
    cardsError,
    setCardsLoading,
    setCards,
    setCardsError,
    addCard,
    updateCard,
  } = useFinance();
  const refreshCtx = useRefresh();
  const [title, setTitle] = useState('');
  const [closingDay, setClosingDay] = useState(27);
  const [dueDay, setDueDay] = useState(5);
  const [saving, setSaving] = useState(false);
  const [deletingCard, setDeletingCard] = useState<Card | null>(null);
  const [editingCard, setEditingCard] = useState<Card | null>(null);
  const [editTitle, setEditTitle] = useState('');
  const [editClosingDay, setEditClosingDay] = useState(27);
  const [editDueDay, setEditDueDay] = useState(5);
  const [editSaving, setEditSaving] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const canSubmit =
    title.trim().length > 0 &&
    closingDay >= 1 &&
    closingDay <= 31 &&
    dueDay >= 1 &&
    dueDay <= 31;

  const canSubmitEdit =
    !!editingCard &&
    editTitle.trim().length > 0 &&
    editClosingDay >= 1 &&
    editClosingDay <= 31 &&
    editDueDay >= 1 &&
    editDueDay <= 31;

  const reload = useCallback(async () => {
    setCardsLoading();
    setActionError(null);
    try {
      const response = await listCards(accessToken);
      setCards(response.items);
    } catch (err) {
      setCardsError(cardsErrorMessage(err));
    }
  }, [accessToken, setCards, setCardsError, setCardsLoading]);

  useEffect(() => {
    if (cardsStatus === 'idle') {
      const timeout = window.setTimeout(() => {
        void reload();
      }, 0);
      return () => window.clearTimeout(timeout);
    }
  }, [cardsStatus, reload]);

  async function submit() {
    setFormError(null);
    setActionError(null);
    if (!canSubmit || saving) return;

    setSaving(true);
    try {
      const created = await createCard(accessToken, {
        title: title.trim(),
        closingDay,
        dueDay,
        currency: 'BRL',
      });
      addCard(created);
      refreshCtx?.refreshAll();
      setTitle('');
      setClosingDay(27);
      setDueDay(5);
    } catch (err) {
      setFormError(cardsErrorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  function startEditing(card: Card) {
    setEditingCard(card);
    setEditTitle(card.title);
    setEditClosingDay(card.closingDay);
    setEditDueDay(card.dueDay);
    setEditError(null);
    setActionError(null);
  }

  async function cardForMutation(card: Card): Promise<Card> {
    if (card.etag?.trim()) {
      return card;
    }

    const fresh = await getCard(accessToken, card.id);
    updateCard(fresh);
    return fresh;
  }

  async function submitEdit() {
    if (!editingCard || !canSubmitEdit || editSaving) return;

    setEditSaving(true);
    setEditError(null);
    setActionError(null);
    try {
      const latest = await cardForMutation(editingCard);
      const updated = await patchCard(accessToken, latest.id, {
        title: editTitle.trim(),
        closingDay: editClosingDay,
        dueDay: editDueDay,
        currency: 'BRL',
      }, latest.etag);
      updateCard(updated);
      refreshCtx?.refreshAll();
      setEditingCard(null);
    } catch (err) {
      setEditError(cardsErrorMessage(err));
    } finally {
      setEditSaving(false);
    }
  }

  const isLoading = cardsStatus === 'idle' || cardsStatus === 'loading';
  const hasBlockingError = cardsStatus === 'error' && cards.length === 0;

  return (
    <>
    <div className="pb-bottom-nav">
      <div className="mx-auto flex w-full max-w-5xl items-center gap-3 px-4 pb-3 pt-2 sm:px-5 md:px-8 lg:px-10">
        <button
          onClick={onBack}
          className="w-9 h-9 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line transition"
          aria-label="Voltar"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="15 18 9 12 15 6"/></svg>
        </button>
        <div className="text-sm font-semibold tracking-tight text-ink">Cartões</div>
      </div>

      <div className="mx-auto grid w-full max-w-5xl gap-4 px-4 sm:px-5 md:grid-cols-[minmax(0,1.05fr)_minmax(320px,0.95fr)] md:px-8 lg:px-10">
        <div className="min-w-0">
          <div className="rounded-2xl bg-paper-card border border-paper-line p-4 shadow-soft">
            <div className="flex items-center justify-between mb-4">
              <div className="text-[10px] uppercase tracking-wider text-ink-muted">
                Novo cartão
              </div>
            </div>

            <div className="space-y-4">
              <div>
                <label className="text-[11px] uppercase tracking-wider text-ink-muted">
                  Título
                </label>
                <input
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  placeholder="Ex.: Nubank, Itaú Click"
                  className="mt-1 w-full h-11 px-3 rounded-xl bg-paper border border-paper-line text-sm text-ink outline-none focus:border-ink/50"
                />
              </div>

              <DaySelector
                label="Fechamento da fatura"
                hint="Dia em que a fatura é fechada"
                value={closingDay}
                onChange={setClosingDay}
              />

              <DaySelector
                label="Vencimento da fatura"
                hint="Dia em que a fatura vence"
                value={dueDay}
                onChange={setDueDay}
              />
            </div>

            <button
              onClick={submit}
              disabled={!canSubmit || saving}
              className="mt-5 w-full h-11 rounded-xl bold-surface text-sm font-medium disabled:opacity-40 transition"
            >
              {saving ? 'Salvando...' : 'Salvar cartão'}
            </button>
            {formError && (
              <div className="mt-3 rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg">
                {formError}
              </div>
            )}
          </div>
        </div>

        <div className="min-w-0">
          <div className="flex items-center justify-between mb-2 px-1">
            <div className="text-[10px] uppercase tracking-wider text-ink-muted">
              Cartões cadastrados
            </div>
            <div className="text-[10px] text-ink-muted">{cards.length}</div>
          </div>

          {actionError && (
            <div className="mb-3 rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg">
              {actionError}
            </div>
          )}

          {cardsStatus === 'error' && cards.length > 0 && (
            <div className="mb-3 rounded-xl border border-paper-line bg-paper-card px-3 py-2 text-[12px] text-ink-muted">
              {cardsError ?? 'Não foi possível atualizar a lista de cartões.'}
            </div>
          )}

          {isLoading ? (
            <div className="rounded-2xl bg-paper-card border border-paper-line p-5 text-center text-sm text-ink-muted shadow-soft">
              Carregando cartões...
            </div>
          ) : hasBlockingError ? (
            <div className="rounded-2xl bg-paper-card border border-paper-line p-5 text-center shadow-soft">
              <div className="text-sm text-ink-muted">
                {cardsError ?? 'Não foi possível carregar os cartões.'}
              </div>
              <button
                onClick={() => void reload()}
                className="mt-3 h-9 px-4 rounded-xl border border-paper-line text-xs font-medium text-ink hover:bg-paper-line transition"
              >
                Tentar novamente
              </button>
            </div>
          ) : cards.length === 0 ? (
            <div className="rounded-2xl bg-paper-card border border-paper-line p-5 text-center text-sm text-ink-muted shadow-soft">
              Nenhum cartão cadastrado.
            </div>
          ) : (
            <div className="space-y-2">
              {cards.map((card) => (
                <div
                  key={card.id}
                  className="rounded-2xl bg-paper-card border border-paper-line p-4 shadow-soft"
                >
                  {editingCard?.id === card.id ? (
                    <div className="space-y-3">
                      <div className="flex items-center justify-between gap-3">
                        <div className="text-[10px] uppercase tracking-wider text-ink-muted">
                          Editar cartão
                        </div>
                        <button
                          type="button"
                          onClick={() => setEditingCard(null)}
                          disabled={editSaving}
                          className="w-8 h-8 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line disabled:opacity-40 transition"
                          aria-label="Cancelar edição"
                        >
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                        </button>
                      </div>
                      <div>
                        <label className="text-[11px] uppercase tracking-wider text-ink-muted">
                          Título
                        </label>
                        <input
                          value={editTitle}
                          onChange={(e) => setEditTitle(e.target.value)}
                          className="mt-1 w-full h-10 px-3 rounded-xl bg-paper border border-paper-line text-sm text-ink outline-none focus:border-ink/50"
                        />
                      </div>
                      <DaySelector
                        label="Fechamento da fatura"
                        hint="Dia em que a fatura é fechada"
                        value={editClosingDay}
                        onChange={setEditClosingDay}
                      />
                      <DaySelector
                        label="Vencimento da fatura"
                        hint="Dia em que a fatura vence"
                        value={editDueDay}
                        onChange={setEditDueDay}
                      />
                      {editError && (
                        <div className="rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg">
                          {editError}
                        </div>
                      )}
                      <div className="flex justify-end gap-2">
                        <button
                          type="button"
                          onClick={() => setEditingCard(null)}
                          disabled={editSaving}
                          className="h-9 px-4 rounded-xl border border-paper-line text-xs font-medium text-ink-muted hover:bg-paper-line disabled:opacity-40 transition"
                        >
                          Cancelar
                        </button>
                        <button
                          type="button"
                          onClick={() => void submitEdit()}
                          disabled={!canSubmitEdit || editSaving}
                          className="h-9 px-4 rounded-xl bold-surface text-xs font-medium disabled:opacity-40 transition"
                        >
                          {editSaving ? 'Salvando...' : 'Salvar'}
                        </button>
                      </div>
                    </div>
                  ) : (
                    <>
                      <div className="flex items-start gap-3">
                        <div className="w-9 h-9 rounded-full bg-paper-line grid place-items-center shrink-0 text-accent-neg">
                          <CategoryIcon category="credit_card" size={16} />
                        </div>
                        <div className="flex-1 min-w-0 text-left">
                          <div className="text-sm font-medium text-ink truncate">
                            {card.title}
                          </div>
                          <div className="mt-0.5 text-[11px] text-ink-muted">
                            Fecha dia {String(card.closingDay).padStart(2, '0')} - Vence dia {String(card.dueDay).padStart(2, '0')}
                          </div>
                        </div>
                        <button
                          onClick={() => startEditing(card)}
                          className="w-8 h-8 rounded-full grid place-items-center text-ink-muted hover:text-ink hover:bg-paper-line transition"
                          aria-label="Editar cartão"
                        >
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
                        </button>
                        <button
                          onClick={() => setDeletingCard(card)}
                          className="w-8 h-8 rounded-full grid place-items-center text-ink-muted hover:text-accent-neg hover:bg-paper-line transition"
                          aria-label="Remover cartão"
                        >
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6M14 11v6"/><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/></svg>
                        </button>
                      </div>
                      <button
                        onClick={() => onOpenInvoice(card.id)}
                        className="mt-3 w-full h-9 rounded-xl border border-paper-line text-xs font-medium text-ink hover:bg-paper-line transition flex items-center justify-center gap-2"
                      >
                        Ver fatura
                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="9 18 15 12 9 6"/></svg>
                      </button>
                    </>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>

    {deletingCard && (
      <DeleteCardSheet
        card={deletingCard}
        accessToken={accessToken}
        onClose={() => setDeletingCard(null)}
        onDeleted={() => { setDeletingCard(null); refreshCtx?.refreshAll(); }}
      />
    )}
    </>
  );
}

function cardsErrorMessage(err: unknown) {
  if (err instanceof CardsApiError) {
    if (err.code === 'invalid_title') return 'Informe um título válido para o cartão.';
    if (err.code === 'invalid_billing_day') return 'Informe dias entre 1 e 31.';
    if (err.code === 'unsupported_currency') return 'Apenas BRL está disponível no momento.';
    if (err.code === 'card_conflict' || err.code === 'precondition_failed') {
      return 'Este cartão mudou em outro dispositivo. Atualize e tente novamente.';
    }
    if (err.code === 'unauthorized') return 'Sua sessão expirou. Entre novamente.';
    if (err.code === 'rate_limited') return 'Muitas tentativas. Aguarde um pouco e tente de novo.';
    if (err.code === 'cards_dependency_unavailable') return 'Não foi possível acessar o serviço de cartões agora.';
    return err.message || 'Não foi possível concluir a operação.';
  }

  return err instanceof Error ? err.message : 'Não foi possível concluir a operação.';
}

function DaySelector({
  label,
  hint,
  value,
  onChange,
}: {
  label: string;
  hint: string;
  value: number;
  onChange: (v: number) => void;
}) {
  function clamp(n: number) {
    if (Number.isNaN(n)) return 1;
    return Math.min(31, Math.max(1, Math.round(n)));
  }
  return (
    <div>
      <div className="mb-1.5">
        <label className="text-[11px] uppercase tracking-wider text-ink-muted">
          {label}
        </label>
        <div className="text-[10px] text-ink-muted">{hint}</div>
      </div>
      <div className="flex items-center h-11 rounded-xl bg-paper border border-paper-line overflow-hidden focus-within:border-ink/50">
        <button
          type="button"
          onClick={() => onChange(clamp(value - 1))}
          disabled={value <= 1}
          className="w-11 h-full grid place-items-center text-ink-muted hover:text-ink hover:bg-paper-line disabled:opacity-30 disabled:hover:bg-transparent transition"
          aria-label="Diminuir dia"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="5" y1="12" x2="19" y2="12"/></svg>
        </button>
        <input
          type="number"
          min={1}
          max={31}
          inputMode="numeric"
          value={value}
          onChange={(e) => onChange(clamp(parseInt(e.target.value, 10)))}
          className="flex-1 min-w-0 h-full bg-transparent text-center text-sm font-semibold text-ink tabular-nums outline-none [appearance:textfield] [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none"
        />
        <button
          type="button"
          onClick={() => onChange(clamp(value + 1))}
          disabled={value >= 31}
          className="w-11 h-full grid place-items-center text-ink-muted hover:text-ink hover:bg-paper-line disabled:opacity-30 disabled:hover:bg-transparent transition"
          aria-label="Aumentar dia"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
        </button>
      </div>
    </div>
  );
}

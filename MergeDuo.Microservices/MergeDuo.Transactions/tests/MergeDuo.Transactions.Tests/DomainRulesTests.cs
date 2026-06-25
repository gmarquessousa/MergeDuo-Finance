using MergeDuo.Transactions.Domain.Documents;
using MergeDuo.Transactions.Domain.Exceptions;
using MergeDuo.Transactions.Domain.Rules;

namespace MergeDuo.Transactions.Tests;

public sealed class DomainRulesTests
{
    [Fact]
    public void YearMonth_is_derived_from_date()
    {
        Assert.Equal("2026-05", YearMonthRules.FromDate(new DateOnly(2026, 5, 5)));
        Assert.Throws<TransactionsBadRequestException>(() => YearMonthRules.EnsureValid("2026-13"));
    }

    [Fact]
    public void Category_derives_kind()
    {
        Assert.Equal("in", CategoryRules.KindFor(CategoryRules.Income));
        Assert.Equal("out", CategoryRules.KindFor(CategoryRules.CreditCard));
        Assert.Equal("invest", CategoryRules.KindFor(CategoryRules.Investment));
    }

    [Fact]
    public void Money_rejects_invalid_scale()
    {
        Assert.Equal(10.25m, MoneyRules.EnsureValid(10.25m));
        Assert.Throws<TransactionsBadRequestException>(() => MoneyRules.EnsureValid(10.257m));
    }

    [Fact]
    public void Card_invoice_calculates_due_date()
    {
        var card = new CardDocument { Id = "card_nubank_01", UserId = "usr_gmarques", ClosingDay = 28, DueDay = 5 };

        var first = CardInvoiceRules.DueDateForPurchase(card, new DateOnly(2026, 4, 10), 1);
        var second = CardInvoiceRules.DueDateForPurchase(card, new DateOnly(2026, 4, 10), 2);

        Assert.Equal(new DateOnly(2026, 5, 5), first);
        Assert.Equal(new DateOnly(2026, 6, 5), second);
    }

    [Fact]
    public void Installments_split_amount_with_last_adjustment()
    {
        var values = InstallmentRules.SplitAmount(760m, 3);

        Assert.Equal([253.33m, 253.33m, 253.34m], values);
        Assert.Equal(760m, values.Sum());
    }
}

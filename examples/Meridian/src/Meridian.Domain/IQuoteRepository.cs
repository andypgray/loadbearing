namespace Meridian.Domain;

public interface IQuoteRepository
{
    Task Add(Quote quote);

    Task<Quote?> Get(string reference);
}
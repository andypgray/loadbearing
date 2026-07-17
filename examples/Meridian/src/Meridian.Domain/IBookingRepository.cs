namespace Meridian.Domain;

public interface IBookingRepository
{
    Task Add(Booking booking);

    Task<Booking?> Get(string reference);
}
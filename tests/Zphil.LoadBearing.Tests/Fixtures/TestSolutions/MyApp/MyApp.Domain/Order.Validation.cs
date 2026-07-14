namespace MyApp.Domain;

public partial class Order
{
    public bool Validate() => Total.Amount >= 0;
}

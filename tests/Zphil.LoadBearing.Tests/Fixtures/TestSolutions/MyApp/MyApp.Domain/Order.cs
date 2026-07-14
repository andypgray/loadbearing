namespace MyApp.Domain;

public partial class Order
{
    public string Reference { get; set; }

    public Money Total { get; set; }

    public Line AddLine(string name, Money price) => new Line(name, price);

    public class Line
    {
        public Line(string name, Money price)
        {
            Name = name;
            Price = price;
        }

        public string Name { get; }

        public Money Price { get; }
    }
}

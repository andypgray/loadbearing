using MyApp.Web;

namespace MyApp.Domain;

public class OrderService
{
    public string DescribeOrder(Order order)
    {
        HomeController controller = new HomeController();
        return controller.RenderOrder(order.Reference.ToWebDisplay());
    }

    public object ViolationMarker() => typeof(HomeController);
}
